namespace MentorTrainingRunner;

internal sealed record ReportInterpreterOptions(
    string RunId,
    string ResultsDirectory,
    string Prompt,
    string? OpenAiModel,
    string? OpenAiApiKey,
    bool CheckOpenAi)
{
    private const string DefaultResultsDirectory = @"X:\\workspace\\ml-agents\\results";
    private const string DefaultPrompt = "Explain current results";
    private const string DefaultModel = "gpt-4o-mini";

    public static bool TryParse(string[] args, out ReportInterpreterOptions? options, out string? error)
    {
        options = null;
        error = null;

        string? runId = null;
        string? resultsDirectory = null;
        string prompt = DefaultPrompt;
        string? model = null;
        string? apiKey = null;
        var checkOpenAi = false;

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

                    runId = value.Trim();
                    break;
                }

                case "results-dir":
                {
                    if (!TryReadValue(args, ref i, "results-dir", out var value, out error))
                    {
                        return false;
                    }

                    resultsDirectory = NormalizeDirectory(value, ref error);
                    if (resultsDirectory is null)
                    {
                        return false;
                    }

                    break;
                }

                case "prompt":
                {
                    if (!TryReadValue(args, ref i, "prompt", out var value, out error))
                    {
                        return false;
                    }

                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        prompt = value.Trim();
                    }

                    break;
                }

                case "openai-model":
                {
                    if (!TryReadValue(args, ref i, "openai-model", out var value, out error))
                    {
                        return false;
                    }

                    model = value.Trim();
                    break;
                }

                case "openai-api-key":
                {
                    if (!TryReadValue(args, ref i, "openai-api-key", out var value, out error))
                    {
                        return false;
                    }

                    apiKey = value.Trim();
                    break;
                }

                case "check-openai":
                {
                    checkOpenAi = true;
                    break;
                }

                default:
                    error = $"Unknown option '--{key}'.";
                    return false;
            }
        }

        if (runId is null)
        {
            error = "--run-id is required.";
            return false;
        }

        resultsDirectory ??= NormalizeDirectory(DefaultResultsDirectory, ref error);
        if (resultsDirectory is null)
        {
            return false;
        }

        model ??= DefaultModel;

        options = new ReportInterpreterOptions(runId, resultsDirectory, prompt, model, apiKey, checkOpenAi);
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
}
