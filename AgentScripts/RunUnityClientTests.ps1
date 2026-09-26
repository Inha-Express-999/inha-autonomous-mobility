[CmdletBinding()]
param([ValidateRange(30, 1200)] [int]$TimeoutSeconds = 600)
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$version = ((Get-Content (Join-Path $repoRoot 'ProjectSettings/ProjectVersion.txt') |
    Where-Object { $_ -match '^m_EditorVersion:' }) -split ':', 2)[1].Trim()
$unityExe = "C:/Program Files/Unity/Hub/Editor/$version/Editor/Unity.exe"
if (-not (Test-Path -LiteralPath $unityExe)) { throw "Missing Unity $version" }
$runRoot = Join-Path $repoRoot ('tmp/unity-client-tests-' + [guid]::NewGuid().ToString('N'))
$sources = @{}
function Copy-RecordedSource([string]$relative) {
    $source = Join-Path $repoRoot $relative
    $destination = Join-Path $runRoot $relative
    New-Item -ItemType Directory -Path (Split-Path $destination) -Force | Out-Null
    Copy-Item -LiteralPath $source -Destination $destination
    $sources[$relative] = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
}
foreach ($directory in @('Domain', 'Networking', 'Presentation', 'PC', 'Mobile')) {
    foreach ($file in Get-ChildItem (Join-Path $repoRoot "Assets/CampusSim/Scripts/$directory") -File) {
        if ($file.Extension -in @('.cs', '.asmdef')) {
            Copy-RecordedSource "Assets/CampusSim/Scripts/$directory/$($file.Name)"
        }
    }
}
foreach ($relative in @('Assets/CampusSim/Tests/EditMode/FixtureClientTests.cs',
        'Assets/CampusSim/Tests/EditMode/InhaExpress.Client.Tests.EditMode.asmdef',
        'ProjectSettings/ProjectVersion.txt', 'ProjectSettings/TagManager.asset')) {
    Copy-RecordedSource $relative
}
New-Item -ItemType Directory -Path (Join-Path $runRoot 'Packages') -Force | Out-Null
$original = Get-Content (Join-Path $repoRoot 'Packages/manifest.json') -Raw | ConvertFrom-Json
$dependencies = @{}
foreach ($property in $original.dependencies.PSObject.Properties) {
    if ($property.Name.StartsWith('com.unity.modules.') -or
        $property.Name -in @('com.unity.inputsystem', 'com.unity.ugui', 'com.unity.test-framework')) {
        $dependencies[$property.Name] = $property.Value
    }
}
$dependencies['com.unity.nuget.newtonsoft-json'] = '3.2.2'
@{ dependencies = $dependencies } | ConvertTo-Json -Depth 4 |
    Set-Content (Join-Path $runRoot 'Packages/manifest.json')
@{ editor = $version; scope = 'isolated client EditMode presentation and fixture tests'; sources = $sources } |
    ConvertTo-Json -Depth 4 | Set-Content (Join-Path $runRoot 'source-manifest.json')
$resultPath = Join-Path $runRoot 'results.xml'
$logPath = Join-Path $runRoot 'editor.log'
Write-Host "Unity client tests: $runRoot"
$arguments = "-batchmode -nographics -projectPath `"$runRoot`" -runTests -testPlatform EditMode " +
    "-testFilter InhaExpress.Client.Tests.FixtureClientTests -testResults `"$resultPath`" -logFile `"$logPath`""
$process = Start-Process -FilePath $unityExe -ArgumentList $arguments -WindowStyle Hidden -PassThru
try {
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) { throw "Client tests timed out: $logPath" }
    if (-not (Test-Path -LiteralPath $resultPath)) { throw "No client test results: $logPath" }
    [xml]$result = Get-Content -LiteralPath $resultPath -Raw
    $summary = $result.'test-run'
    if ($process.ExitCode -ne 0 -or $summary.result -ne 'Passed' -or [int]$summary.total -lt 1) {
        throw "Client tests failed: $resultPath"
    }
    Write-Host "Passed $($summary.passed)/$($summary.total) client tests: $resultPath"
} finally {
    if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force }
}
