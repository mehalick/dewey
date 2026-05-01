using System.Net.Http.Json;
using System.Text.Json;

namespace Dewey.Web.Offline;

// Queue + flush of write operations (currently only POSTs of session logs).
// Flow: page enqueues an outbox entry; if online we attempt immediate flush;
// otherwise it sits in IDB until OnlineMonitor fires Changed(true) or until
// the next page calls TryFlushAsync.
public sealed class Outbox
{
    private readonly IndexedDb _idb;
    private readonly HttpClient _http;
    private readonly OnlineMonitor _online;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public event Action? Changed;
    public int PendingCount { get; private set; }

    public Outbox(IndexedDb idb, HttpClient http, OnlineMonitor online)
    {
        _idb = idb;
        _http = http;
        _online = online;
        _online.Changed += async _ => { await TryFlushAsync(); };
    }

    public async Task EnqueueAsync<T>(string url, T body)
    {
        var entry = new OutboxEntry
        {
            Id = Guid.NewGuid().ToString("N"),
            Url = url,
            Method = "POST",
            BodyJson = JsonSerializer.Serialize(body),
            QueuedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };
        await _idb.OutboxPutAsync(entry);
        await RefreshCountAsync();
        await TryFlushAsync();
    }

    public async Task TryFlushAsync()
    {
        if (!_online.IsOnline) { await RefreshCountAsync(); return; }
        if (!await _gate.WaitAsync(0)) return;
        try
        {
            var pending = await _idb.OutboxAllAsync();
            foreach (var entry in pending)
            {
                try
                {
                    using var content = new StringContent(entry.BodyJson, System.Text.Encoding.UTF8, "application/json");
                    var res = await _http.PostAsync(entry.Url, content);
                    // 2xx = success; 409 = duplicate (idempotent retry hit existing row) — drop it.
                    if (res.IsSuccessStatusCode || (int)res.StatusCode == 409)
                    {
                        await _idb.OutboxDeleteAsync(entry.Id);
                    }
                    // 4xx other than 409 = bad request, won't fix on retry. Drop.
                    else if ((int)res.StatusCode >= 400 && (int)res.StatusCode < 500)
                    {
                        await _idb.OutboxDeleteAsync(entry.Id);
                    }
                    // 5xx / network: leave queued for next attempt.
                    else { break; }
                }
                catch (HttpRequestException) { break; }
            }
            await RefreshCountAsync();
        }
        finally { _gate.Release(); }
    }

    public async Task RefreshCountAsync()
    {
        var pending = await _idb.OutboxAllAsync();
        PendingCount = pending.Length;
        Changed?.Invoke();
    }
}
