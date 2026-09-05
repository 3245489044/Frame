@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion

:: ============================================
::   code-runner/fallback.bat — Code Runner 备用脚本
::   当适配器失败时使用，直接调用系统命令执行
::   接口：fallback.bat --type <类型> --file <文件> --workdir <目录> [--newwindow <true/false>]
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
    echo [Fallback] 错误: 未指定文件路径
    exit /b 1
)

:: 解析完整路径
if not "%WORK_DIR%"=="" (
    if not exist "%FILE_PATH%" (
        set "FULL_PATH=%WORK_DIR%\%FILE_PATH%"
    ) else (
        set "FULL_PATH=%FILE_PATH%"
    )
) else (
    set "FULL_PATH=%FILE_PATH%"
)

echo [Fallback] 备用执行: !FULL_PATH!
echo [Fallback] 工作目录: %WORK_DIR%

:: ========== 最稳定的执行方式 ==========
:: 使用 start 命令在新窗口中启动，完全隔离
:: 这是最接近在文件资源管理器中双击的效果

if /i "%NEW_WINDOW%"=="true" (
    echo [Fallback] 在新窗口中启动（最稳定模式）...
    
    :: 方式1: 使用 cmd /c start（最可靠）
    if /i "%FILE_TYPE%"=="bat" (
        start "启动器" cmd /c "cd /d "%WORK_DIR%" && call "!FULL_PATH!""
    ) else (
        start "启动器" "!FULL_PATH!"
    )
) else (
    echo [Fallback] 在当前窗口执行...
    cd /d "%WORK_DIR%"
    
    if /i "%FILE_TYPE%"=="bat" (
        call "!FULL_PATH!"
    ) else if /i "%FILE_TYPE%"=="ps1" (
        powershell -ExecutionPolicy ByPass -File "!FULL_PATH!"
    ) else if /i "%FILE_TYPE%"=="py" (
        python -u "!FULL_PATH!"
    ) else if /i "%FILE_TYPE%"=="js" (
        node "!FULL_PATH!"
    ) else (
        "!FULL_PATH!"
    )
)

if !errorlevel! equ 0 (
    echo [Fallback] 执行成功
    exit /b 0
) else (
    echo [Fallback] 执行失败，错误码: !errorlevel!
    exit /b !errorlevel!
)
