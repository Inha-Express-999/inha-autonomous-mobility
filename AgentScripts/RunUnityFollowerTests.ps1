[CmdletBinding()]
param(
    [ValidateRange(30, 1200)] [int]$TimeoutSeconds = 600
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
# Only follower source dependencies are copied; this is not full-project validation.
$runRoot = Join-Path $repoRoot ("tmp/unity-follower-" + [Guid]::NewGuid().ToString('N'))
foreach ($relative in @('Assets/Domain', 'Assets/Presentation', 'Assets/Tests',
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
foreach ($name in @('VehicleRouteFollower.cs', 'MapCoordinateConverter.cs')) {
    Copy-RecordedSource "Assets/CampusSim/Scripts/Presentation/$name" "Assets/Presentation/$name"
}
Copy-RecordedSource 'Assets/CampusSim/Tests/PlayMode/VehicleRouteFollowerPlayModeTests.cs' `
    'Assets/Tests/VehicleRouteFollowerPlayModeTests.cs'
Copy-RecordedSource 'ProjectSettings/ProjectVersion.txt' 'ProjectSettings/ProjectVersion.txt'

@{ name = 'InhaExpress.Client.Presentation'; references = @('InhaExpress.Client.Domain') } |
    ConvertTo-Json | Set-Content (Join-Path $runRoot 'Assets/Presentation/Presentation.asmdef')
@{ name = 'InhaExpress.Client.Tests.PlayMode';
    references = @('InhaExpress.Client.Domain', 'InhaExpress.Client.Presentation');
    optionalUnityReferences = @('TestAssemblies'); autoReferenced = $false } |
    ConvertTo-Json | Set-Content (Join-Path $runRoot 'Assets/Tests/Tests.asmdef')
@{ dependencies = @{
    'com.unity.test-framework' = '1.6.0'; 'com.unity.modules.physics' = '1.0.0';
    'com.unity.modules.imgui' = '1.0.0'; 'com.unity.modules.jsonserialize' = '1.0.0'
} } | ConvertTo-Json | Set-Content (Join-Path $runRoot 'Packages/manifest.json')
@{ editor = $version; scope = 'isolated follower PlayMode Physics tests'; sources = $sources } |
    ConvertTo-Json -Depth 4 | Set-Content (Join-Path $runRoot 'source-manifest.json')

$resultPath = Join-Path $runRoot 'results.xml'
$logPath = Join-Path $runRoot 'editor.log'
Write-Host "Unity follower test project and logs: $runRoot"
$arguments = "-batchmode -nographics -projectPath `"$runRoot`" -runTests " +
    "-testPlatform PlayMode -testResults `"$resultPath`" -logFile `"$logPath`""
$process = Start-Process -FilePath $unityExe -ArgumentList $arguments -WindowStyle Hidden -PassThru
try {
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        throw "Unity follower test run timed out. See $logPath"
    }
    if (-not (Test-Path -LiteralPath $resultPath)) {
        throw "Unity produced no test results (exit=$($process.ExitCode)). See $logPath"
    }
    [xml]$result = Get-Content -LiteralPath $resultPath -Raw
    $summary = $result.'test-run'
    if ($process.ExitCode -ne 0 -or $summary.result -ne 'Passed' -or [int]$summary.total -lt 1) {
        throw "Unity tests failed: result=$($summary.result), total=$($summary.total), failed=$($summary.failed). See $resultPath"
    }
    Write-Host "Passed $($summary.passed)/$($summary.total) follower PlayMode tests. Results: $resultPath"
} finally {
    # Stop only the process created by this run, leaving existing Editors untouched.
    if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force }
}
