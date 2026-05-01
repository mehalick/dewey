using System.Globalization;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Dewey.Shared.Contracts;

namespace Dewey.Api.Books;

public sealed class SessionRepository
{
    private readonly IAmazonDynamoDB _ddb;
    private readonly string _sessionsTable;
    private readonly string _booksTable;

    public SessionRepository(IAmazonDynamoDB ddb)
    {
        _ddb = ddb;
        _sessionsTable = Environment.GetEnvironmentVariable("DEWEY_SESSIONS_TABLE")
            ?? throw new InvalidOperationException("DEWEY_SESSIONS_TABLE not set");
        _booksTable = Environment.GetEnvironmentVariable("DEWEY_BOOKS_TABLE")
            ?? throw new InvalidOperationException("DEWEY_BOOKS_TABLE not set");
    }

    // Atomically writes the session and updates the book's latest progress / status.
    // Idempotent on (userId, bookId, sessionId) via attribute_not_exists guard.
    public async Task LogAsync(string userId, string bookId, LogSessionRequest req, CancellationToken ct)
    {
        if (req.Type is not ("start" or "stop"))
            throw new ArgumentException("type must be 'start' or 'stop'");
        if (req.ProgressPct < 0 || req.ProgressPct > 100)
            throw new ArgumentException("progressPct must be 0..100");
        if (string.IsNullOrWhiteSpace(req.SessionId) || string.IsNullOrWhiteSpace(req.OccurredAt))
            throw new ArgumentException("sessionId and occurredAt required");

        var status = DeriveStatus(req.Type, req.ProgressPct);

        var transact = new TransactWriteItemsRequest
        {
            TransactItems =
            [
                new TransactWriteItem
                {
                    Put = new Put
                    {
                        TableName = _sessionsTable,
                        Item = new Dictionary<string, AttributeValue>
                        {
                            ["userBookKey"] = new() { S = $"{userId}#{bookId}" },
                            ["occurredAtSessionId"] = new() { S = $"{req.OccurredAt}#{req.SessionId}" },
                            ["sessionId"] = new() { S = req.SessionId },
                            ["type"] = new() { S = req.Type },
                            ["progressPct"] = new() { N = req.ProgressPct.ToString(CultureInfo.InvariantCulture) },
                            ["occurredAt"] = new() { S = req.OccurredAt },
                        },
                        ConditionExpression = "attribute_not_exists(occurredAtSessionId)",
                    },
                },
                new TransactWriteItem
                {
                    Update = new Update
                    {
                        TableName = _booksTable,
                        Key = new Dictionary<string, AttributeValue>
                        {
                            ["userId"] = new() { S = userId },
                            ["bookId"] = new() { S = bookId },
                        },
                        UpdateExpression =
                            "SET latestProgressPct = :p, latestSessionAt = :t, #s = :st",
                        ExpressionAttributeNames = new() { ["#s"] = "status" },
                        ExpressionAttributeValues = new()
                        {
                            [":p"] = new AttributeValue { N = req.ProgressPct.ToString(CultureInfo.InvariantCulture) },
                            [":t"] = new AttributeValue { S = req.OccurredAt },
                            [":st"] = new AttributeValue { S = status },
                        },
                        ConditionExpression = "attribute_exists(bookId)",
                    },
                },
            ],
        };

        await _ddb.TransactWriteItemsAsync(transact, ct);
    }

    public async Task<SessionEvent[]> ListAsync(string userId, string bookId, CancellationToken ct)
    {
        var res = await _ddb.QueryAsync(new QueryRequest
        {
            TableName = _sessionsTable,
            KeyConditionExpression = "userBookKey = :k",
            ExpressionAttributeValues = new()
            {
                [":k"] = new AttributeValue { S = $"{userId}#{bookId}" },
            },
            ScanIndexForward = false, // newest first
        }, ct);
        return [.. res.Items.Select(FromItem)];
    }

    private static SessionEvent FromItem(Dictionary<string, AttributeValue> item) => new(
        SessionId: item.GetValueOrDefault("sessionId")?.S ?? "",
        Type: item.GetValueOrDefault("type")?.S ?? "",
        ProgressPct: int.TryParse(item.GetValueOrDefault("progressPct")?.N, out var p) ? p : 0,
        OccurredAt: item.GetValueOrDefault("occurredAt")?.S ?? "");

    private static string DeriveStatus(string type, int pct) =>
        type == "stop" && pct >= 100 ? "finished" : "reading";
}
