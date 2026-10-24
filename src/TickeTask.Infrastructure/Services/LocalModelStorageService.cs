using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using TickeTask.Application.Abstractions;
using TickeTask.Application.Common;
using TickeTask.Application.Dtos;

namespace TickeTask.Infrastructure.Services;

public sealed partial class LocalModelStorageService : IModelControlClient, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Regex FolderNamePattern = new(@"^[\p{L}\p{N} _-]{1,60}$", RegexOptions.CultureInvariant);
    private static readonly Regex ModelNamePattern = new(@"^[a-z0-9][a-z0-9._:/-]{1,100}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly AppRuntime _runtime;
    private readonly ILogger<LocalModelStorageService>? _logger;
    private readonly object _state = new();
    private readonly SemaphoreSlim _initialization = new(1, 1);
    private readonly SemaphoreSlim _operations = new(1, 1);

    private ModelStorageSelection _selection;
    private string _customRoot = string.Empty;
    private ModelDownloadJob _job = IdleJob();
    private CancellationTokenSource? _pullSource;
    private Task? _pullTask;
    private bool _initialized;

    public LocalModelStorageService(AppRuntime runtime, ILogger<LocalModelStorageService>? logger = null)
    {
        _runtime = runtime;
        _logger = logger;
        _selection = new ModelStorageSelection("primary", string.Empty, Label("primary", string.Empty), PrimaryFull);
    }

    private string PrimaryFull => Path.TrimEndingDirectorySeparator(Path.GetFullPath(_runtime.ModelPrimaryDirectory));

    private string ConfigDirectory => _runtime.ModelConfigDirectory;
    private string SelectionPath => Path.Combine(ConfigDirectory, "storage-selection.json");
    private string JobPath => Path.Combine(ConfigDirectory, "download-job.json");
    private string LinkPath => Path.Combine(ConfigDirectory, "models");

    public async Task<ModelStorageStatus?> GetStatusAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        lock (_state)
        {
            return new ModelStorageStatus(
                new ModelStorageRoots(
                    _runtime.ModelPrimaryLabel, _runtime.ModelSecondaryLabel, _customRoot,
                    _runtime.ModelPrimaryHostPath, _runtime.ModelSecondaryHostPath),
                _selection,
                _job);
        }
    }

    public async Task<ModelFolderListing?> GetFoldersAsync(string slot, string folder, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        var target = ExistingFolder(slot, folder);
        return new ModelFolderListing(slot, target.Relative, Browse(target.Full), target.Full);
    }

    public async Task<ModelFolderListing?> CreateFolderAsync(string slot, string folder, string name, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await _operations.WaitAsync(ct);
        try
        {
            ThrowIfDownloading();
            var parent = ExistingFolder(slot, folder);
            var folderName = (name ?? string.Empty).Trim();
            if (!FolderNamePattern.IsMatch(folderName) || folderName.Equals("models", StringComparison.OrdinalIgnoreCase))
                throw ApiException.BadRequest("Klasör adı geçersiz.");

            var path = Path.Combine(parent.Full, folderName);
            if (Directory.Exists(path) || File.Exists(path)) throw ApiException.BadRequest("Bu ada sahip bir klasör zaten var.");
            try
            {
                Directory.CreateDirectory(path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw ApiException.BadRequest("Klasör oluşturulamadı.");
            }
            return new ModelFolderListing(slot, parent.Relative, Browse(parent.Full), parent.Full);
        }
        finally
        {
            _operations.Release();
        }
    }

    public async Task<ModelStorageSelection?> SelectAsync(string slot, string folder, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await _operations.WaitAsync(ct);
        try
        {
            ThrowIfDownloading();
            var relative = NormalizeFolder(folder);
            return await ApplySelectionAsync(slot, relative, ct);
        }
        finally
        {
            _operations.Release();
        }
    }

    public async Task<ModelStorageSelection?> SelectPathAsync(string path, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await _operations.WaitAsync(ct);
        try
        {
            ThrowIfDownloading();
            var mapped = MapHostPath(path);
            var full = mapped ?? NormalizeAbsolutePath(path);
            if (!Directory.Exists(full))
                throw ApiException.BadRequest("Klasör bulunamadı. Tam yolu kontrol edin. Docker ile çalışıyorsanız klasörün konteynere bağlı olduğundan emin olun.");
            var slot = SlotForPath(full);
            if (slot is not null) return await ApplySelectionAsync(slot, RelativeTo(slot, full), ct);

            var previous = _customRoot;
            _customRoot = full;
            try
            {
                return await ApplySelectionAsync("custom", string.Empty, ct);
            }
            catch
            {
                _customRoot = previous;
                throw;
            }
        }
        finally
        {
            _operations.Release();
        }
    }

    private async Task<ModelStorageSelection> ApplySelectionAsync(string slot, string folder, CancellationToken ct)
    {
        var changed = _selection.Slot != slot || _selection.Folder != folder;
        var selection = await ActivateAsync(slot, folder, ct);
        if (changed)
        {
            ModelDownloadJob job;
            lock (_state)
            {
                _job = IdleJob();
                job = _job;
            }
            await SaveJobAsync(job, ct);
        }
        return selection;
    }


    public async Task PullAsync(string model, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        await _operations.WaitAsync(ct);
        try
        {
            ThrowIfDownloading();
            var requested = model ?? string.Empty;
            if (!ModelNamePattern.IsMatch(requested)) throw ApiException.BadRequest("Model adı geçersiz.");
            var source = new CancellationTokenSource();
            ModelDownloadJob job;
            lock (_state)
            {
                _pullSource = source;
                _job = new ModelDownloadJob("running", requested, 0, 0, "İndirme başlıyor");
                job = _job;
            }
            await SaveJobAsync(job, ct);
            _pullTask = Task.Run(() => RunPullAsync(requested, source), CancellationToken.None);
        }
        finally
        {
            _operations.Release();
        }
    }

    public async Task CancelAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        lock (_state)
        {
            if (_job.State == "running") _pullSource?.Cancel();
        }
    }

    private void ThrowIfDownloading()
    {
        lock (_state)
        {
            if (_job.State == "running") throw ApiException.Conflict("İndirme sırasında konum değiştirilemez.");
        }
    }

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (Volatile.Read(ref _initialized)) return;
        await _initialization.WaitAsync(ct);
        try
        {
            if (_initialized) return;
            Directory.CreateDirectory(ConfigDirectory);
            Directory.CreateDirectory(_runtime.ModelPrimaryDirectory);
            Directory.CreateDirectory(_runtime.ModelSecondaryDirectory);

            var saved = await LoadSelectionAsync(ct);
            _customRoot = saved.CustomRoot;
            var savedSelection = ValidSlot(saved.Selection.Slot)
                ? saved.Selection
                : new ModelStorageSelection("primary", string.Empty, Label("primary", string.Empty), PrimaryFull);
            var candidates = new List<ModelStorageSelection>();
            if (savedSelection.Slot == "custom")
            {
                if (_customRoot.Length > 0) candidates.Add(savedSelection);
            }
            else
            {
                candidates.Add(savedSelection);
            }
            candidates.Add(new ModelStorageSelection("primary", string.Empty, Label("primary", string.Empty), PrimaryFull));

            var activated = false;
            foreach (var candidate in candidates)
            {
                try
                {
                    await ActivateAsync(candidate.Slot, NormalizeFolder(candidate.Folder), ct);
                    activated = true;
                    break;
                }
                catch (Exception exception) when (exception is ApiException or IOException or UnauthorizedAccessException)
                {
                    _logger?.LogWarning(exception, "Model storage location {Slot}/{Folder} could not be activated", candidate.Slot, candidate.Folder);
                }
            }
            if (!activated) _selection = new ModelStorageSelection("primary", string.Empty, Label("primary", string.Empty), PrimaryFull);

            var job = await LoadJobAsync(ct);
            if (job.State == "running")
            {
                job = job with
                {
                    State = "error",
                    Message = "İndirme sunucu yeniden başladığı için kesildi. Modeli indir düğmesine tekrar basın; mevcut dosyalar kaldığı yerden kullanılır."
                };
                await SaveJobAsync(job, ct);
            }
            lock (_state) _job = job;
            _initialized = true;
        }
        finally
        {
            _initialization.Release();
        }
    }

    private async Task<ModelStorageSelection> ActivateAsync(string slot, string folder, CancellationToken ct)
    {
        var target = ExistingFolder(slot, folder);
        var models = ModelsPath(target.Full);
        Directory.CreateDirectory(models);
        EnsureModelsLink(models);
        var selection = new ModelStorageSelection(slot, target.Relative, Label(slot, target.Relative), target.Full);
        lock (_state) _selection = selection;
        await SaveSelectionAsync(selection, ct);
        return selection;
    }

    private void EnsureModelsLink(string models)
    {
        var temporary = $"{LinkPath}.next";
        if (File.Exists(LinkPath) && !IsLink(LinkPath))
            throw ApiException.BadRequest("Model konumu için ayrılmış yolda bir dosya var.");
        if (Directory.Exists(LinkPath) && !IsLink(LinkPath))
        {
            // A fresh Ollama container can create this as a real empty directory
            // before the app has a chance to create the shared link. Only replace
            // it when it is empty; never delete a directory that contains models.
            try
            {
                Directory.Delete(LinkPath, recursive: false);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _logger?.LogWarning(exception, "The reserved Ollama models directory is not empty at {Path}", LinkPath);
                throw ApiException.BadRequest("Model konumu için ayrılmış klasör dolu. İçeriğini yedekledikten sonra tekrar deneyin.");
            }
        }
        RemoveLink(temporary);
        var linkCreated = false;
        try
        {
            Directory.CreateSymbolicLink(temporary, models);
            linkCreated = true;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            if (OperatingSystem.IsWindows() && TryCreateJunction(temporary, models)) linkCreated = true;
            else _logger?.LogWarning(exception, "A directory link for the Ollama model store could not be created at {Path}", LinkPath);
        }

        if (!linkCreated)
        {
            RemoveLink(temporary);
            return;
        }

        try
        {
            RemoveLink(LinkPath);
            Directory.Move(temporary, LinkPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            RemoveLink(temporary);
            _logger?.LogWarning(exception, "The Ollama model store link could not be activated at {Path}", LinkPath);
        }
    }

    private static bool TryCreateJunction(string link, string target)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c mklink /J \"{link}\" \"{target}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            if (process is null) return false;
            if (!process.WaitForExit(10000))
            {
                process.Kill(true);
                return false;
            }
            return process.ExitCode == 0 && Directory.Exists(link);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return false;
        }
    }

    private static bool IsLink(string path)
    {
        // LinkTarget works for broken links too, unlike Directory.Exists and
        // File.Exists which follow the link and return false for a missing target.
        try
        {
            if (new FileInfo(path).LinkTarget is not null) return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }

        try
        {
            if (new DirectoryInfo(path).LinkTarget is not null) return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }

        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void RemoveLink(string path)
    {
        if (!IsLink(path)) return;
        try
        {
            // File.Delete removes a file link or a directory symlink on Unix.
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            try
            {
                // Windows directory links and junctions are removed this way.
                Directory.Delete(path, recursive: false);
            }
            catch (Exception nested) when (nested is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private (string Slot, string Relative, string Full) ExistingFolder(string slot, string folder)
    {
        if (!ValidSlot(slot)) throw ApiException.BadRequest("Depolama konumu geçersiz.");
        var relative = NormalizeFolder(folder);
        if (slot == "custom")
        {
            if (relative.Length > 0) throw ApiException.BadRequest("Özel konum için alt klasör seçilemez.");
            if (_customRoot.Length == 0) throw ApiException.BadRequest("Özel model konumu tanımlı değil.");
            if (!Directory.Exists(_customRoot)) throw ApiException.BadRequest("Özel model konumu bulunamadı. Yolu yeniden seçin.");
            return (slot, string.Empty, _customRoot);
        }
        var root = RootPath(slot);
        var full = LocationPath(root, relative);
        if (!Directory.Exists(full)) throw ApiException.BadRequest("Klasör bulunamadı.");
        EnsureRealPathWithinRoot(root, relative);
        return (slot, relative, full);
    }

    private static string NormalizeAbsolutePath(string? value)
    {
        var path = (value ?? string.Empty).Trim().Trim('"');
        if (path.Length == 0) throw ApiException.BadRequest("Model klasörü yolu boş olamaz.");
        if (path.Length > 500) throw ApiException.BadRequest("Model klasörü yolu çok uzun.");
        path = Environment.ExpandEnvironmentVariables(path);
        if (path == "~" || path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith("~\\", StringComparison.Ordinal))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            path = Path.Combine(home, path[1..].TrimStart('/', '\\'));
        }
        if (!Path.IsPathFullyQualified(path))
            throw ApiException.BadRequest("Tam klasör yolu girin. Örnek: D:\\modeller veya /home/kullanici/modeller");
        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw ApiException.BadRequest("Klasör yolu geçersiz.");
        }
    }

    private string? MapHostPath(string? value)
    {
        var path = (value ?? string.Empty).Trim().Trim('"');
        if (path.Length == 0) return null;
        path = Environment.ExpandEnvironmentVariables(path);
        var normalized = path.Replace('\\', '/').TrimEnd('/');
        var comparable = normalized.ToLowerInvariant();
        if (comparable.Length == 0) return null;

        foreach (var (slot, host) in new[]
                 {
                     ("primary", _runtime.ModelPrimaryHostPath),
                     ("secondary", _runtime.ModelSecondaryHostPath)
                 })
        {
            if (string.IsNullOrWhiteSpace(host) || !LooksFullyQualified(host)) continue;
            var root = host.Replace('\\', '/').TrimEnd('/').ToLowerInvariant();
            if (comparable == root)
                return Path.TrimEndingDirectorySeparator(Path.GetFullPath(RootPath(slot)));
            if (!comparable.StartsWith(root + "/", StringComparison.Ordinal)) continue;

            var relative = normalized[(root.Length + 1)..];
            try
            {
                return LocationPath(RootPath(slot), NormalizeFolder(relative));
            }
            catch (ApiException)
            {
                return null;
            }
        }
        return null;
    }

    private static bool LooksFullyQualified(string path) =>
        Path.IsPathFullyQualified(path) ||
        System.Text.RegularExpressions.Regex.IsMatch(path, @"^[A-Za-z]:[/\\]") ||
        path.StartsWith(@"\\", StringComparison.Ordinal);

    private string? SlotForPath(string full)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        foreach (var slot in new[] { "primary", "secondary" })
        {
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(RootPath(slot)));
            if (IsWithin(root, full, comparison, Path.DirectorySeparatorChar)) return slot;
        }
        return null;
    }

    private string RelativeTo(string slot, string full)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(RootPath(slot)));
        var relative = Path.GetRelativePath(root, full);
        return relative == "." ? string.Empty : NormalizeFolder(relative.Replace('\\', '/'));
    }

    private static void EnsureRealPathWithinRoot(string root, string relative)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var separator = Path.DirectorySeparatorChar;
        var realRoot = ResolveFinalTarget(root) ?? Path.TrimEndingDirectorySeparator(root);
        var current = realRoot;
        foreach (var part in relative.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(current, part);
            if (!Directory.Exists(candidate)) throw ApiException.BadRequest("Klasör bulunamadı.");
            current = ResolveFinalTarget(candidate) ?? candidate;
            if (!IsWithin(realRoot, current, comparison, separator))
                throw ApiException.BadRequest("Bağlantılı klasörler seçilemez.");
        }
        if (!IsWithin(realRoot, current, comparison, separator))
            throw ApiException.BadRequest("Bağlantılı klasörler seçilemez.");
    }

    private static string? ResolveFinalTarget(string path)
    {
        try
        {
            var info = new DirectoryInfo(path);
            if (info.LinkTarget is null) return null;
            return info.ResolveLinkTarget(true)?.FullName;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private string LocationPath(string root, string relative)
    {
        var rootFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var full = relative.Length == 0
            ? rootFull
            : Path.GetFullPath(Path.Combine(rootFull, relative.Replace('/', Path.DirectorySeparatorChar)));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!IsWithin(rootFull, full, comparison, Path.DirectorySeparatorChar))
            throw ApiException.BadRequest("İzin verilen konumun dışına çıkılamaz.");
        return full;
    }

    private static bool IsWithin(string root, string path, StringComparison comparison, char separator)
    {
        var trimmed = Path.TrimEndingDirectorySeparator(path);
        return trimmed.Equals(Path.TrimEndingDirectorySeparator(root), comparison) ||
               trimmed.StartsWith(Path.TrimEndingDirectorySeparator(root) + separator, comparison);
    }

    private static string NormalizeFolder(string? folder)
    {
        var value = folder ?? string.Empty;
        if (value.Length > 240 || value.Contains('\\') || value.Contains('\0'))
            throw ApiException.BadRequest("Klasör adı geçersiz.");
        var parts = value.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var invalidChars = Path.GetInvalidFileNameChars();
        foreach (var part in parts)
        {
            if (part is "." or ".." || part.StartsWith('.') || part.IndexOfAny(invalidChars) >= 0 ||
                part.EndsWith('.') || part.EndsWith(' '))
                throw ApiException.BadRequest("Klasör adı geçersiz.");
        }
        return string.Join('/', parts);
    }

    private static bool ValidSlot(string? slot) => slot is "primary" or "secondary" or "custom";

    private string RootPath(string slot) => slot switch
    {
        "primary" => _runtime.ModelPrimaryDirectory,
        "secondary" => _runtime.ModelSecondaryDirectory,
        "custom" => _customRoot.Length > 0 ? _customRoot : throw ApiException.BadRequest("Özel model konumu tanımlı değil."),
        _ => throw ApiException.BadRequest("Depolama konumu geçersiz.")
    };

    private string Label(string slot, string folder) =>
        $"{RootLabel(slot)}{(folder.Length > 0 ? $"/{folder}" : string.Empty)}";

    private string RootLabel(string slot) => slot switch
    {
        "secondary" => _runtime.ModelSecondaryLabel,
        "custom" => _customRoot,
        _ => _runtime.ModelPrimaryLabel
    };

    private static string ModelsPath(string target) =>
        Path.GetFileName(target).Equals("models", StringComparison.OrdinalIgnoreCase) ? target : Path.Combine(target, "models");

    private static List<string> Browse(string path)
    {
        IEnumerable<string> names;
        try
        {
            names = Directory.EnumerateDirectories(path)
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrWhiteSpace(name) && !name.StartsWith('.') && name != "models")
                .Select(name => name!);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw ApiException.BadRequest("Klasör okunamadı.");
        }

        var comparer = StringComparer.Create(CultureInfo.GetCultureInfo("tr-TR"), false);
        return names.OrderBy(name => name, comparer).Take(200).ToList();
    }

    private async Task<(ModelStorageSelection Selection, string CustomRoot)> LoadSelectionAsync(CancellationToken ct)
    {
        try
        {
            if (!File.Exists(SelectionPath)) return (_selection, string.Empty);
            var text = await File.ReadAllTextAsync(SelectionPath, ct);
            using var document = JsonDocument.Parse(text);
            var slot = document.RootElement.TryGetProperty("slot", out var slotElement) ? slotElement.GetString() ?? string.Empty : string.Empty;
            var folder = document.RootElement.TryGetProperty("folder", out var folderElement) ? folderElement.GetString() ?? string.Empty : string.Empty;
            var custom = document.RootElement.TryGetProperty("customPath", out var customElement) ? customElement.GetString() ?? string.Empty : string.Empty;
            if (!ValidSlot(slot)) return (_selection, string.Empty);

            var customRoot = string.Empty;
            if (!string.IsNullOrWhiteSpace(custom))
            {
                try
                {
                    var full = NormalizeAbsolutePath(custom);
                    if (Directory.Exists(full)) customRoot = full;
                }
                catch (ApiException)
                {
                    customRoot = string.Empty;
                }
            }

            if (slot == "custom")
            {
                if (customRoot.Length == 0) return (_selection, string.Empty);
                return (new ModelStorageSelection("custom", string.Empty, customRoot, customRoot), customRoot);
            }

            var relative = NormalizeFolder(folder);
            var path = LocationPath(RootPath(slot), relative);
            return (new ModelStorageSelection(slot, relative, Label(slot, relative), path), customRoot);
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException or ApiException)
        {
            return (_selection, string.Empty);
        }
    }

    private async Task<ModelDownloadJob> LoadJobAsync(CancellationToken ct)
    {
        try
        {
            if (!File.Exists(JobPath)) return IdleJob();
            var text = await File.ReadAllTextAsync(JobPath, ct);
            return JsonSerializer.Deserialize<ModelDownloadJob>(text, JsonOptions) ?? IdleJob();
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return IdleJob();
        }
    }

    private async Task SaveSelectionAsync(ModelStorageSelection selection, CancellationToken ct)
    {
        await WriteStateAsync(SelectionPath, new { slot = selection.Slot, folder = selection.Folder, customPath = _customRoot }, ct);
    }

    private async Task SaveJobAsync(ModelDownloadJob job, CancellationToken ct)
    {
        await WriteStateAsync(JobPath, job, ct);
    }

    private async Task WriteStateAsync(string path, object value, CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, JsonOptions), new UTF8Encoding(false), ct);
            RestrictFile(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger?.LogWarning(exception, "Model storage state could not be written to {Path}", path);
        }
    }

    private static void RestrictFile(string path)
    {
        if (OperatingSystem.IsWindows()) return;
        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static ModelDownloadJob IdleJob() => new("idle", string.Empty, 0, 0, string.Empty);

    public void Dispose()
    {
        CancellationTokenSource? source;
        lock (_state) source = _pullSource;
        source?.Cancel();
        try
        {
            _pullTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }
        _initialization.Dispose();
        _operations.Dispose();
    }
}
