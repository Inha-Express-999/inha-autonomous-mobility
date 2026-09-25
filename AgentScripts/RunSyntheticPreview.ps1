[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$UnityPlayerPath,

    [string]$PythonPath = "python",

    [ValidateRange(5, 180)]
    [int]$StartupTimeoutSeconds = 30
)

$ErrorActionPreference = "Stop"
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$unityPlayer = [System.IO.Path]::GetFullPath($UnityPlayerPath, (Get-Location).Path)
$mapPath = Join-Path $projectRoot "maps/fixtures/campus-synthetic-6.json"
$healthUri = "http://127.0.0.1:8765/health"
$runId = [Guid]::NewGuid().ToString("N")
$logDirectory = Join-Path ([System.IO.Path]::GetTempPath()) "inha-mobility-preview-$runId"
$serverProcess = $null

if (-not (Test-Path -LiteralPath $unityPlayer -PathType Leaf)) {
    throw "Unity Player executable was not found: $unityPlayer"
}
if (-not (Test-Path -LiteralPath $mapPath -PathType Leaf)) {
    throw "Synthetic map fixture was not found: $mapPath"
}

$pythonCommand = Get-Command $PythonPath -ErrorAction SilentlyContinue
if ($null -eq $pythonCommand -or $pythonCommand.CommandType -ne "Application") {
    throw "Python executable was not found: $PythonPath"
}
$pythonExecutable = $pythonCommand.Source

$portProbe = [System.Net.Sockets.TcpClient]::new()
try {
    $connectAttempt = $portProbe.BeginConnect([System.Net.IPAddress]::Loopback, 8765, $null, $null)
    if ($connectAttempt.AsyncWaitHandle.WaitOne(500)) {
        try {
            $portProbe.EndConnect($connectAttempt)
            throw "Port 8765 is already in use. Stop its owner before starting the synthetic preview."
        } catch [System.Net.Sockets.SocketException] {
            # A refused connection means the port has no active listener.
        }
    }
} finally {
    $portProbe.Close()
}

New-Item -ItemType Directory -Path $logDirectory | Out-Null
$stdoutLog = Join-Path $logDirectory "server.stdout.log"
$stderrLog = Join-Path $logDirectory "server.stderr.log"
$previousPythonPath = $env:PYTHONPATH
$backendSource = Join-Path $projectRoot "backend/src"
if ([string]::IsNullOrWhiteSpace($previousPythonPath)) {
    $env:PYTHONPATH = $backendSource
} else {
    $env:PYTHONPATH = "$backendSource;$previousPythonPath"
}

try {
    $serverProcess = Start-Process -FilePath $pythonExecutable `
        -ArgumentList @("-m", "campus_sim.cli", "serve", "--host", "127.0.0.1", `
            "--port", "8765", "--map", "maps/fixtures/campus-synthetic-6.json") `
        -WorkingDirectory $projectRoot `
        -RedirectStandardOutput $stdoutLog `
        -RedirectStandardError $stderrLog `
        -WindowStyle Hidden `
        -PassThru
} finally {
    $env:PYTHONPATH = $previousPythonPath
}

try {
    $deadline = [DateTime]::UtcNow.AddSeconds($StartupTimeoutSeconds)
    $health = $null
    while ([DateTime]::UtcNow -lt $deadline) {
        if ($serverProcess.HasExited) {
            $errorTail = if (Test-Path -LiteralPath $stderrLog) {
                (Get-Content -LiteralPath $stderrLog -Tail 30) -join [Environment]::NewLine
            } else { "No server stderr log was created." }
            throw "Synthetic Python server exited with code $($serverProcess.ExitCode).`n$errorTail"
        }

        try {
            $health = Invoke-RestMethod -Uri $healthUri -TimeoutSec 1
            if ($health.status -eq "ok") { break }
        } catch {
            Start-Sleep -Milliseconds 250
        }
    }

    if ($null -eq $health -or $health.status -ne "ok") {
        throw "Python server did not become healthy within $StartupTimeoutSeconds seconds. Logs: $logDirectory"
    }
    if ($health.map_version -ne "synthetic-campus-6stop-v1") {
        throw "Unexpected server map_version '$($health.map_version)'; expected synthetic-campus-6stop-v1."
    }

    Write-Host "Python server ready: $healthUri (map=$($health.map_version), nodes=$($health.node_count), edges=$($health.edge_count))"
    Write-Host "Starting Unity Player. Close it to stop the temporary server."
    Write-Host "Server logs: $logDirectory"
    $playerProcess = Start-Process -FilePath $unityPlayer -WorkingDirectory $projectRoot -PassThru
    $playerProcess.WaitForExit()
    if ($playerProcess.ExitCode -ne 0) {
        throw "Unity Player exited with code $($playerProcess.ExitCode)."
    }
} finally {
    if ($serverProcess -and -not $serverProcess.HasExited) {
        Stop-Process -Id $serverProcess.Id -Force -ErrorAction SilentlyContinue
    }
}
