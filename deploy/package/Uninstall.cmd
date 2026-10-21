@echo off
rem Double-click to remove SqlSense from SSMS 22. Close SSMS first.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Uninstall.ps1" %*
pause
