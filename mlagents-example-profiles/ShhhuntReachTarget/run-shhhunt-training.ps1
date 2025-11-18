param(
    [string]$RunId = "run_$(Get-Date -Format yyyyMMdd_HHmmss)",
    [string]$EnvPath = "X:\workspace\Shhhunt\Builds\Shhhunt.exe",
    [string]$ResultsDir = "X:\workspace\ml-agents\results",
    [int]$BasePort = 5005,
    [switch]$NoGraphics
)

$ErrorActionPreference = "Stop"

# Force UTF-8 everywhere to avoid CP1252 encode errors when conda replays stdout
chcp 65001 | Out-Null
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$env:PYTHONIOENCODING = "utf-8"
$env:PYTHONUTF8 = "1"

$conda = "C:\Users\juanj\anaconda3\Scripts\conda.exe"
$profilePath = Join-Path $PSScriptRoot "ShhhuntReachTarget.yaml"

$arguments = @(
    "run","-n","mlagents","mlagents-learn",
    $profilePath,
    "--run-id=$RunId",
    "--env=$EnvPath",
    "--results-dir=$ResultsDir",
    "--force",
    "--base-port=$BasePort"
)
if ($NoGraphics.IsPresent) { $arguments += "--no-graphics" }

Write-Host "Running:`n$conda $($arguments -join ' ')" -ForegroundColor Cyan
& $conda @arguments
