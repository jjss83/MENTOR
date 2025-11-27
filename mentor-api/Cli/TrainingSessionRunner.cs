using System.Buffers;
using System.Diagnostics;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using System.Globalization;
using System.Linq;

namespace MentorTrainingRunner;

internal sealed class TrainingSessionRunner
{
    private const int DefaultTensorboardPort = 6006;
    private const int DefaultBasePort = 5005;
    private const int BasePortBlockSize = 20;
    private const int BasePortStride = BasePortBlockSize;
    private const int MaxBasePortProbes = 200;
    private readonly TrainingOptions _options;
    private readonly TextWriter _outputWriter;
    private readonly TextWriter _errorWriter;
    private readonly Stream _standardOutput;
    private readonly Stream _standardError;
    private readonly bool _enableConsoleCancel;
    private readonly int? _tensorboardPort;
    private readonly int _basePort;
    private readonly string? _basePortMessage;
    private Process? _mlAgentsProcess;
    private bool _cancelRequested;
    private bool _reuseExistingTensorboard;

    public TrainingSessionRunner(
        TrainingOptions options,
        TextWriter? outputWriter = null,
        TextWriter? errorWriter = null,
        Stream? standardOutput = null,
        Stream? standardError = null,
        bool enableConsoleCancel = true, int? tensorboardPort = null)
    {
        _options = options;
        _outputWriter = outputWriter ?? Console.Out;
        _errorWriter = errorWriter ?? Console.Error;
        _standardOutput = standardOutput ?? Console.OpenStandardOutput();
        _standardError = standardError ?? Console.OpenStandardError();
        _enableConsoleCancel = enableConsoleCancel;
        _tensorboardPort = _options.LaunchTensorBoard ? DetermineTensorboardPort(tensorboardPort) : tensorboardPort;
        _basePort = ResolveBasePort(out _basePortMessage);
    }

    public string? TensorboardUrl => _tensorboardPort.HasValue ? $"http://localhost:{_tensorboardPort.Value}" : null;

    public void RequestCancel()
    {
        _cancelRequested = true;
        TryTerminateProcessTree(_mlAgentsProcess);
    }

    public async Task<int> RunAsync()
    {
        Directory.CreateDirectory(_options.ResultsDirectory);

        var process = CreateProcess();
        _mlAgentsProcess = process;
        var tensorboardProcess = _options.LaunchTensorBoard && !_reuseExistingTensorboard ? CreateTensorBoardProcess() : null;
        WriteLine($"Writing training artifacts to '{_options.ResultsDirectory}'.");
        if (!string.IsNullOrWhiteSpace(_basePortMessage))
        {
            WriteLine(_basePortMessage);
        }
        WriteLine();
        WriteLine("Starting training session with command:");
        WriteLine(FormatCommand(process.StartInfo));
        WriteLine();

        Task? tensorboardTask = null;
        if (tensorboardProcess is not null)
        {
            WriteLine("Starting TensorBoard in parallel with command:");
            WriteLine(FormatCommand(tensorboardProcess.StartInfo));
            WriteLine();
        }
        else if (_options.LaunchTensorBoard && _reuseExistingTensorboard && _tensorboardPort.HasValue)
        {
            WriteLine($"TensorBoard already running on port {_tensorboardPort.Value}; not launching another instance.");
            WriteLine();
        }

        var cancelRequested = false;
        ConsoleCancelEventHandler? cancelHandler = null;
        if (_enableConsoleCancel)
        {
            cancelHandler = (_, e) =>
            {
                if (!cancelRequested)
                {
                    cancelRequested = true;
                    e.Cancel = true;
                    WriteLine("Cancellation requested. Stopping ML-Agents process...");
                    TryTerminateProcessTree(process);
                    if (tensorboardProcess is not null)
                    {
                        WriteLine("Stopping TensorBoard process...");
                        TryTerminateProcessTree(tensorboardProcess);
                    }
                }
                else
                {
                    e.Cancel = false;
                }
            };

            Console.CancelKeyPress += cancelHandler;
        }

        try
        {
            if (tensorboardProcess is not null)
            {
                if (!tensorboardProcess.Start())
                {
                    throw new InvalidOperationException("Failed to start TensorBoard process.");
                }

                var tensorboardOutputPump = PumpStreamAsync(tensorboardProcess.StandardOutput.BaseStream, _standardOutput);
                var tensorboardErrorPump = PumpStreamAsync(tensorboardProcess.StandardError.BaseStream, _standardError);
                var tensorboardWait = tensorboardProcess.WaitForExitAsync();
                tensorboardTask = Task.WhenAll(tensorboardOutputPump, tensorboardErrorPump, tensorboardWait);
            }

            if (!process.Start())
            {
                throw new InvalidOperationException("Failed to start ML-Agents training process.");
            }

            var outputPump = PumpStreamAsync(process.StandardOutput.BaseStream, _standardOutput);
            var errorPump = PumpStreamAsync(process.StandardError.BaseStream, _standardError);

            await Task.WhenAll(process.WaitForExitAsync(), outputPump, errorPump).ConfigureAwait(false);
            var exitCode = process.ExitCode;

            if (tensorboardProcess is not null && !tensorboardProcess.HasExited)
            {
                WriteLine();
                WriteLine("Training session finished. Stopping TensorBoard...");
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
            if (_enableConsoleCancel && cancelHandler is not null)
            {
                Console.CancelKeyPress -= cancelHandler;
            }
        }
    }

    private int? DetermineTensorboardPort(int? explicitPort)
    {
        var desiredPort = explicitPort ?? DefaultTensorboardPort;
        if (IsPortInUse(desiredPort))
        {
            _reuseExistingTensorboard = true;
            return desiredPort;
        }

        _reuseExistingTensorboard = false;
        return desiredPort;
    }

    private static bool IsPortInUse(int port)
    {
        try
        {
            var listeners = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners();
            return listeners.Any(ep => ep.Port == port);
        }
        catch
        {
            return false;
        }
    }

    private int ResolveBasePort(out string? message)
    {
        message = null;
        var requested = _options.BasePort ?? DefaultBasePort;
        var candidate = requested;

        for (var attempt = 0; attempt < MaxBasePortProbes; attempt++)
        {
            if (IsPortRangeFree(candidate))
            {
                if (_options.BasePort.HasValue && candidate != _options.BasePort.Value)
                {
                    message = $"Base port {_options.BasePort.Value} is busy; using {candidate} instead.";
                }
                else if (!_options.BasePort.HasValue)
                {
                    message = candidate == DefaultBasePort
                        ? $"No base port specified; using default {DefaultBasePort}."
                        : $"Auto-selected base port {candidate} (starting from {DefaultBasePort}).";
                }

                return candidate;
            }

            candidate += BasePortStride;
        }

        message = $"Could not find a free base port after probing {MaxBasePortProbes} ranges; using {requested}.";
        return requested;
    }

    private bool IsPortRangeFree(int basePort)
    {
        for (var offset = 0; offset < BasePortBlockSize; offset++)
        {
            if (IsPortInUse(basePort + offset))
            {
                return false;
            }
        }

        return true;
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

        SetIsolatedTempDirectory(startInfo);

        if (_options.SkipConda)
        {
            startInfo.FileName = "mlagents-learn";
            AppendMlAgentsArguments(startInfo.ArgumentList);
        }
        else
        {
            startInfo.FileName = ResolveCondaExecutable();
            startInfo.ArgumentList.Add("run");
            startInfo.ArgumentList.Add("--live-stream");
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

        if (!string.IsNullOrWhiteSpace(_options.EnvExecutablePath))
        {
            arguments.Add($"--env={_options.EnvExecutablePath}");
        }

        arguments.Add($"--results-dir={_options.ResultsDirectory}");
        arguments.Add("--force");

        arguments.Add($"--base-port={_basePort}");

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

        SetIsolatedTempDirectory(startInfo);

        if (_options.SkipConda)
        {
            startInfo.FileName = "tensorboard";
        }
        else
        {
            startInfo.FileName = ResolveCondaExecutable();
            startInfo.ArgumentList.Add("run");
            startInfo.ArgumentList.Add("--live-stream");
            startInfo.ArgumentList.Add("-n");
            startInfo.ArgumentList.Add(_options.CondaEnvironmentName);
            startInfo.ArgumentList.Add("tensorboard");
        }

        startInfo.ArgumentList.Add("--logdir");
        startInfo.ArgumentList.Add(_options.ResultsDirectory);

        if (_tensorboardPort.HasValue)
        {
            startInfo.ArgumentList.Add("--port");
            startInfo.ArgumentList.Add(_tensorboardPort.Value.ToString(CultureInfo.InvariantCulture));
        }

        startInfo.ArgumentList.Add("--host");
        startInfo.ArgumentList.Add("localhost");

        return new Process { StartInfo = startInfo };
    }

    private static void SetIsolatedTempDirectory(ProcessStartInfo startInfo)
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "mentor-cli");
        Directory.CreateDirectory(tempRoot);

        var isolatedTemp = Path.Combine(tempRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(isolatedTemp);

        startInfo.Environment["TMP"] = isolatedTemp;
        startInfo.Environment["TEMP"] = isolatedTemp;
        startInfo.Environment["TMPDIR"] = isolatedTemp;
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

    private void TryTerminateProcessTree(Process process)
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
            WriteError($"Failed to terminate training process: {ex.Message}");
        }
    }

    private void WriteLine(string? message = null)
    {
        if (message is null)
        {
            _outputWriter.WriteLine();
        }
        else
        {
            _outputWriter.WriteLine(message);
        }

        _outputWriter.Flush();
    }

    private void WriteError(string message)
    {
        _errorWriter.WriteLine(message);
        _errorWriter.Flush();
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

