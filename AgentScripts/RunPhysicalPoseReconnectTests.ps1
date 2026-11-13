param([string]$PythonExecutable='python',[int]$TimeoutSeconds=180,[switch]$PrepareOnly)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$run=Join-Path $repo ('tmp/pose-unity-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $run -Force | Out-Null
$files=@(Get-ChildItem (Join-Path $repo 'Assets/CampusSim/Scripts/MapClient') -Filter *.cs | ForEach-Object { 'Assets/CampusSim/Scripts/MapClient/'+$_.Name })
$files+=@('IAutonomousVehicle.cs','IOperatorVehicle.cs','PhysicalLidarSource.cs','ConnectionObservationBuffer.cs','UnityAutonomousClient.cs') | ForEach-Object {'Assets/CampusSim/Scripts/Presentation/'+$_}
$files+= 'Assets/CampusSim/Tests/PlayMode/PhysicalPoseReconnectTests.cs'
$hashes=@{}
foreach($file in $files){$dst=Join-Path $run $file; New-Item -ItemType Directory (Split-Path $dst) -Force | Out-Null; Copy-Item (Join-Path $repo $file) $dst; $hashes[$file]=(Get-FileHash (Join-Path $repo $file)).Hash}
New-Item -ItemType Directory "$run/Packages","$run/ProjectSettings","$run/AgentScripts" -Force | Out-Null
'{"name":"Reconnect.Runtime","autoReferenced":true}' | Set-Content "$run/Assets/CampusSim/Scripts/Replay.asmdef"
'{"name":"Reconnect.Tests","references":["Reconnect.Runtime"],"optionalUnityReferences":["TestAssemblies"]}' | Set-Content "$run/Assets/CampusSim/Tests/PlayMode/Tests.asmdef"
'{"dependencies":{"com.unity.test-framework":"1.6.0","com.unity.modules.physics":"1.0.0","com.unity.modules.vehicles":"1.0.0","com.unity.modules.jsonserialize":"1.0.0","com.unity.modules.imgui":"1.0.0","com.unity.modules.unitywebrequest":"1.0.0"}}' | Set-Content "$run/Packages/manifest.json"
Copy-Item "$repo/ProjectSettings/ProjectVersion.txt" "$run/ProjectSettings/ProjectVersion.txt"
$fixture=Join-Path $repo 'AgentScripts/pose_reconnect_fixture.py'
$literal=$fixture.Replace('\','/')
"import runpy; runpy.run_path('$literal',run_name='__main__')" | Set-Content "$run/AgentScripts/pose_reconnect_fixture.py"
$hashes['AgentScripts/pose_reconnect_fixture.py']=(Get-FileHash $fixture).Hash
$hashes | ConvertTo-Json | Set-Content "$run/source-manifest.json"
Set-Content "$repo/tmp/pose-unity-latest.txt" $run
$pythonPath=(Get-Command $PythonExecutable).Source
Set-Content -Encoding utf8 "$run/AgentScripts/test-python.txt" $pythonPath
if($PrepareOnly){ Write-Output "Prepared Unity project: $run"; return }
$oldPython=$env:CAMPUS_BRAKE_TEST_PYTHON
$env:CAMPUS_BRAKE_TEST_PYTHON=$pythonPath
$unity='C:/Program Files/Unity/Hub/Editor/6000.3.21f1/Editor/Unity.exe'
try { $proc=Start-Process $unity -ArgumentList "-batchmode -nographics -projectPath `"$run`" -runTests -testPlatform PlayMode -testFilter InhaExpress.Client.Tests.PhysicalPoseReconnectTests -testResults `"$run/results.xml`" -logFile `"$run/editor.log`"" -WindowStyle Hidden -PassThru }
finally { $env:CAMPUS_BRAKE_TEST_PYTHON=$oldPython }
if(-not $proc.WaitForExit($TimeoutSeconds*1000)){ $proc.Kill(); throw "Owned test process timed out: $run" }
if(-not(Test-Path "$run/results.xml")){throw "No Unity result: $run"}
[xml]$xml=Get-Content "$run/results.xml"
if($xml.'test-run'.result -ne 'Passed' -or [int]$xml.'test-run'.passed -ne 1){throw "Test failed: $run/results.xml"}
Write-Output "Passed 1/1: $run/results.xml"
