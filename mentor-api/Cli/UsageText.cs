using System.Text;

namespace MentorTrainingRunner;

internal static class UsageText
{
    public static string GetTrainingUsage()
    {
        var builder = new StringBuilder();
        builder.AppendLine("Usage:");
        builder.AppendLine("  dotnet run -- --env-path <path-to-env-exe> --config <trainer-config.yaml> [options]\\n");
        builder.AppendLine("Options:");
        builder.AppendLine("  --run-id <id>             Optional run identifier. Default: run-<behavior>-<UTC timestamp>");
        builder.AppendLine("  --results-dir <path>      Directory to store training artifacts. Default: X:\\workspace\\ml-agents\\results");
        builder.AppendLine("  --conda-env <name>        Name of the ML-Agents Conda environment. Default: mlagents");
        builder.AppendLine("  --base-port <port>        Base port to use when launching the environment (auto-selects from 5005 if omitted)");
        builder.AppendLine("  --no-graphics             Launches the environment without rendering");
        builder.AppendLine("  --skip-conda              Assume the ML-Agents tooling is already on PATH");
        builder.AppendLine("  --tensorboard             Also start TensorBoard pointed at the results directory");
        builder.AppendLine();
        builder.AppendLine("Report usage:");
        builder.AppendLine("  dotnet run -- report --run-id <id> [--results-dir <path>]");
        builder.AppendLine();
        builder.AppendLine("Report interpreter usage:");
        builder.AppendLine("  dotnet run -- report-interpreter --run-id <id> [--results-dir <path>] [--prompt \"Explain current results\"] [--openai-model <model>] [--openai-api-key <key>] [--check-openai]");
        return builder.ToString();
    }

    public static string GetReportUsage()
    {
        var builder = new StringBuilder();
        builder.AppendLine("Usage:");
        builder.AppendLine("  dotnet run -- report --run-id <id> [--results-dir <path>]\\n");
        builder.AppendLine("Options:");
        builder.AppendLine("  --run-id <id>        Run identifier to inspect (required)");
        builder.AppendLine("  --results-dir <path> Directory that contains run artifacts. Default: X:\\workspace\\ml-agents\\results");
        return builder.ToString();
    }

    public static string GetReportInterpreterUsage()
    {
        var builder = new StringBuilder();
        builder.AppendLine("Usage:");
        builder.AppendLine("  dotnet run -- report-interpreter --run-id <id> [--results-dir <path>] [--prompt <text>] [--openai-model <model>] [--openai-api-key <key>] [--check-openai]\\n");
        builder.AppendLine("Options:");
        builder.AppendLine("  --run-id <id>        Run identifier to inspect (required)");
        builder.AppendLine("  --results-dir <path> Directory that contains run artifacts. Default: X:\\workspace\\ml-agents\\results");
        builder.AppendLine("  --prompt <text>      Prompt to send along with the report. Default: Explain current results");
        builder.AppendLine("  --openai-model <m>   OpenAI chat completion model. Default: gpt-4o-mini");
        builder.AppendLine("  --openai-api-key <k> Explicit API key (otherwise uses OPENAI_API_KEY env var)");
        builder.AppendLine("  --check-openai       Skip report generation and issue a simple connectivity check call");
        return builder.ToString();
    }
}
