# Mentor ML API

Minimal ASP.NET Core Web API that shells out to the local Unity ML-Agents installation and exposes APIs for running jobs and inspecting their status.

## Prerequisites

- Windows machine with .NET 9 SDK
- Unity ML-Agents repo cloned to `X:\workspace\ml-agents`
- Conda environment named `mlagents` that can run `mlagents-learn`

## Configuration

`appsettings.json` contains the `MlAgents` section. Common overrides:

| Setting | Notes |
| --- | --- |
| `WorkingDirectory` | Path to the `ml-agents` repo. |
| `UseCondaRun` | When `true`, prefixes commands with `conda run --no-capture-output -n <env>`. Defaults to `true` so the service automatically runs inside the configured Conda env. |
| `CondaExecutable` | Only used when `UseCondaRun` is `true`; point to `conda.exe` if needed. |
| `CondaEnvironmentName` | Name of the Conda env that holds ML-Agents (required when `UseCondaRun` is `true`). |
| `DefaultConfigPath` | Trainer YAML relative to `WorkingDirectory`. |
| `DefaultUnityEnvironmentPath` | Optional built Unity player path. Not set by default so runs expect you to hit Play in the Editor unless you supply an `environmentPath`. |
| `DefaultNoGraphics` | Adds `--no-graphics` when `true`. Defaults to `false` per the official docs. |
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

### Request Payload

Most body fields map directly to the [`mlagents-learn` CLI switches documented here](https://github.com/bascoul/Ml-Agents/blob/master/docs/Training-ML-Agents.md). Paths are resolved relative to the configured ML-Agents working directory, and optional flags are omitted unless explicitly set.

| Field | CLI flag / argument | Notes |
| --- | --- | --- |
| `configPath` | `<trainer-config-file>` | Overrides the trainer YAML (required unless a default is configured). |
| `runId` | `--run-id` | When omitted the API generates `mentor-<timestamp>`. |
| `environmentPath` | `--env` | Optional Unity executable path. Leave blank to attach via the Editor. |
| `noGraphics` | `--no-graphics` | Defaults to `false` so you can still render visuals unless you opt in. |
| `curriculumPath` | `--curriculum` | Optional curriculum JSON that must exist on disk. |
| `keepCheckpoints` | `--keep-checkpoints` | Positive integer describing how many checkpoints to retain. |
| `lesson` | `--lesson` | Non-negative lesson index to start curriculum training from. |
| `loadModel` | `--load` | Start from an existing model stored under `models/<runId>/`. |
| `numRuns` | `--num-runs` | Positive integer for benchmarking concurrent training sessions. |
| `saveFrequency` | `--save-freq` | Positive integer controlling checkpoint frequency. |
| `seed` | `--seed` | Integer seed forwarded directly to ML-Agents. |
| `slow` | `--slow` | Runs the Unity player using inference-time time scale/target FPS. |
| `train` | `--train` / `--inference` | `true` adds `--train`, `false` adds `--inference`, and `null` leaves the mode unspecified (letting ML-Agents defaults apply). |
| `workerId` | `--worker-id` | Non-negative integer used when launching multiple envs simultaneously. |
| `dockerTargetName` | `--docker-target-name` | Useful when invoking inside Docker-based stacks. |
| `additionalArguments` | (verbatim) | Appended after all generated switches for advanced flags such as `--resume`, sampler files, etc. |

Unspecified fields are simply omitted from the generated command, so the underlying defaults from ML-Agents still apply.

## Checking Running Processes

Call `GET /mlagents/processes` to see any ML-Agents jobs that are currently running via this service. A typical response looks like:

```json
[
  {
    "id": "7d6f1c8e-4d0d-4ad5-9276-71a7365ce9bf",
    "runId": "mentor-3dball",
    "processId": 14352,
    "command": "conda run --no-capture-output -n mlagents ...",
    "workingDirectory": "X:/workspace/ml-agents",
    "startedAt": "2025-11-10T18:35:05.214109Z",
    "elapsed": "00:01:43.8123456"
  }
]
```

When no processes are active the endpoint returns an empty array.

## Integration Notes

- Use whichever request fields mirror the CLI flags you need; the service now covers the documented switches without relying on raw `additionalArguments`.
- Any file path you supply (config, environment, curriculum) is resolved relative to the configured ML-Agents working directory when it is not absolute.
- `UseCondaRun` is enabled by default so the service wraps execution via `conda run -n <env>`. Disable it only if you need to call `mlagents-learn` directly (for example, when Conda isn't available).
- If `conda` is not on PATH, set `CondaExecutable` to the absolute path (e.g. `C:\tools\miniconda3\Scripts\conda.exe`).
- The API is Windows-focused because it shells through `cmd.exe`; adjust the `ProcessStartInfo` logic if you need cross-platform support later.





