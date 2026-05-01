using Microsoft.JSInterop;

namespace Dewey.Web.Auth;

// In-memory + sessionStorage holder for the current user's tokens.
public sealed class AuthSession
{
    private const string StorageKey = "dewey.auth";
    private readonly IJSRuntime _js;
    private AuthTokens? _tokens;

    public AuthSession(IJSRuntime js) => _js = js;

    public AuthTokens? Tokens => _tokens;
    public bool IsSignedIn => _tokens is not null;
    public event Action? Changed;

    public async Task LoadAsync()
    {
        var json = await _js.InvokeAsync<string?>("sessionStorage.getItem", StorageKey);
        if (!string.IsNullOrEmpty(json))
        {
            _tokens = System.Text.Json.JsonSerializer.Deserialize(json, AuthJsonContext.Default.AuthTokens);
            Changed?.Invoke();
        }
    }

    public async Task SetAsync(AuthTokens tokens)
    {
        _tokens = tokens;
        var json = System.Text.Json.JsonSerializer.Serialize(tokens, AuthJsonContext.Default.AuthTokens);
        await _js.InvokeVoidAsync("sessionStorage.setItem", StorageKey, json);
        Changed?.Invoke();
    }

    public async Task ClearAsync()
    {
        _tokens = null;
        await _js.InvokeVoidAsync("sessionStorage.removeItem", StorageKey);
        Changed?.Invoke();
    }
}

public sealed record AuthTokens(string IdToken, string AccessToken, string RefreshToken, string Email);

[System.Text.Json.Serialization.JsonSerializable(typeof(AuthTokens))]
internal partial class AuthJsonContext : System.Text.Json.Serialization.JsonSerializerContext;
