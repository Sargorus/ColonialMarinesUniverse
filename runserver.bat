@echo off
REM CMU14
if exist "%~dp0runserver.local.bat" call "%~dp0runserver.local.bat"

netstat -ano | findstr /C:":1212" >nul
if not errorlevel 1 (
    echo [CMU14] Port 1212 is already in use.
    echo Close the running server first, then run this file again.
    pause
    exit /b 1
)

dotnet run --project Content.Server --configuration Tools
pause
