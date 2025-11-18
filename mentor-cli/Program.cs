using System.Linq;

namespace MentorTrainingRunner;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (IsReportInterpreterCommand(args))
        {
            return await RunReportInterpreterAsync(args);
        }

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

    private static bool IsReportInterpreterCommand(string[] args)
    {
        return args.Length > 0 && string.Equals(args[0], "report-interpreter", StringComparison.OrdinalIgnoreCase);
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

    private static async Task<int> RunReportInterpreterAsync(string[] args)
    {
        var interpreterArgs = args.Skip(1).ToArray();
        if (!ReportInterpreterOptions.TryParse(interpreterArgs, out var options, out var error) || options is null)
        {
            PrintReportInterpreterUsage(error);
            return 1;
        }

        var runner = new ReportInterpreterRunner(options);
        try
        {
            return await runner.RunAsync();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Report interpretation failed: {ex.Message}");
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

        Console.Write(UsageText.GetTrainingUsage());
    }

    private static void PrintReportUsage(string? error)
    {
        if (!string.IsNullOrWhiteSpace(error))
        {
            Console.Error.WriteLine(error);
            Console.Error.WriteLine();
        }

        Console.Write(UsageText.GetReportUsage());
    }

    private static void PrintReportInterpreterUsage(string? error)
    {
        if (!string.IsNullOrWhiteSpace(error))
        {
            Console.Error.WriteLine(error);
            Console.Error.WriteLine();
        }

        Console.Write(UsageText.GetReportInterpreterUsage());
    }
}
