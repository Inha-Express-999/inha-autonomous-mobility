param(
    [string]$PythonPath = "python",
    [int]$Port = 18766,
    [switch]$DtoOnly
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$editorVersionLine = Get-Content (Join-Path $repoRoot "ProjectSettings\ProjectVersion.txt") |
    Where-Object { $_ -match "^m_EditorVersion:" } | Select-Object -First 1
if (-not $editorVersionLine) { throw "Unity Editor version is missing from ProjectVersion.txt." }
$editorVersion = ($editorVersionLine -split ":", 2)[1].Trim()
$unityData = Join-Path "C:\Program Files\Unity\Hub\Editor\$editorVersion\Editor\Data" "MonoBleedingEdge"
$mono = Join-Path $unityData "bin\mono.exe"
$csc = Join-Path $unityData "lib\mono\4.5\csc.exe"
$netstandard = Join-Path $unityData "lib\mono\4.5\Facades\netstandard.dll"
$newtonsoft = Get-ChildItem (Join-Path $repoRoot "Library\PackageCache") `
    -Directory -Filter "com.unity.nuget.newtonsoft-json@*" |
    ForEach-Object { Join-Path $_.FullName "Runtime\Newtonsoft.Json.dll" } |
    Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not (Test-Path -LiteralPath $mono) -or -not (Test-Path -LiteralPath $csc) -or
    -not (Test-Path -LiteralPath $netstandard) -or -not $newtonsoft) {
    throw "Unity Mono compiler/runtime or Newtonsoft.Json package was not found for Editor $editorVersion."
}

if (Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue) {
    throw "Port $Port already has a listener. Choose another port; the script will not stop it."
}

$pythonCommand = Get-Command $PythonPath -ErrorAction Stop
$pythonExe = if ($pythonCommand.Source) { $pythonCommand.Source } else { $pythonCommand.Path }
$runDirectory = Join-Path $env:TEMP ("inha-unity-ws-smoke-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $runDirectory | Out-Null
$clientExe = Join-Path $runDirectory "UnityClientSmoke.exe"
$jsonCopy = Join-Path $runDirectory "Newtonsoft.Json.dll"
Copy-Item -LiteralPath $newtonsoft -Destination $jsonCopy

$sources = @(
    (Get-ChildItem (Join-Path $repoRoot "Assets\CampusSim\Scripts\Domain") -Filter "*.cs" |
        ForEach-Object { $_.FullName })
    (Join-Path $repoRoot "Assets\CampusSim\Scripts\Networking\IClientDataSource.cs")
    (Join-Path $repoRoot "Assets\CampusSim\Scripts\Networking\WebSocketClientDataSource.cs")
    (Join-Path $PSScriptRoot "UnityDebugStub.cs")
    (Join-Path $PSScriptRoot "UnityClientSmoke.cs")
)
& $mono $csc /nologo /langversion:latest /target:exe ("/out:" + $clientExe) `
    ("/reference:" + $newtonsoft) ("/reference:" + $netstandard) $sources
if ($LASTEXITCODE -ne 0) { throw "Unity WebSocket smoke client compilation failed." }
if ($DtoOnly) {
    & $mono $clientExe --dto-self-test
    if ($LASTEXITCODE -ne 0) { throw "Sensor DTO self-test failed." }
    Write-Host "Sensor DTO edge-case self-test passed. Compiled client: $clientExe"
    return
}

$previousPythonPath = $env:PYTHONPATH
$env:PYTHONPATH = "$PSScriptRoot;$(Join-Path $repoRoot 'backend\src')"
$server = $null
$client = $null
try {
    $server = Start-Process -FilePath $pythonExe -WindowStyle Hidden -PassThru `
        -WorkingDirectory $PSScriptRoot `
        -ArgumentList @("-m", "uvicorn", "drop_first_ack_app:app", "--host", "127.0.0.1", "--port", "$Port")
    $healthUrl = "http://127.0.0.1:$Port/health"
    $healthy = $false
    for ($attempt = 0; $attempt -lt 100; $attempt++) {
        Start-Sleep -Milliseconds 100
        try {
            if ((Invoke-WebRequest $healthUrl -TimeoutSec 1).StatusCode -eq 200) {
                $healthy = $true
                break
            }
        } catch { }
    }
    if (-not $healthy) { throw "Smoke ASGI server failed to start. Check that backend dev dependencies are installed." }

    $stdout = Join-Path $runDirectory "client.stdout.log"
    $stderr = Join-Path $runDirectory "client.stderr.log"
    $client = Start-Process -FilePath $mono -WindowStyle Hidden -PassThru `
        -WorkingDirectory $runDirectory -RedirectStandardOutput $stdout -RedirectStandardError $stderr `
        -ArgumentList @($clientExe, "ws://127.0.0.1:$Port/v1/client/ws")
    $client.WaitForExit(25000) | Out-Null
    if (-not $client.HasExited) { throw "Client smoke test timed out. Logs: $runDirectory" }
    if (Test-Path -LiteralPath $stdout) { Get-Content -LiteralPath $stdout }
    if (Test-Path -LiteralPath $stderr) { Get-Content -LiteralPath $stderr }
    if ($client.ExitCode -ne 0) { throw "Client smoke failed with exit code $($client.ExitCode). Logs: $runDirectory" }
    Write-Host "Unity C# source WebSocket smoke passed. Temporary logs: $runDirectory"
} finally {
    if ($client -and -not $client.HasExited) { Stop-Process -Id $client.Id -Force }
    if ($server -and -not $server.HasExited) {
        Stop-Process -Id $server.Id -Force
        $server.WaitForExit()
    }
    $env:PYTHONPATH = $previousPythonPath
}
