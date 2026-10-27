@echo off
rem Jega, for testing: puts the newest settings backup (with the key) back. See ClearKey.ps1.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0ClearKey.ps1" -Restore
pause
