[CmdletBinding()]
param(
    [ValidateRange(30, 1200)] [int]$TimeoutSeconds = 600,
    [string]$PythonPath = "python",
    [ValidateRange(1024, 65535)] [int]$Port = 18767,
    [switch]$Player
)

$ErrorActionPreference = "Stop"
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$versionLine = Get-Content (Join-Path $repoRoot "ProjectSettings/ProjectVersion.txt") |
    Where-Object { $_ -match '^m_EditorVersion:' } | Select-Object -First 1
$version = ($versionLine -split ':', 2)[1].Trim()
$unityExe = "C:/Program Files/Unity/Hub/Editor/$version/Editor/Unity.exe"
if (-not (Test-Path -LiteralPath $unityExe -PathType Leaf)) {
    throw "Required Unity Editor was not found: $unityExe"
}

# A unique disposable project avoids locking or changing the user's open scene.
# Production service/Physics components are copied; this is not full-project validation.
$runRoot = Join-Path $repoRoot ("tmp/unity-physics-integration-" + [Guid]::NewGuid().ToString('N'))
foreach ($relative in @('Assets/Domain', 'Assets/Networking', 'Assets/Presentation', 'Assets/Tests',
        'Packages', 'ProjectSettings')) {
    New-Item -ItemType Directory -Path (Join-Path $runRoot $relative) -Force | Out-Null
}
$sources = @{}
function Copy-RecordedSource([string]$relative, [string]$destination) {
    $source = Join-Path $repoRoot $relative
    Copy-Item -LiteralPath $source -Destination (Join-Path $runRoot $destination)
    $sources[$relative] = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
}
Get-ChildItem (Join-Path $repoRoot 'Assets/CampusSim/Scripts/Domain') -File |
    Where-Object { $_.Extension -in @('.cs', '.asmdef') } | ForEach-Object {
        Copy-RecordedSource "Assets/CampusSim/Scripts/Domain/$($_.Name)" "Assets/Domain/$($_.Name)"
    }
Get-ChildItem (Join-Path $repoRoot 'Assets/CampusSim/Scripts/Networking') -File |
    Where-Object { $_.Extension -in @('.cs', '.asmdef') } | ForEach-Object {
        Copy-RecordedSource "Assets/CampusSim/Scripts/Networking/$($_.Name)" "Assets/Networking/$($_.Name)"
    }
foreach ($name in @('VehicleRouteFollower.cs', 'MapCoordinateConverter.cs', 'WorldStateStore.cs',
        'ClientRuntimeHost.cs', 'VehicleActorSpawner.cs', 'VehicleEgoLocalizationReporter.cs',
        'VehicleRaycastSensorRig.cs')) {
    Copy-RecordedSource "Assets/CampusSim/Scripts/Presentation/$name" "Assets/Presentation/$name"
}
Copy-RecordedSource 'Assets/CampusSim/Tests/PlayMode/VehicleServiceIntegrationTests.cs' `
    'Assets/Tests/VehicleServiceIntegrationTests.cs'
Copy-RecordedSource 'ProjectSettings/ProjectVersion.txt' 'ProjectSettings/ProjectVersion.txt'
Copy-RecordedSource 'ProjectSettings/TagManager.asset' 'ProjectSettings/TagManager.asset'
if ($Player) {
    New-Item -ItemType Directory -Path (Join-Path $runRoot 'Assets/BuildSupport') | Out-Null
    Copy-RecordedSource 'AgentScripts/UnityPhysicsIntegration/HeadlessPlayerBuild.cs' 'Assets/BuildSupport/HeadlessPlayerBuild.cs'
    Copy-RecordedSource 'AgentScripts/UnityPhysicsIntegration/PlayerResults.cs' 'Assets/Tests/PlayerResults.cs'
    @{ name = 'Integration.BuildSupport'; includePlatforms = @('Editor');
        optionalUnityReferences = @('TestAssemblies') } |
        ConvertTo-Json | Set-Content (Join-Path $runRoot 'Assets/BuildSupport/BuildSupport.asmdef')
}
foreach ($file in Get-ChildItem (Join-Path $repoRoot 'backend/src') -Recurse -Filter '*.py') {
    $relative = [IO.Path]::GetRelativePath($repoRoot, $file.FullName)
    $sources[$relative] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
}
foreach ($relative in @('maps/fixtures/physics-integration-3.json',
        'AgentScripts/UnityPhysicsIntegration/server.py', 'configs/safety.json',
        'configs/planning.json', 'configs/crowd.json', 'configs/dispatch.json', 'VERSION')) {
    $sources[$relative] = (Get-FileHash -LiteralPath (Join-Path $repoRoot $relative) -Algorithm SHA256).Hash
}

@{ name = 'InhaExpress.Client.Presentation'; references = @('InhaExpress.Client.Domain', 'InhaExpress.Client.Networking') } |
    ConvertTo-Json | Set-Content (Join-Path $runRoot 'Assets/Presentation/Presentation.asmdef')
@{ name = 'InhaExpress.Client.Tests.PlayMode';
    references = @('InhaExpress.Client.Domain', 'InhaExpress.Client.Networking', 'InhaExpress.Client.Presentation');
    optionalUnityReferences = @('TestAssemblies'); autoReferenced = $false } |
    ConvertTo-Json | Set-Content (Join-Path $runRoot 'Assets/Tests/Tests.asmdef')
@{ dependencies = @{
    'com.unity.nuget.newtonsoft-json' = '3.2.2'; 'com.unity.test-framework' = '1.6.0'; 'com.unity.modules.physics' = '1.0.0';
    'com.unity.modules.imgui' = '1.0.0'; 'com.unity.modules.jsonserialize' = '1.0.0'
} } | ConvertTo-Json | Set-Content (Join-Path $runRoot 'Packages/manifest.json')
@{ editor = $version; player = [bool]$Player;
    scope = 'isolated production service, WebSocket and Physics integration'; sources = $sources } |
    ConvertTo-Json -Depth 4 | Set-Content (Join-Path $runRoot 'source-manifest.json')

$resultPath = Join-Path $runRoot 'results.xml'
$logPath = Join-Path $runRoot 'editor.log'
Write-Host "Unity Physics integration project and logs: $runRoot"
$testPlatform = if ($Player) { 'StandaloneWindows64' } else { 'PlayMode' }
$arguments = "-batchmode -nographics -projectPath `"$runRoot`" -runTests " +
    "-testPlatform $testPlatform -testResults `"$resultPath`" -logFile `"$logPath`""
$process = $null
$server = $null
$previousPythonPath = $env:PYTHONPATH
$previousUrl = $env:INHA_UNITY_E2E_URL
$previousVersion = $env:INHA_UNITY_E2E_VERSION
$previousTrace = $env:INHA_UNITY_E2E_TRACE
$previousResults = $env:INHA_UNITY_E2E_RESULTS
try {
    if (Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue) {
        throw "Port $Port is in use. Select another port."
    }
    $pythonExe = (Get-Command $PythonPath -ErrorAction Stop).Source
    $env:PYTHONPATH = Join-Path $repoRoot 'backend/src'
    $server = Start-Process -FilePath $pythonExe -WindowStyle Hidden -PassThru `
        -WorkingDirectory (Join-Path $repoRoot 'AgentScripts/UnityPhysicsIntegration') `
        -ArgumentList @('-m', 'uvicorn', 'server:app', '--host', '127.0.0.1', '--port', "$Port") `
        -RedirectStandardOutput (Join-Path $runRoot 'server.stdout.log') `
        -RedirectStandardError (Join-Path $runRoot 'server.stderr.log')
    $healthy = $false
    for ($attempt = 0; $attempt -lt 80; $attempt++) {
        if ($server.HasExited) { throw "Integration server exited. See $runRoot/server.stderr.log" }
        try {
            $health = Invoke-RestMethod "http://127.0.0.1:$Port/health" -TimeoutSec 1
            if ($health.map_version -eq 'synthetic-physics-integration-v1') { $healthy = $true; break }
        } catch { }
        Start-Sleep -Milliseconds 100
    }
    if (-not $healthy) { throw "Integration server startup timed out." }
    $env:INHA_UNITY_E2E_URL = "ws://127.0.0.1:$Port/v1/client/ws"
    $env:INHA_UNITY_E2E_VERSION = (Get-Content (Join-Path $repoRoot 'VERSION') -Raw).Trim()
    $env:INHA_UNITY_E2E_TRACE = Join-Path $runRoot 'snapshot-trace.csv'
    $env:INHA_UNITY_E2E_RESULTS = $resultPath
    'tick,x,y,speed,motion,reason,requestStatus' | Set-Content $env:INHA_UNITY_E2E_TRACE
    $process = Start-Process -FilePath $unityExe -ArgumentList $arguments -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        throw "Unity Physics integration run timed out. See $logPath"
    }
    if ($Player) {
        $playerExe = Join-Path $runRoot 'TestPlayer/Integration.exe'
        if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $playerExe)) {
            throw "Unity test Player build failed. See $logPath"
        }
        $playerLog = Join-Path $runRoot 'player.log'
        $process = Start-Process -FilePath $playerExe -WindowStyle Hidden -PassThru `
            -ArgumentList "-batchmode -nographics -logFile `"$playerLog`""
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            throw "Unity test Player timed out. See $playerLog"
        }
    }
    if (-not (Test-Path -LiteralPath $resultPath)) {
        throw "Unity produced no test results (exit=$($process.ExitCode)). See $logPath"
    }
    [xml]$result = Get-Content -LiteralPath $resultPath -Raw
    $summary = $result.'test-run'
    if ($process.ExitCode -ne 0 -or $summary.result -ne 'Passed' -or [int]$summary.total -lt 1) {
        throw "Unity tests failed: result=$($summary.result), total=$($summary.total), failed=$($summary.failed). See $resultPath"
    }
    Write-Host "Passed $($summary.passed)/$($summary.total) Physics integration tests. Results: $resultPath"
} finally {
    # Stop only the process created by this run, leaving existing Editors untouched.
    if ($process -and -not $process.HasExited) { Stop-Process -Id $process.Id -Force }
    if ($server -and -not $server.HasExited) { Stop-Process -Id $server.Id -Force }
    $env:PYTHONPATH = $previousPythonPath
    $env:INHA_UNITY_E2E_URL = $previousUrl
    $env:INHA_UNITY_E2E_VERSION = $previousVersion
    $env:INHA_UNITY_E2E_TRACE = $previousTrace
    $env:INHA_UNITY_E2E_RESULTS = $previousResults
}
