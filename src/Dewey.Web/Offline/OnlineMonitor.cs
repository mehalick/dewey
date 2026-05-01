using Microsoft.JSInterop;

namespace Dewey.Web.Offline;

// Bridges window online/offline events back into Blazor and exposes a Changed
// event for components/services to react.
public sealed class OnlineMonitor : IAsyncDisposable
{
    private readonly IndexedDb _idb;
    private DotNetObjectReference<OnlineMonitor>? _ref;
    public bool IsOnline { get; private set; } = true;
    public event Action<bool>? Changed;

    public OnlineMonitor(IndexedDb idb) => _idb = idb;

    public async Task InitAsync()
    {
        IsOnline = await _idb.IsOnlineAsync();
        _ref = DotNetObjectReference.Create(this);
        await _idb.RegisterOnlineListenerAsync(_ref);
    }

    [JSInvokable]
    public Task OnOnlineChanged(bool online)
    {
        IsOnline = online;
        Changed?.Invoke(online);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _ref?.Dispose();
        return ValueTask.CompletedTask;
    }
}
