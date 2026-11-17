param(
    [string]$Database = 'tmp/physical-service.db',
    [ValidateRange(1024, 65535)][int]$Port = 8765,
    [switch]$CheckOnly
)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskPython = Join-Path $taskRoot '.venv/Scripts/python.exe'
if (!(Test-Path -LiteralPath $taskPython)) {
    throw 'Run scripts/setup_pc_server.ps1 first to prepare the project Python environment.'
}
if (![IO.Path]::IsPathRooted($Database)) { $Database = Join-Path $taskRoot $Database }
$taskDatabase = [IO.Path]::GetFullPath($Database)
$taskOldDatabase = $env:CAMPUS_SIM_RECOVERY_DB
Push-Location -LiteralPath $taskRoot
try {
    $env:CAMPUS_SIM_RECOVERY_DB = $taskDatabase
    if ($CheckOnly) {
        & $taskPython -c "from campus_sim.api import app; print('Recovery store ready:', app.state.demo.recovery_state())"
    } else {
        $taskListener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, $Port)
        try { $taskListener.Start() }
        catch { throw "Port $Port is occupied. Stop the existing server before starting recovery mode." }
        finally { $taskListener.Stop() }
        Write-Host "Recovery database: $taskDatabase"
        Write-Host "Server: http://127.0.0.1:$Port -- keep this window open; Ctrl+C stops the server."
        & $taskPython -m campus_sim.desktop_server --host 127.0.0.1 --port $Port --state-db $taskDatabase
    }
    if ($LASTEXITCODE -ne 0) { throw 'Recovery server failed. Preserve the database and inspect the error above.' }
} finally {
    $env:CAMPUS_SIM_RECOVERY_DB = $taskOldDatabase
    Pop-Location
}
