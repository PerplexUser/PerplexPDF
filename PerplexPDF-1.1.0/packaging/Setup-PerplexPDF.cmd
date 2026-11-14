@echo off
setlocal
if not exist "%~dp0Setup-PerplexPDF.ps1" (
    echo Extract the entire PerplexPDF ZIP to a folder, then run this file there.
    pause
    exit /b 1
)
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Setup-PerplexPDF.ps1"
if errorlevel 1 (
    echo.
    echo PerplexPDF setup failed. See the message above.
    pause
    exit /b 1
)
