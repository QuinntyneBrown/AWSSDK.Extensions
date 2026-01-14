@echo off
setlocal

echo ========================================
echo   Starting Enterprise File Storage
echo ========================================
echo.

set SCRIPT_DIR=%~dp0
set BACKEND_DIR=%SCRIPT_DIR%..\src\FileStorage
set FRONTEND_DIR=%SCRIPT_DIR%..\src\FileStorage.Workspace

echo [1/2] Starting FileStorage Backend...
cd /d "%BACKEND_DIR%"
start "FileStorage-Backend" cmd /c "dotnet run --urls=http://localhost:5000"

echo [2/2] Starting File Storage Frontend...
cd /d "%FRONTEND_DIR%"
start "FileStorage-Frontend" cmd /c "npm start"

echo.
echo ========================================
echo   Services Started
echo ========================================
echo.
echo   Backend:  http://localhost:5000
echo   Swagger:  http://localhost:5000/swagger
echo   Frontend: http://localhost:4200
echo.
echo   Run down.bat to stop all services
echo ========================================

endlocal
