@echo off
chcp 65001 >nul
title 解决方案文件精简器

:: ============================================
::  解决方案文件精简器 - 启动器
::  删除 .sln 中 x64/x86 的冗余构建配置
::
::  用法:
::    GenerateSolution.bat              交互模式
::    GenerateSolution.bat --check      仅检查状态
::    GenerateSolution.bat --force      强制精简
::    GenerateSolution.bat --help       显示帮助
::
::  示例:
::    GenerateSolution.bat
::    GenerateSolution.bat --force
:: ============================================

:: 获取当前脚本所在目录
set "SCRIPT_DIR=%~dp0"

:: 构建 PowerShell 脚本路径
set "PS_SCRIPT=%SCRIPT_DIR%GenerateSolution.ps1"

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
    if "%1"=="" (
        rem 交互模式已由 PowerShell 内部处理暂停
    ) else if "%1"=="--check" (
        rem --check 模式通过错误码返回状态，不额外暂停
    ) else (
        echo.
        echo 脚本执行出错，错误码: %ERRORLEVEL%
        pause
    )
)

exit /b %ERRORLEVEL%
