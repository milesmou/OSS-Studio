@echo off
setlocal

cd /d "%~dp0"
call "%~dp0tauri\publish.cmd"
if errorlevel 1 exit /b 1

echo.
echo [OSS Studio] Published to "%~dp0release\OSS-Studio.exe"
endlocal
