@echo off
setlocal
cd /d "%~dp0"

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0run-all.ps1"

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo DDisplay launcher encountered an error.
    pause
)
