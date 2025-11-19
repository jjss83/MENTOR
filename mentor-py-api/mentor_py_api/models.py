from __future__ import annotations

from typing import Optional

from pydantic import BaseModel, Field


class BaseRequest(BaseModel):
    class Config:
        populate_by_name = True
        allow_population_by_field_name = True


class TrainingRequest(BaseRequest):
    results_dir: Optional[str] = Field(None, alias="resultsDir")
    conda_env: Optional[str] = Field(None, alias="condaEnv")
    base_port: Optional[int] = Field(None, alias="basePort")
    no_graphics: Optional[bool] = Field(None, alias="noGraphics")
    skip_conda: Optional[bool] = Field(None, alias="skipConda")
    tensorboard: Optional[bool]
    env_path: Optional[str] = Field(None, alias="envPath")
    config: Optional[str]
    run_id: Optional[str] = Field(None, alias="runId")


class TrainingStatusRequest(BaseRequest):
    results_dir: Optional[str] = Field(None, alias="resultsDir")
    run_id: Optional[str] = Field(None, alias="runId")


class ReportRequest(BaseRequest):
    results_dir: Optional[str] = Field(None, alias="resultsDir")
    run_id: Optional[str] = Field(None, alias="runId")


class ReportInterpreterRequest(ReportRequest):
    prompt: Optional[str]
    openai_model: Optional[str] = Field(None, alias="openaiModel")
    openai_api_key: Optional[str] = Field(None, alias="openaiApiKey")
    check_openai: Optional[bool] = Field(None, alias="checkOpenAi")
