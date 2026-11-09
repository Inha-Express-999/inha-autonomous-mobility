param([string]$PythonExecutable = 'python')
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskVenv = Join-Path $taskRoot '.venv'
$taskPython = Join-Path $taskVenv 'Scripts/python.exe'
if (!(Test-Path -LiteralPath $taskPython)) {
    & $PythonExecutable -c "import sys; assert sys.version_info >= (3,11), 'Python 3.11 or newer required'"
    if ($LASTEXITCODE -ne 0) { throw 'Check the Python executable and version.' }
    & $PythonExecutable -m venv $taskVenv
    if ($LASTEXITCODE -ne 0) { throw 'Could not create project .venv.' }
}
& $taskPython -m ensurepip --upgrade
if ($LASTEXITCODE -ne 0) { throw 'Could not initialize pip. Check temporary-directory permissions.' }
& $taskPython -m pip install -e "$taskRoot/backend[dev]"
if ($LASTEXITCODE -ne 0) { throw 'Dependency installation failed. Re-run after resolving the reported error.' }
& $taskPython -m pip check
if ($LASTEXITCODE -ne 0) { throw 'Dependency conflict detected.' }
Push-Location -LiteralPath $taskRoot
try {
    & $taskPython -c "import fastapi, uvicorn, pyproj, websockets; from campus_sim.api import create_app; app=create_app(); print('Server imports OK')"
    if ($LASTEXITCODE -ne 0) { throw 'Server import check failed.' }
}
finally { Pop-Location }
Write-Host "Ready: $taskPython"
Write-Host 'Restart Unity Hub and Unity Editor to clear previous session overrides, then play PC_Bootstrap.'
