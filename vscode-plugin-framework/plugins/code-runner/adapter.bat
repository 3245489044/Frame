@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion

:: ============================================
::   code-runner/adapter.bat — Code Runner 适配器
::   标准接口：adapter.bat --type <类型> --file <文件> --workdir <目录> [--newwindow <true/false>]
::   返回值：0=成功, 1=失败, 2=插件不可用
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
if "%FILE_PATH%"=="" (
    echo [错误] 未指定文件路径
    exit /b 1
)

:: 解析文件路径（如果是相对路径，基于工作目录拼接）
if not "%WORK_DIR%"=="" (
    if not exist "%FILE_PATH%" (
        set "FULL_PATH=%WORK_DIR%\%FILE_PATH%"
    ) else (
        set "FULL_PATH=%FILE_PATH%"
    )
) else (
    set "FULL_PATH=%FILE_PATH%"
)

:: 去除首尾空格
set "FULL_PATH=!FULL_PATH: =!

echo [Code Runner] 执行文件: !FULL_PATH!
echo [Code Runner] 工作目录: %WORK_DIR%
echo [Code Runner] 新窗口: %NEW_WINDOW%

:: 获取文件扩展名
set "EXT=%~x1"
if "%EXT%"=="" set "EXT=.bat"

:: 根据类型选择执行方式
if /i "%FILE_TYPE%"=="bat" goto :exec_bat
if /i "%FILE_TYPE%"=="cmd" goto :exec_bat
if /i "%FILE_TYPE%"=="ps1" goto :exec_ps1
if /i "%FILE_TYPE%"=="py" goto :exec_py
if /i "%FILE_TYPE%"=="js" goto :exec_js
goto :exec_default

:exec_bat
echo [Code Runner] 使用 cmd /c 执行 bat 文件...
if /i "%NEW_WINDOW%"=="true" (
    :: 在新窗口中启动，完全隔离环境
    :: 使用完整路径避免中文文件名识别问题
    start "Code Runner" cmd /c "!FULL_PATH!"
) else (
    :: 在当前窗口中执行
    cd /d "%WORK_DIR%"
    cmd /c "!FULL_PATH!"
)
exit /b !errorlevel!

:exec_ps1
echo [Code Runner] 使用 PowerShell 执行...
if /i "%NEW_WINDOW%"=="true" (
    start "Code Runner" powershell -ExecutionPolicy ByPass -File "!FULL_PATH!"
) else (
    cd /d "%WORK_DIR%"
    powershell -ExecutionPolicy ByPass -File "!FULL_PATH!"
)
exit /b !errorlevel!

:exec_py
echo [Code Runner] 使用 Python 执行...
cd /d "%WORK_DIR%"
python -u "!FULL_PATH!"
exit /b !errorlevel!

:exec_js
echo [Code Runner] 使用 Node.js 执行...
cd /d "%WORK_DIR%"
node "!FULL_PATH!"
exit /b !errorlevel!

:exec_default
echo [Code Runner] 未知类型 %FILE_TYPE%，尝试直接执行...
if /i "%NEW_WINDOW%"=="true" (
    start "Code Runner" "!FULL_PATH!"
) else (
    "!FULL_PATH!"
)
exit /b !errorlevel!
