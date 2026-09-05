@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion

:: ============================================
::   config-loader.bat — 配置加载器
::   读取 JSON 配置文件并设置环境变量
::   接口：config-loader.bat <配置类型>
::   配置类型: global | entries | plugin:<插件名>
:: ============================================

set "CONFIG_TYPE=%~1"
set "FRAMEWORK_DIR=%~dp0.."

if "%CONFIG_TYPE%"=="" goto :usage

:: 去除路径末尾的反斜杠
if "%FRAMEWORK_DIR:~-1%"=="\" set "FRAMEWORK_DIR=%FRAMEWORK_DIR:~0,-1%"

if /i "%CONFIG_TYPE%"=="global" goto :load_global
if /i "%CONFIG_TYPE%"=="entries" goto :load_entries

:: 检查是否是 plugin:<name> 格式
set "PREFIX=%CONFIG_TYPE:~0,7%"
if /i "%PREFIX%"=="plugin:" goto :load_plugin

goto :usage

:usage
echo 用法: config-loader.bat ^<配置类型^>
echo.
echo 配置类型:
echo   global              加载全局配置
echo   entries             加载启动项注册表
echo   plugin:^<插件名^>     加载指定插件的配置
echo.
exit /b 1

:load_global
if not exist "%FRAMEWORK_DIR%\config\global.config.json" (
    echo 错误: 全局配置文件不存在
    exit /b 1
)
:: 使用 findstr 提取关键配置项
for /f "tokens=2 delims=:" %%a in ('findstr /i "encoding" "%FRAMEWORK_DIR%\config\global.config.json"') do (
    set "GLOBAL_ENCODING=%%a"
    set "GLOBAL_ENCODING=!GLOBAL_ENCODING:"=!"
    set "GLOBAL_ENCODING=!GLOBAL_ENCODING: =!"
    set "GLOBAL_ENCODING=!GLOBAL_ENCODING:,=!"
)
for /f "tokens=2 delims=:" %%a in ('findstr /i "terminal" "%FRAMEWORK_DIR%\config\global.config.json"') do (
    set "GLOBAL_TERMINAL=%%a"
    set "GLOBAL_TERMINAL=!GLOBAL_TERMINAL:"=!"
    set "GLOBAL_TERMINAL=!GLOBAL_TERMINAL: =!"
    set "GLOBAL_TERMINAL=!GLOBAL_TERMINAL:,=!"
)
echo [配置] 编码: %GLOBAL_ENCODING%
echo [配置] 终端: %GLOBAL_TERMINAL%
exit /b 0

:load_entries
if not exist "%FRAMEWORK_DIR%\config\entries.config.json" (
    echo 错误: 启动项配置文件不存在
    exit /b 1
)
:: 提取所有 entry 的 name
echo [配置] 加载启动项列表:
set "ENTRY_INDEX=0"
for /f "tokens=2 delims=:" %%a in ('findstr /i ""name"" "%FRAMEWORK_DIR%\config\entries.config.json"') do (
    set "ENTRY_NAME=%%a"
    set "ENTRY_NAME=!ENTRY_NAME:"=!"
    set "ENTRY_NAME=!ENTRY_NAME: =!"
    set "ENTRY_NAME=!ENTRY_NAME:,=!"
    if not "!ENTRY_NAME!"=="entries" (
        echo   [!ENTRY_INDEX!] !ENTRY_NAME!
        set /a "ENTRY_INDEX+=1"
    )
)
exit /b 0

:load_plugin
set "PLUGIN_NAME=%CONFIG_TYPE:~7%"
if not exist "%FRAMEWORK_DIR%\plugins\%PLUGIN_NAME%\plugin.config.json" (
    echo 错误: 插件 "%PLUGIN_NAME%" 的配置文件不存在
    exit /b 1
)
echo [配置] 加载插件: %PLUGIN_NAME%
for /f "tokens=2 delims=:" %%a in ('findstr /i "displayName" "%FRAMEWORK_DIR%\plugins\%PLUGIN_NAME%\plugin.config.json"') do (
    set "PLUGIN_DISPLAY=%%a"
    set "PLUGIN_DISPLAY=!PLUGIN_DISPLAY:"=!"
    set "PLUGIN_DISPLAY=!PLUGIN_DISPLAY: =!"
    set "PLUGIN_DISPLAY=!PLUGIN_DISPLAY:,=!"
    echo [配置] 显示名: !PLUGIN_DISPLAY!
)
exit /b 0
