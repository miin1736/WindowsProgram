@echo off
rem SnapContext version A (AI rewrites your typed memo; only the memo text is sent to the Anthropic API).
rem Needs the ANTHROPIC_API_KEY environment variable. Stops a running SnapContext first.
taskkill /im SnapContext.exe /f >nul 2>&1
if not exist "%~dp0..\src\SnapContext\bin\Debug\net8.0-windows10.0.19041.0\SnapContext.exe" (
  echo SnapContext.exe was not found. Build first: dotnet build src\SnapContext\SnapContext.csproj
  pause
  exit /b 1
)
if "%ANTHROPIC_API_KEY%"=="" (
  echo [WARNING] ANTHROPIC_API_KEY is not set, so AI suggestions will show an error message.
  pause
)
start "" "%~dp0..\src\SnapContext\bin\Debug\net8.0-windows10.0.19041.0\SnapContext.exe" --mode A
