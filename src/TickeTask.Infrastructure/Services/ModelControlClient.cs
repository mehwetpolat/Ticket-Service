using System.Net.Http.Json;
using System.Text.Json;
using System.Text;
using System.Text.Json.Nodes;
using TickeTask.Application.Abstractions;
using TickeTask.Application.Common;
using TickeTask.Application.Dtos;

namespace TickeTask.Infrastructure.Services;

public sealed class ModelControlClient(AppRuntime runtime) : IModelControlClient
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(12) };

    private Uri BaseUri => new(runtime.ModelControlUrl.TrimEnd('/') + "/");

    public Task<ModelStorageStatus?> GetStatusAsync(CancellationToken ct = default) =>
        GetAsync<ModelStorageStatus>("status", ct);

    public Task<ModelFolderListing?> GetFoldersAsync(string slot, string folder, CancellationToken ct = default) =>
        GetAsync<ModelFolderListing>($"folders?slot={Uri.EscapeDataString(slot)}&folder={Uri.EscapeDataString(folder)}", ct);

    public async Task<ModelFolderListing?> CreateFolderAsync(string slot, string folder, string name, CancellationToken ct = default) =>
        await PostAsync<ModelFolderListing>("folder", new { action = "folder", slot, folder, name }, ct);

    public async Task<ModelStorageSelection?> SelectAsync(string slot, string folder, CancellationToken ct = default)
    {
        var data = await PostAsync<JsonObject>("select", new { action = "select", slot, folder }, ct);
        return data?["selection"]?.Deserialize<ModelStorageSelection>(JsonDefaults.Options);
    }

    public async Task<ModelStorageSelection?> SelectPathAsync(string path, CancellationToken ct = default)
    {
        var data = await PostAsync<JsonObject>("select-path", new { action = "select-path", path }, ct);
        return data?["selection"]?.Deserialize<ModelStorageSelection>(JsonDefaults.Options);
    }

    public Task PullAsync(string model, CancellationToken ct = default) =>
        PostAsync<JsonObject>("pull", new { action = "pull", model }, ct);

    public Task CancelAsync(CancellationToken ct = default) =>
        PostAsync<JsonObject>("cancel", new { action = "cancel" }, ct);

    private async Task<T?> GetAsync<T>(string path, CancellationToken ct) where T : class
    {
        try
        {
            using var response = await Http.GetAsync(new Uri(BaseUri, path), ct);
            return await ReadAsync<T>(response, ct);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { throw Unavailable(); }
        catch (HttpRequestException) { throw Unavailable(); }
    }

    private async Task<T?> PostAsync<T>(string path, object body, CancellationToken ct) where T : class
    {
        try
        {
            using var content = new StringContent(JsonSerializer.Serialize(body, JsonDefaults.Options), Encoding.UTF8, "application/json");
            using var response = await Http.PostAsync(new Uri(BaseUri, path), content, ct);
            return await ReadAsync<T>(response, ct);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { throw Unavailable(); }
        catch (HttpRequestException) { throw Unavailable(); }
    }

    private static async Task<T?> ReadAsync<T>(HttpResponseMessage response, CancellationToken ct) where T : class
    {
        var text = await response.Content.ReadAsStringAsync(ct);
        JsonNode? data = null;
        try { data = text.Length > 0 ? JsonNode.Parse(text) : null; } catch { }
        if (!response.IsSuccessStatusCode)
        {
            var error = data?["error"]?.GetValue<string>() ?? "Model konumu yöneticisi isteği reddetti.";
            throw ApiException.BadRequest(error);
        }
        if (data is null) return null;
        return data.Deserialize<T>(JsonDefaults.Options);
    }

    private static ApiException Unavailable() =>
        ApiException.Unsupported("Model konumu yöneticisine ulaşılamıyor. Uygulamayı başlatma dosyasıyla yeniden açın.");
}

