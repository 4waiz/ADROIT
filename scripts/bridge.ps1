# SPDX-License-Identifier: LicenseRef-BRIDGE
# BRIDGE | Digital Twin Studio -- start it on Windows.
#
#   powershell -ExecutionPolicy Bypass -File scripts\bridge.ps1            # serve on http://127.0.0.1:8800
#   powershell -ExecutionPolicy Bypass -File scripts\bridge.ps1 -Open      # ...and open the browser
#   powershell -ExecutionPolicy Bypass -File scripts\bridge.ps1 -Doctor    # check this machine
#   powershell -ExecutionPolicy Bypass -File scripts\bridge.ps1 -Build     # rebuild the browser client first
#
# Needs uv (https://docs.astral.sh/uv/) and, to build the client, Node.js 20+.
# A checkout inside OneDrive keeps its Python environment outside the synced
# folder (several GB of packages); set UV_PROJECT_ENVIRONMENT to choose where.

param(
    [int]$Port = 8800,
    [string]$Scenario = "pick_place",
    [string]$BindHost = "127.0.0.1",
    [switch]$Open,
    [switch]$Doctor,
    [switch]$Build,
    [switch]$Paused,
    [switch]$NoSync
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if (-not $env:UV_PROJECT_ENVIRONMENT -and $root -like "*OneDrive*") {
    $env:UV_PROJECT_ENVIRONMENT = Join-Path $env:USERPROFILE ".venvs\bridge-adroit"
}
if (-not (Get-Command uv -ErrorAction SilentlyContinue)) {
    Write-Error "uv is not installed: https://docs.astral.sh/uv/getting-started/installation/"
}

if (-not $NoSync) {
    $envPath = if ($env:UV_PROJECT_ENVIRONMENT) { $env:UV_PROJECT_ENVIRONMENT } else { ".venv" }
    Write-Host "[bridge] syncing the Python environment ($envPath)"
    # Long paths: one optional git dependency carries deep submodule paths.
    $env:GIT_CONFIG_COUNT = "1"; $env:GIT_CONFIG_KEY_0 = "core.longpaths"; $env:GIT_CONFIG_VALUE_0 = "true"
    uv sync --extra superdex
    if ($LASTEXITCODE -ne 0) { Write-Error "uv sync failed" }
}

if ($Doctor) {
    uv run --no-sync python -m bridge_studio doctor
    exit $LASTEXITCODE
}

$client = Join-Path $root "bridge_studio\static\index.html"
if ($Build -or -not (Test-Path $client)) {
    if (-not (Get-Command npm -ErrorAction SilentlyContinue)) {
        Write-Error "The browser client is not built and Node.js is not installed: https://nodejs.org (LTS)."
    }
    Write-Host "[bridge] building the browser client"
    Push-Location (Join-Path $root "frontend")
    try {
        if (-not (Test-Path "node_modules")) { npm ci; if ($LASTEXITCODE -ne 0) { npm install } }
        npm run build
        if ($LASTEXITCODE -ne 0) { Write-Error "the client build failed" }
    } finally { Pop-Location }
}

$serveArgs = @("run", "--no-sync", "python", "-m", "bridge_studio", "--port", "$Port", "--scenario", $Scenario, "--host", $BindHost)
if ($Open) { $serveArgs += "--open" }
if ($Paused) { $serveArgs += "--paused" }
uv @serveArgs
exit $LASTEXITCODE
