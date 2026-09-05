@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion

:: ============================================
::   dotnet-test/fallback.bat — .NET 测试备用脚本
::   当适配器失败时使用
:: ============================================

:: 解析参数
:parse_args
if "%~1"=="" goto :execute
if /i "%~1"=="--type" set "FILE_TYPE=%~2" & shift & shift & goto :parse_args
if /i "%~1"=="--file" set "FILE_PATH=%~2" & shift & shift & goto :parse_args
if /i "%~1"=="--workdir" set "WORK_DIR=%~2" & shift & shift & goto :parse_args
if /i "%~1"=="--newwindow" set "NEW_WINDOW=%~2" & shift & shift & goto :parse_args
shift & goto :parse_args

:execute
echo [Fallback] .NET 测试备用执行...

:: 检查 dotnet 是否可用
where dotnet >nul 2>nul
if !errorlevel! neq 0 (
    echo [Fallback] 错误: dotnet CLI 不可用
    pause
    exit /b 1
)

cd /d "%WORK_DIR%"

:: 在新窗口中运行测试，避免终端干扰
start ".NET 测试" cmd /c "cd /d "%WORK_DIR%" && dotnet test && pause"

exit /b 0
