# Mentor ML UI

Single-page React + Vite console that calls the Mentor ML API and shows the latest stdout/stderr returned by a training run.

## Prerequisites

- Node.js 18+ (Node 22 is already installed in this workspace)
- Mentor ML API running locally (defaults to `https://localhost:7136`).

## Environment

Copy `.env.example` to `.env` if you need to override the API base URL:

```powershell
Copy-Item .env.example .env
# Edit .env if the API lives on another host/port
```

`VITE_API_BASE_URL` must match the public URL of the ASP.NET API (including protocol).

## Development

```powershell
cd X:\workspace\MENTOR\MentorMlUi
 npm install      # first time only
 npm run dev -- --open
```

The Vite dev server proxies API calls directly to the configured base URL, so ensure the backend is already running.

## Production Build

```powershell
npm run build
npm run preview   # optional smoke test using Vite's preview server
```

The `dist/` folder contains static assets ready to be hosted behind IIS, Nginx, Azure Static Web Apps, or even served by the ASP.NET backend via static file middleware.

## Usage

- Fill out the run id, config path, environment path, and optional CLI arguments (one per line).
- Submit the form to call `/mlagents/run`.
- The "Latest Response" panel shows command metadata, exit code, timestamps, and the buffered stdout/stderr returned by the API.
- Use the status pill (Idle/Running/Success/Error) to quickly identify outcome; reset clears the current form and logs.

Feel free to extend the UI with run history, polling, or WebSocket streaming if the API later exposes those capabilities.