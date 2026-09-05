@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion

:: ============================================
::  快速清理所有 bin 和 obj 目录（纯 .bat 实现）
::
::  与 CleanupExtraBuildOutput.ps1 的关系：
::  - clean.bat：纯 .bat 实现，不依赖 PowerShell，适合无 PowerShell 环境
::  - clean.ps1：PowerShell 实现，支持 -WhatIf 模拟模式
::  - CleanupExtraBuildOutput.ps1：重量级，包含 .csproj 配置修复
::
::  日常清理推荐使用 clean.ps1（功能更完善）
:: ============================================

echo ========================================
echo  正在清理所有 bin 和 obj 目录...
echo ========================================
echo.

set "SCRIPT_DIR=%~dp0"
if "!SCRIPT_DIR:~-1!"=="\" set "SCRIPT_DIR=!SCRIPT_DIR:~0,-1!"

REM 从 Tools 目录向上返回一级到项目根目录
for %%i in ("!SCRIPT_DIR!") do set "ROOT_DIR=%%~dpi"
if "!ROOT_DIR:~-1!"=="\" set "ROOT_DIR=!ROOT_DIR:~0,-1!"

echo  根目录: !ROOT_DIR!
echo.

set "DELETED_COUNT=0"

for /f "tokens=*" %%d in ('dir "!ROOT_DIR!" /s /b /ad 2^>nul') do (
    set "CURRENT_DIR=%%~nxd"
    if /i "!CURRENT_DIR!"=="bin" (
        rmdir /s /q "%%d" 2>nul
        if not exist "%%d" (
            set /a DELETED_COUNT+=1
            echo  [已删除] %%d
        ) else (
            echo  [失败] %%d ^(可能被占用^)
        )
    )
    if /i "!CURRENT_DIR!"=="obj" (
        rmdir /s /q "%%d" 2>nul
        if not exist "%%d" (
            set /a DELETED_COUNT+=1
            echo  [已删除] %%d
        ) else (
            echo  [失败] %%d ^(可能被占用^)
        )
    )
)

echo.
echo ========================================
echo  清理完成！共删除了 !DELETED_COUNT! 个目录
echo ========================================

pause
endlocal
