# Mentor ML API

A minimal ASP.NET Core API that launches a Unity ML-Agents training session by wrapping the locally installed toolkit. The API assumes:

- Unity ML-Agents is already cloned to `X:\workspace\ml-agents`.
- A Conda environment named `mlagents` exposes the `mlagents-learn` CLI (matching the [Unity sample documentation](https://docs.unity3d.com/Packages/com.unity.ml-agents@4.0/manual/Sample.html)).

## Configuration

`appsettings.json` contains an `MlAgents` section that you can customize:

| Setting | Description |
| --- | --- |
| `WorkingDirectory` | Root of the `ml-agents` repo. This becomes the process working directory. |
| `CondaExecutable` | Defaults to `conda`. Point this to an alternate launcher if needed. |
| `CondaEnvironmentName` | The Conda environment that contains ML-Agents. |
| `DefaultConfigPath` | Trainer YAML used when the request omits `configPath`. The Unity sample uses `config/ppo/3DBall.yaml`. |
| `DefaultUnityEnvironmentPath` | Optional built player path for the Unity environment. Leave blank when training directly in the Editor. |
| `DefaultNoGraphics` | Adds `--no-graphics` when `true`. |
| `MaxOutputLines` | Maximum lines of stdout/stderr that are buffered and returned to the caller. |

Per-environment overrides can live in `appsettings.Development.json` or user-secrets.

## Running the API

```powershell
cd X:\workspace\MENTOR\MentorMlApi
 dotnet run
```

The API listens on the standard ASP.NET ports (configured in `launchSettings.json`).

## Triggering a training run

Send a POST request to `/mlagents/run`. The body is optional; any provided fields override the defaults.

Example using `curl`:

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

Sample response (abridged):

```json
{
  "command": "conda run --no-capture-output -n \"mlagents\" mlagents-learn \"X:/workspace/ml-agents/config/ppo/3DBall.yaml\" --run-id=mentor-3dball --env=\"X:/workspace/ml-agents/Project/Builds/3DBall\" --no-graphics",
  "workingDirectory": "X:/workspace/ml-agents",
  "exitCode": 0,
  "standardOutput": ["..."],
  "standardError": ["..."]
}
```

A non-zero `exitCode` indicates the CLI reported an error (for example, if the Unity environment is not reachable). The response includes the last `MaxOutputLines` lines from stdout/stderr to aid debugging.

## Companion Web Console

The `MentorMlUi` folder houses a React + Vite single-page app that calls the API and displays the latest run output.

1. Copy `.env.example` to `.env` if you need to point at a different API base URL (defaults to `https://localhost:7136`).
2. Install and start the dev server:

   ```powershell
   cd X:\workspace\MENTOR\MentorMlUi
    npm install
    npm run dev -- --open
   ```

3. The UI renders form inputs for all request parameters and streams the most recent stdout/stderr returned by the API.

`npm run build` emits a production bundle inside `MentorMlUi/dist` that can be served from any static host (or proxied by ASP.NET if desired).

## Notes

- Cancellation (Ctrl+C on the server process or HTTP client aborts) will terminate the ML-Agents process tree.
- To run a different sample from the Unity docs, supply a different `configPath`, `runId`, or `environmentPath` in the POST body.
- If `conda` is not on `PATH`, update `CondaExecutable` with the fully-qualified path to `conda.exe` or your preferred launcher.