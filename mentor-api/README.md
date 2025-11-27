# Mentor API

This minimal ASP.NET Core service exposes HTTP endpoints that wrap the Mentor training runner so you can trigger ML-Agents training jobs, query their status, and generate human-readable progress reports. It is intended to run side-by-side with your Unity environment builds and the ML-Agents Python toolchain on the same host.

## Prerequisites
- .NET 9 SDK (matches the `net9.0` target of `MentorApi.csproj`).
- Unity environment executables exported with ML-Agents enabled, or plan to press Play in the Unity Editor (omit `envPath` for that flow).
- A configured ML-Agents Python environment (typically via Conda) available on the machine.

## Run Locally
```bash
cd mentor-api
 dotnet run
```
This launches Kestrel on `http://localhost:5113` with Swagger UI enabled in `Development` mode. When the API starts it attempts to resume any unfinished training runs it finds under the default results directory (`X:/workspace/MENTOR/ml-agents-training-results`).

### Run with hot reload (dotnet watch)
Use `dotnet watch` during development to rebuild and restart the API when files change:

```bash
dotnet watch --project mentor-api run
```

The watcher restarts on saves; stop it with Ctrl+C.

## HTTP Endpoints
| Method | Route | Description |
| --- | --- | --- |
| `GET` | `/health` | Basic liveness probe; returns `{ "status": "ok" }` when the API is ready. |
| `POST` | `/train` | Starts a new ML-Agents training job via the CLI runner. |
| `POST` | `/train-status` | Returns the latest known status for a given `runId`. |
| `POST` | `/report` | Generates a JSON summary for a completed run. |

Swagger exposes example payloads for each endpoint when you browse `http://localhost:5113/swagger`.

## Training Request Payload
`/train` accepts the following JSON contract:

| Field | Type | Notes |
| --- | --- | --- |
| `envPath` | string | Full path to your Unity environment executable (`.exe`). Optional when you'll launch the Unity Editor yourself or when resuming with a stored path. |
| `config` | string | Path to the ML-Agents trainer YAML file. Defaults to `config/ppo/3DBall.yaml`. |
| `runId` | string | Optional custom run identifier; otherwise a timestamped ID is generated. |
| `resultsDir` | string | Directory where ML-Agents will write training artifacts. Defaults to `X:/workspace/MENTOR/ml-agents-training-results`. |
| `condaEnv` | string | Name of the Conda env that contains ML-Agents (`mlagents` by default). |
| `basePort` | int | Port offset for environment launches; if omitted, a free block is auto-selected starting at 5005 so multiple runs can coexist. |
| `noGraphics` | bool | Mirrors `--no-graphics`. |
| `skipConda` | bool | Skip Conda activation if tooling is already on `PATH`. |
| `tensorboard` | bool | Launch TensorBoard alongside training. |

### cURL example
```bash
curl -X POST http://localhost:5113/train \
  -H "Content-Type: application/json" \
  -d '{
        "noGraphics": true,
        "tensorboard": true,
        "envPath": "X:/workspace/Shhhunt/Builds/Shhhunt.exe",
        "config": "X:/workspace/MENTOR/mlagents-example-profiles/ShhhuntReachTarget/ShhhuntReachTarget.yaml"
      }'
```
To train against the Unity Editor instead of a built executable, drop `envPath`, start `/train`, and press Play in the Editor when ML-Agents waits for the connection.

### Sample payloads
Three ready-to-use payloads for common scenarios:
```json
{
  "noGraphics": true,
  "tensorboard": true,
  "envPath": "X:/workspace/Shhhunt/Builds/Shhhunt.exe",
  "config": "X:/workspace/MENTOR/mlagents-example-profiles/ShhhuntReachTarget/ShhhuntReachTarget.yaml"
}
```
```json
{
  "noGraphics": true,
  "tensorboard": true,
  "envPath": "X:/workspace/ml-agents/Project/Build/UnityEnvironment.exe",
  "config": "X:/workspace/MENTOR/mlagents-example-profiles/3DBall/3DBall.yaml"
}
```
```json
{
  "config": "X:/workspace/MENTOR/mlagents-example-profiles/ShhhuntReachTargetObstacle/ShhhuntReachTargetObstacle.yaml",
  "runId": "run-shhhunt-editor"
}
```

## Query Training Status
Request body:
```json
{
  "runId": "run-3DBall-2024-08-10-153000",
  "resultsDir": "X:/workspace/MENTOR/ml-agents-training-results"
}
```
Example response:
```json
{
  "runId": "run-3DBall-2024-08-10-153000",
  "status": "running",
  "completed": false,
  "exitCode": null,
  "resultsDirectory": "X:/workspace/MENTOR/ml-agents-training-results",
  "trainingStatusPath": "X:/workspace/MENTOR/ml-agents-training-results/run-3DBall-2024-08-10-153000/run_logs/training_status.json",
  "message": null,
  "tensorboardUrl": "http://localhost:6006"
}
```

## Generate Reports
- `/report` reuses the CLI `report` command to produce a JSON summary document. Use it after training has finished.

Typical request:
```json
{
  "runId": "run-3DBall-2024-08-10-153000",
  "resultsDir": "X:/workspace/MENTOR/ml-agents-training-results"
}
```

## Troubleshooting
- Verify the Unity executable path and YAML exist; the API surfaces parser errors straight from `TrainingOptions.TryParse`.
- Conflicts (`409`) indicate a `runId` is already active; query `/train-status` or choose a new identifier.
- Inspect `mentor-api.log` within the run folder to see the raw CLI output emitted by `TrainingSessionRunner`.
- If TensorBoard was requested, open `http://localhost:6006` after training starts.

