@echo off
chcp 65001 >nul
echo Installing STOOM AI for Hancom HWP...
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1" -Install
if errorlevel 1 (echo Installation failed. See the message above.) else (echo Installation completed. Restart HWP to load STOOM AI.)
pause
