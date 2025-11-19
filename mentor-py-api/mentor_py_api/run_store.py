from __future__ import annotations

import asyncio
import json
import os
from dataclasses import dataclass
from pathlib import Path
from threading import RLock
from typing import Callable, Dict, List, Optional

from .options import DEFAULT_RESULTS_DIRECTORY, TrainingOptions
from .training_runner import TrainingSessionRunner


@dataclass
class TrainingStatusPayload:
    run_id: str
    status: str
    completed: bool
    exit_code: Optional[int]
    results_directory: Optional[str]
    training_status_path: Optional[str]
    message: Optional[str]
    tensorboard_url: Optional[str]

    @classmethod
    def from_files(cls, run_id: str, results_directory: str) -> "TrainingStatusPayload":
        training_status_path = build_training_status_path(results_directory, run_id)
        if os.path.isfile(training_status_path):
            status_text = _try_read_status(training_status_path)
            normalized = _normalize_status(status_text)
            return cls(
                run_id=run_id,
                status=normalized,
                completed=True,
                exit_code=None,
                results_directory=results_directory,
                training_status_path=training_status_path,
                message=None,
                tensorboard_url=None,
            )

        run_directory = build_run_directory(results_directory, run_id)
        if os.path.isdir(run_directory):
            return cls(
                run_id=run_id,
                status="unknown",
                completed=False,
                exit_code=None,
                results_directory=results_directory,
                training_status_path=training_status_path,
                message="Run directory exists but training_status.json has not been written yet.",
                tensorboard_url=None,
            )

        return cls(
            run_id=run_id,
            status="not-found",
            completed=False,
            exit_code=None,
            results_directory=results_directory,
            training_status_path=training_status_path,
            message=f"No run data found at '{run_directory}'.",
            tensorboard_url=None,
        )


def _normalize_status(status: Optional[str]) -> str:
    lookup = {
        "success": "succeeded",
        "succeeded": "succeeded",
        "completed": "succeeded",
        "failure": "failed",
        "failed": "failed",
    }
    if status:
        normalized = status.lower()
        return lookup.get(normalized, status)
    return "completed"


def _try_read_status(path: str) -> Optional[str]:
    try:
        with open(path, "r", encoding="utf-8") as stream:
            data = json.load(stream)
        if isinstance(data, dict):
            value = data.get("status")
            if isinstance(value, str):
                return value
        return None
    except (OSError, json.JSONDecodeError):
        return None


@dataclass
class TrainingRunOutcome:
    exit_code: Optional[int]
    error: Optional[Exception]

    @property
    def is_success(self) -> bool:
        return self.error is None and (self.exit_code or 0) == 0

    @classmethod
    def from_exit_code(cls, exit_code: int) -> "TrainingRunOutcome":
        return cls(exit_code=exit_code, error=None)

    @classmethod
    def from_error(cls, error: Exception) -> "TrainingRunOutcome":
        return cls(exit_code=None, error=error)


@dataclass
class TrainingRunMetadata:
    env_path: Optional[str]
    config_path: str
    run_id: str
    results_directory: str
    conda_environment_name: str
    base_port: Optional[int]
    no_graphics: bool
    skip_conda: bool
    launch_tensorboard: bool

    metadata_file_name = "run_metadata.json"

    @classmethod
    def save(cls, run_directory: Path, options: TrainingOptions) -> None:
        metadata_path = run_directory / "run_logs" / cls.metadata_file_name
        metadata_path.parent.mkdir(parents=True, exist_ok=True)
        payload = {
            "envPath": options.env_executable_path,
            "configPath": options.trainer_config_path,
            "runId": options.run_id,
            "resultsDirectory": options.results_directory,
            "condaEnvironmentName": options.conda_environment_name,
            "basePort": options.base_port,
            "noGraphics": options.no_graphics,
            "skipConda": options.skip_conda,
            "launchTensorboard": options.launch_tensorboard,
        }
        metadata_path.write_text(json.dumps(payload, indent=2), encoding="utf-8")

    @classmethod
    def try_load(cls, run_directory: Path) -> Optional["TrainingRunMetadata"]:
        metadata_path = run_directory / "run_logs" / cls.metadata_file_name
        if not metadata_path.exists():
            return None
        try:
            payload = json.loads(metadata_path.read_text(encoding="utf-8"))
            return cls(
                env_path=payload.get("envPath"),
                config_path=payload["configPath"],
                run_id=payload["runId"],
                results_directory=payload["resultsDirectory"],
                conda_environment_name=payload["condaEnvironmentName"],
                base_port=payload.get("basePort"),
                no_graphics=payload.get("noGraphics", False),
                skip_conda=payload.get("skipConda", False),
                launch_tensorboard=payload.get("launchTensorboard", False),
            )
        except (OSError, KeyError, json.JSONDecodeError):
            return None

    def to_options(self) -> TrainingOptions:
        return TrainingOptions(
            env_executable_path=self.env_path,
            trainer_config_path=self.config_path,
            run_id=self.run_id,
            results_directory=self.results_directory,
            conda_environment_name=self.conda_environment_name,
            base_port=self.base_port,
            no_graphics=self.no_graphics,
            skip_conda=self.skip_conda,
            launch_tensorboard=self.launch_tensorboard,
        )


class TrainingRunState:
    def __init__(
        self,
        options: TrainingOptions,
        log_path: str,
        tensorboard_url: Optional[str],
        task: asyncio.Task[TrainingRunOutcome],
    ) -> None:
        self.options = options
        self.log_path = log_path
        self.tensorboard_url = tensorboard_url
        self._task = task

    @classmethod
    def start_new(cls, options: TrainingOptions) -> "TrainingRunState":
        loop = asyncio.get_running_loop()
        run_directory = Path(options.results_directory) / options.run_id
        run_logs_directory = run_directory / "run_logs"
        run_logs_directory.mkdir(parents=True, exist_ok=True)
        TrainingRunMetadata.save(run_directory, options)
        log_path = run_logs_directory / "mentor-api.log"
        runner = TrainingSessionRunner(options, str(log_path))

        async def _execute() -> TrainingRunOutcome:
            try:
                exit_code = await runner.run()
                return TrainingRunOutcome.from_exit_code(exit_code)
            except Exception as exc:  # noqa: BLE001
                return TrainingRunOutcome.from_error(exc)

        task: asyncio.Task[TrainingRunOutcome] = loop.create_task(_execute())
        return cls(options, str(log_path), runner.tensorboard_url, task)

    @property
    def is_completed(self) -> bool:
        return self._task.done()

    def to_payload(self) -> TrainingStatusPayload:
        status = "running"
        exit_code: Optional[int] = None
        message: Optional[str] = None

        if self._task.cancelled():
            status = "failed"
            message = "Training was canceled."
        elif self._task.done():
            try:
                outcome = self._task.result()
                exit_code = outcome.exit_code
                status = "succeeded" if outcome.is_success else "failed"
                if outcome.error:
                    message = str(outcome.error)
            except Exception as exc:  # noqa: BLE001
                status = "failed"
                message = str(exc)

        training_status_path = build_training_status_path(self.options.results_directory, self.options.run_id)
        completed = status != "running"
        return TrainingStatusPayload(
            run_id=self.options.run_id,
            status=status,
            completed=completed,
            exit_code=exit_code,
            results_directory=self.options.results_directory,
            training_status_path=training_status_path,
            message=message,
            tensorboard_url=self.tensorboard_url,
        )


@dataclass
class TrainingStartResult:
    is_started: bool
    run: Optional[TrainingRunState]
    message: Optional[str]

    @classmethod
    def started(cls, run: TrainingRunState) -> "TrainingStartResult":
        return cls(True, run, None)

    @classmethod
    def conflict(cls, run: TrainingRunState, message: Optional[str]) -> "TrainingStartResult":
        return cls(False, run, message)


class TrainingRunStore:
    def __init__(self) -> None:
        self._runs: Dict[str, TrainingRunState] = {}
        self._lock = RLock()

    def try_start(self, options: TrainingOptions) -> TrainingStartResult:
        with self._lock:
            existing = self._runs.get(options.run_id)
            if existing and not existing.is_completed:
                return TrainingStartResult.conflict(existing, f"Training run '{options.run_id}' is already in progress.")
            state = TrainingRunState.start_new(options)
            self._runs[options.run_id] = state
            return TrainingStartResult.started(state)

    def get_status(self, run_id: str, results_dir_override: Optional[str]) -> TrainingStatusPayload:
        with self._lock:
            tracked = self._runs.get(run_id)
        if tracked:
            return tracked.to_payload()
        results_dir = self._resolve_results_directory(results_dir_override)
        return TrainingStatusPayload.from_files(run_id, results_dir)

    def resume_unfinished_runs(
        self,
        log: Optional[Callable[[str], None]] = None,
        results_dir_override: Optional[str] = None,
    ) -> List[str]:
        messages: List[str] = []
        results_dir = self._resolve_results_directory(results_dir_override)
        if not os.path.isdir(results_dir):
            msg = f"Results directory '{results_dir}' does not exist. Nothing to resume."
            messages.append(msg)
            if log:
                log(msg)
            return messages

        for run_directory in Path(results_dir).iterdir():
            if not run_directory.is_dir():
                continue
            run_id = run_directory.name
            status_path = build_training_status_path(results_dir, run_id)
            status = _try_read_status(status_path)
            if _is_completed_status(status):
                continue

            metadata = TrainingRunMetadata.try_load(run_directory)
            if not metadata:
                skipped = f"Skipped '{run_id}' because run_metadata.json is missing or unreadable."
                messages.append(skipped)
                if log:
                    log(skipped)
                continue

            env_path = metadata.env_path
            if not env_path or not os.path.isfile(env_path) or not env_path.lower().endswith(".exe"):
                skipped = f"Skipped '{run_id}' because envPath is missing or not a valid .exe ({env_path or '<null>'})."
                messages.append(skipped)
                if log:
                    log(skipped)
                continue

            start_result = self.try_start(metadata.to_options())
            status_label = (status or "unknown").lower()
            if start_result.is_started:
                resumed = f"Resumed unfinished training '{run_id}' (previous status: {status_label})."
                messages.append(resumed)
                if log:
                    log(resumed)
            else:
                conflict = start_result.message or f"Training run '{run_id}' is already active."
                messages.append(conflict)
                if log:
                    log(conflict)
        return messages

    def _resolve_results_directory(self, results_dir_override: Optional[str]) -> str:
        candidate = results_dir_override or DEFAULT_RESULTS_DIRECTORY
        try:
            return os.path.abspath(candidate)
        except OSError:
            return candidate


def build_training_status_path(results_directory: str, run_id: str) -> str:
    return os.path.join(results_directory, run_id, "run_logs", "training_status.json")


def build_run_directory(results_directory: str, run_id: str) -> str:
    return os.path.join(results_directory, run_id)


def _is_completed_status(status: Optional[str]) -> bool:
    if not status:
        return False
    return status.lower() in {"succeeded", "success", "failed", "failure", "completed"}
