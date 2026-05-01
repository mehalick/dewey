using Microsoft.JSInterop;

namespace Dewey.Web.Offline;

// Thin wrapper around wwwroot/js/idb.js. Loads the module lazily.
public sealed class IndexedDb : IAsyncDisposable
{
    private readonly Lazy<Task<IJSObjectReference>> _module;

    public IndexedDb(IJSRuntime js)
    {
        _module = new(() => js.InvokeAsync<IJSObjectReference>("import", "./js/idb.js").AsTask());
    }

    public async Task<bool> IsOnlineAsync()
        => await (await _module.Value).InvokeAsync<bool>("isOnline");

    public async Task RegisterOnlineListenerAsync(DotNetObjectReference<OnlineMonitor> reference)
        => await (await _module.Value).InvokeVoidAsync("registerOnlineListener", reference);

    public async Task OutboxPutAsync(OutboxEntry entry)
        => await (await _module.Value).InvokeVoidAsync("outboxPut", entry);

    public async Task<OutboxEntry[]> OutboxAllAsync()
        => await (await _module.Value).InvokeAsync<OutboxEntry[]>("outboxAll");

    public async Task OutboxDeleteAsync(string id)
        => await (await _module.Value).InvokeVoidAsync("outboxDelete", id);

    public async Task CachePutAsync<T>(string url, T value)
        => await (await _module.Value).InvokeVoidAsync("cachePut", url, value);

    public async Task<T?> CacheGetAsync<T>(string url)
        => await (await _module.Value).InvokeAsync<T?>("cacheGet", url);

    public async ValueTask DisposeAsync()
    {
        if (_module.IsValueCreated)
        {
            var m = await _module.Value;
            await m.DisposeAsync();
        }
    }
}

public sealed class OutboxEntry
{
    public string Id { get; set; } = "";
    public string Url { get; set; } = "";
    public string Method { get; set; } = "POST";
    public string BodyJson { get; set; } = "";
    public long QueuedAt { get; set; }
}
