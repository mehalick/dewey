namespace Dewey.Shared.Contracts;

public sealed record LogSessionRequest(
    string SessionId,
    string Type, // "start" | "stop"
    int ProgressPct,
    string OccurredAt);

public sealed record SessionEvent(
    string SessionId,
    string Type,
    int ProgressPct,
    string OccurredAt);
