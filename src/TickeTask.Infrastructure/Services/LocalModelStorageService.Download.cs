using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using TickeTask.Application.Dtos;

namespace TickeTask.Infrastructure.Services;

public sealed partial class LocalModelStorageService
{
    private static readonly HttpClient ModelHttp = new(new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(15) })
    {
        Timeout = Timeout.InfiniteTimeSpan
    };

    private static readonly Regex DirectDownloadPattern = new(@"^qwen3:(0\.6b|1\.7b|4b|8b)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex DigestPattern = new(@"^sha256:[a-f0-9]{64}$", RegexOptions.CultureInvariant);

    private async Task RunPullAsync(string model, CancellationTokenSource source)
    {
        var token = source.Token;
        try
        {
            for (var attempt = 1; attempt <= 5; attempt++)
            {
                lock (_state) _job = _job with { Attempt = attempt };
                await SaveJobSnapshotAsync();

                try
                {
                    await PullOnceAsync(model, token);
                    if (!await InstalledAsync(model, token))
                        throw new InvalidOperationException("Dosyalar aktarıldı ancak model doğrulanamadı.");
                    await CompleteJobAsync("done", 100, "Model hazır");
                    return;
                }
                catch (Exception exception)
                {
                    if (token.IsCancellationRequested)
                    {
                        await CompleteJobAsync("cancelled", CurrentProgress(), "İndirme iptal edildi");
                        return;
                    }

                    try
                    {
                        if (await InstalledAsync(model, token))
                        {
                            await CompleteJobAsync("done", 100, "Model hazır");
                            return;
                        }
                    }
                    catch (Exception) when (!token.IsCancellationRequested)
                    {
                    }

                    var detail = exception.Message;
                    if (DirectDownloadPattern.IsMatch(model) && IsInterruptedTransfer(detail))
                    {
                        await SetJobMessageAsync("Standart bağlantı kesildi; alternatif indirmeye geçiliyor");
                        try
                        {
                            await DownloadDirectAsync(model, token);
                            await CompleteJobAsync("done", 100, "Model hazır");
                            return;
                        }
                        catch (Exception fallback)
                        {
                            var fallbackDetail = fallback is OperationCanceledException && token.IsCancellationRequested
                                ? "İndirme iptal edildi."
                                : fallback.Message;
                            await CompleteJobAsync(token.IsCancellationRequested ? "cancelled" : "error", CurrentProgress(),
                                token.IsCancellationRequested ? "İndirme iptal edildi" : $"Model indirilemedi: {fallbackDetail}");
                            return;
                        }
                    }

                    if (attempt == 5 || detail.Contains("404", StringComparison.Ordinal))
                    {
                        await CompleteJobAsync("error", CurrentProgress(),
                            $"Model indirilemedi ({detail}). Ağ veya proxy bağlantısını kontrol edin.");
                        return;
                    }

                    await SetJobMessageAsync($"Bağlantı kesildi; yeniden deneniyor ({attempt}/5)");
                    try
                    {
                        await Task.Delay(attempt * 3000, token);
                    }
                    catch (OperationCanceledException)
                    {
                        await CompleteJobAsync("cancelled", CurrentProgress(), "İndirme iptal edildi");
                        return;
                    }
                }
            }
        }
        finally
        {
            lock (_state)
            {
                if (ReferenceEquals(_pullSource, source))
                {
                    _pullSource = null;
                    _pullTask = null;
                }
            }
        }
    }

    private async Task PullOnceAsync(string model, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, OllamaUri("/api/pull"))
        {
            Content = JsonContent.Create(new { model, stream = true })
        };
        HttpResponseMessage response;
        try
        {
            response = await ModelHttp.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        }
        catch (HttpRequestException)
        {
            throw new InvalidOperationException("Model sunucusuna ulaşılamıyor.");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Model sunucusu indirmeyi başlatamadı ({(int)response.StatusCode}).");

            await using var stream = await response.Content.ReadAsStreamAsync(token);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var success = false;
            while (true)
            {
                var line = await reader.ReadLineAsync(token);
                if (line is null) break;
                if (string.IsNullOrWhiteSpace(line)) continue;

                JsonDocument document;
                try
                {
                    document = JsonDocument.Parse(line);
                }
                catch (JsonException)
                {
                    continue;
                }

                using (document)
                {
                    var root = document.RootElement;
                    if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
                        throw new InvalidOperationException(error.GetString() ?? "Model indirilemedi.");
                    var message = root.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.String
                        ? status.GetString()
                        : null;
                    var total = root.TryGetProperty("total", out var totalElement) && totalElement.TryGetInt64(out var totalValue) ? totalValue : 0;
                    var completed = root.TryGetProperty("completed", out var completedElement) && completedElement.TryGetInt64(out var completedValue) ? completedValue : 0;
                    var progress = total > 0 ? Math.Clamp((int)Math.Round(completed * 100d / total), 0, 99) : 0;
                    lock (_state)
                    {
                        _job = _job with
                        {
                            Message = string.IsNullOrWhiteSpace(message) ? _job.Message : message!,
                            Progress = total > 0 ? progress : _job.Progress
                        };
                    }
                    if (string.Equals(message, "success", StringComparison.OrdinalIgnoreCase)) success = true;
                }
            }

            if (!success) throw new InvalidOperationException("İndirme bağlantısı tamamlanmadan kapandı.");
        }
    }

    private async Task DownloadDirectAsync(string model, CancellationToken token)
    {
        if (!DirectDownloadPattern.IsMatch(model)) throw new InvalidOperationException("Bu model için alternatif indirme desteklenmiyor.");
        var tag = model.Split(':', 2)[1];
        var registry = (Environment.GetEnvironmentVariable("MODEL_REGISTRY_URL") ?? "https://registry.ollama.ai/v2/library/qwen3").TrimEnd('/');

        string manifestText;
        try
        {
            using var response = await ModelHttp.GetAsync($"{registry}/manifests/{tag}", token);
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"Model bilgisi alınamadı ({(int)response.StatusCode}).");
            manifestText = await response.Content.ReadAsStringAsync(token);
        }
        catch (HttpRequestException)
        {
            throw new InvalidOperationException("Model kayıt sunucusuna ulaşılamıyor.");
        }

        var descriptors = ReadManifest(manifestText);
        long total;
        try
        {
            total = descriptors.Aggregate(0L, (sum, item) => checked(sum + item.Size));
        }
        catch (OverflowException)
        {
            throw new InvalidOperationException("Model dosyası listesi geçersiz.");
        }

        var selection = CurrentSelection();
        var target = ExistingFolder(selection.Slot, selection.Folder);
        var root = ModelsPath(target.Full);
        var blobDirectory = Path.Combine(root, "blobs");
        Directory.CreateDirectory(blobDirectory);

        var completed = 0L;
        await SetJobMessageAsync("Alternatif bağlantıyla indiriliyor");
        foreach (var descriptor in descriptors)
        {
            token.ThrowIfCancellationRequested();
            var digest = descriptor.Digest[7..];
            var finalPath = Path.Combine(blobDirectory, $"sha256-{digest}");
            var partialPath = $"{finalPath}-partial";

            if (await HashMatchesAsync(finalPath, digest, token))
            {
                completed += descriptor.Size;
                continue;
            }

            var downloadedBefore = completed;
            await DownloadBlobAsync(registry, descriptor, partialPath, bytes =>
            {
                var current = downloadedBefore + bytes;
                var progress = total > 0 ? Math.Min(99, (int)Math.Floor(current * 100d / total)) : 0;
                lock (_state) _job = _job with { Progress = progress, Message = "Alternatif bağlantıyla indiriliyor" };
            }, token);

            lock (_state) _job = _job with { Message = "Dosya bütünlüğü doğrulanıyor" };
            if (!await HashMatchesAsync(partialPath, digest, token))
                throw new InvalidOperationException("İndirilen dosyanın doğrulaması başarısız oldu.");
            File.Move(partialPath, finalPath, true);
            completed += descriptor.Size;
        }

        var manifestPath = Path.Combine(root, "manifests", "registry.ollama.ai", "library", "qwen3", tag);
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        await File.WriteAllTextAsync(manifestPath, manifestText, new UTF8Encoding(false), token);
        if (!await InstalledAsync(model, token))
            throw new InvalidOperationException("Dosyalar hazır ancak model sunucusu modeli göremiyor.");
    }

    private async Task DownloadBlobAsync(string registry, BlobDescriptor descriptor, string partialPath, Action<long> report, CancellationToken token)
    {
        const int chunkSize = 8 * 1024 * 1024;
        var size = descriptor.Size;
        await using (var allocation = new FileStream(partialPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite))
        {
            allocation.SetLength(size);
        }

        using var handle = File.OpenHandle(partialPath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
        var count = size == 0 ? 0 : (size + chunkSize - 1) / chunkSize;
        var next = -1L;
        var downloaded = 0L;

        async Task WorkerAsync()
        {
            while (true)
            {
                var index = Interlocked.Increment(ref next);
                if (index >= count) return;
                var start = index * chunkSize;
                var end = Math.Min(size - 1, start + chunkSize - 1);
                var expected = checked((int)(end - start + 1));
                Exception? failure = null;

                for (var retry = 0; retry < 4; retry++)
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        using var request = new HttpRequestMessage(HttpMethod.Get, $"{registry}/blobs/{descriptor.Digest}");
                        request.Headers.Range = new RangeHeaderValue(start, end);
                        using var response = await ModelHttp.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
                        if (response.StatusCode != HttpStatusCode.PartialContent)
                            throw new InvalidOperationException($"Dosya sunucusu {(int)response.StatusCode} yanıtı verdi.");
                        var bytes = await response.Content.ReadAsByteArrayAsync(token);
                        if (bytes.Length != expected) throw new InvalidOperationException("Eksik dosya parçası alındı.");
                        await RandomAccess.WriteAsync(handle, bytes, start, token);
                        report(Interlocked.Add(ref downloaded, bytes.Length));
                        failure = null;
                        break;
                    }
                    catch (Exception exception) when (!token.IsCancellationRequested)
                    {
                        failure = exception;
                        await Task.Delay(500 * (retry + 1), token);
                    }
                }

                if (failure is not null) throw failure;
            }
        }

        var workers = Enumerable.Range(0, (int)Math.Min(8, Math.Max(1, count))).Select(_ => WorkerAsync()).ToArray();
        await Task.WhenAll(workers);
    }

    private static List<BlobDescriptor> ReadManifest(string manifestText)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(manifestText);
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("Model dosyası listesi geçersiz.");
        }

        using (document)
        {
            var root = document.RootElement;
            if (!root.TryGetProperty("config", out var config) || config.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("layers", out var layers) || layers.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException("Model dosyası listesi geçersiz.");

            var descriptors = new List<BlobDescriptor>();
            AddDescriptor(config, descriptors);
            foreach (var layer in layers.EnumerateArray())
            {
                if (layer.ValueKind != JsonValueKind.Object) throw new InvalidOperationException("Model dosyası listesi geçersiz.");
                AddDescriptor(layer, descriptors);
            }
            return descriptors;
        }
    }

    private static void AddDescriptor(JsonElement element, List<BlobDescriptor> descriptors)
    {
        var digest = element.TryGetProperty("digest", out var digestElement) ? digestElement.GetString() ?? string.Empty : string.Empty;
        if (!element.TryGetProperty("size", out var sizeElement) || !sizeElement.TryGetInt64(out var size) || size < 0 || !DigestPattern.IsMatch(digest))
            throw new InvalidOperationException("Model dosyası listesi geçersiz.");
        descriptors.Add(new BlobDescriptor(digest, size));
    }

    private static async Task<bool> HashMatchesAsync(string path, string digest, CancellationToken token)
    {
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var hash = await SHA256.HashDataAsync(stream, token);
            return Convert.ToHexString(hash).Equals(digest, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private async Task<bool> InstalledAsync(string model, CancellationToken token)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            using var response = await ModelHttp.GetAsync(OllamaUri("/api/tags"), timeout.Token);
            if (!response.IsSuccessStatusCode) return false;
            var text = await response.Content.ReadAsStringAsync(timeout.Token);
            using var document = JsonDocument.Parse(text);
            if (!document.RootElement.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Array) return false;
            return models.EnumerateArray().Any(item =>
                item.TryGetProperty("name", out var name) &&
                string.Equals(name.GetString(), model, StringComparison.Ordinal));
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or InvalidOperationException)
        {
            return false;
        }
    }

    private Uri OllamaUri(string path)
    {
        var value = string.IsNullOrWhiteSpace(_runtime.ModelOllamaUrl) ? "http://127.0.0.1:11434" : _runtime.ModelOllamaUrl;
        if (!Uri.TryCreate(value.TrimEnd('/') + "/", UriKind.Absolute, out var baseUri))
            throw new InvalidOperationException("Model sunucusu adresi geçersiz.");
        return new Uri(baseUri, path.TrimStart('/'));
    }

    private ModelStorageSelection CurrentSelection()
    {
        lock (_state) return _selection;
    }

    private int CurrentProgress()
    {
        lock (_state) return _job.Progress;
    }

    private async Task SetJobMessageAsync(string message)
    {
        lock (_state) _job = _job with { Message = message };
        await SaveJobSnapshotAsync();
    }

    private async Task CompleteJobAsync(string state, int progress, string message)
    {
        lock (_state) _job = _job with { State = state, Progress = progress, Message = message };
        await SaveJobSnapshotAsync();
    }

    private async Task SaveJobSnapshotAsync()
    {
        ModelDownloadJob snapshot;
        lock (_state) snapshot = _job;
        await SaveJobAsync(snapshot, CancellationToken.None);
    }

    private static bool IsInterruptedTransfer(string message) =>
        Regex.IsMatch(message, @"\bEOF\b|prematurely|terminated|connection reset|unexpected end|tamamlanmadan kapandı", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly record struct BlobDescriptor(string Digest, long Size);
}
