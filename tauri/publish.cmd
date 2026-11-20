@echo off
setlocal

cd /d "%~dp0"
for %%I in ("%~dp0..\release") do set "RELEASE_DIR=%%~fI"

where npm >nul 2>nul
if errorlevel 1 (
    echo [OSS Studio Tauri] Node.js was not found. Please install Node.js 20.19+ or 22.12+.
    pause
    exit /b 1
)

where cargo >nul 2>nul
if errorlevel 1 (
    echo [OSS Studio Tauri] Rust was not found. Please install the Rust MSVC toolchain.
    pause
    exit /b 1
)

if not exist "node_modules" (
    echo [OSS Studio Tauri] Installing frontend dependencies...
    call npm install
    if errorlevel 1 (
        echo [OSS Studio Tauri] npm install failed.
        pause
        exit /b 1
    )
)

echo [OSS Studio Tauri] Building release executable...
call npm run tauri -- build --no-bundle
if errorlevel 1 (
    echo.
    echo [OSS Studio Tauri] Build failed. Review the error message above.
    pause
    exit /b 1
)

if not exist "%RELEASE_DIR%" mkdir "%RELEASE_DIR%"
copy /Y "src-tauri\target\release\oss-studio.exe" "%RELEASE_DIR%\OSS-Studio.exe" >nul
if errorlevel 1 (
    echo [OSS Studio Tauri] Copy failed.
    pause
    exit /b 1
)

echo.
echo [OSS Studio Tauri] Publish completed: "%RELEASE_DIR%\OSS-Studio.exe"
echo [OSS Studio Tauri] Windows 10/11 requires the Microsoft Edge WebView2 Runtime.
endlocal
