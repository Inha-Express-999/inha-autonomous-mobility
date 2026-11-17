@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0start_recovery_server.ps1" %*
exit /b %errorlevel%
