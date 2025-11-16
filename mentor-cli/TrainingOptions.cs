using System.Globalization;

namespace MentorTrainingRunner;

internal sealed record TrainingOptions(
    string EnvExecutablePath,
    string TrainerConfigPath,
    string RunId,
    string ResultsDirectory,
    string CondaEnvironmentName,
    int? BasePort,
    bool NoGraphics,
    bool SkipConda,
    bool LaunchTensorBoard)
{
    private const string DefaultResultsDirectory = @"X:\\workspace\\ml-agents\\results";
    private const string DefaultCondaEnvironmentName = "mlagents";

    public static bool TryParse(string[] args, out TrainingOptions? options, out string? error)
    {
        options = null;
        error = null;

        var builder = new OptionsBuilder();

        for (var i = 0; i < args.Length; i++)
        {
            var rawArg = args[i];
            if (!rawArg.StartsWith("--", StringComparison.Ordinal))
            {
                error = $"Unrecognized argument '{rawArg}'.";
                return false;
            }

            var key = rawArg[2..];
            switch (key)
            {
                case "env-path":
                {
                    if (!TryReadValue(args, ref i, "env-path", out var value, out error))
                    {
                        return false;
                    }

                    builder.EnvExecutablePath = NormalizeFile(value, "environment executable", ref error);
                    if (builder.EnvExecutablePath is null)
                    {
                        return false;
                    }

                    break;
                }

                case "config":
                {
                    if (!TryReadValue(args, ref i, "config", out var value, out error))
                    {
                        return false;
                    }

                    builder.TrainerConfigPath = NormalizeFile(value, "trainer config", ref error);
                    if (builder.TrainerConfigPath is null)
                    {
                        return false;
                    }

                    break;
                }

                case "run-id":
                {
                    if (!TryReadValue(args, ref i, "run-id", out var value, out error))
                    {
                        return false;
                    }

                    if (string.IsNullOrWhiteSpace(value))
                    {
                        error = "--run-id must not be empty.";
                        return false;
                    }

                    builder.RunId = value.Trim();
                    break;
                }

                case "results-dir":
                {
                    if (!TryReadValue(args, ref i, "results-dir", out var value, out error))
                    {
                        return false;
                    }

                    builder.ResultsDirectory = NormalizeDirectory(value, ref error);
                    if (builder.ResultsDirectory is null)
                    {
                        return false;
                    }

                    break;
                }

                case "conda-env":
                {
                    if (!TryReadValue(args, ref i, "conda-env", out var value, out error))
                    {
                        return false;
                    }

                    builder.CondaEnvironmentName = value.Trim();
                    if (builder.CondaEnvironmentName.Length == 0)
                    {
                        error = "--conda-env must not be empty.";
                        return false;
                    }

                    break;
                }

                case "base-port":
                {
                    if (!TryReadValue(args, ref i, "base-port", out var value, out error))
                    {
                        return false;
                    }

                    if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port) || port <= 0)
                    {
                        error = "--base-port must be a positive integer.";
                        return false;
                    }

                    builder.BasePort = port;
                    break;
                }

                case "no-graphics":
                    builder.NoGraphics = true;
                    break;

                case "skip-conda":
                    builder.SkipConda = true;
                    break;

                case "tensorboard":
                    builder.LaunchTensorBoard = true;
                    break;

                default:
                    error = $"Unknown option '--{key}'.";
                    return false;
            }
        }

        if (builder.EnvExecutablePath is null)
        {
            error = "--env-path is required.";
            return false;
        }

        if (builder.TrainerConfigPath is null)
        {
            error = "--config is required.";
            return false;
        }

        builder.ResultsDirectory ??= NormalizeDirectory(DefaultResultsDirectory, ref error);
        if (builder.ResultsDirectory is null)
        {
            return false;
        }

        builder.RunId ??= $"run_{DateTime.UtcNow:yyyyMMdd_HHmmss}";
        builder.CondaEnvironmentName ??= DefaultCondaEnvironmentName;

        options = new TrainingOptions(
            builder.EnvExecutablePath,
            builder.TrainerConfigPath,
            builder.RunId,
            builder.ResultsDirectory,
            builder.CondaEnvironmentName,
            builder.BasePort,
            builder.NoGraphics,
            builder.SkipConda,
            builder.LaunchTensorBoard);

        return true;
    }

    private static bool TryReadValue(string[] args, ref int index, string flag, out string value, out string? error)
    {
        if (index + 1 >= args.Length)
        {
            error = $"Missing value for --{flag}.";
            value = string.Empty;
            return false;
        }

        value = args[++index];
        error = null;
        return true;
    }

    private static string? NormalizeFile(string path, string description, ref string? error)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath))
            {
                error = $"Could not find the specified {description} at '{fullPath}'.";
                return null;
            }

            return fullPath;
        }
        catch (Exception ex)
        {
            error = $"Failed to resolve {description} path '{path}': {ex.Message}";
            return null;
        }
    }

    private static string? NormalizeDirectory(string path, ref string? error)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex)
        {
            error = $"Failed to resolve directory path '{path}': {ex.Message}";
            return null;
        }
    }

    private sealed class OptionsBuilder
    {
        public string? EnvExecutablePath { get; set; }
        public string? TrainerConfigPath { get; set; }
        public string? RunId { get; set; }
        public string? ResultsDirectory { get; set; }
        public string? CondaEnvironmentName { get; set; }
        public int? BasePort { get; set; }
        public bool NoGraphics { get; set; }
        public bool SkipConda { get; set; }
        public bool LaunchTensorBoard { get; set; }
    }
}
