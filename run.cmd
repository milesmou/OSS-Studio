@echo off
setlocal

cd /d "%~dp0tauri"

where npm >nul 2>nul
if errorlevel 1 (
    echo [OSS Studio] Node.js was not found. Please install Node.js 20.19+ or 22.12+.
    pause
    exit /b 1
)

where cargo >nul 2>nul
if errorlevel 1 (
    echo [OSS Studio] Rust was not found. Please install the Rust MSVC toolchain.
    pause
    exit /b 1
)

if not exist "node_modules" (
    echo [OSS Studio] Installing frontend dependencies...
    call npm install
    if errorlevel 1 (
        echo [OSS Studio] npm install failed.
        pause
        exit /b 1
    )
)

echo [OSS Studio] Starting Tauri development build...
call npm run tauri -- dev
if errorlevel 1 (
    echo.
    echo [OSS Studio] Development build failed. Review the error message above.
    pause
    exit /b 1
)

endlocal
