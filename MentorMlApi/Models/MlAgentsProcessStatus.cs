namespace MentorMlApi.Models;

public sealed record MlAgentsProcessStatus(
    Guid Id,
    string RunId,
    int ProcessId,
    string Command,
    string WorkingDirectory,
    DateTimeOffset StartedAt,
    TimeSpan Elapsed);
