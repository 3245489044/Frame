@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion

:: ============================================
::   runner.bat — VSCode 插件统一启动器入口
::   显示菜单，让用户选择要执行的启动项
::   用法：runner.bat [--direct <条目索引>]
:: ============================================

set "FRAMEWORK_DIR=%~dp0"
if "%FRAMEWORK_DIR:~-1%"=="\" set "FRAMEWORK_DIR=%FRAMEWORK_DIR:~0,-1%"

title VSCode 插件统一启动器

:: 检查是否直接指定了条目索引
if /i "%~1"=="--direct" (
    set "DIRECT_INDEX=%~2"
    if "!DIRECT_INDEX!"=="" (
        echo 错误: --direct 参数需要指定条目索引
        exit /b 1
    )
    goto :execute_direct
)

:main_menu
cls
echo ============================================
echo     VSCode 插件统一启动器
echo     模块化框架 v2.0
echo ============================================
echo.
echo  框架目录: %FRAMEWORK_DIR%
echo.

:: 检测工作区根目录
for /f %%a in ('call "%FRAMEWORK_DIR%\core\path-resolver.bat" --detect-root') do set "WORKSPACE_ROOT=%%a"
echo  工作区: %WORKSPACE_ROOT%
echo.

:: 加载配置
call "%FRAMEWORK_DIR%\core\config-loader.bat" global >nul 2>nul

:: 显示菜单并获取选择
call "%FRAMEWORK_DIR%\core\menu.bat" --select
set "CHOICE=%errorlevel%"

if "%CHOICE%"=="255" (
    echo.
    echo 已退出
    exit /b 0
)

:execute_direct
echo.
echo ============================================
echo  执行启动项...
echo ============================================
echo.

:: 调用执行调度器
if not "%DIRECT_INDEX%"=="" (
    call "%FRAMEWORK_DIR%\core\executor.bat" %DIRECT_INDEX%
) else (
    call "%FRAMEWORK_DIR%\core\executor.bat" %CHOICE%
)

if !errorlevel! equ 0 (
    echo.
    echo ============================================
    echo  执行成功
    echo ============================================
) else (
    echo.
    echo ============================================
    echo  执行失败 (错误码: !errorlevel!)
    echo ============================================
)

echo.
echo 按任意键返回主菜单...
pause >nul
goto :main_menu
