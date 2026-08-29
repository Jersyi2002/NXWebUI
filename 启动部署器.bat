@echo off
cd /d "%~dp0"
if not exist "dist\NxWebUIDeployer.exe" (
  echo Building...
  dotnet build "%~dp0NxWebUIDeployer.slnx" -c Release
)
start "" "%~dp0dist\NxWebUIDeployer.exe"
