using Amazon.S3;
using Amazon.S3.Model;

namespace Dewey.Api.Books;

public sealed class CoverCache
{
    private readonly IAmazonS3 _s3;
    private readonly HttpClient _http;
    private readonly string _bucket;
    private readonly ILogger<CoverCache> _log;

    public CoverCache(IAmazonS3 s3, IHttpClientFactory httpFactory, IConfiguration config, ILogger<CoverCache> log)
    {
        _s3 = s3;
        _http = httpFactory.CreateClient("covers");
        _bucket = Environment.GetEnvironmentVariable("DEWEY_COVERS_BUCKET")
            ?? throw new InvalidOperationException("DEWEY_COVERS_BUCKET not set");
        _log = log;
    }

    // Returns the public path under CloudFront (e.g. "/covers/{id}.jpg") that
    // the web app can render directly. Returns null on any failure.
    public async Task<string?> EnsureCachedAsync(string googleVolumeId, string sourceUrl, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(sourceUrl)) return null;

        var key = $"covers/{googleVolumeId}.jpg";
        if (await ExistsAsync(key, ct)) return "/" + key;

        try
        {
            using var res = await _http.GetAsync(sourceUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!res.IsSuccessStatusCode) return null;
            var contentType = res.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
            await using var body = await res.Content.ReadAsStreamAsync(ct);

            // S3 PutObject requires a seekable stream when content-length isn't
            // pre-known. Buffer to memory; covers are small (<200KB typical).
            using var ms = new MemoryStream();
            await body.CopyToAsync(ms, ct);
            ms.Position = 0;

            await _s3.PutObjectAsync(new PutObjectRequest
            {
                BucketName = _bucket,
                Key = key,
                InputStream = ms,
                ContentType = contentType,
                DisablePayloadSigning = true,
            }, ct);

            return "/" + key;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to cache cover for {VolumeId}", googleVolumeId);
            return null;
        }
    }

    private async Task<bool> ExistsAsync(string key, CancellationToken ct)
    {
        try
        {
            await _s3.GetObjectMetadataAsync(_bucket, key, ct);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }
}
