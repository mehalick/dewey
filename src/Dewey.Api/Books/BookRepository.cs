using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Dewey.Shared.Contracts;

namespace Dewey.Api.Books;

public sealed class BookRepository
{
    private readonly IAmazonDynamoDB _ddb;
    private readonly string _table;

    public BookRepository(IAmazonDynamoDB ddb)
    {
        _ddb = ddb;
        _table = Environment.GetEnvironmentVariable("DEWEY_BOOKS_TABLE")
            ?? throw new InvalidOperationException("DEWEY_BOOKS_TABLE not set");
    }

    public async Task<BookSummary> AddAsync(string userId, BookSummary book, CancellationToken ct)
    {
        await _ddb.PutItemAsync(new PutItemRequest
        {
            TableName = _table,
            Item = ToItem(userId, book),
            ConditionExpression = "attribute_not_exists(bookId)",
        }, ct);
        return book;
    }

    public async Task<BookSummary?> GetAsync(string userId, string bookId, CancellationToken ct)
    {
        var res = await _ddb.GetItemAsync(new GetItemRequest
        {
            TableName = _table,
            Key = Key(userId, bookId),
        }, ct);
        return res.IsItemSet ? FromItem(res.Item) : null;
    }

    public async Task<BookSummary[]> ListAsync(string userId, CancellationToken ct)
    {
        var res = await _ddb.QueryAsync(new QueryRequest
        {
            TableName = _table,
            KeyConditionExpression = "userId = :u",
            ExpressionAttributeValues = new() { [":u"] = new AttributeValue { S = userId } },
        }, ct);
        return [.. res.Items.Select(FromItem)];
    }

    public async Task<bool> DeleteAsync(string userId, string bookId, CancellationToken ct)
    {
        var res = await _ddb.DeleteItemAsync(new DeleteItemRequest
        {
            TableName = _table,
            Key = Key(userId, bookId),
            ReturnValues = ReturnValue.ALL_OLD,
        }, ct);
        return res.Attributes.Count > 0;
    }

    private static Dictionary<string, AttributeValue> Key(string userId, string bookId) => new()
    {
        ["userId"] = new AttributeValue { S = userId },
        ["bookId"] = new AttributeValue { S = bookId },
    };

    private static Dictionary<string, AttributeValue> ToItem(string userId, BookSummary b)
    {
        var item = new Dictionary<string, AttributeValue>
        {
            ["userId"] = new() { S = userId },
            ["bookId"] = new() { S = b.BookId },
            ["googleVolumeId"] = new() { S = b.GoogleVolumeId },
            ["title"] = new() { S = b.Title },
            ["authors"] = new() { L = [.. b.Authors.Select(a => new AttributeValue { S = a })] },
            ["addedAt"] = new() { S = b.AddedAt },
            ["latestProgressPct"] = new() { N = b.LatestProgressPct.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            ["status"] = new() { S = b.Status },
        };
        if (b.PageCount is not null)
            item["pageCount"] = new AttributeValue { N = b.PageCount.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        if (b.CoverUrl is not null)
            item["coverUrl"] = new AttributeValue { S = b.CoverUrl };
        if (b.LatestSessionAt is not null)
            item["latestSessionAt"] = new AttributeValue { S = b.LatestSessionAt };
        return item;
    }

    private static BookSummary FromItem(Dictionary<string, AttributeValue> item) => new(
        BookId: item["bookId"].S,
        GoogleVolumeId: item.GetValueOrDefault("googleVolumeId")?.S ?? "",
        Title: item.GetValueOrDefault("title")?.S ?? "",
        Authors: item.TryGetValue("authors", out var a)
            ? [.. a.L.Select(x => x.S)]
            : [],
        PageCount: item.TryGetValue("pageCount", out var pc) && int.TryParse(pc.N, out var pcv) ? pcv : null,
        CoverUrl: item.GetValueOrDefault("coverUrl")?.S,
        AddedAt: item.GetValueOrDefault("addedAt")?.S ?? "",
        LatestProgressPct: item.TryGetValue("latestProgressPct", out var lp) && int.TryParse(lp.N, out var lpv) ? lpv : 0,
        LatestSessionAt: item.GetValueOrDefault("latestSessionAt")?.S,
        Status: item.GetValueOrDefault("status")?.S ?? "not_started");
}
