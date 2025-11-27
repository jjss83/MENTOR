# Mentor Webapp

Minimal single-page monitor for Mentor API training runs.

## Features
- Apple-inspired glassy UI with live health indicator.
- Polls `/health` and `/train-status` every 5 seconds.
- Summaries for running/completed/failed runs plus log tails per run.
- Editable API base URL (stored in localStorage).

## Use
1. Start the API (`dotnet run` in `mentor-api`), which defaults to `http://localhost:5113`.
2. Open `mentor-webapp/index.html` in a modern browser.
3. Adjust the API base field if your API is hosted elsewhere, then hit **Set API**.

> Note: If the browser blocks requests due to CORS, serve this folder from the same host/port as the API or enable CORS on the API.
