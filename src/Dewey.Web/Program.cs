using Dewey.Web;
using Dewey.Web.Auth;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var cognito = new CognitoOptions();
builder.Configuration.GetSection("Cognito").Bind(cognito);
builder.Services.AddSingleton(cognito);

builder.Services.AddScoped<AuthSession>();
builder.Services.AddTransient<AuthHeaderHandler>();

// Plain HttpClient for Cognito (no auth header).
builder.Services.AddHttpClient<CognitoClient>();

// API HttpClient: same-origin via CloudFront /api/*, with bearer header injected.
builder.Services.AddHttpClient("api", c =>
{
    c.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress);
}).AddHttpMessageHandler<AuthHeaderHandler>();
builder.Services.AddScoped(sp => sp.GetRequiredService<IHttpClientFactory>().CreateClient("api"));

var host = builder.Build();

// Hydrate auth state from sessionStorage on startup.
var session = host.Services.GetRequiredService<AuthSession>();
await session.LoadAsync();

await host.RunAsync();
