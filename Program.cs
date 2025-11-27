    public StartTensorboardResult StartTensorboard(StartTensorboardRequest request)
    {
        var rawResultsDir = string.IsNullOrWhiteSpace(request.ResultsDir) ? TrainingOptions.DefaultResultsDirectory : request.ResultsDir;
        var resultsDir = ResolveResultsDirectory(rawResultsDir);
        var condaEnv = string.IsNullOrWhiteSpace(request.CondaEnv) ? null : request.CondaEnv.Trim();
        var skipConda = request.SkipConda ?? false;
        var tensorboardPort = request.Port ?? 6006;

        if (!string.IsNullOrWhiteSpace(request.RunId))
        {
            var runDirectory = BuildRunDirectory(resultsDir, request.RunId);
            var metadata = TrainingRunMetadata.TryLoad(runDirectory);
            if (metadata is not null)
            {
                resultsDir = ResolveResultsDirectory(metadata.ResultsDirectory);
                condaEnv ??= metadata.CondaEnvironmentName;
                if (!request.SkipConda.HasValue)
                {
                    skipConda = metadata.SkipConda;
                }
            }
        }

        if (!Directory.Exists(resultsDir))
        {
            Directory.CreateDirectory(resultsDir);
        }

        var activePorts = GetActiveTcpPorts();
        if (activePorts.Contains(tensorboardPort))
        {
            return new StartTensorboardResult(false, true, $"http://localhost:{tensorboardPort}", $"TensorBoard already running on port {tensorboardPort}.");
        }

        try
        {
            var process = CreateTensorboardProcess(resultsDir, condaEnv, skipConda, tensorboardPort);
            if (!process.Start())
            {
                return new StartTensorboardResult(false, false, null, "Failed to start TensorBoard process.");
            }

            var earlyExitMessage = ObserveEarlyTensorboardExit(process);
            if (earlyExitMessage is not null)
            {
                return new StartTensorboardResult(false, false, null, earlyExitMessage);
            }

            return new StartTensorboardResult(true, false, $"http://localhost:{tensorboardPort}", $"TensorBoard started on port {tensorboardPort}.");
        }
        catch (Exception ex)
        {
            return new StartTensorboardResult(false, false, null, ex.Message);
        }
    }
    private static string? ObserveEarlyTensorboardExit(Process process, int waitMilliseconds = 3000)
    {
        var waited = 0;
        while (waited < waitMilliseconds)
        {
            var slice = Math.Min(500, waitMilliseconds - waited);
            if (process.WaitForExit(slice))
            {
                var stderr = string.Empty;
                var stdout = string.Empty;
                try
                {
                    stderr = process.StandardError.ReadToEnd();
                    stdout = process.StandardOutput.ReadToEnd();
                }
                catch
                {
                    // ignore read failures
                }

                var message = $"TensorBoard exited immediately with code {process.ExitCode}.";
                var tail = string.Join(" ", new[] { stdout, stderr }.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()));
                if (!string.IsNullOrWhiteSpace(tail))
                {
                    message += " Output: " + tail;
                }

                return message;
            }

            waited += slice;
        }

        return null;
    }

    }
    private static string? ObserveEarlyTensorboardExit(Process process, int waitMilliseconds = 3000)
    {
        var waited = 0;
        while (waited < waitMilliseconds)
        {
            var slice = Math.Min(500, waitMilliseconds - waited);
            if (process.WaitForExit(slice))
            {
                var stderr = string.Empty;
                var stdout = string.Empty;
                try
                {
                    stderr = process.StandardError.ReadToEnd();
                    stdout = process.StandardOutput.ReadToEnd();
                }
                catch
                {
                    // ignore read failures
                }

                var message = $"TensorBoard exited immediately with code {process.ExitCode}.";
                var tail = string.Join(" ", new[] { stdout, stderr }.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()));
                if (!string.IsNullOrWhiteSpace(tail))
                {
                    message += " Output: " + tail;
                }

                return message;
            }

            waited += slice;
        }

        return null;
    }
    }
    public StartTensorboardResult StartTensorboard(StartTensorboardRequest request)
    {
        var rawResultsDir = string.IsNullOrWhiteSpace(request.ResultsDir) ? TrainingOptions.DefaultResultsDirectory : request.ResultsDir;
        var resultsDir = ResolveResultsDirectory(rawResultsDir);
        var condaEnv = string.IsNullOrWhiteSpace(request.CondaEnv) ? null : request.CondaEnv.Trim();
        var skipConda = request.SkipConda ?? false;
        var tensorboardPort = request.Port ?? 6006;

        if (!string.IsNullOrWhiteSpace(request.RunId))
        {
            var runDirectory = BuildRunDirectory(resultsDir, request.RunId);
            var metadata = TrainingRunMetadata.TryLoad(runDirectory);
            if (metadata is not null)
            {
                resultsDir = ResolveResultsDirectory(metadata.ResultsDirectory);
                condaEnv ??= metadata.CondaEnvironmentName;
                if (!request.SkipConda.HasValue)
                {
                    skipConda = metadata.SkipConda;
                }
            }
        }

        if (!Directory.Exists(resultsDir))
        {
            Directory.CreateDirectory(resultsDir);
        }

        var activePorts = GetActiveTcpPorts();
        if (activePorts.Contains(tensorboardPort))
        {
            return new StartTensorboardResult(false, true, $"http://localhost:{tensorboardPort}", $"TensorBoard already running on port {tensorboardPort}.");
        }

        try
        {
            var process = CreateTensorboardProcess(resultsDir, condaEnv, skipConda, tensorboardPort);
            if (!process.Start())
            {
                return new StartTensorboardResult(false, false, null, "Failed to start TensorBoard process.");
            }

            var earlyExitMessage = ObserveEarlyTensorboardExit(process);
            if (earlyExitMessage is not null)
            {
                return new StartTensorboardResult(false, false, null, earlyExitMessage);
            }

            return new StartTensorboardResult(true, false, $"http://localhost:{tensorboardPort}", $"TensorBoard started on port {tensorboardPort}.");
        }
        catch (Exception ex)
        {
            return new StartTensorboardResult(false, false, null, ex.Message);
        }
    }
    }
    public StartTensorboardResult StartTensorboard(StartTensorboardRequest request)
    {
        var rawResultsDir = string.IsNullOrWhiteSpace(request.ResultsDir) ? TrainingOptions.DefaultResultsDirectory : request.ResultsDir;
        var resultsDir = ResolveResultsDirectory(rawResultsDir);
        var condaEnv = string.IsNullOrWhiteSpace(request.CondaEnv) ? null : request.CondaEnv.Trim();
        var skipConda = request.SkipConda ?? false;
        var tensorboardPort = request.Port ?? 6006;

        if (!string.IsNullOrWhiteSpace(request.RunId))
        {
            var runDirectory = BuildRunDirectory(resultsDir, request.RunId);
            var metadata = TrainingRunMetadata.TryLoad(runDirectory);
            if (metadata is not null)
            {
                resultsDir = ResolveResultsDirectory(metadata.ResultsDirectory);
                condaEnv ??= metadata.CondaEnvironmentName;
                if (!request.SkipConda.HasValue)
                {
                    skipConda = metadata.SkipConda;
                }
            }
        }

        if (!Directory.Exists(resultsDir))
        {
            Directory.CreateDirectory(resultsDir);
        }

        var activePorts = GetActiveTcpPorts();
        if (activePorts.Contains(tensorboardPort))
        {
            return new StartTensorboardResult(false, true, $"http://localhost:{tensorboardPort}", $"TensorBoard already running on port {tensorboardPort}.");
        }

        try
        {
            var process = CreateTensorboardProcess(resultsDir, condaEnv, skipConda, tensorboardPort);
            if (!process.Start())
            {
                return new StartTensorboardResult(false, false, null, "Failed to start TensorBoard process.");
            }

            var earlyExitMessage = ObserveEarlyTensorboardExit(process);
            if (earlyExitMessage is not null)
            {
                return new StartTensorboardResult(false, false, null, earlyExitMessage);
            }

            return new StartTensorboardResult(true, false, $"http://localhost:{tensorboardPort}", $"TensorBoard started on port {tensorboardPort}.");
        }
        catch (Exception ex)
        {
            return new StartTensorboardResult(false, false, null, ex.Message);
        }
    }
    }
