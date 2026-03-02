@echo off
setlocal

echo ========================================
echo Publish + WebDeploy to MonsterASP
echo ========================================

set "ROOT_DIR=%~dp0"
set "PROJECT=EJCFitnessGym.csproj"
set "PUBLISH_PROFILE=site55020-WebDeploy"
set "CONFIGURATION=Release"
set "PROD_SETTINGS=appsettings.Production.json"

cd /d "%ROOT_DIR%"

if not exist "%PROJECT%" (
    echo ERROR: %PROJECT% was not found in %ROOT_DIR%
    exit /b 1
)

if not exist "%PROD_SETTINGS%" (
    echo ERROR: %PROD_SETTINGS% is missing. Startup will fail in Production.
    exit /b 1
)

for %%I in ("%PROD_SETTINGS%") do (
    if %%~zI EQU 0 (
        echo ERROR: %PROD_SETTINGS% is empty. Startup will fail with HTTP 500.30.
        exit /b 1
    )
)

echo.
echo Running publish profile "%PUBLISH_PROFILE%"...
dotnet publish "%PROJECT%" /p:PublishProfile=%PUBLISH_PROFILE% /p:Configuration=%CONFIGURATION%
if errorlevel 1 (
    echo.
    echo Deployment failed.
    exit /b 1
)

echo.
echo Deployment completed successfully.
echo WebDeploy pushed only changed files based on the publish profile.
exit /b 0
