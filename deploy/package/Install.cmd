@echo off
rem Double-click to install SqlSense into SSMS 22. Close SSMS first.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install.ps1" %*
pause
