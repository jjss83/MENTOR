using System.Diagnostics;
using System.Text;

namespace MentorTrainingRunner;

internal sealed class TrainingSessionRunner
{
    private readonly TrainingOptions _options;

    public TrainingSessionRunner(TrainingOptions options)
    {
        _options = options;
    }

    public async Task<int> RunAsync()
    {
        Directory.CreateDirectory(_options.ResultsDirectory);

        using var process = CreateProcess();
        Console.WriteLine($"Writing training artifacts to '{_options.ResultsDirectory}'.");
        Console.WriteLine();
        Console.WriteLine("Starting training session with command:");
        Console.WriteLine(FormatCommand(process.StartInfo));
        Console.WriteLine();

        process.OutputDataReceived += (_, data) =>
        {
            if (data.Data is not null)
            {
                Console.WriteLine(data.Data);
            }
        };

        process.ErrorDataReceived += (_, data) =>
        {
            if (data.Data is not null)
            {
                Console.Error.WriteLine(data.Data);
            }
        };

        var cancelRequested = false;
        ConsoleCancelEventHandler? cancelHandler = null;
        cancelHandler = (_, e) =>
        {
            if (!cancelRequested)
            {
                cancelRequested = true;
                e.Cancel = true;
                Console.WriteLine("Cancellation requested. Stopping ML-Agents process...");
                TryTerminateProcessTree(process);
            }
            else
            {
                e.Cancel = false;
            }
        };
        Console.CancelKeyPress += cancelHandler;

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("Failed to start ML-Agents training process.");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync().ConfigureAwait(false);
            return process.ExitCode;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    private Process CreateProcess()
    {
        var startInfo = new ProcessStartInfo
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        if (_options.SkipConda)
        {
            startInfo.FileName = "mlagents-learn";
            AppendMlAgentsArguments(startInfo.ArgumentList);
        }
        else
        {
            startInfo.FileName = ResolveCondaExecutable();
            startInfo.ArgumentList.Add("run");
            startInfo.ArgumentList.Add("-n");
            startInfo.ArgumentList.Add(_options.CondaEnvironmentName);
            startInfo.ArgumentList.Add("mlagents-learn");
            AppendMlAgentsArguments(startInfo.ArgumentList);
        }

        return new Process { StartInfo = startInfo };
    }

    private void AppendMlAgentsArguments(ICollection<string> arguments)
    {
        arguments.Add(_options.TrainerConfigPath);
        arguments.Add($"--run-id={_options.RunId}");
        arguments.Add($"--env={_options.EnvExecutablePath}");
        arguments.Add($"--results-dir={_options.ResultsDirectory}");
        arguments.Add("--force");

        if (_options.BasePort.HasValue)
        {
            arguments.Add($"--base-port={_options.BasePort.Value}");
        }

        if (_options.NoGraphics)
        {
            arguments.Add("--no-graphics");
        }
    }

    private static string ResolveCondaExecutable()
    {
        var fromEnv = Environment.GetEnvironmentVariable("CONDA_EXE");
        if (!string.IsNullOrWhiteSpace(fromEnv) && File.Exists(fromEnv))
        {
            return fromEnv;
        }

        return "conda";
    }

    private static void TryTerminateProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to terminate training process: {ex.Message}");
        }
    }

    private static string FormatCommand(ProcessStartInfo startInfo)
    {
        var builder = new StringBuilder();
        builder.Append(QuoteIfNeeded(startInfo.FileName));
        foreach (var arg in startInfo.ArgumentList)
        {
            builder.Append(' ');
            builder.Append(QuoteIfNeeded(arg));
        }

        return builder.ToString();
    }

    private static string QuoteIfNeeded(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "\"\"";
        }

        return value.Any(c => char.IsWhiteSpace(c) || c == '\"')
            ? $"\"{value.Replace("\"", "\\\"")}\""
            : value;
    }
}