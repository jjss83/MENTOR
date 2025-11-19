from __future__ import annotations

import json
from pathlib import Path
from typing import Any, Dict

from .options import ReportOptions


class TrainingReportGenerator:
    def __init__(self, options: ReportOptions) -> None:
        self.options = options

    async def generate_report(self) -> Dict[str, Any]:
        run_directory = Path(self.options.results_directory) / self.options.run_id
        if not run_directory.exists():
            raise FileNotFoundError(f"Run directory not found at '{run_directory}'.")

        run_logs_directory = run_directory / "run_logs"
        if not run_logs_directory.exists():
            raise FileNotFoundError(f"Run logs directory not found at '{run_logs_directory}'.")

        training_status_path = run_logs_directory / "training_status.json"
        if not training_status_path.exists():
            raise FileNotFoundError(
                f"training_status.json not found at '{training_status_path}'. Ensure the run completed successfully."
            )

        artifacts: Dict[str, Any] = {}
        training_status_content = self._load_json(training_status_path)
        artifacts["trainingStatus"] = self._build_artifact(training_status_path, training_status_content)

        timers_path = run_logs_directory / "timers.json"
        if timers_path.exists():
            artifacts["timers"] = self._build_artifact(timers_path, self._load_json(timers_path))
        else:
            artifacts["timers"] = self._build_missing_artifact(timers_path)

        configuration_path = run_directory / "configuration.yaml"
        if configuration_path.exists():
            configuration_text = configuration_path.read_text(encoding="utf-8", errors="ignore")
            artifacts["configuration"] = self._build_artifact(configuration_path, configuration_text)
        else:
            artifacts["configuration"] = self._build_missing_artifact(configuration_path)

        return {
            "runId": self.options.run_id,
            "resultsDirectory": self.options.results_directory,
            "runDirectory": str(run_directory),
            "runLogsDirectory": str(run_logs_directory),
            "artifacts": artifacts,
        }

    @staticmethod
    def _build_artifact(path: Path, content: Any) -> Dict[str, Any]:
        return {"path": str(path), "exists": True, "content": content}

    @staticmethod
    def _build_missing_artifact(path: Path) -> Dict[str, Any]:
        return {"path": str(path), "exists": False, "content": None}

    @staticmethod
    def _load_json(path: Path) -> Any:
        try:
            return json.loads(path.read_text(encoding="utf-8"))
        except json.JSONDecodeError as exc:
            raise ValueError(f"Failed to parse JSON from '{path}': {exc}") from exc
