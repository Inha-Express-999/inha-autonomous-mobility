[CmdletBinding()]
param([ValidateRange(30,600)][int]$TimeoutSeconds=180, [string]$PythonExecutable='python', [switch]$IncludeClientIntegration)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$version=((Get-Content (Join-Path $repo 'ProjectSettings/ProjectVersion.txt') | Where-Object {$_ -match '^m_EditorVersion:'}) -split ':',2)[1].Trim()
$unity="C:/Program Files/Unity/Hub/Editor/$version/Editor/Unity.exe"
if(-not(Test-Path -LiteralPath $unity)){throw "Unity missing: $version"}
$run=Join-Path $repo ('tmp/transport-brake-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $run -Force | Out-Null
$sources=@{}
$pythonPath=(Get-Command $PythonExecutable -ErrorAction Stop).Source
& $pythonPath -c 'from websockets.asyncio.server import serve'
if($LASTEXITCODE -ne 0){throw 'Selected Python needs the project websockets dependency.'}
$files=@(
 'Assets/CampusSim/Scripts/Domain/TransportSafetyGate.cs',
 'Assets/CampusSim/Scripts/Domain/InhaExpress.Client.Domain.asmdef',
 'Assets/CampusSim/Scripts/Simulation/VehicleController.cs',
 'Assets/CampusSim/Scripts/Simulation/TransportVehicleBrakeBridge.cs',
 'Assets/CampusSim/Scripts/Simulation/InhaExpress.Simulation.asmdef',
 'Assets/CampusSim/Tests/PlayMode/TransportBrakePlayModeTests.cs',
 'AgentScripts/brake_socket_fixture.py',
 'ProjectSettings/ProjectVersion.txt')
$references=@('InhaExpress.Simulation','InhaExpress.Client.Domain')
$expected=3
$dependencies=@{'com.unity.inputsystem'='1.20.0';'com.unity.test-framework'='1.6.0';'com.unity.modules.physics'='1.0.0';'com.unity.modules.vehicles'='1.0.0';'com.unity.modules.imgui'='1.0.0';'com.unity.modules.jsonserialize'='1.0.0'}
if($IncludeClientIntegration) {
 & $pythonPath -c 'import fastapi, uvicorn, pydantic'
 if($LASTEXITCODE -ne 0){throw 'Selected Python needs the backend dependencies.'}
 $expected=8
 $references+=@('InhaExpress.Client.Networking','InhaExpress.Client.Presentation')
 $files+=@('Assets/CampusSim/Tests/PlayMode/ClientBrakeBindingPlayModeTests.cs','AgentScripts/brake_runtime_fixture.py')
 foreach($directory in @('Domain','Networking','Simulation','Presentation')) {
  $files+=Get-ChildItem (Join-Path $repo "Assets/CampusSim/Scripts/$directory") -File |
   Where-Object {$_.Extension -in @('.cs','.asmdef')} | ForEach-Object {"Assets/CampusSim/Scripts/$directory/$($_.Name)"}
 }
 $files+=Get-ChildItem (Join-Path $repo 'backend/src') -Recurse -Filter '*.py' |
  ForEach-Object {$_.FullName.Substring($repo.Length+1).Replace('\','/')}
 $files+=Get-ChildItem (Join-Path $repo 'configs') -Recurse -File |
  ForEach-Object {$_.FullName.Substring($repo.Length+1).Replace('\','/')}
 $files+=Get-ChildItem (Join-Path $repo 'maps/fixtures') -Recurse -Filter '*.json' |
  ForEach-Object {$_.FullName.Substring($repo.Length+1).Replace('\','/')}
 $original=Get-Content (Join-Path $repo 'Packages/manifest.json') -Raw | ConvertFrom-Json
 foreach($property in $original.dependencies.psobject.Properties) {
  if($property.Name.StartsWith('com.unity.modules.')){$dependencies[$property.Name]=$property.Value}
 }
 $dependencies['com.unity.ugui']='2.0.0'
 $dependencies['com.unity.nuget.newtonsoft-json']='3.2.2'
}
foreach($file in ($files | Select-Object -Unique)) {
 $destination=Join-Path $run $file
 New-Item -ItemType Directory -Path (Split-Path $destination) -Force | Out-Null
 Copy-Item -LiteralPath (Join-Path $repo $file) -Destination $destination
 $sources[$file]=(Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash
}
if($IncludeClientIntegration) {
 & $pythonPath -c 'import sys; sys.path.insert(0, sys.argv[1]); from campus_sim.api import app' (Join-Path $run 'backend/src')
 if($LASTEXITCODE -ne 0){throw 'Copied backend fixture cannot initialize.'}
 Write-Output 'Copied backend startup: PASS'
}
@{name='TransportBrake.Tests';references=$references;optionalUnityReferences=@('TestAssemblies');autoReferenced=$false} |
 ConvertTo-Json | Set-Content (Join-Path $run 'Assets/CampusSim/Tests/PlayMode/Tests.asmdef')
New-Item -ItemType Directory -Path (Join-Path $run 'Packages') | Out-Null
@{dependencies=$dependencies} |
 ConvertTo-Json -Depth 4 | Set-Content (Join-Path $run 'Packages/manifest.json')
@{scope='isolated WheelCollider + real loopback WebSocket process-loss fixture; not runtime v4 integration';client_v3_integration=[bool]$IncludeClientIntegration;sources=$sources;editor=$version;python=$pythonPath;expected_tests=$expected} |
 ConvertTo-Json -Depth 4 | Set-Content (Join-Path $run 'source-manifest.json')
$result=Join-Path $run 'results.xml'
$log=Join-Path $run 'editor.log'
Write-Output "Test directory: $run"
$arguments="-batchmode -nographics -projectPath `"$run`" -runTests -testPlatform PlayMode -testFilter InhaExpress.Client.Tests.TransportBrakePlayModeTests -testResults `"$result`" -logFile `"$log`""
$oldPython=$env:CAMPUS_BRAKE_TEST_PYTHON
$env:CAMPUS_BRAKE_TEST_PYTHON=$pythonPath
try { $process=Start-Process -FilePath $unity -ArgumentList $arguments -WindowStyle Hidden -PassThru }
finally { $env:CAMPUS_BRAKE_TEST_PYTHON=$oldPython }
try {
 if(-not $process.WaitForExit($TimeoutSeconds*1000)){throw "Test timeout: $log"}
 if(-not(Test-Path -LiteralPath $result)){throw "No results: $log"}
 [xml]$xml=Get-Content -LiteralPath $result -Raw
 $summary=$xml.'test-run'
 if($process.ExitCode -ne 0 -or $summary.result -ne 'Passed' -or [int]$summary.passed -ne $expected -or [int]$summary.total -ne $expected){throw "Tests failed: $result"}
 Write-Output "Passed $($summary.passed)/$($summary.total): $result"
} finally {
 if(-not $process.HasExited){Stop-Process -Id $process.Id -Force}
}
