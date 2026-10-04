@echo off
rem SnapContext version C (prompt templates, no AI, nothing leaves this PC).
rem Stops a running SnapContext first because two copies cannot share Ctrl+Alt+S.
taskkill /im SnapContext.exe /f >nul 2>&1
if not exist "%~dp0..\src\SnapContext\bin\Debug\net8.0-windows10.0.19041.0\SnapContext.exe" (
  echo SnapContext.exe was not found. Build first: dotnet build src\SnapContext\SnapContext.csproj
  pause
  exit /b 1
)
start "" "%~dp0..\src\SnapContext\bin\Debug\net8.0-windows10.0.19041.0\SnapContext.exe" --mode C
