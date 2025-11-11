namespace MentorMlApi.Options;

public sealed class MlAgentsSettings
{
    public string WorkingDirectory { get; set; } = @"X:\workspace\ml-agents";
    public bool UseCondaRun { get; set; } = false;
    public string CondaExecutable { get; set; } = "conda";
    public string CondaEnvironmentName { get; set; } = "mlagents";
    public string DefaultConfigPath { get; set; } = "config/ppo/3DBall.yaml";
    public string? DefaultUnityEnvironmentPath { get; set; }
        = null;
    public bool DefaultNoGraphics { get; set; } = false;
    public int MaxOutputLines { get; set; } = 400;
}
