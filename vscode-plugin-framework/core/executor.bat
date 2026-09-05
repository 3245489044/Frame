@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion

:: ============================================
::   executor.bat — 执行调度器
::   根据用户选择，调用对应插件的适配器执行
::   接口：executor.bat <条目索引>
:: ============================================

set "ENTRY_INDEX=%~1"
if "%ENTRY_INDEX%"=="" (
    echo 错误: 请提供启动项索引
    exit /b 1
)

set "FRAMEWORK_DIR=%~dp0.."
if "%FRAMEWORK_DIR:~-1%"=="\" set "FRAMEWORK_DIR=%FRAMEWORK_DIR:~0,-1%"

:: ========== 1. 检测工作区根目录 ==========
for /f %%a in ('call "%FRAMEWORK_DIR%\core\path-resolver.bat" --detect-root') do set "WORKSPACE_ROOT=%%a"
echo [执行] 工作区根目录: %WORKSPACE_ROOT%

:: ========== 2. 从 entries.config.json 读取指定条目 ==========
set "ENTRY_FOUND=0"
set "CURRENT_INDEX=0"

for /f "tokens=*" %%a in ('type "%FRAMEWORK_DIR%\config\entries.config.json"') do (
    set "LINE=%%a"
    
    echo !LINE! | findstr /i ""name"" >nul
    if !errorlevel! equ 0 if !ENTRY_FOUND! equ 0 (
        for /f "tokens=2 delims=:" %%b in ("!LINE!") do (
            set "TMP=%%b"
            set "TMP=!TMP:"=!"
            set "TMP=!TMP: =!"
            set "TMP=!TMP:,=!"
            if not "!TMP!"=="entries" (
                if !CURRENT_INDEX! equ !ENTRY_INDEX! (
                    set "ENTRY_NAME=!TMP!"
                )
                set /a "CURRENT_INDEX+=1"
            )
        )
    )
)

:: 重新解析获取完整条目信息
set "CURRENT_INDEX=0"
set "READING_TARGET=0"
set "READING_TYPE=0"
set "READING_PLUGIN=0"
set "READING_WORKDIR=0"
set "READING_NEWWINDOW=0"

for /f "tokens=*" %%a in ('type "%FRAMEWORK_DIR%\config\entries.config.json"') do (
    set "LINE=%%a"
    
    echo !LINE! | findstr /i ""name"" >nul
    if !errorlevel! equ 0 (
        for /f "tokens=2 delims=:" %%b in ("!LINE!") do (
            set "TMP=%%b"
            set "TMP=!TMP:"=!"
            set "TMP=!TMP: =!"
            set "TMP=!TMP:,=!"
            if not "!TMP!"=="entries" (
                if !CURRENT_INDEX! equ !ENTRY_INDEX! (
                    set "ENTRY_NAME=!TMP!"
                    set "ENTRY_FOUND=1"
                )
                set /a "CURRENT_INDEX+=1"
            )
        )
        goto :skip_entry
    )
    
    if !ENTRY_FOUND! equ 1 (
        echo !LINE! | findstr /i ""type"" >nul
        if !errorlevel! equ 0 (
            for /f "tokens=2 delims=:" %%b in ("!LINE!") do (
                set "ENTRY_TYPE=%%b"
                set "ENTRY_TYPE=!ENTRY_TYPE:"=!"
                set "ENTRY_TYPE=!ENTRY_TYPE: =!"
                set "ENTRY_TYPE=!ENTRY_TYPE:,=!"
            )
        )
        echo !LINE! | findstr /i ""target"" >nul
        if !errorlevel! equ 0 (
            for /f "tokens=2 delims=:" %%b in ("!LINE!") do (
                set "ENTRY_TARGET=%%b"
                set "ENTRY_TARGET=!ENTRY_TARGET:"=!"
                set "ENTRY_TARGET=!ENTRY_TARGET: =!"
                set "ENTRY_TARGET=!ENTRY_TARGET:,=!"
            )
        )
        echo !LINE! | findstr /i ""plugin"" >nul
        if !errorlevel! equ 0 (
            for /f "tokens=2 delims=:" %%b in ("!LINE!") do (
                set "ENTRY_PLUGIN=%%b"
                set "ENTRY_PLUGIN=!ENTRY_PLUGIN:"=!"
                set "ENTRY_PLUGIN=!ENTRY_PLUGIN: =!"
                set "ENTRY_PLUGIN=!ENTRY_PLUGIN:,=!"
            )
        )
        echo !LINE! | findstr /i ""workDir"" >nul
        if !errorlevel! equ 0 (
            for /f "tokens=2 delims=:" %%b in ("!LINE!") do (
                set "ENTRY_WORKDIR=%%b"
                set "ENTRY_WORKDIR=!ENTRY_WORKDIR:"=!"
                set "ENTRY_WORKDIR=!ENTRY_WORKDIR: =!"
                set "ENTRY_WORKDIR=!ENTRY_WORKDIR:,=!"
            )
        )
        echo !LINE! | findstr /i ""newWindow"" >nul
        if !errorlevel! equ 0 (
            for /f "tokens=2 delims=:" %%b in ("!LINE!") do (
                set "ENTRY_NEWWINDOW=%%b"
                set "ENTRY_NEWWINDOW=!ENTRY_NEWWINDOW: =!"
                set "ENTRY_NEWWINDOW=!ENTRY_NEWWINDOW:,=!"
            )
        )
    )
    :skip_entry
)

if !ENTRY_FOUND! equ 0 (
    echo 错误: 未找到索引 %ENTRY_INDEX% 对应的启动项
    exit /b 1
)

echo [执行] 启动项: %ENTRY_NAME%
echo [执行] 类型: %ENTRY_TYPE%
echo [执行] 目标: %ENTRY_TARGET%
echo [执行] 插件: %ENTRY_PLUGIN%

:: ========== 3. 解析工作目录 ==========
set "WORK_DIR=%ENTRY_WORKDIR:%%WORKSPACE_ROOT%%=%WORKSPACE_ROOT%"
echo [执行] 工作目录: %WORK_DIR%

:: ========== 4. 调用插件适配器 ==========
set "ADAPTER=%FRAMEWORK_DIR%\plugins\%ENTRY_PLUGIN%\adapter.bat"
set "FALLBACK=%FRAMEWORK_DIR%\plugins\%ENTRY_PLUGIN%\fallback.bat"

if not exist "%ADAPTER%" (
    echo [执行] 适配器不存在，尝试备用脚本...
    goto :try_fallback
)

echo [执行] 调用适配器: %ADAPTER%
echo.

:: 调用适配器
call "%ADAPTER%" --type "%ENTRY_TYPE%" --file "%ENTRY_TARGET%" --workdir "%WORK_DIR%" --newwindow "%ENTRY_NEWWINDOW%"

if !errorlevel! equ 0 (
    echo.
    echo [执行] 执行成功
    exit /b 0
)

echo [执行] 适配器返回错误码: !errorlevel!

:try_fallback
if not exist "%FALLBACK%" (
    echo [执行] 备用脚本也不存在，执行失败
    exit /b 1
)

echo [执行] 尝试备用脚本: %FALLBACK%
echo.

call "%FALLBACK%" --type "%ENTRY_TYPE%" --file "%ENTRY_TARGET%" --workdir "%WORK_DIR%" --newwindow "%ENTRY_NEWWINDOW%"

if !errorlevel! equ 0 (
    echo.
    echo [执行] 备用脚本执行成功
    exit /b 0
)

echo [执行] 执行失败
exit /b 1
