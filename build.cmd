@echo off
rem Double-click to build. Results: dist\build-log.txt and dist\selftest-report.txt
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1"
if errorlevel 1 (
  echo.
  echo Build FAILED. See dist\build-log.txt
) else (
  echo.
  echo Build OK: dist\CodexMarkdownReader.exe
)
if not "%1"=="--no-pause" pause
