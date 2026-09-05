@echo off
chcp 65001 >nul
title 删除本地除 Main/main 以外的所有分支

:: ============================================
::  Remove-BranchesExceptMain.bat
::  包装器 - 通过批处理调用 PowerShell 脚本
::  解决 .ps1 文件双击闪退的问题
::
::  用法:
::    Remove-BranchesExceptMain.bat           交互式删除
::    Remove-BranchesExceptMain.bat -Force    直接删除，不提示
:: ============================================

:: 获取当前脚本所在目录
set "SCRIPT_DIR=%~dp0"

:: 构建 PowerShell 脚本路径
set "PS_SCRIPT=%SCRIPT_DIR%Remove-BranchesExceptMain.ps1"

:: 检查 PowerShell 脚本是否存在
if not exist "%PS_SCRIPT%" (
    echo [错误] 找不到 PowerShell 脚本: %PS_SCRIPT%
    pause
    exit /b 1
)

echo ============================================
echo  正在执行分支清理脚本...
echo ============================================
echo.

:: 传递所有参数给 PowerShell 脚本
powershell -NoProfile -ExecutionPolicy Bypass -File "%PS_SCRIPT%" %*

:: 记录执行结果
if %ERRORLEVEL% EQU 0 (
    echo.
    echo ============================================
    echo  脚本执行完毕
    echo ============================================
) else (
    echo.
    echo ============================================
    echo  脚本执行出错，错误码: %ERRORLEVEL%
    echo ============================================
)

echo.
pause
