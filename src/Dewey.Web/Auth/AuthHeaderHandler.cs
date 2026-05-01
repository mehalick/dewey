using System.Net.Http.Headers;

namespace Dewey.Web.Auth;

public sealed class AuthHeaderHandler : DelegatingHandler
{
    private readonly AuthSession _session;
    public AuthHeaderHandler(AuthSession session) => _session = session;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (_session.Tokens is { } t)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", t.IdToken);
        }
        return base.SendAsync(request, cancellationToken);
    }
}
