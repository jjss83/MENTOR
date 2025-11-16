using System.Buffers;
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
        using var tensorboardProcess = _options.LaunchTensorBoard ? CreateTensorBoardProcess() : null;
        Console.WriteLine($"Writing training artifacts to '{_options.ResultsDirectory}'.");
        Console.WriteLine();
        Console.WriteLine("Starting training session with command:");
        Console.WriteLine(FormatCommand(process.StartInfo));
        Console.WriteLine();

        Task? tensorboardTask = null;
        if (tensorboardProcess is not null)
        {
            Console.WriteLine("Starting TensorBoard in parallel with command:");
            Console.WriteLine(FormatCommand(tensorboardProcess.StartInfo));
            Console.WriteLine();
        }

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
                if (tensorboardProcess is not null)
                {
                    Console.WriteLine("Stopping TensorBoard process...");
                    TryTerminateProcessTree(tensorboardProcess);
                }
            }
            else
            {
                e.Cancel = false;
            }
        };
        Console.CancelKeyPress += cancelHandler;

        try
        {
            if (tensorboardProcess is not null)
            {
                if (!tensorboardProcess.Start())
                {
                    throw new InvalidOperationException("Failed to start TensorBoard process.");
                }

                var tensorboardOutputPump = PumpStreamAsync(tensorboardProcess.StandardOutput.BaseStream, Console.OpenStandardOutput());
                var tensorboardErrorPump = PumpStreamAsync(tensorboardProcess.StandardError.BaseStream, Console.OpenStandardError());
                var tensorboardWait = tensorboardProcess.WaitForExitAsync();
                tensorboardTask = Task.WhenAll(tensorboardOutputPump, tensorboardErrorPump, tensorboardWait);
            }

            if (!process.Start())
            {
                throw new InvalidOperationException("Failed to start ML-Agents training process.");
            }

            var outputPump = PumpStreamAsync(process.StandardOutput.BaseStream, Console.OpenStandardOutput());
            var errorPump = PumpStreamAsync(process.StandardError.BaseStream, Console.OpenStandardError());

            await Task.WhenAll(process.WaitForExitAsync(), outputPump, errorPump).ConfigureAwait(false);
            var exitCode = process.ExitCode;

            if (tensorboardProcess is not null && !tensorboardProcess.HasExited)
            {
                Console.WriteLine();
                Console.WriteLine("Training session finished. Stopping TensorBoard...");
                TryTerminateProcessTree(tensorboardProcess);
            }

            if (tensorboardTask is not null)
            {
                await tensorboardTask.ConfigureAwait(false);
            }

            return exitCode;
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

    private Process CreateTensorBoardProcess()
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
            startInfo.FileName = "tensorboard";
        }
        else
        {
            startInfo.FileName = ResolveCondaExecutable();
            startInfo.ArgumentList.Add("run");
            startInfo.ArgumentList.Add("-n");
            startInfo.ArgumentList.Add(_options.CondaEnvironmentName);
            startInfo.ArgumentList.Add("tensorboard");
        }

        startInfo.ArgumentList.Add("--logdir");
        startInfo.ArgumentList.Add(_options.ResultsDirectory);

        return new Process { StartInfo = startInfo };
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

    private static Task PumpStreamAsync(Stream source, Stream destination)
    {
        return Task.Run(async () =>
        {
            var buffer = ArrayPool<byte>.Shared.Rent(8192);
            try
            {
                while (true)
                {
                    var bytesRead = await source.ReadAsync(buffer.AsMemory(0, buffer.Length)).ConfigureAwait(false);
                    if (bytesRead == 0)
                    {
                        break;
                    }

                    await destination.WriteAsync(buffer.AsMemory(0, bytesRead)).ConfigureAwait(false);
                    await destination.FlushAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        });
    }
}
