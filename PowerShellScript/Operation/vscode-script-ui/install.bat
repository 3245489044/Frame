@echo off
chcp 65001 >nul
title 安装 WorkFlowPrj Script 工具面板扩展

echo ============================================
echo   WorkFlowPrj Script 工具面板 — 安装脚本
echo ============================================
echo.

set "SCRIPT_DIR=%~dp0"
set "EXT_DIR=%USERPROFILE%\.vscode\extensions\workflowprj-script-ui"

echo 目标目录: %EXT_DIR%
echo.

:: 如果已存在则先删除
if exist "%EXT_DIR%" (
    echo [信息] 检测到旧版本，正在删除...
    rmdir /s /q "%EXT_DIR%"
)

:: 复制文件
echo [信息] 正在安装扩展...
xcopy /E /I /Y "%SCRIPT_DIR%" "%EXT_DIR%" >nul

if %ERRORLEVEL% equ 0 (
    echo.
    echo [成功] 扩展安装完成！
    echo.
    echo 请重启 VS Code 或按 F1 执行 "Developer: Reload Window" 以加载扩展。
    echo.
    echo 安装后，VS Code 侧边栏将出现 "Script 工具" 图标。
) else (
    echo.
    echo [错误] 安装失败，错误码: %ERRORLEVEL%
)

echo.
pause
