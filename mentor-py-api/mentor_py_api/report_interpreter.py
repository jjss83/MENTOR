from __future__ import annotations

import asyncio
import json
import os
from dataclasses import dataclass
from typing import Any, Dict, Optional, TextIO

import httpx

from .options import ReportInterpreterOptions, ReportOptions
from .report_generator import TrainingReportGenerator

MAX_RETRIES = 3
SYSTEM_PROMPT = (
    "You are the Report Interpreter Agent for Mentor CLI runs. Given the JSON payload, explain the current results "
    "concisely: identify run-id, missing artifacts, summarize training_status checkpoints/metadata, timers highlights, "
    "and configuration notes. Keep it short and actionable."
)


class ReportInterpreterRunner:
    def __init__(
        self,
        options: ReportInterpreterOptions,
        output_writer: Optional[TextIO] = None,
        error_writer: Optional[TextIO] = None,
    ) -> None:
        import sys

        self.options = options
        self.output_writer = output_writer or sys.stdout
        self.error_writer = error_writer or sys.stderr

    async def run(self) -> int:
        if self.options.check_openai:
            return await self._run_check()

        generator = TrainingReportGenerator(ReportOptions(self.options.run_id, self.options.results_directory))
        try:
            report = await generator.generate_report()
        except Exception as exc:  # noqa: BLE001
            self._write_error(f"Failed to generate report: {exc}")
            return 1

        payload = {"agent": "ReportInterpreter", "prompt": self.options.prompt, "report": report}
        api_key = self._resolve_api_key()
        if not api_key:
            self._emit_payload(payload, note="OPENAI_API_KEY not set; skipping LLM call.")
            return 0

        try:
            completion = await self._call_openai_with_retry(api_key, payload, self.options.prompt)
            self._emit_payload(payload, completion)
            return 0
        except OpenAiResponseError as exc:
            note = f"LLM call failed after retries: {exc.status_code} body: {exc.response_body}"
            self._emit_payload(payload, note=note)
            return 1
        except Exception as exc:  # noqa: BLE001
            self._emit_payload(payload, note=f"LLM call failed after retries: {exc}")
            return 1

    async def _run_check(self) -> int:
        payload = {
            "agent": "ReportInterpreter",
            "prompt": "Check OpenAI connectivity",
            "report": {"status": "noop"},
        }
        api_key = self._resolve_api_key()
        if not api_key:
            self._emit_payload(payload, note="OPENAI_API_KEY not set; cannot run check.")
            return 1

        try:
            completion = await self._call_openai_with_retry(api_key, payload, "Echo: ok")
            self._emit_payload(payload, completion)
            return 0
        except OpenAiResponseError as exc:
            note = f"LLM call failed after retries: {exc.status_code} body: {exc.response_body}"
            self._emit_payload(payload, note=note)
            return 1
        except Exception as exc:  # noqa: BLE001
            self._emit_payload(payload, note=f"LLM call failed after retries: {exc}")
            return 1

    def _resolve_api_key(self) -> Optional[str]:
        return self.options.openai_api_key or os.environ.get("OPENAI_API_KEY")

    def _emit_payload(self, payload: Dict[str, Any], completion: Optional[str] = None, note: Optional[str] = None) -> None:
        serializer_options = {"ensure_ascii": False, "indent": 2}
        root = {"request": payload, "note": note, "response": completion}
        self._write_line(json.dumps(root, **serializer_options))
        if completion:
            self._write_line()
            self._write_line("--- OpenAI Response (plain text) ---")
            self._write_line(completion)

    async def _call_openai_with_retry(self, api_key: str, payload: Dict[str, Any], user_prompt: str) -> str:
        delay = 1.0
        last_error: Optional[Exception] = None
        for attempt in range(1, MAX_RETRIES + 1):
            try:
                return await self._call_openai(api_key, payload, user_prompt)
            except OpenAiResponseError as exc:
                last_error = exc
                if exc.status_code != 429 or attempt == MAX_RETRIES:
                    break
            except Exception as exc:  # noqa: BLE001
                last_error = exc
                break
            await asyncio.sleep(delay)
            delay *= 2
        if isinstance(last_error, Exception):
            raise last_error
        raise RuntimeError("OpenAI call failed without an exception.")

    async def _call_openai(self, api_key: str, payload: Dict[str, Any], user_prompt: str) -> str:
        headers = {
            "Authorization": f"Bearer {api_key}",
            "Content-Type": "application/json",
            "User-Agent": "MentorTrainingRunner/1.0",
        }
        body = {
            "model": self.options.openai_model,
            "messages": [
                {"role": "system", "content": SYSTEM_PROMPT},
                {"role": "user", "content": user_prompt},
                {"role": "user", "content": json.dumps(payload, separators=(",", ":"))},
            ],
        }
        async with httpx.AsyncClient(timeout=60) as client:
            response = await client.post("https://api.openai.com/v1/chat/completions", headers=headers, json=body)
        if response.status_code >= 400:
            response_body = response.text
            truncated = response_body[:4000] + ("..." if len(response_body) > 4000 else "")
            raise OpenAiResponseError(response.status_code, truncated)
        data = response.json()
        message = data["choices"][0]["message"]["content"]
        if not message:
            raise RuntimeError("OpenAI response did not contain content.")
        return message

    def _write_line(self, message: Optional[str] = None) -> None:
        if message is None:
            self.output_writer.write("\n")
        else:
            self.output_writer.write(message + "\n")
        self.output_writer.flush()

    def _write_error(self, message: str) -> None:
        self.error_writer.write(message + "\n")
        self.error_writer.flush()


@dataclass(slots=True)
class OpenAiResponseError(Exception):
    status_code: int
    response_body: str

    def __str__(self) -> str:  # pragma: no cover - human readable message
        return f"OpenAI call failed with status {self.status_code}"
