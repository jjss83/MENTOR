namespace MentorMlApi.Options;

public sealed class MlAgentsSettings
{
    public string WorkingDirectory { get; set; } = @"X:\workspace\ml-agents";
    public string CondaExecutable { get; set; } = "conda";
    public string CondaEnvironmentName { get; set; } = "mlagents";
    public string DefaultConfigPath { get; set; } = @"config/ppo/3DBall.yaml";
    public string? DefaultUnityEnvironmentPath { get; set; }
        = @"Project/Builds/Example";
    public bool DefaultNoGraphics { get; set; } = true;
    public int MaxOutputLines { get; set; } = 400;
}