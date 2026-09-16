@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Build.ps1" -Mode Build
if errorlevel 1 (
  echo.
  echo Build failed. Please check the messages above.
  exit /b 1
)
echo.
echo Build completed successfully.
exit /b 0
