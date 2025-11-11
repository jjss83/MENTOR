namespace MentorMlApi.Models;

public sealed record MlAgentsRunRequest
{
    public string? ConfigPath { get; init; }
    public string? RunId { get; init; }
    public string? EnvironmentPath { get; init; }
    public bool? NoGraphics { get; init; }
    public string? CurriculumPath { get; init; }
    public int? KeepCheckpoints { get; init; }
    public int? Lesson { get; init; }
    public bool? LoadModel { get; init; }
    public int? NumRuns { get; init; }
    public int? SaveFrequency { get; init; }
    public int? Seed { get; init; }
    public bool? Slow { get; init; }
    public bool? Train { get; init; }
    public int? WorkerId { get; init; }
    public string? DockerTargetName { get; init; }
    public IReadOnlyList<string>? AdditionalArguments { get; init; }
};
