@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion

:: ============================================
::   menu.bat — 菜单系统
::   显示启动菜单并获取用户选择
::   接口：menu.bat [--show | --select]
:: ============================================

set "CMD=%~1"
set "FRAMEWORK_DIR=%~dp0.."

if "%FRAMEWORK_DIR:~-1%"=="\" set "FRAMEWORK_DIR=%FRAMEWORK_DIR:~0,-1%"

if "%CMD%"=="--show" goto :show_menu
if "%CMD%"=="--select" goto :select_entry
goto :usage

:usage
echo 用法: menu.bat [选项]
echo.
echo 选项:
echo   --show                 显示菜单
echo   --select               显示菜单并等待选择
echo.
exit /b 1

:show_menu
echo ============================================
echo     VSCode 插件统一启动器
echo ============================================
echo.
echo  可用启动项:
echo.

set "ENTRY_INDEX=0"
for /f "tokens=*" %%a in ('type "%FRAMEWORK_DIR%\config\entries.config.json" ^| findstr /i "name"') do (
    set "LINE=%%a"
    set "LINE=!LINE:"=!"
    set "LINE=!LINE: =!"
    set "LINE=!LINE:,=!"
    for /f "tokens=2 delims=:" %%b in ("!LINE!") do (
        if not "%%b"=="entries" (
            set "ENTRY_NAME[!ENTRY_INDEX!]=%%b"
            set /a "ENTRY_INDEX+=1"
        )
    )
)

:: 显示带描述的菜单
set "ENTRY_INDEX=0"
for /f "tokens=*" %%a in ('type "%FRAMEWORK_DIR%\config\entries.config.json"') do (
    set "LINE=%%a"
    echo !LINE! | findstr /i "name" >nul
    if !errorlevel! equ 0 (
        for /f "tokens=2 delims=:" %%b in ("!LINE!") do (
            set "NAME=%%b"
            set "NAME=!NAME:"=!"
            set "NAME=!NAME: =!"
            set "NAME=!NAME:,=!"
            if not "!NAME!"=="entries" (
                set "CURRENT_NAME=!NAME!"
            )
        )
    )
    echo !LINE! | findstr /i "description" >nul
    if !errorlevel! equ 0 (
        for /f "tokens=2 delims=:" %%b in ("!LINE!") do (
            set "DESC=%%b"
            set "DESC=!DESC:"=!"
            set "DESC=!DESC: =!"
            set "DESC=!DESC:,=!"
            echo   [!ENTRY_INDEX!] !CURRENT_NAME! - !DESC!
            set /a "ENTRY_INDEX+=1"
        )
    )
)

echo.
echo   [Q] 退出
echo.
exit /b 0

:select_entry
call :show_menu

set /p "CHOICE=请选择 (0-%ENTRY_INDEX% 或 Q): "

if /i "!CHOICE!"=="Q" exit /b 255

:: 验证输入是否为数字
set "IS_NUM=!CHOICE!"
set "IS_NUM=!IS_NUM:0=!"
set "IS_NUM=!IS_NUM:1=!"
set "IS_NUM=!IS_NUM:2=!"
set "IS_NUM=!IS_NUM:3=!"
set "IS_NUM=!IS_NUM:4=!"
set "IS_NUM=!IS_NUM:5=!"
set "IS_NUM=!IS_NUM:6=!"
set "IS_NUM=!IS_NUM:7=!"
set "IS_NUM=!IS_NUM:8=!"
set "IS_NUM=!IS_NUM:9=!"

if not "!IS_NUM!"=="" (
    echo 无效输入，请输入数字或 Q
    pause
    goto :select_entry
)

if !CHOICE! geq !ENTRY_INDEX! (
    echo 无效选择，请输入 0-!ENTRY_INDEX!
    pause
    goto :select_entry
)

:: 返回选择的索引
exit /b !CHOICE!
