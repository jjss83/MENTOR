# Mentor API

This minimal ASP.NET Core service exposes HTTP endpoints that wrap the Mentor training runner so you can trigger ML-Agents training jobs and query their status. It is intended to run side-by-side with your Unity environment builds and the ML-Agents Python toolchain on the same host.

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
| `GET` | `/train-status` | Lists the latest known status for all runs (in-memory or found on disk). |
| `GET` | `/train-status/{runId}` | Returns the latest known status for the specified `runId`. |
| `POST` | `/tensorboard/start` | Starts TensorBoard for the results directory if it is not already running. |

Swagger exposes example payloads for each endpoint when you browse `http://localhost:5113/swagger`.

## Training Request Payload
`/train` accepts the following JSON contract:

| Field | Type | Notes |
| --- | --- | --- |
| `envPath` | string | Full path to your Unity environment executable (`.exe`). Optional when you'll launch the Unity Editor yourself or when resuming with a stored path. |
| `config` | string | Path to the ML-Agents trainer YAML file. Defaults to `config/ppo/3DBall.yaml`. |
| `runId` | string | Optional custom run identifier; otherwise `rt-YYMMDD-<n>` is generated (UTC date with a daily counter). |
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
- All runs: `GET /train-status?resultsDir=X:/workspace/MENTOR/ml-agents-training-results`
- Specific run: `GET /train-status/rt-241201-1?resultsDir=X:/workspace/MENTOR/ml-agents-training-results`

Example response for a single run:
```json
{
  "runId": "rt-241201-1",
  "status": "running",
  "completed": false,
  "exitCode": null,
  "resultsDirectory": "X:/workspace/MENTOR/ml-agents-training-results",
  "trainingStatusPath": "X:/workspace/MENTOR/ml-agents-training-results/rt-241201-1/run_logs/training_status.json",
  "message": null,
  "tensorboardUrl": "http://localhost:6006"
}
```

## Start TensorBoard
- POST `/tensorboard/start`
- Body fields:
  - `resultsDir` (optional): directory containing run outputs; defaults to `X:/workspace/MENTOR/ml-agents-training-results`.
  - `runId` (optional): if provided, metadata from the run is used to reuse its Conda env and skipConda preference.
  - `condaEnv` (optional): override Conda env name (defaults to `mlagents` when Conda is used).
  - `skipConda` (optional): set to `true` to call `tensorboard` directly without Conda.
  - `port` (optional): port to bind; defaults to 6006.

If TensorBoard is already running on the requested port, the endpoint returns the existing URL instead of starting a new process.

## Reports
The `/report` API endpoint has been removed. Use the CLI `report` command directly if you still need to generate a summary for a completed run.

## Troubleshooting
- Verify the Unity executable path and YAML exist; the API surfaces parser errors straight from `TrainingOptions.TryParse`.
- Conflicts (`409`) indicate a `runId` is already active; query `/train-status/{runId}` or choose a new identifier.
- Inspect `mentor-api.log` within the run folder to see the raw CLI output emitted by `TrainingSessionRunner`.
- If TensorBoard was requested, open `http://localhost:6006` after training starts.
