@echo off
chcp 65001 >nul
title WorkFlowPrj Script 工具集启动器

:: ============================================================
:: ScriptLauncher.bat — WorkFlowPrj Script 统一启动器（双击入口）
:: ============================================================
:: 通过 PScriptManager 框架调用，支持命令行参数透传
:: 
:: ============================================================
:: 使用方式:
:: ============================================================
::   双击运行                    → 显示交互菜单
::   ScriptLauncher.bat -List    → 列出所有工具
::   ScriptLauncher.bat -Tool <别名>  → 直接运行指定工具
:: 
:: 可用工具别名（-Tool 参数）:
:: ──── 构建与清理 ────
::   cleanup       清理构建输出并修复项目配置
::   clean         快速清理 bin/obj 目录
::   kill-proc     清理编译和 VS Code 运行时的冗余进程
::   nuget-upgrade NuGet 包批量升级（扫描并升级所有 NuGet 包）
::   rebuild       编译前清理进程并重新编译
::   ts-build      编译 TypeScriptPrj 中所有 TypeScript 项目
:: 
:: ──── 解决方案管理 ────
::   sln-clean     精简 .sln 配置（仅保留 Debug|Any CPU）
::   sln-sync      同步 .sln 文件与磁盘上的项目目录
::   fix-cross     跨项目分析修复（引用、依赖等）
:: 
:: ──── 代码分析 ────
::   analyze       分析 csproj 引用关系并生成报告
:: 
:: ──── Git 工具 ────
::   git-clean     删除除 Main 外的所有本地分支
:: 
:: ──── 环境配置 ────
::   env-config    更新 VS Code / MCP / 启动项配置并生成文档
::   validate-mcp  验证 CLine MCP 配置文件 JSON 格式
::   setup-pester  一键搭建 PowerShell Pester 测试环境
:: 
:: ──── VS Code 工具 ────
::   fix-coderunner 修复 VS Code Code Runner 配置
::   register-entry 注册 BatTool 启动项到 entries.config.json
::   sync-entries   同步 VS Code entries 配置中的 workDir
::   sync-ext       VS Code 扩展同步（导出/安装/差异对比）
:: 
:: ──── FileDriven 测试 ────
::   filedriven    FileDriven GUI 模式测试运行器
::   log           分层交互式日志查看器
:: 
:: ──── 测试工具 ────
::   test          运行 Pester 测试（支持按分类筛选）
:: 
:: ──── NuGet 升级工具详细用法 ────
::   ScriptLauncher.bat -Tool nuget-upgrade
::     交互模式：列出可升级包，逐个确认
:: 
::   ScriptLauncher.bat -Tool nuget-upgrade -All
::     一键升级所有可升级的包
:: 
::   ScriptLauncher.bat -Tool nuget-upgrade -CheckOnly
::     仅检查可升级的包并列出，不执行实际升级
:: 
::   ScriptLauncher.bat -Tool nuget-upgrade -PackageName "Newtonsoft.Json"
::     升级指定包（支持通配符 *，如 "Microsoft.Extensions.*"）
:: 
::   ScriptLauncher.bat -Tool nuget-upgrade -All -WhatIf
::     预览将要执行的变更，不实际修改文件
:: 
::   ScriptLauncher.bat -Tool nuget-upgrade -All -SkipBuild
::     升级后跳过编译验证
:: 
::   ScriptLauncher.bat -Tool nuget-upgrade -All -Concurrency 8
::     使用 8 个并发任务扫描（默认 4，60+ 项目建议 4-8）
:: ============================================================

set "SCRIPT_DIR=%~dp0"
set "PSM_INIT_SCRIPT=%SCRIPT_DIR%PScriptManager\_init.ps1"
set "LAUNCHER_SCRIPT=%SCRIPT_DIR%PScriptManager\Commands\Show-LauncherMenu.ps1"

:: 检查 PScriptManager 初始化脚本是否存在
if not exist "%PSM_INIT_SCRIPT%" (
    echo [错误] 找不到 PScriptManager 框架
    echo 预期路径: %PSM_INIT_SCRIPT%
    echo.
    pause
    exit /b 1
)

:: 检查启动器脚本是否存在
if not exist "%LAUNCHER_SCRIPT%" (
    echo [错误] 找不到启动器脚本
    echo 预期路径: %LAUNCHER_SCRIPT%
    echo.
    pause
    exit /b 1
)

:: 解析参数
if /i "%~1"=="-List" (
    echo 正在启动 WorkFlowPrj Script 工具集（PScriptManager 框架）...
    echo.
    powershell.exe -NoProfile -ExecutionPolicy Bypass -Command ^
        ". '%PSM_INIT_SCRIPT%' -ProjectRoot '%SCRIPT_DIR%..';" ^
        ". '%LAUNCHER_SCRIPT%';" ^
        "Show-LauncherMenu -List"
    goto :end
)

if /i "%~1"=="-Tool" (
    if not "%~2"=="" (
        set "TOOL_NAME=%~2"
        :: 收集 -Tool 和 tool_name 之后的额外参数
        set "EXTRA_ARGS="
        set "SKIP=2"
        goto :collect_extra
    )
)

:: 无参数或未知参数，显示交互菜单
echo 正在启动 WorkFlowPrj Script 工具集（PScriptManager 框架）...
echo.
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command ^
    ". '%PSM_INIT_SCRIPT%' -ProjectRoot '%SCRIPT_DIR%..';" ^
    ". '%LAUNCHER_SCRIPT%';" ^
    "Show-LauncherMenu"
goto :end

:collect_extra
:: 收集第 3 个参数及之后的参数
setlocal enabledelayedexpansion
set "ARGS="
set "COUNT=0"
for %%a in (%*) do (
    set /a COUNT+=1
    if !COUNT! gtr 2 (
        if defined ARGS (
            set "ARGS=!ARGS! %%a"
        ) else (
            set "ARGS=%%a"
        )
    )
)
endlocal & set "EXTRA_ARGS=%ARGS%"

:: 执行工具
if defined EXTRA_ARGS (
    powershell.exe -NoProfile -ExecutionPolicy Bypass -Command ^
        ". '%PSM_INIT_SCRIPT%' -ProjectRoot '%SCRIPT_DIR%..';" ^
        ". '%LAUNCHER_SCRIPT%';" ^
        "Show-LauncherMenu -Tool '%TOOL_NAME%' -PassThruArgs @('%EXTRA_ARGS: =','%')"
) else (
    powershell.exe -NoProfile -ExecutionPolicy Bypass -Command ^
        ". '%PSM_INIT_SCRIPT%' -ProjectRoot '%SCRIPT_DIR%..';" ^
        ". '%LAUNCHER_SCRIPT%';" ^
        "Show-LauncherMenu -Tool '%TOOL_NAME%'"
)
goto :end

:end
if %ERRORLEVEL% neq 0 (
    echo.
    echo [错误] 执行失败，错误码: %ERRORLEVEL%
    pause
)
