@echo off
setlocal

echo ========================================
echo   Stopping Enterprise File Storage
echo ========================================
echo.

echo [1/2] Stopping FileStorage Backend...
taskkill /FI "WINDOWTITLE eq FileStorage-Backend*" /F >nul 2>&1
for /f "tokens=5" %%a in ('netstat -aon ^| findstr ":5000" ^| findstr "LISTENING"') do (
    taskkill /PID %%a /F >nul 2>&1
)

echo [2/2] Stopping File Storage Frontend...
taskkill /FI "WINDOWTITLE eq FileStorage-Frontend*" /F >nul 2>&1
for /f "tokens=5" %%a in ('netstat -aon ^| findstr ":4200" ^| findstr "LISTENING"') do (
    taskkill /PID %%a /F >nul 2>&1
)

echo.
echo ========================================
echo   All services stopped
echo ========================================

endlocal
