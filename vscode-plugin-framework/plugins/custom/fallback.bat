@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion

:: ============================================
::   custom/fallback.bat — 自定义命令备用脚本
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
echo [Fallback] 自定义命令备用执行...

set /p "CUSTOM_CMD=请输入要执行的命令: "

if "%CUSTOM_CMD%"=="" (
    echo [Fallback] 未输入命令，取消执行
    exit /b 1
)

cd /d "%WORK_DIR%"

:: 最稳定方式：新窗口执行
start "自定义命令" cmd /c "%CUSTOM_CMD% && pause"

exit /b 0
