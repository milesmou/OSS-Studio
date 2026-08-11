@echo off
setlocal

cd /d "%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 (
    echo [OSS Studio] .NET SDK was not found. Please install .NET 10 SDK first.
    pause
    exit /b 1
)

set "PROJECT=%~dp0OSS-Studio.csproj"
set "OUTPUT=%~dp0Release"

echo [OSS Studio] Publishing NativeAOT single-file application...
dotnet publish "%PROJECT%" ^
    --configuration Release ^
    --runtime win-x64 ^
    --self-contained true ^
    --output "%OUTPUT%" ^
    -p:PublishAot=true ^
    -p:PublishTrimmed=true ^
    -p:TrimMode=full ^
    -p:PublishSingleFile=true ^
    -p:DebugType=None ^
    -p:DebugSymbols=false ^
    -p:StripSymbols=true

if errorlevel 1 (
    echo.
    echo [OSS Studio] Publish failed. Review the error message above.
    pause
    exit /b 1
)

echo.
echo [OSS Studio] Publish completed: "%OUTPUT%\OSS-Studio.exe"
endlocal
