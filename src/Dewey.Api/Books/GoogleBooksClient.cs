using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Dewey.Shared.Contracts;

namespace Dewey.Api.Books;

public sealed class GoogleBooksClient
{
    private readonly HttpClient _http;
    private readonly string? _apiKey;

    public GoogleBooksClient(HttpClient http, IConfiguration config)
    {
        _http = http;
        _http.BaseAddress = new Uri("https://www.googleapis.com/books/v1/");
        _apiKey = Environment.GetEnvironmentVariable("DEWEY_GOOGLE_BOOKS_KEY");
    }

    public async Task<BookSearchResult[]> SearchAsync(string query, int max = 10, CancellationToken ct = default)
    {
        var url = $"volumes?q={Uri.EscapeDataString(query)}&maxResults={max}"
                  + (string.IsNullOrEmpty(_apiKey) ? "" : $"&key={_apiKey}");
        var res = await _http.GetFromJsonAsync(url, GoogleBooksJsonContext.Default.VolumesResponse, ct);
        if (res?.Items is null) return [];
        return Array.ConvertAll(res.Items, ToResult);
    }

    public async Task<GoogleVolume?> GetVolumeAsync(string volumeId, CancellationToken ct = default)
    {
        var url = $"volumes/{Uri.EscapeDataString(volumeId)}"
                  + (string.IsNullOrEmpty(_apiKey) ? "" : $"?key={_apiKey}");
        return await _http.GetFromJsonAsync(url, GoogleBooksJsonContext.Default.GoogleVolume, ct);
    }

    public static BookSearchResult ToResult(GoogleVolume v)
    {
        var info = v.VolumeInfo ?? new VolumeInfo();
        var thumb = info.ImageLinks?.Thumbnail
                  ?? info.ImageLinks?.SmallThumbnail;
        // Force https — Google sometimes returns http URLs.
        if (thumb is not null && thumb.StartsWith("http://", StringComparison.Ordinal))
            thumb = "https://" + thumb["http://".Length..];
        return new BookSearchResult(
            GoogleVolumeId: v.Id ?? string.Empty,
            Title: info.Title ?? "(untitled)",
            Authors: info.Authors ?? [],
            PageCount: info.PageCount,
            CoverUrl: thumb,
            Description: info.Description);
    }
}

public sealed class VolumesResponse
{
    [JsonPropertyName("items")] public GoogleVolume[]? Items { get; set; }
}

public sealed class GoogleVolume
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("volumeInfo")] public VolumeInfo? VolumeInfo { get; set; }
}

public sealed class VolumeInfo
{
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("authors")] public string[]? Authors { get; set; }
    [JsonPropertyName("pageCount")] public int? PageCount { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("imageLinks")] public ImageLinks? ImageLinks { get; set; }
}

public sealed class ImageLinks
{
    [JsonPropertyName("smallThumbnail")] public string? SmallThumbnail { get; set; }
    [JsonPropertyName("thumbnail")] public string? Thumbnail { get; set; }
}

[JsonSerializable(typeof(VolumesResponse))]
[JsonSerializable(typeof(GoogleVolume))]
internal partial class GoogleBooksJsonContext : JsonSerializerContext;
