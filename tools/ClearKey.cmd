@echo off
rem Jega, for testing: takes the Datafordeler key out of this PC's settings (a backup first). See ClearKey.ps1.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0ClearKey.ps1"
pause
