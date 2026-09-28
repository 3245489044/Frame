@echo off
chcp 65001 >nul
title TypeScript 项目编译（TypeScriptPrj）

:: ============================================================
::  编译TypeScript项目.bat — 一键编译 TypeScriptPrj 下所有 TypeScript 项目
:: ------------------------------------------------------------
::  复用现有逻辑（不另写构建规则）:
::    1) 解决方案内项目 → 现有 TsSln CLI: node build\TsSln\cli.js <命令> [参数]
::       （其内部经 tsc --build TsWorkspace/tsconfig.json 一次性构建全部引用项目,
::         自动拓扑排序 + 增量 + 陈旧 tsbuildinfo 自愈）
::    2) 解决方案未覆盖的 workspace 包 → 复用各包 package.json 的 build 脚本
::       （FsSink、WorkflowGraph；二者不在 TypeScript.sln 与 tsconfig references 中）
::
::  用法:
::    编译TypeScript项目.bat                         编译全部
::    编译TypeScript项目.bat --force                 忽略增量, 强制全量重编译
::    编译TypeScript项目.bat BasicMethod             只编译指定解决方案项目
::    编译TypeScript项目.bat --no-deps BasicMethod   只编译该项目, 不构建依赖
::    编译TypeScript项目.bat clean                   清理（含 FsSink/WorkflowGraph）
::    编译TypeScript项目.bat rebuild                 清理后重新编译
::    编译TypeScript项目.bat list                    列出解决方案内项目
::    编译TypeScript项目.bat info                    显示项目信息
::    编译TypeScript项目.bat install                 安装依赖
::    编译TypeScript项目.bat --no-pause              结束后不暂停（供脚本/CI 调用）
::    编译TypeScript项目.bat --help                  显示帮助
:: ============================================================

setlocal
set "TS_ROOT=%~dp0"
set "TSSln_CLI=%TS_ROOT%build\TsSln\cli.js"
set "ARGS=%*"
set "CMD=build"
set "NO_PAUSE="
set "PM="
set "RC=0"

:: ---------- 参数解析 ----------
:: --no-pause 为本脚本级开关; 其余参数原样透传给现有 TsSln CLI
echo %ARGS% | findstr /i /c:"--no-pause" >nul 2>nul && set "NO_PAUSE=1"

if not "%~1"=="" if /i "%~1"=="--help" goto :show_help
if not "%~1"=="" if /i "%~1"=="-h" goto :show_help
if not "%~1"=="" if /i "%~1"=="/?" goto :show_help

:: 剔除脚本级开关后组装 CLI 参数
set "PASS=%ARGS:--no-pause=%"
if "%PASS%"=="" set "PASS=build"
if "%PASS:~0,1%"=="-" set "PASS=build %PASS%"
if "%PASS:~0,1%"==" " set "PASS=build%PASS%"
set "CLI_ARGS=%PASS%"

:: 首词既非已知命令也非选项 => 视为项目名, 补上 build 命令（CLI 要求命令在前）
set "FIRST="
for /f "tokens=1" %%a in ("%CLI_ARGS%") do set "FIRST=%%a"
set "IS_CMD="
for %%c in (build clean rebuild list info install watch) do if /i "%FIRST%"=="%%c" set "IS_CMD=1"
if not defined IS_CMD if not "%FIRST:~0,1%"=="-" set "CLI_ARGS=build %CLI_ARGS%"

:: 由 CLI 参数首词推导本地分派命令
set "CMD=build"
for /f "tokens=1" %%a in ("%CLI_ARGS%") do set "CMD=%%a"

:: 范围判定：命令后出现非选项词 => 限定到具体项目, 跳过补建未入解决方案的包
set "SCOPE_ALL=1"
set "REST="
for /f "tokens=1,*" %%a in ("%CLI_ARGS%") do set "REST=%%b"
setlocal enabledelayedexpansion
for %%w in (%REST%) do (
    set "W=%%w"
    if not "!W:~0,1!"=="-" set "SCOPE_ALL="
)
endlocal & set "SCOPE_ALL=%SCOPE_ALL%"

:: ---------- 前置检查 ----------
cd /d "%TS_ROOT%"
if errorlevel 1 (
    echo [错误] 无法进入目录: %TS_ROOT%
    set "RC=1"
    goto :finish
)

where node >nul 2>nul
if errorlevel 1 (
    echo [错误] 未找到 node, 请先安装 Node.js
    set "RC=1"
    goto :finish
)

:: 仓库以 pnpm 为准（pnpm-workspace.yaml + pnpm-lock.yaml），npm 仅作回退
where pnpm >nul 2>nul
if not errorlevel 1 set "PM=pnpm"
if not defined PM (
    where npm >nul 2>nul
    if not errorlevel 1 set "PM=npm"
)
if not defined PM (
    echo [错误] 未找到 pnpm / npm
    set "RC=1"
    goto :finish
)

:: ---------- 自举：build\TsSln 被 .gitignore 忽略, 全新克隆时先编译出 CLI ----------
if not exist "%TSSln_CLI%" (
    echo [信息] 未找到 TsSln CLI, 正在用现有脚本生成: build\TsSln\cli.js
    call %PM% run build:ts
    if errorlevel 1 (
        echo [错误] TsSln CLI 生成失败
        set "RC=1"
        goto :finish
    )
)

:: ---------- 依赖兜底 ----------
if not exist "%TS_ROOT%node_modules" (
    echo [信息] node_modules 缺失, 正在安装依赖...
    call %PM% install
    if errorlevel 1 (
        echo [错误] 依赖安装失败
        set "RC=1"
        goto :finish
    )
)

echo.
echo ============================================================
echo   编译 TypeScript 项目
echo   根目录:   %TS_ROOT%
echo   命令:     %CMD%
echo   参数:     %CLI_ARGS%
if defined SCOPE_ALL (echo   范围:     全部项目) else (echo   范围:     指定项目（跳过补建）)
echo   包管理器: %PM%
echo ============================================================
echo.

:: ---------- 命令分派：仅 build / clean / rebuild 需要补建未入解决方案的包 ----------
if /i "%CMD%"=="list"    goto :cli_only
if /i "%CMD%"=="info"    goto :cli_only
if /i "%CMD%"=="install" goto :cli_only
if /i "%CMD%"=="watch"   goto :cli_only
if /i "%CMD%"=="clean"   goto :flow_clean
if /i "%CMD%"=="rebuild" goto :flow_rebuild
goto :flow_build

:: ============================================================
::  流程：仅透传现有 TsSln CLI（list / info / install / watch）
:: ============================================================
:cli_only
call :cli
set "RC=%ERRORLEVEL%"
goto :finish

:: ============================================================
::  流程：编译（CLI 构建解决方案内项目 + 补建 FsSink/WorkflowGraph）
:: ============================================================
:flow_build
call :cli
set "RC=%ERRORLEVEL%"
if defined SCOPE_ALL call :supplement build
if errorlevel 1 if "%RC%"=="0" set "RC=1"
goto :finish

:: ============================================================
::  流程：清理（CLI 清理解决方案内项目 + 补建包清理）
:: ============================================================
:flow_clean
call :cli
set "RC=%ERRORLEVEL%"
if defined SCOPE_ALL call :supplement clean
goto :finish

:: ============================================================
::  流程：重新编译（清理 + 编译），补建包先清后建
:: ============================================================
:flow_rebuild
call :cli
set "RC=%ERRORLEVEL%"
if defined SCOPE_ALL call :supplement clean
if defined SCOPE_ALL call :supplement build
if errorlevel 1 if "%RC%"=="0" set "RC=1"
goto :finish

:: ============================================================
::  子过程：调用现有 TsSln CLI（参数原样透传）
:: ============================================================
:cli
node "%TSSln_CLI%" %CLI_ARGS%
exit /b %ERRORLEVEL%

:: ============================================================
::  子过程：补建解决方案未覆盖的 workspace 包
::    用法: call :supplement build|clean
:: ============================================================
:supplement
echo.
echo ------------------------------------------------------------
echo   [补建] 解决方案未覆盖的 workspace 包: FsSink, WorkflowGraph
echo ------------------------------------------------------------
set "SUP_FAIL=0"
for %%P in (FsSink WorkflowGraph) do (
    call :supplement_one %1 %%P
    if errorlevel 1 set "SUP_FAIL=1"
)
exit /b %SUP_FAIL%

:: 单个包：build 复用其 package.json 的 build 脚本; clean 移除编译产物
:supplement_one
set "PKG_DIR=%TS_ROOT%TsWorkspace\%~2"
if not exist "%PKG_DIR%\package.json" (
    echo   [跳过] %~2 — 未找到 package.json
    exit /b 0
)
if /i "%~1"=="clean" goto :sup_one_clean
goto :sup_one_build

:sup_one_build
echo.
echo   [编译] %~2  ^(复用其 package.json 的 build 脚本^)
pushd "%PKG_DIR%"
call %PM% run build
set "ONE_RC=%ERRORLEVEL%"
popd
if not "%ONE_RC%"=="0" echo   [失败] %~2 编译失败 ^(退出码 %ONE_RC%^)
exit /b %ONE_RC%

:sup_one_clean
echo.
echo   [清理] %~2
pushd "%PKG_DIR%"
if exist "build" rmdir /s /q "build"
if exist "tsconfig.tsbuildinfo" del /q "tsconfig.tsbuildinfo" >nul 2>nul
popd
exit /b 0

:: ============================================================
::  完成
:: ============================================================
:finish
echo.
echo ============================================================
if "%RC%"=="0" (
    echo   [成功] TypeScript 项目编译完成
) else (
    echo   [失败] 编译过程存在错误, 退出码: %RC%
)
echo ============================================================
if defined NO_PAUSE exit /b %RC%
pause
exit /b %RC%

:: ============================================================
::  帮助
:: ============================================================
:show_help
echo.
echo ============================================================
echo   编译TypeScript项目.bat — 帮助
echo ============================================================
echo.
echo 作用: 一键编译 TypeScriptPrj 下所有 TypeScript 项目
echo       1) 解决方案内项目: 复用 TsSln CLI ^(tsc --build TsWorkspace/tsconfig.json^)
echo       2) 未入解决方案的包: FsSink、WorkflowGraph ^(复用其 build 脚本^)
echo.
echo 用法: %~nx0 [命令] [选项] [项目名]
echo.
echo 命令:
echo   ^(默认^)        编译全部
echo   clean         清理（含 FsSink/WorkflowGraph）
echo   rebuild       清理后重新编译
echo   list          列出解决方案内项目
echo   info          显示项目信息
echo   install       安装依赖
echo.
echo 选项:
echo   --force, -f   忽略增量, 强制全量重编译
echo   --no-deps     只编译指定项目, 不构建其依赖
echo   --no-pause    结束后不暂停（供脚本/CI 调用）
echo   --help, -h    显示此帮助
echo.
echo 说明: 指定单个项目时只构建该项目（跳过 FsSink/WorkflowGraph 补建）
echo.
echo 示例:
echo   %~nx0
echo   %~nx0 --force
echo   %~nx0 BasicMethod
echo   %~nx0 --no-deps MCPBridge
echo   %~nx0 clean
echo.
echo 提示: 首次运行（build\TsSln 缺失）会自动生成 CLI; 更多 TsSln 命令见 TsSln\README.md
echo.
if defined NO_PAUSE exit /b 0
pause
exit /b 0

