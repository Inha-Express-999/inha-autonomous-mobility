[CmdletBinding()]
param(
    [ValidateRange(30, 1200)] [int]$TimeoutSeconds = 600,
    [string]$PythonPath = "python"
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

# Preserve GUID references and original nested vehicle visual assets.
$runRoot = Join-Path $repoRoot ("tmp/unity-prefabs-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path (Join-Path $runRoot 'Packages') -Force | Out-Null
& $PythonPath (Join-Path $PSScriptRoot 'UnityPhysicsIntegration/copy_prefab_project.py') $runRoot
if ($LASTEXITCODE -ne 0) { throw 'Prefab dependency copy failed.' }
@{ name = 'InhaExpress.Client.Tests.EditMode'; includePlatforms = @('Editor');
    references = @('InhaExpress.Client.Domain', 'InhaExpress.Client.Networking', 'InhaExpress.Client.Presentation');
    optionalUnityReferences = @('TestAssemblies'); autoReferenced = $false } |
    ConvertTo-Json | Set-Content (Join-Path $runRoot 'Assets/CampusSim/Tests/EditMode/Tests.asmdef')
@{ dependencies = @{
    'com.unity.test-framework' = '1.6.0'; 'com.unity.modules.physics' = '1.0.0';
    'com.unity.modules.imgui' = '1.0.0'; 'com.unity.modules.jsonserialize' = '1.0.0';
    'com.unity.nuget.newtonsoft-json' = '3.2.2'; 'com.unity.inputsystem' = '1.20.0';
    'com.unity.ugui' = '2.0.0'; 'com.unity.render-pipelines.universal' = '17.3.0'
} } | ConvertTo-Json | Set-Content (Join-Path $runRoot 'Packages/manifest.json')

$resultPath = Join-Path $runRoot 'results.xml'
$logPath = Join-Path $runRoot 'editor.log'
Write-Host "Unity prefab test project and logs: $runRoot"
$arguments = "-batchmode -nographics -projectPath `"$runRoot`" -runTests " +
    "-testPlatform EditMode -testResults `"$resultPath`" -logFile `"$logPath`""
$process = Start-Process -FilePath $unityExe -ArgumentList $arguments -WindowStyle Hidden -PassThru
try {
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        throw "Unity prefab test run timed out. See $logPath"
    }
    if (-not (Test-Path -LiteralPath $resultPath)) {
        throw "Unity produced no test results (exit=$($process.ExitCode)). See $logPath"
    }
    [xml]$result = Get-Content -LiteralPath $resultPath -Raw
    $summary = $result.'test-run'
    if ($process.ExitCode -ne 0 -or $summary.result -ne 'Passed' -or [int]$summary.total -lt 1) {
        throw "Unity tests failed: result=$($summary.result), total=$($summary.total), failed=$($summary.failed). See $resultPath"
    }
    Write-Host "Passed $($summary.passed)/$($summary.total) prefab EditMode tests. Results: $resultPath"
} finally {
    # Stop only the process created by this run, leaving existing Editors untouched.
    if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force }
}
