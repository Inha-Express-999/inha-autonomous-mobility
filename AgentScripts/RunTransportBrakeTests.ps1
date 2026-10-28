[CmdletBinding()]
param([ValidateRange(30,900)][int]$TimeoutSeconds=600,[string]$PythonExecutable='python',[switch]$IncludeClientIntegration)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if($IncludeClientIntegration){throw 'Older schema-v3 /v1/client/ws backend is not the campus physics service. See Docs/WEEK8_INTEGRATION.md. This switch is unsupported.'}
$version=((Get-Content (Join-Path $repo 'ProjectSettings/ProjectVersion.txt') | Where-Object {$_ -match '^m_EditorVersion:'}) -split ':',2)[1].Trim()
$unity="C:/Program Files/Unity/Hub/Editor/$version/Editor/Unity.exe"
if(-not(Test-Path -LiteralPath $unity)){throw "Unity missing: $version"}
$run=Join-Path $repo ('tmp/transport-brake-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $run -Force | Out-Null
$pythonPath=(Get-Command $PythonExecutable -ErrorAction Stop).Source
$result=Join-Path $run 'results.xml'
$log=Join-Path $run 'editor.log'
$filter='InhaExpress.Client.Tests.TransportBrakePlayModeTests.WorkerDisconnectAppliesWheelBrakeAndStopsMovingBody;InhaExpress.Client.Tests.TransportBrakePlayModeTests.PythonProcessLossReachesWheelBrakesThroughRealWebSocket;InhaExpress.Client.Tests.TransportBrakePlayModeTests.SilenceWithPausedSimulationLatchesBrakeAndReconnectRequiresFreshState;InhaExpress.Client.Tests.TransportBrakePlayModeTests.CompetingSyntheticDriverIsRejected;InhaExpress.Client.Tests.TransportBrakePlayModeTests.FixtureCannotBindPhysicalVehicle;InhaExpress.Client.Tests.TransportBrakePlayModeTests.InactiveVehicleBindsAndDisabledHostKeepsBrake;InhaExpress.Client.Tests.TeamVehicleBrakePlayModeTests.TeamPrefabStartsBrakedAndBindsToLivePcHost'
$arguments="-batchmode -nographics -projectPath `"$repo`" -runTests -testPlatform PlayMode -testFilter `"$filter`" -testResults `"$result`" -logFile `"$log`""
$oldPython=$env:CAMPUS_BRAKE_TEST_PYTHON
$env:CAMPUS_BRAKE_TEST_PYTHON=$pythonPath
try {$process=Start-Process -FilePath $unity -ArgumentList $arguments -WindowStyle Hidden -PassThru}
finally {$env:CAMPUS_BRAKE_TEST_PYTHON=$oldPython}
if(-not $process.WaitForExit($TimeoutSeconds*1000)){throw "Test still running: PID=$($process.Id), log=$log"}
if(-not(Test-Path -LiteralPath $result)){throw "No results: $log"}
[xml]$xml=Get-Content -LiteralPath $result -Raw
$summary=$xml.'test-run'
if($process.ExitCode -ne 0 -or $summary.result -ne 'Passed' -or [int]$summary.passed -ne 7 -or [int]$summary.total -ne 7){throw "Tests failed: $result"}
Write-Output "Passed $($summary.passed)/$($summary.total): $result"
