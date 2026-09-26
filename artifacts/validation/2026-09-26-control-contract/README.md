# Control contract compilation regression

Unity 6000.3.21f1 · `AgentScripts/RunUnityFollowerTests.ps1` · PlayMode 9/9 passed.
Copied Domain sources include new VehicleControlDto and optional snapshot commands.
Source manifest binds the executed source. Tests exercise the existing follower,
not new-command network deserialization or actuation. Those gates remain pending.
Existing Unity allocation diagnostics remain. No Player/original-scene claim.
