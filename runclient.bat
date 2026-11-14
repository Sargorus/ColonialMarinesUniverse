@echo off
REM CMU14
if exist "%~dp0runclient.local.bat" call "%~dp0runclient.local.bat"
dotnet run --project Content.Client --configuration Tools
pause
