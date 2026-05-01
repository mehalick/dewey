using System.Net.Http.Json;
using System.Text.Json;

namespace Dewey.Web.Offline;

// Network-first, cache-fallback for API GETs. Cached responses are stored as
// JSON strings keyed by the request URL; on success we refresh the entry.
public sealed class ApiCache
{
    private readonly HttpClient _http;
    private readonly IndexedDb _idb;

    public ApiCache(HttpClient http, IndexedDb idb)
    {
        _http = http;
        _idb = idb;
    }

    public async Task<T?> GetAsync<T>(string url, CancellationToken ct = default)
    {
        try
        {
            var fresh = await _http.GetFromJsonAsync<T>(url, ct);
            if (fresh is not null)
                await _idb.CachePutAsync(url, fresh);
            return fresh;
        }
        catch (HttpRequestException)
        {
            return await _idb.CacheGetAsync<T>(url);
        }
        catch (TaskCanceledException)
        {
            return await _idb.CacheGetAsync<T>(url);
        }
    }
}
