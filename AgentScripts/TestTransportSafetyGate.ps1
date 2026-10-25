# Compile and execute the actual pure C# gate. No Unity/Physics test is claimed.
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Add-Type -Path (Join-Path $repo 'Assets/CampusSim/Scripts/Domain/TransportSafetyGate.cs')
$script:checks = 0
function Check([bool]$condition, [string]$name) {
    if (-not $condition) { throw "FAILED: $name" }
    $script:checks++
}
function ReadyGate {
    $g = [InhaExpress.Client.Domain.TransportSafetyGate]::new(0.5)
    Check ($g.BeginSession('r','s','m',0)) 'session'
    Check ($g.ConfirmFullSnapshot('r','s','m')) 'snapshot'
    Check ($g.ConfirmFreshObservations('r','s','m')) 'observations'
    return $g
}
$g = [InhaExpress.Client.Domain.TransportSafetyGate]::new(0.5)
Check ($g.Poll(0,0)) 'initial brake'
Check ($g.BeginSession('r','s','m',0)) 'begin'
Check (-not $g.AcceptCommand('r','s','m',1,0,1,0,0)) 'no snapshot'
Check ($g.ConfirmFullSnapshot('r','s','m')) 'full snapshot'
Check (-not $g.AcceptCommand('r','s','m',1,0,1,0,0)) 'no fresh sensors'
Check ($g.ConfirmFreshObservations('r','s','m')) 'fresh sensors'
Check ($g.AcceptCommand('r','s','m',1,0,1,0,0)) 'valid command'
Check (-not $g.AcceptCommand('r','s','m',1,0,1,0,0.4)) 'duplicate rejected'
Check ($g.Poll(0,0.5)) 'wall timeout despite paused simulation'
Check ($g.Reason -eq 'COMMAND_SILENCE') 'silence reason'
Check (-not $g.AcceptCommand('r','s','m',2,0,1,0,0.6)) 'timeout latches until new session'
Check ($g.BeginSession('r','s2','m',0.6)) 'new session'
Check (-not $g.ConfirmFullSnapshot('r','s','m')) 'old snapshot rejected'
Check ($g.ConfirmFullSnapshot('r','s2','m')) 'new snapshot'
Check (-not $g.AcceptCommand('r','s2','m',1,0,1,0,0.6)) 'reconnect needs sensors'
Check ($g.ConfirmFreshObservations('r','s2','m')) 'new sensors'
Check (-not $g.AcceptCommand('r','s','m',99,0,1,0,0.6)) 'old command rejected'
Check ($g.AcceptCommand('r','s2','m',1,0,1,0,0.6)) 'new command accepted'
$g.Disconnect('SOCKET_CLOSED')
Check ($g.BrakeRequired) 'explicit disconnect brakes'
$g = ReadyGate
Check ($g.AcceptCommand('r','s','m',1,0,0.2,0,0)) 'expiry setup'
Check ($g.Poll(0.2,0.1)) 'exact simulation deadline expires'
Check ($g.Reason -eq 'COMMAND_EXPIRED') 'expiry reason'
Check (-not $g.AcceptCommand('r','s','m',2,0.2,1,0.2,0.1)) 'expired requires new observations'
$g = ReadyGate
Check (-not $g.AcceptCommand('r','s','wrong-map',1,0,1,0,0)) 'wrong map'
Check (-not $g.AcceptCommand('r','s','m',1,0.1,1,0,0)) 'future command'
Check ($g.Poll(0,[double]::NaN)) 'nonfinite wall clock brakes'
$g = ReadyGate
Check ($g.AcceptCommand('r','s','m',1,0,1,0.1,0.1)) 'clock regression setup'
Check ($g.Poll(0.05,0.2)) 'simulation regression brakes'
Write-Output "PASS: $script:checks assertions (pure C#; Unity Physics NOT tested)"
