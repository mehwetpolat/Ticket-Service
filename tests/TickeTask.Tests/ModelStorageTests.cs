using System.Net;
using System.Net.Sockets;
using System.Text;
using TickeTask.Application.Abstractions;
using TickeTask.Application.Common;
using TickeTask.Infrastructure.Services;

namespace TickeTask.Tests;

public sealed class ModelStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ticketask-model-tests", Guid.NewGuid().ToString("N"));
    private readonly AppRuntime _runtime;
    private readonly LocalModelStorageService _storage;

    public ModelStorageTests()
    {
        _runtime = TestRuntime.Create();
        _runtime.ModelConfigDirectory = Path.Combine(_root, "config");
        _runtime.ModelPrimaryDirectory = Path.Combine(_root, "primary");
        _runtime.ModelSecondaryDirectory = Path.Combine(_root, "secondary");
        _runtime.ModelPrimaryLabel = "Birinci konum";
        _runtime.ModelSecondaryLabel = "İkinci konum";
        _storage = new LocalModelStorageService(_runtime);
    }

    [Fact]
    public async Task Reports_roots_and_default_selection()
    {
        var status = await _storage.GetStatusAsync();

        Assert.NotNull(status);
        Assert.Equal("Birinci konum", status.Roots.Primary);
        Assert.Equal("İkinci konum", status.Roots.Secondary);
        Assert.Equal("primary", status.Selection.Slot);
        Assert.Equal("Birinci konum", status.Selection.Label);
        Assert.Equal("idle", status.Job.State);
    }

    [Fact]
    public async Task Replaces_empty_ollama_models_directory_with_a_link()
    {
        Directory.CreateDirectory(_runtime.ModelConfigDirectory);
        Directory.CreateDirectory(_runtime.ModelPrimaryDirectory);
        Directory.CreateDirectory(_runtime.ModelSecondaryDirectory);
        var link = Path.Combine(_runtime.ModelConfigDirectory, "models");
        Directory.CreateDirectory(link);

        var status = await _storage.GetStatusAsync();

        Assert.NotNull(status);
        Assert.True(Directory.Exists(Path.Combine(_runtime.ModelPrimaryDirectory, "models")));
        if (Directory.Exists(link)) Assert.True(IsLink(link));
    }

    [Fact]
    public async Task Repairs_a_broken_ollama_models_link()
    {
        Directory.CreateDirectory(_runtime.ModelConfigDirectory);
        Directory.CreateDirectory(_runtime.ModelPrimaryDirectory);
        Directory.CreateDirectory(_runtime.ModelSecondaryDirectory);
        var link = Path.Combine(_runtime.ModelConfigDirectory, "models");
        try
        {
            Directory.CreateSymbolicLink(link, Path.Combine(_root, "missing", "models"));
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            // Windows without symlink privilege cannot create the precondition.
            return;
        }

        var status = await _storage.GetStatusAsync();

        Assert.NotNull(status);
        Assert.True(Directory.Exists(Path.Combine(_runtime.ModelPrimaryDirectory, "models")));
        Assert.True(IsLink(link));
        Assert.True(Directory.Exists(link));
    }

    private static bool IsLink(string path)
    {
        try
        {
            return new DirectoryInfo(path).LinkTarget is not null ||
                   (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    [Fact]
    public async Task Creates_and_selects_folder_and_persists_selection()
    {
        var listing = await _storage.CreateFolderAsync("primary", "", "Yeni Klasör");
        Assert.NotNull(listing);
        Assert.Contains("Yeni Klasör", listing.Directories);

        var selection = await _storage.SelectAsync("primary", "Yeni Klasör");
        Assert.NotNull(selection);
        Assert.Equal("Yeni Klasör", selection.Folder);
        Assert.Equal("Birinci konum/Yeni Klasör", selection.Label);

        using var reloaded = new LocalModelStorageService(_runtime);
        var status = await reloaded.GetStatusAsync();
        Assert.Equal("Yeni Klasör", status!.Selection.Folder);
        Assert.Equal("Birinci konum/Yeni Klasör", status.Selection.Label);
        Assert.True(Directory.Exists(Path.Combine(_runtime.ModelPrimaryDirectory, "Yeni Klasör", "models")));
    }

    [Fact]
    public async Task Lists_nested_folders_and_rejects_traversal()
    {
        await _storage.CreateFolderAsync("primary", "", "Parent");

        var error = await Assert.ThrowsAsync<ApiException>(() => _storage.GetFoldersAsync("primary", "../outside"));
        Assert.Equal(400, error.StatusCode);

        var invalidName = await Assert.ThrowsAsync<ApiException>(() => _storage.CreateFolderAsync("primary", "", "../escape"));
        Assert.Equal(400, invalidName.StatusCode);

        var missing = await Assert.ThrowsAsync<ApiException>(() => _storage.SelectAsync("primary", "missing"));
        Assert.Equal(400, missing.StatusCode);
    }

    [Fact]
    public async Task Selects_an_exact_absolute_path_outside_configured_roots()
    {
        var custom = Path.Combine(_root, "exact", "model-home");
        Directory.CreateDirectory(custom);

        var selection = await _storage.SelectPathAsync(custom);

        Assert.NotNull(selection);
        Assert.Equal("custom", selection.Slot);
        Assert.Equal(custom, selection.Path);
        Assert.True(Directory.Exists(Path.Combine(custom, "models")));

        using var reloaded = new LocalModelStorageService(_runtime);
        var status = await reloaded.GetStatusAsync();
        Assert.NotNull(status);
        Assert.Equal("custom", status.Selection.Slot);
        Assert.Equal(custom, status.Selection.Path);
        Assert.Equal(custom, status.Roots.Custom);
    }

    [Fact]
    public async Task Absolute_path_inside_a_root_selects_the_relative_folder()
    {
        await _storage.CreateFolderAsync("secondary", "", "Tam Yol");
        var full = Path.Combine(_runtime.ModelSecondaryDirectory, "Tam Yol");

        var selection = await _storage.SelectPathAsync(full);

        Assert.NotNull(selection);
        Assert.Equal("secondary", selection.Slot);
        Assert.Equal("Tam Yol", selection.Folder);
        Assert.Equal(full, selection.Path);
    }

    [Fact]
    public async Task Maps_a_configured_host_path_to_the_model_root()
    {
        await _storage.CreateFolderAsync("primary", "", "Host Alt");
        _runtime.ModelPrimaryHostPath = "D:/ticketask-host-models";

        var selection = await _storage.SelectPathAsync("D:/ticketask-host-models/Host Alt");

        Assert.NotNull(selection);
        Assert.Equal("primary", selection.Slot);
        Assert.Equal("Host Alt", selection.Folder);
        Assert.Equal(Path.Combine(_runtime.ModelPrimaryDirectory, "Host Alt"), selection.Path);
    }

    [Fact]
    public async Task Rejects_relative_exact_paths()
    {
        var error = await Assert.ThrowsAsync<ApiException>(() => _storage.SelectPathAsync("models/alt"));

        Assert.Equal(400, error.StatusCode);
    }

    [Fact]
    public async Task Rejects_missing_exact_paths()
    {
        var error = await Assert.ThrowsAsync<ApiException>(() =>
            _storage.SelectPathAsync(Path.Combine(_root, "olmayan", "klasor")));

        Assert.Equal(400, error.StatusCode);
    }

    [Fact]
    public async Task Rejects_invalid_model_names_before_starting_download()
    {
        var error = await Assert.ThrowsAsync<ApiException>(() => _storage.PullAsync("../model"));

        Assert.Equal(400, error.StatusCode);
        var job = (await _storage.GetStatusAsync())!.Job;
        Assert.Equal("idle", job.State);
    }

    [Fact]
    public async Task Pulls_model_through_ollama_and_completes_job()
    {
        using var ollama = new FakeOllamaServer("qwen3:4b");
        var runtime = new AppRuntime
        {
            ModelConfigDirectory = Path.Combine(_root, "pull", "config"),
            ModelPrimaryDirectory = Path.Combine(_root, "pull", "primary"),
            ModelSecondaryDirectory = Path.Combine(_root, "pull", "secondary"),
            ModelPrimaryLabel = "Birinci konum",
            ModelSecondaryLabel = "İkinci konum",
            ModelOllamaUrl = ollama.Url
        };
        using var storage = new LocalModelStorageService(runtime);

        await storage.PullAsync("qwen3:4b");
        var status = await storage.GetStatusAsync();
        for (var attempt = 0; attempt < 100 && status!.Job.State == "running"; attempt++)
        {
            await Task.Delay(50);
            status = await storage.GetStatusAsync();
        }

        Assert.NotNull(status);
        Assert.Equal("done", status.Job.State);
        Assert.Equal(100, status.Job.Progress);
        Assert.Equal(1, ollama.PullRequests);
    }

    public void Dispose()
    {
        _storage.Dispose();
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed class FakeOllamaServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stopping = new();
        private readonly Task _acceptLoop;
        private readonly string _model;
        private volatile bool _pulled;

        public FakeOllamaServer(string model)
        {
            _model = model;
            _listener.Start();
            Url = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}";
            _acceptLoop = Task.Run(AcceptLoopAsync);
        }

        public string Url { get; }
        public int PullRequests { get; private set; }

        private async Task AcceptLoopAsync()
        {
            while (!_stopping.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(_stopping.Token);
                }
                catch (Exception exception) when (exception is OperationCanceledException or SocketException or ObjectDisposedException)
                {
                    return;
                }
                _ = Task.Run(() => HandleAsync(client));
            }
        }

        private async Task HandleAsync(TcpClient client)
        {
            using (client)
            using (var stream = client.GetStream())
            using (var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true))
            {
                var request = await reader.ReadLineAsync();
                if (request is null) return;
                var length = 0;
                while (true)
                {
                    var header = await reader.ReadLineAsync();
                    if (string.IsNullOrEmpty(header)) break;
                    if (header.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                        int.TryParse(header["Content-Length:".Length..].Trim(), out length);
                }
                if (length > 0)
                {
                    var buffer = new char[length];
                    await reader.ReadBlockAsync(buffer, 0, length);
                }

                var path = request.Split(' ').ElementAtOrDefault(1) ?? string.Empty;
                string body;
                if (path.StartsWith("/api/pull", StringComparison.Ordinal))
                {
                    PullRequests++;
                    _pulled = true;
                    body = "{\"status\":\"pulling\"}\n{\"status\":\"success\"}\n";
                }
                else if (path.StartsWith("/api/tags", StringComparison.Ordinal))
                {
                    body = _pulled
                        ? $"{{\"models\":[{{\"name\":\"{_model}\"}}]}}"
                        : "{\"models\":[]}";
                }
                else
                {
                    body = "{\"error\":\"not found\"}";
                }

                var payload = Encoding.UTF8.GetBytes(body);
                var headers = Encoding.ASCII.GetBytes(
                    $"HTTP/1.1 200 OK\r\nContent-Type: application/x-ndjson\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(headers);
                await stream.WriteAsync(payload);
                await stream.FlushAsync();
            }
        }

        public void Dispose()
        {
            _stopping.Cancel();
            _listener.Stop();
            try
            {
                _acceptLoop.Wait(TimeSpan.FromSeconds(2));
            }
            catch (AggregateException)
            {
            }
            _stopping.Dispose();
        }
    }
}
