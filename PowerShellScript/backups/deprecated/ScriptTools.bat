@echo off
chcp 65001 >nul
title Script 工具集

:: ============================================
::  Script 工具集启动器 - 快速启动
::  直接调用 PowerShell 脚本运行
::
::  用法:
::    ScriptTools.bat <工具文件名>      运行指定工具
::    ScriptTools.bat --list            列出可用工具
::    ScriptTools.bat --info            显示配置信息
::    ScriptTools.bat --show <文件名>   显示指定工具内容
::    ScriptTools.bat --help            显示帮助
::
::  不传参进入交互选择界面
::
::  示例:
::    ScriptTools.bat CleanupExtraBuildOutput.ps1
::    ScriptTools.bat --list
:: ============================================

:: 获取当前脚本所在目录
set "SCRIPT_DIR=%~dp0"

:: 构建 PowerShell 脚本路径
set "PS_SCRIPT=%SCRIPT_DIR%ScriptTools.ps1"

:: 检查 PowerShell 脚本是否存在
if not exist "%PS_SCRIPT%" (
    echo [错误] 找不到 PowerShell 脚本: %PS_SCRIPT%
    pause
    exit /b 1
)

:: 传递所有参数给 PowerShell 脚本
powershell -NoProfile -ExecutionPolicy Bypass -File "%PS_SCRIPT%" %*

:: 如果 PowerShell 执行出错则暂停
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo 脚本执行出错，错误码: %ERRORLEVEL%
    pause
)

exit /b %ERRORLEVEL%
