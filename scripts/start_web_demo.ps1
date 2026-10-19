param(
    [ValidateRange(1024, 65535)]
    [int]$Port = 8765
)

$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskPreviousPythonPath = $env:PYTHONPATH
$taskPython = (Get-Command python -ErrorAction Stop).Source
foreach ($taskCandidate in @('.venv/Scripts/python.exe', 'backend/.venv/Scripts/python.exe')) {
    $taskCandidatePath = Join-Path $taskRoot $taskCandidate
    if (Test-Path -LiteralPath $taskCandidatePath) {
        $taskPython = $taskCandidatePath
        break
    }
}

Push-Location -LiteralPath $taskRoot
try {
    $taskImportPaths = @((Join-Path $taskRoot 'backend/src'))
    $taskLocalDependencies = Join-Path $taskRoot '.utmp/fifo-dijkstra-deps'
    if (Test-Path -LiteralPath $taskLocalDependencies) {
        $taskImportPaths += $taskLocalDependencies
    }
    if ($taskPreviousPythonPath) { $taskImportPaths += $taskPreviousPythonPath }
    $env:PYTHONPATH = $taskImportPaths -join [IO.Path]::PathSeparator

    & $taskPython -c "import fastapi, uvicorn, pyproj"
    if ($LASTEXITCODE -ne 0) {
        throw 'Install dependencies first: python -m pip install -r backend/requirements-dev.lock'
    }
    & $taskPython -m campus_sim.map_builder | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Map build failed. Check source files and configs/osm_runtime.json.' }

    Write-Host "Inha Mobil: http://127.0.0.1:$Port/demo/" -ForegroundColor Cyan
    Write-Host 'Keep this terminal open. Press Ctrl+C to stop. Use -Port 8766 if needed.'
    & $taskPython -m uvicorn campus_sim.api:app --host 127.0.0.1 --port $Port --log-level warning
    if ($LASTEXITCODE -ne 0) { throw 'Server exited with an error. Check whether the port is in use.' }
}
finally {
    $env:PYTHONPATH = $taskPreviousPythonPath
    Pop-Location
}
