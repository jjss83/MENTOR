namespace MentorMlApi.Models;

public sealed record MlAgentsRunRequest
{
    public string? ConfigPath { get; init; }
    public string? RunId { get; init; }
    public string? EnvironmentPath { get; init; }
    public bool? NoGraphics { get; init; }
    public IReadOnlyList<string>? AdditionalArguments { get; init; }
};