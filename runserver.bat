@echo off
REM CMU14
if exist "%~dp0runserver.local.bat" call "%~dp0runserver.local.bat"
dotnet run --project Content.Server --configuration Tools
pause
