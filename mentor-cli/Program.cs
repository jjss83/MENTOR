using System.Linq;

namespace MentorTrainingRunner;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (IsReportCommand(args))
        {
            return await RunReportAsync(args);
        }

        return await RunTrainingAsync(args);
    }

    private static bool IsReportCommand(string[] args)
    {
        return args.Length > 0 && string.Equals(args[0], "report", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<int> RunTrainingAsync(string[] args)
    {
        if (!TrainingOptions.TryParse(args, out var options, out var error) || options is null)
        {
            PrintTrainingUsage(error);
            return 1;
        }

        var runner = new TrainingSessionRunner(options);
        try
        {
            return await runner.RunAsync();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Training session failed: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> RunReportAsync(string[] args)
    {
        var reportArgs = args.Skip(1).ToArray();
        if (!ReportOptions.TryParse(reportArgs, out var options, out var error) || options is null)
        {
            PrintReportUsage(error);
            return 1;
        }

        var generator = new TrainingReportGenerator(options);
        try
        {
            return await generator.RunAsync();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Report generation failed: {ex.Message}");
            return 1;
        }
    }

    private static void PrintTrainingUsage(string? error)
    {
        if (!string.IsNullOrWhiteSpace(error))
        {
            Console.Error.WriteLine(error);
            Console.Error.WriteLine();
        }

        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run -- --env-path <path-to-env-exe> --config <trainer-config.yaml> [options]\n");
        Console.WriteLine("Options:");
        Console.WriteLine("  --run-id <id>             Optional run identifier. Default: run_<UTC timestamp>");
        Console.WriteLine("  --results-dir <path>      Directory to store training artifacts. Default: X:\\workspace\\ml-agents\\results");
        Console.WriteLine("  --conda-env <name>        Name of the ML-Agents Conda environment. Default: mlagents");
        Console.WriteLine("  --base-port <port>        Base port to use when launching the environment");
        Console.WriteLine("  --no-graphics             Launches the environment without rendering");
        Console.WriteLine("  --skip-conda              Assume the ML-Agents tooling is already on PATH");
        Console.WriteLine("  --tensorboard             Also start TensorBoard pointed at the results directory");
        Console.WriteLine();
        Console.WriteLine("Report usage:");
        Console.WriteLine("  dotnet run -- report --run-id <id> [--results-dir <path>]");
    }

    private static void PrintReportUsage(string? error)
    {
        if (!string.IsNullOrWhiteSpace(error))
        {
            Console.Error.WriteLine(error);
            Console.Error.WriteLine();
        }

        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run -- report --run-id <id> [--results-dir <path>]\n");
        Console.WriteLine("Options:");
        Console.WriteLine("  --run-id <id>        Run identifier to inspect (required)");
        Console.WriteLine("  --results-dir <path> Directory that contains run artifacts. Default: X:\\workspace\\ml-agents\\results");
    }
}
