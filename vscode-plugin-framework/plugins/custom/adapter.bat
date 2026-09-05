@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion

:: ============================================
::   custom/adapter.bat — 自定义命令适配器
::   标准接口：adapter.bat --type <类型> --file <文件> --workdir <目录> [--newwindow <true/false>]
::   返回值：0=成功, 1=失败
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
echo [自定义] 自定义命令执行器
echo [自定义] 工作目录: %WORK_DIR%
echo.

:: 提示用户输入命令
set /p "CUSTOM_CMD=请输入要执行的命令: "

if "%CUSTOM_CMD%"=="" (
    echo [自定义] 未输入命令，取消执行
    exit /b 1
)

echo.
echo [自定义] 执行: %CUSTOM_CMD%
echo.

cd /d "%WORK_DIR%"

if /i "%NEW_WINDOW%"=="true" (
    start "自定义命令" cmd /c "cd /d "%WORK_DIR%" && %CUSTOM_CMD% && pause"
) else (
    %CUSTOM_CMD%
)

exit /b !errorlevel!
