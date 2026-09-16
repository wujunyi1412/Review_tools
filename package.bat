@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Build.ps1" -Mode Package
if errorlevel 1 (
  echo.
  echo Package failed. Please check the messages above.
  exit /b 1
)
echo.
echo Package completed successfully.
exit /b 0
