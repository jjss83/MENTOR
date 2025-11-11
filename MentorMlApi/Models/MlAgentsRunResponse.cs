namespace MentorMlApi.Models;

public sealed record MlAgentsRunResponse(
    string Command,
    string WorkingDirectory,
    int ExitCode,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    IReadOnlyList<string> StandardOutput,
    IReadOnlyList<string> StandardError);