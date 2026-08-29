@echo off
cd /d "%~dp0"
if not exist "NxWebUITool\deploy\startup\NxWebUI.men" (
  echo Building plugin...
  dotnet build "%~dp0NxWebUITool\NxWebUITool.slnx" -c Release
  if errorlevel 1 exit /b 1
)
if not exist "dist\NxWebUIDeployer.exe" (
  echo Building deployer...
  dotnet build "%~dp0NxWebUIDeployer.slnx" -c Release
  if errorlevel 1 exit /b 1
)
start "" "%~dp0dist\NxWebUIDeployer.exe"
