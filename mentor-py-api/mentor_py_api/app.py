from __future__ import annotations

import io
import json
from fastapi import FastAPI, HTTPException
from fastapi.openapi.docs import get_swagger_ui_html, get_swagger_ui_oauth2_redirect_html
from fastapi.responses import HTMLResponse, JSONResponse, PlainTextResponse

from .cli_args import from_report, from_report_interpreter, from_training
from .models import ReportInterpreterRequest, ReportRequest, TrainingRequest, TrainingStatusRequest
from .options import ReportInterpreterOptions, ReportOptions, TrainingOptions
from .report_generator import TrainingReportGenerator
from .report_interpreter import ReportInterpreterRunner
from .run_store import TrainingRunStore
from .usage_text import get_report_interpreter_usage, get_report_usage, get_training_usage

app = FastAPI(title="Mentor Py API", version="0.1.0")
run_store = TrainingRunStore()


SWAGGER_UI_ROUTE = "/swagger"
SWAGGER_JSON_ROUTE = "/swagger/v1/swagger.json"
SWAGGER_OAUTH_ROUTE = "/swagger/oauth2-redirect"


@app.get(SWAGGER_UI_ROUTE, include_in_schema=False)
async def swagger_ui() -> HTMLResponse:
    return get_swagger_ui_html(
        openapi_url=SWAGGER_JSON_ROUTE,
        title="Mentor Py API - Swagger UI",
        oauth2_redirect_url=SWAGGER_OAUTH_ROUTE,
        swagger_ui_parameters={"displayRequestDuration": True},
    )


@app.get(SWAGGER_OAUTH_ROUTE, include_in_schema=False)
async def swagger_ui_redirect() -> HTMLResponse:
    return get_swagger_ui_oauth2_redirect_html()


@app.get(SWAGGER_JSON_ROUTE, include_in_schema=False)
async def swagger_json() -> JSONResponse:
    return JSONResponse(app.openapi())

@app.on_event("startup")
async def startup_event() -> None:
    print("[Resume] Checking for unfinished training runs...")
    messages = run_store.resume_unfinished_runs(log=lambda msg: print(f"[Resume] {msg}"))
    if not messages:
        print("[Resume] No unfinished training runs found.")


@app.get("/health")
def health_check() -> dict:
    return {"status": "ok"}


@app.post("/train")
async def train(request: TrainingRequest) -> JSONResponse:
    resolved_env_path = request.env_path or None
    resolved_config = request.config or "config/ppo/3DBall.yaml"
    resolved_run_id = _normalize_run_id(request.run_id)

    cli_args = from_training(request, resolved_env_path, resolved_config, resolved_run_id)
    options, error = TrainingOptions.try_parse(cli_args)
    if not options:
        raise HTTPException(status_code=400, detail={"error": error or "Invalid training options.", "usage": get_training_usage()})

    start_result = run_store.try_start(options)
    if not start_result.is_started or not start_result.run:
        raise HTTPException(
            status_code=409,
            detail={
                "error": start_result.message or f"A training session with runId '{options.run_id}' is already running.",
                "runId": options.run_id,
            },
        )

    payload = {
        "success": True,
        "runId": start_result.run.options.run_id,
        "status": "running",
        "resultsDirectory": start_result.run.options.results_directory,
        "logPath": start_result.run.log_path,
        "tensorboardUrl": start_result.run.tensorboard_url,
    }
    return JSONResponse(payload)


@app.post("/train-status")
async def train_status(request: TrainingStatusRequest) -> JSONResponse:
    if not request.run_id:
        raise HTTPException(status_code=400, detail={"error": "runId is required."})
    status_payload = run_store.get_status(request.run_id, request.results_dir)
    return JSONResponse(status_payload.__dict__)


@app.post("/report")
async def report(request: ReportRequest) -> JSONResponse:
    if not request.run_id:
        raise HTTPException(status_code=400, detail={"error": "runId is required.", "usage": get_report_usage()})

    cli_args = from_report(request)
    options, error = ReportOptions.try_parse(cli_args)
    if not options:
        raise HTTPException(status_code=400, detail={"error": error or "Invalid report options.", "usage": get_report_usage()})

    generator = TrainingReportGenerator(options)
    try:
        report_data = await generator.generate_report()
    except Exception as exc:  # noqa: BLE001
        raise HTTPException(status_code=400, detail={"error": str(exc)}) from exc

    return JSONResponse(report_data)


@app.post("/report-interpreter")
async def report_interpreter(request: ReportInterpreterRequest):  # -> Response
    if not request.run_id:
        raise HTTPException(
            status_code=400,
            detail={"error": "runId is required.", "usage": get_report_interpreter_usage()},
        )

    cli_args = from_report_interpreter(request)
    options, error = ReportInterpreterOptions.try_parse(cli_args)
    if not options:
        raise HTTPException(
            status_code=400,
            detail={"error": error or "Invalid interpreter options.", "usage": get_report_interpreter_usage()},
        )

    output = io.StringIO()
    errors = io.StringIO()
    runner = ReportInterpreterRunner(options, output_writer=output, error_writer=errors)
    exit_code = await runner.run()

    error_text = errors.getvalue().strip()
    if error_text:
        raise HTTPException(
            status_code=400,
            detail={"error": error_text, "usage": get_report_interpreter_usage(), "exitCode": exit_code},
        )

    payload_text = output.getvalue().strip()
    try:
        payload = json.loads(payload_text)
    except json.JSONDecodeError:
        payload = None

    if exit_code != 0:
        raise HTTPException(
            status_code=400,
            detail={"error": "report-interpreter failed", "output": payload_text, "exitCode": exit_code},
        )

    if payload is not None:
        return JSONResponse(payload)
    return PlainTextResponse(payload_text, media_type="application/json")


def _normalize_run_id(requested_run_id: str | None) -> str | None:
    if not requested_run_id or not requested_run_id.strip():
        return None
    trimmed = requested_run_id.strip()
    if trimmed.lower() == "first3dballrun":
        return None
    return trimmed


def create_app() -> FastAPI:
    return app

