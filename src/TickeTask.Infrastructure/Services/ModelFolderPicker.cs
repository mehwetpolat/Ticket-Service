using System.Diagnostics;
using System.Text;
using TickeTask.Application.Abstractions;
using TickeTask.Application.Common;

namespace TickeTask.Infrastructure.Services;

/// <summary>
/// Opens the operating system folder picker on the machine that hosts the
/// application. This only works when TickeTask runs on the user's desktop; a
/// container has no access to the host desktop, so callers get a clear message
/// and can type the full path instead.
/// </summary>
public sealed class ModelFolderPicker : IModelFolderPicker
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(10);

    public async Task<string?> PickAsync(string? initialPath, CancellationToken ct = default)
    {
        if (!CanOpenDialog())
            throw ApiException.Unsupported("Bu ortamda bilgisayarın klasör seçicisi açılamıyor. Klasörün tam yolunu yazarak devam edin.");

        if (!await Gate.WaitAsync(TimeSpan.FromSeconds(5), ct))
            throw ApiException.Conflict("Zaten açık bir klasör seçici var. Önce onu kapatın.");

        try
        {
            var start = CleanStart(initialPath);
            if (OperatingSystem.IsWindows()) return await PickWindowsAsync(start, ct);
            if (OperatingSystem.IsMacOS()) return await PickMacAsync(start, ct);
            return await PickLinuxAsync(start, ct);
        }
        finally
        {
            Gate.Release();
        }
    }

    private static bool CanOpenDialog()
    {
        if (string.Equals(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"), "true", StringComparison.OrdinalIgnoreCase))
            return false;
        if (File.Exists("/.dockerenv")) return false;
        if (OperatingSystem.IsWindows()) return Environment.UserInteractive;
        if (OperatingSystem.IsMacOS()) return Environment.UserInteractive;
        return !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISPLAY")) ||
               !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));
    }

    private static string? CleanStart(string? initialPath)
    {
        var value = (initialPath ?? string.Empty).Trim().Trim('"');
        if (value.Length == 0) return null;
        try
        {
            return Directory.Exists(value) ? value : Directory.GetParent(value)?.FullName;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private static async Task<string?> PickWindowsAsync(string? start, CancellationToken ct)
    {
        const string script = """
            Add-Type -AssemblyName System.Windows.Forms | Out-Null
            [System.Windows.Forms.Application]::EnableVisualStyles()
            $dialog = New-Object System.Windows.Forms.FolderBrowserDialog
            $dialog.Description = 'TickeTask model klasörünü seçin'
            $dialog.ShowNewFolderButton = $true
            $dialog.RootFolder = [System.Environment+SpecialFolder]::MyComputer
            $start = $env:TICKETASK_PICKER_START
            if (-not [string]::IsNullOrWhiteSpace($start) -and (Test-Path -LiteralPath $start)) {
                $dialog.SelectedPath = $start
            }
            if ($dialog.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {
                [Console]::Out.Write($dialog.SelectedPath)
            }
            """;

        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -STA -ExecutionPolicy Bypass -EncodedCommand {encoded}",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        if (!string.IsNullOrWhiteSpace(start)) startInfo.Environment["TICKETASK_PICKER_START"] = start;
        return await RunAsync(startInfo, ct);
    }

    private static async Task<string?> PickMacAsync(string? start, CancellationToken ct)
    {
        var target = string.IsNullOrWhiteSpace(start) ? string.Empty : $" default location (POSIX file \"{EscapeAppleScript(start)}\")";
        var script = $"POSIX path of (choose folder with prompt \"TickeTask model klasörünü seçin\"{target})";
        var startInfo = new ProcessStartInfo
        {
            FileName = "osascript",
            Arguments = $"-e {Quote(script)}",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        return await RunAsync(startInfo, ct);
    }

    private static async Task<string?> PickLinuxAsync(string? start, CancellationToken ct)
    {
        var zenity = FindCommand("zenity");
        if (zenity is not null)
        {
            var arguments = "--file-selection --directory --title=\"TickeTask model klasörünü seçin\"";
            if (!string.IsNullOrWhiteSpace(start)) arguments += $" --filename=\"{start.TrimEnd('/')}/\"";
            return await RunAsync(Command(zenity, arguments), ct);
        }

        var kdialog = FindCommand("kdialog");
        if (kdialog is not null)
        {
            var arguments = "--getexistingdirectory";
            if (!string.IsNullOrWhiteSpace(start)) arguments += $" \"{start}\"";
            return await RunAsync(Command(kdialog, arguments), ct);
        }

        throw ApiException.Unsupported("Bilgisayarda klasör seçici aracı bulunamadı. Klasörün tam yolunu yazarak devam edin.");
    }

    private static ProcessStartInfo Command(string fileName, string arguments) => new()
    {
        FileName = fileName,
        Arguments = arguments,
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true
    };

    private static string? FindCommand(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        return path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(directory => Path.Combine(directory, name))
            .FirstOrDefault(File.Exists);
    }

    private static async Task<string?> RunAsync(ProcessStartInfo startInfo, CancellationToken ct)
    {
        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start()) return null;
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            throw ApiException.Unsupported("Klasör seçici açılamadı. Klasörün tam yolunu yazarak devam edin.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);
        try
        {
            var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            var output = (await outputTask).Trim();
            var error = (await errorTask).Trim();
            if (process.ExitCode != 0)
            {
                if (!string.IsNullOrWhiteSpace(error))
                    throw ApiException.Unsupported($"Klasör seçici kapatıldı: {error}");
                return null;
            }
            return output.Length == 0 ? null : output;
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            if (ct.IsCancellationRequested) throw;
            throw ApiException.Unsupported("Klasör seçici zaman aşımına uğradı. Tam yolu yazarak devam edebilirsiniz.");
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(true);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
    }

    private static string EscapeAppleScript(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";
}
