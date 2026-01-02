@echo off
echo === Trade Analyzer - Auto Load ===
echo.

REM Run PowerShell script to generate auto_data.js
powershell -ExecutionPolicy Bypass -File "%~dp0load_data.ps1"

echo.
echo Opening Trade Analyzer in browser...

REM Open index.html in default browser
start "" "%~dp0index.html"

echo.
echo Done! The browser should now be open with your data loaded.
timeout /t 3
