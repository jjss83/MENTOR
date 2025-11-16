using MentorTrainingRunner;

namespace MentorTrainingRunner;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (!TrainingOptions.TryParse(args, out var options, out var error) || options is null)
        {
            PrintUsage(error);
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

    private static void PrintUsage(string? error)
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
    }
}