# Mentor Py API

Python implementation of the Mentor automation API for orchestrating Unity ML-Agents training runs from HTTP requests. The service mirrors the .NET `mentor-api` surface area so that dashboards or MCP tools can kick off training, monitor progress, generate structured reports, and optionally ask an LLM to interpret the results.

## Highlights

- **FastAPI service** that exposes `/train`, `/train-status`, `/report`, `/report-interpreter`, and `/health` endpoints.
- **Training runner** that shells out to `mlagents-learn`, optionally through a Conda environment, and can launch TensorBoard beside the run.
- **Run store & resumption** keeps track of active jobs, prevents duplicate `runId`s, and resumes unfinished runs on startup by reading `run_metadata.json` in each results directory.
- **Report generation** packages `training_status.json`, timers, and configuration artifacts into a repeatable JSON payload.
- **Report interpreter** can call OpenAI chat completions (or just echo the JSON) to provide a summarized explanation of the run.

## Requirements

| Requirement | Notes |
| --- | --- |
| Python 3.10+ | FastAPI and pydantic rely on 3.10 features. |
| `mlagents-learn` & `tensorboard` | Installed either globally or inside a Conda env named `mlagents` (default). |
| Unity built environment | Provide the `.exe` (or skip `envPath` to attach to an editor run). |
| OpenAI API key (optional) | Needed only when calling `/report-interpreter` with LLM mode. Set `OPENAI_API_KEY` or pass `openaiApiKey`. |

## Installation

```powershell
cd mentor-py-api
python -m venv .venv
.\.venv\Scripts\activate
pip install -e .
# or include tooling: pip install -e .[dev]
```

## Running the API

```powershell
# default: uvicorn on 127.0.0.1:5113
python -m mentor_py_api

# alternative with hot-reload
uvicorn mentor_py_api.app:app --host 0.0.0.0 --port 5113 --reload
```


Once the server is running, open http://127.0.0.1:5113/swagger for the Swagger UI (the JSON lives at /swagger/v1/swagger.json). This mirrors the .NET mentor-api surface so tooling that expects /swagger keeps working.

When the server starts it logs "[Resume]" messages while scanning `X:\workspace\ml-agents\results` (or the directory you configure) for unfinished runs and reattaches to them.

## Training lifecycle

1. `POST /train` to launch a run via `mlagents-learn`.
2. `POST /train-status` any time to poll exit code/log paths.
3. `POST /report` once the run has produced artifacts.
4. `POST /report-interpreter` to summarize the report JSON or run an OpenAI completion.

### `/train`

| Field | Type | Description | Default |
| --- | --- | --- | --- |
| `envPath` | string | Path to the Unity environment executable. | Required if you are not connecting to the Editor. |
| `config` | string | Trainer YAML consumed by `mlagents-learn`. | `config/ppo/3DBall.yaml` |
| `runId` | string | Identifier for the run; used as the folder name. `first3dballrun` is treated as "auto" so dashboards can reuse tutorials. | Derived from behavior + timestamp when omitted. |
| `resultsDir` | string | Where to write `<runId>`. | `X:\workspace\ml-agents\results` |
| `condaEnv` | string | Conda environment that contains ML-Agents tooling. | `mlagents` |
| `basePort` | number | Overrides the ML-Agents base port. | Determined by ML-Agents |
| `noGraphics` | bool | Launch Unity in headless mode. | `false` |
| `skipConda` | bool | Set to `true` when `mlagents-learn` is already on PATH (no `conda run`). | `false` |
| `tensorboard` | bool | Also launch TensorBoard pointed at the run directory. | `false` |

Example request:

```powershell
curl -X POST http://127.0.0.1:5113/train `
  -H "Content-Type: application/json" `
  -d '{
    "envPath": "X:/workspace/ml-agents/Builds/3DBall.exe",
    "config": "config/ppo/3DBall.yaml",
    "resultsDir": "X:/workspace/ml-agents/results",
    "runId": "run-3dball-dev",
    "condaEnv": "mlagents",
    "basePort": 5005,
    "noGraphics": true,
    "tensorboard": true
  }'
```

Successful responses look like:

```json
{
  "success": true,
  "runId": "run-3dball-dev",
  "status": "running",
  "resultsDirectory": "X:/workspace/ml-agents/results",
  "logPath": "X:/workspace/ml-agents/results/run-3dball-dev/run_logs/mentor.log",
  "tensorboardUrl": "http://localhost:6006"
}
```

### `/train-status`

Body fields:

```json
{
  "runId": "run-3dball-dev",
  "resultsDir": "X:/workspace/ml-agents/results"
}
```

The response echoes `runId`, `status` (`running`, `succeeded`, `failed`, `unknown`, or `not-found`), whether it has `completed`, any `exitCode`, and where the `trainingStatusPath` lives. If the API is restarted, this endpoint falls back to reading `training_status.json` from disk.

### `/report`

Generates a structured blob that includes:

- `runId`, `resultsDirectory`, `runDirectory`, `runLogsDirectory`
- `artifacts.trainingStatus` (parsed `training_status.json`)
- `artifacts.timers` (or `exists: false` if missing)
- `artifacts.configuration` (YAML text or a missing marker)

Request payload matches `report` CLI arguments:

```json
{ "runId": "run-3dball-dev", "resultsDir": "X:/workspace/ml-agents/results" }
```

### `/report-interpreter`

Extends `/report` by running the JSON through `ReportInterpreterRunner`.

| Field | Description |
| --- | --- |
| `prompt` | Custom instructions for the interpreter. Default: "Explain current results". |
| `openaiModel` | Chat-completions model to call. Default: `gpt-4o-mini`. |
| `openaiApiKey` | Overrides the `OPENAI_API_KEY` environment variable. |
| `checkOpenAi` | When `true`, skips report generation and just tests OpenAI connectivity. |

If no API key is available the service still returns the JSON payload plus a note saying the LLM call was skipped. Errors are reported with `exitCode`, making it easy to bubble up to clients.

### `/health`

Simple readiness probe that returns `{ "status": "ok" }`.

## Development tips

- Linting: `ruff check .`
- The project exposes `mentor_py_api.app:create_app()` for unit tests or ASGI lifespan control.
- Logs for each training run live under `<resultsDir>/<runId>/run_logs/mentor.log`. TensorBoard output is streamed into the same file.
- Run metadata is captured in `run_metadata.json`; delete that file (or the whole folder) to prevent automatic resumption on boot.

## Related projects

This package is intended to be a drop-in replacement for the .NET `mentor-api` so the existing MCP/CLI tooling can switch between runtimes without needing to change request bodies.
