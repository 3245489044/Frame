@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion

:: ============================================
::   install.bat — 安装脚本
::   将框架集成到 VS Code 项目中
::   用法：install.bat [项目目录]
:: ============================================

set "TARGET_DIR=%~1"
if "%TARGET_DIR%"=="" set "TARGET_DIR=%CD%"

echo ============================================
echo   VSCode 插件框架安装程序
echo ============================================
echo.
echo  目标项目: %TARGET_DIR%
echo.

:: 检查目标目录是否存在
if not exist "%TARGET_DIR%" (
    echo 错误: 目标目录不存在
    exit /b 1
)

:: 检查目标目录是否有 .vscode 文件夹
if not exist "%TARGET_DIR%\.vscode" (
    echo [安装] 创建 .vscode 目录...
    mkdir "%TARGET_DIR%\.vscode"
)

:: ========== 生成 .vscode/tasks.json ==========
echo [安装] 生成 VS Code Tasks 配置...

set "TASKS_FILE=%TARGET_DIR%\.vscode\tasks.json"

(
echo {
echo     "version": "2.0.0",
echo     "tasks": [
echo         {
echo             "label": "🔧 启动器菜单",
echo             "type": "shell",
echo             "command": "${workspaceFolder}/vscode-plugin-framework/runner.bat",
echo             "group": {
echo                 "kind": "build",
echo                 "isDefault": true
echo             },
echo             "presentation": {
echo                 "echo": true,
echo                 "reveal": "always",
echo                 "focus": true,
echo                 "panel": "new"
echo             },
echo             "problemMatcher": []
echo         }
echo     ]
echo }
) > "%TASKS_FILE%"

echo [安装] Tasks 配置已生成: %TASKS_FILE%

:: ========== 生成 .vscode/launch.json（如果不存在）==========
if not exist "%TARGET_DIR%\.vscode\launch.json" (
    echo [安装] 生成 VS Code Launch 配置...
    
    set "LAUNCH_FILE=%TARGET_DIR%\.vscode\launch.json"
    
    (
    echo {
    echo     "version": "0.2.0",
    echo     "configurations": [
    echo         {
    echo             "name": "🔧 启动器菜单",
    echo             "type": "coreclr",
    echo             "request": "launch",
    echo             "preLaunchTask": "🔧 启动器菜单",
    echo             "program": "${workspaceFolder}/vscode-plugin-framework/runner.bat",
    echo             "cwd": "${workspaceFolder}",
    echo             "console": "externalTerminal",
    echo             "stopAtEntry": false
    echo         }
    echo     ]
    echo }
    ) > "%LAUNCH_FILE%"
    
    echo [安装] Launch 配置已生成: %LAUNCH_FILE%
)

:: ========== 生成 .vscode/settings.json 中的 Code Runner 配置 ==========
echo [安装] 更新 VS Code 设置...

:: 检查 settings.json 是否存在
if exist "%TARGET_DIR%\.vscode\settings.json" (
    echo [安装] 检测到已有 settings.json，请手动添加以下配置:
    echo.
    echo   "code-runner.runInTerminal": false,
    echo   "code-runner.executorMap": {
    echo       "bat": "cmd /c \"$fullFileName\""
    echo   }
    echo.
) else (
    (
    echo {
    echo     "code-runner.runInTerminal": false,
    echo     "code-runner.executorMap": {
    echo         "bat": "cmd /c \"$fullFileName\""
    echo     }
    echo }
    ) > "%TARGET_DIR%\.vscode\settings.json"
    echo [安装] settings.json 已生成
)

echo.
echo ============================================
echo   安装完成！
echo ============================================
echo.
echo  使用方式:
echo    1. 在 VS Code 中按 Ctrl+Shift+B 打开启动器菜单
echo    2. 或直接双击 vscode-plugin-framework/runner.bat
echo    3. 编辑 config/entries.config.json 添加启动项
echo    4. 在 plugins/ 目录下添加新的插件模块
echo.
echo  按任意键退出...
pause >nul
