using Dewey.Web;
using Dewey.Web.Auth;
using Dewey.Web.Offline;
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

builder.Services.AddHttpClient<CognitoClient>();

builder.Services.AddHttpClient("api", c =>
{
    c.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress);
}).AddHttpMessageHandler<AuthHeaderHandler>();
builder.Services.AddScoped(sp => sp.GetRequiredService<IHttpClientFactory>().CreateClient("api"));

builder.Services.AddScoped<IndexedDb>();
builder.Services.AddScoped<OnlineMonitor>();
builder.Services.AddScoped<Outbox>();
builder.Services.AddScoped<ApiCache>();

var host = builder.Build();

var session = host.Services.GetRequiredService<AuthSession>();
await session.LoadAsync();

var monitor = host.Services.GetRequiredService<OnlineMonitor>();
await monitor.InitAsync();

var outbox = host.Services.GetRequiredService<Outbox>();
await outbox.TryFlushAsync();

await host.RunAsync();
