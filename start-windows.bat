@echo off
cd /d "%~dp0"
dotnet run --project src\LaunchForge.Desktop
if errorlevel 1 pause
