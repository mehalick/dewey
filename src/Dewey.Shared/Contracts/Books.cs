namespace Dewey.Shared.Contracts;

public sealed record BookSearchResult(
    string GoogleVolumeId,
    string Title,
    string[] Authors,
    int? PageCount,
    string? CoverUrl,
    string? Description);

public sealed record AddBookRequest(string GoogleVolumeId);

public sealed record BookSummary(
    string BookId,
    string GoogleVolumeId,
    string Title,
    string[] Authors,
    int? PageCount,
    string? CoverUrl,
    string AddedAt,
    int LatestProgressPct,
    string? LatestSessionAt,
    string Status);
