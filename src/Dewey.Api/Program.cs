using System.Security.Claims;
using System.Text.Json.Serialization;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.AspNetCoreServer.Hosting;
using Amazon.Lambda.Serialization.SystemTextJson;
using Amazon.S3;
using Dewey.Api.Books;
using Dewey.Shared.Contracts;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, ApiJsonContext.Default);
});

var region = Environment.GetEnvironmentVariable("DEWEY_AWS_REGION") ?? "us-east-1";
var userPoolId = Environment.GetEnvironmentVariable("DEWEY_USER_POOL_ID")
    ?? throw new InvalidOperationException("DEWEY_USER_POOL_ID not set");
var userPoolClientId = Environment.GetEnvironmentVariable("DEWEY_USER_POOL_CLIENT_ID")
    ?? throw new InvalidOperationException("DEWEY_USER_POOL_CLIENT_ID not set");
var authority = $"https://cognito-idp.{region}.amazonaws.com/{userPoolId}";

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = authority;
        options.RequireHttpsMetadata = true;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = authority,
            ValidateAudience = true,
            ValidAudiences = [userPoolClientId],
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddSingleton<IAmazonDynamoDB>(_ => new AmazonDynamoDBClient());
builder.Services.AddSingleton<IAmazonS3>(_ => new AmazonS3Client());
builder.Services.AddSingleton<BookRepository>();
builder.Services.AddSingleton<SessionRepository>();
builder.Services.AddSingleton<CoverCache>();
builder.Services.AddHttpClient<GoogleBooksClient>();
builder.Services.AddHttpClient("covers");

builder.Services.AddAWSLambdaHosting(
    LambdaEventSource.HttpApi,
    new SourceGeneratorLambdaJsonSerializer<LambdaJsonContext>());

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/api/health", () => new HealthResponse("ok", "0.1.0"));

app.MapGet("/api/me", (ClaimsPrincipal user) =>
{
    var sub = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
    var email = user.FindFirstValue("email") ?? string.Empty;
    return Results.Ok(new MeResponse(sub ?? string.Empty, email));
}).RequireAuthorization();

var books = app.MapGroup("/api/books").RequireAuthorization();

books.MapGet("/search", async (string q, GoogleBooksClient gb, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(q)) return Results.Ok(Array.Empty<BookSearchResult>());
    var results = await gb.SearchAsync(q, max: 10, ct);
    return Results.Ok(results);
});

books.MapGet("/", async (ClaimsPrincipal user, BookRepository repo, CancellationToken ct) =>
{
    var userId = user.FindFirstValue("sub")!;
    var list = await repo.ListAsync(userId, ct);
    return Results.Ok(list);
});

books.MapPost("/", async (
    AddBookRequest req,
    ClaimsPrincipal user,
    GoogleBooksClient gb,
    CoverCache covers,
    BookRepository repo,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(req.GoogleVolumeId))
        return Results.BadRequest("googleVolumeId required");

    var volume = await gb.GetVolumeAsync(req.GoogleVolumeId, ct);
    if (volume is null) return Results.NotFound();

    var sr = GoogleBooksClient.ToResult(volume);
    var cachedCover = sr.CoverUrl is null ? null
        : await covers.EnsureCachedAsync(sr.GoogleVolumeId, sr.CoverUrl, ct);

    var userId = user.FindFirstValue("sub")!;
    var book = new BookSummary(
        BookId: Guid.NewGuid().ToString("N"),
        GoogleVolumeId: sr.GoogleVolumeId,
        Title: sr.Title,
        Authors: sr.Authors,
        PageCount: sr.PageCount,
        CoverUrl: cachedCover ?? sr.CoverUrl,
        AddedAt: DateTimeOffset.UtcNow.ToString("O"),
        LatestProgressPct: 0,
        LatestSessionAt: null,
        Status: "not_started");

    await repo.AddAsync(userId, book, ct);
    return Results.Created($"/api/books/{book.BookId}", book);
});

books.MapGet("/{bookId}", async (
    string bookId,
    ClaimsPrincipal user,
    BookRepository repo,
    CancellationToken ct) =>
{
    var userId = user.FindFirstValue("sub")!;
    var book = await repo.GetAsync(userId, bookId, ct);
    return book is null ? Results.NotFound() : Results.Ok(book);
});

books.MapDelete("/{bookId}", async (
    string bookId,
    ClaimsPrincipal user,
    BookRepository repo,
    CancellationToken ct) =>
{
    var userId = user.FindFirstValue("sub")!;
    var deleted = await repo.DeleteAsync(userId, bookId, ct);
    return deleted ? Results.NoContent() : Results.NotFound();
});

books.MapPost("/{bookId}/sessions", async (
    string bookId,
    LogSessionRequest req,
    ClaimsPrincipal user,
    SessionRepository repo,
    CancellationToken ct) =>
{
    var userId = user.FindFirstValue("sub")!;
    try
    {
        await repo.LogAsync(userId, bookId, req, ct);
        return Results.NoContent();
    }
    catch (ArgumentException ex) { return Results.BadRequest(ex.Message); }
    catch (TransactionCanceledException) { return Results.Conflict(); }
});

books.MapGet("/{bookId}/sessions", async (
    string bookId,
    ClaimsPrincipal user,
    SessionRepository repo,
    CancellationToken ct) =>
{
    var userId = user.FindFirstValue("sub")!;
    var events = await repo.ListAsync(userId, bookId, ct);
    return Results.Ok(events);
});

app.Run();

[JsonSerializable(typeof(HealthResponse))]
[JsonSerializable(typeof(MeResponse))]
[JsonSerializable(typeof(BookSearchResult))]
[JsonSerializable(typeof(BookSearchResult[]))]
[JsonSerializable(typeof(BookSummary))]
[JsonSerializable(typeof(BookSummary[]))]
[JsonSerializable(typeof(AddBookRequest))]
[JsonSerializable(typeof(LogSessionRequest))]
[JsonSerializable(typeof(SessionEvent))]
[JsonSerializable(typeof(SessionEvent[]))]
internal partial class ApiJsonContext : JsonSerializerContext;

[JsonSerializable(typeof(APIGatewayHttpApiV2ProxyRequest))]
[JsonSerializable(typeof(APIGatewayHttpApiV2ProxyResponse))]
internal partial class LambdaJsonContext : JsonSerializerContext;
