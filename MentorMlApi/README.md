# Mentor ML API

Minimal ASP.NET Core Web API that shells out to the local Unity ML-Agents installation and exposes a single `/mlagents/run` endpoint.

## Prerequisites

- Windows machine with .NET 9 SDK
- Unity ML-Agents repo cloned to `X:\workspace\ml-agents`
- Conda environment named `mlagents` that can run `mlagents-learn`

## Configuration

`appsettings.json` contains the `MlAgents` section. Common overrides:

| Setting | Notes |
| --- | --- |
| `WorkingDirectory` | Path to the `ml-agents` repo. |
| `CondaExecutable` | Defaults to `conda`; point to `conda.exe` if needed. |
| `CondaEnvironmentName` | Name of the Conda env that holds ML-Agents. |
| `DefaultConfigPath` | Trainer YAML relative to `WorkingDirectory`. |
| `DefaultUnityEnvironmentPath` | Optional built Unity player path. Leave blank to connect manually. |
| `DefaultNoGraphics` | Adds `--no-graphics` when `true`. |
| `MaxOutputLines` | Number of stdout/stderr lines returned to the caller. |

Use `appsettings.Development.json` or user-secrets for machine-specific paths.

## Running

```powershell
cd X:\workspace\MENTOR\MentorMlApi
 dotnet run
```

The app listens on the default HTTPS port from `launchSettings.json` (7136 during local development).

## Triggering A Run

```bash
curl -X POST https://localhost:7136/mlagents/run \
  -H "Content-Type: application/json" \
  -d '{
        "runId": "mentor-3dball",
        "configPath": "config/ppo/3DBall.yaml",
        "environmentPath": "Project/Builds/3DBall",
        "noGraphics": true
      }'
```

The response echoes the full command, exit code, timestamps, and tail of stdout/stderr so you can inspect failures. Canceling the HTTP request (or stopping the server) terminates the spawned process tree.

## Integration Notes

- When running different Unity samples, override `configPath`, `environmentPath`, or supply extra CLI flags via `additionalArguments`.
- If `conda` is not on PATH, set `CondaExecutable` to the absolute path (e.g. `C:\tools\miniconda3\Scripts\conda.exe`).
- The API is Windows-focused because it shells through `cmd.exe`; adjust the `ProcessStartInfo` logic if you need cross-platform support later.