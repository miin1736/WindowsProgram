@echo off
rem SnapContext baseline (your typed text becomes the caption as-is, no AI). Stops a running SnapContext first.
taskkill /im SnapContext.exe /f >nul 2>&1
if not exist "%~dp0..\src\SnapContext\bin\Debug\net8.0-windows\SnapContext.exe" (
  echo SnapContext.exe was not found. Build first: dotnet build src\SnapContext\SnapContext.csproj
  pause
  exit /b 1
)
start "" "%~dp0..\src\SnapContext\bin\Debug\net8.0-windows\SnapContext.exe"
