@echo off
chcp 65001 >nul
title C# 项目编译（Docker 容器）

:: ============================================
::  C# 项目编译脚本 — 复用 MCP 容器编译 MainProj.sln
::  使用 workflow-mcp-env 容器中的 dotnet SDK 编译
::
::  用法:
::    编译C#项目.bat                    编译 Debug
::    编译C#项目.bat Release            编译 Release
::    编译C#项目.bat Debug              编译 Debug
::    编译C#项目.bat --restore          先 restore 再编译
::    编译C#项目.bat --no-restore       不 restore 直接编译（默认）
::    编译C#项目.bat --status           查看容器状态
::    编译C#项目.bat --help             显示帮助
::
::  示例:
::    编译C#项目.bat
::    编译C#项目.bat Release --restore
::    编译C#项目.bat --status
:: ============================================

:: ★ 通过 PowerShell 动态获取项目根目录
::   复用 ProjectPaths.psm1，与 交互启动.bat / CLI启动.bat / UI启动.bat 使用相同的路径框架
for /f "delims=" %%a in ('powershell -NoProfile -ExecutionPolicy Bypass -Command "Import-Module \"%~dp0..\PowerShellScript\Modules\Core\ProjectPaths.psm1\"; $root = Get-ProjectRoot; Write-Output $root" 2^>nul') do set "PROJECT_ROOT=%%a"

if not defined PROJECT_ROOT (
    echo [错误] 无法获取项目根目录
    pause
    exit /b 1
)

:: 容器名称（与 Deploy-DotnetBuilder.ps1 保持一致）
set "CONTAINER_NAME=workflow-mcp-env"

:: 解决方案路径（容器内路径）
set "SLN_PATH=/workspace/WorkFlowPrj/CsharpPrj/MainProj.sln"

:: 默认配置
set "CONFIGURATION=Debug"
set "RESTORE_FLAG="

:: 解析参数
:parse_args
if "%~1"=="" goto :end_parse
if /i "%~1"=="Debug" set "CONFIGURATION=Debug" & shift & goto :parse_args
if /i "%~1"=="Release" set "CONFIGURATION=Release" & shift & goto :parse_args
if /i "%~1"=="--restore" set "RESTORE_FLAG=" & shift & goto :parse_args
if /i "%~1"=="--no-restore" set "RESTORE_FLAG=--no-restore" & shift & goto :parse_args
if /i "%~1"=="--status" goto :show_status
if /i "%~1"=="--help" goto :show_help
shift
goto :parse_args
:end_parse

:: ============================================
::  检查容器是否运行
:: ============================================
echo [信息] 项目根目录: %PROJECT_ROOT%
echo [信息] 检查容器 %CONTAINER_NAME% 状态...

docker inspect --format="{{.State.Status}}" "%CONTAINER_NAME%" 2>nul | findstr /i "running" >nul
if %ERRORLEVEL% NEQ 0 (
    echo [警告] 容器 %CONTAINER_NAME% 未运行！
    echo [信息] 正在启动容器...
    cd /d "%PROJECT_ROOT%"
    docker compose -f DevEnvironment\Docker\docker-compose.yml up -d mcp-env
    if %ERRORLEVEL% NEQ 0 (
        echo [错误] 容器启动失败！
        pause
        exit /b 1
    )
    echo [信息] 容器已启动，等待就绪...
    :wait_loop
    docker inspect --format="{{.State.Health.Status}}" "%CONTAINER_NAME%" 2>nul | findstr /i "healthy" >nul
    if %ERRORLEVEL% NEQ 0 (
        timeout /t 2 /nobreak >nul
        goto :wait_loop
    )
    echo [信息] 容器已就绪
) else (
    echo [信息] 容器 %CONTAINER_NAME% 运行中
)

:: ============================================
::  执行编译
:: ============================================
echo.
echo ============================================
echo   编译配置: %CONFIGURATION%
echo   解决方案: CsharpPrj/MainProj.sln
echo   Restore:  %RESTORE_FLAG:--no-restore=跳过 restore%
echo   %RESTORE_FLAG:--no-restore=仅编译%
echo ============================================
echo.

:: 切换到项目根目录
cd /d "%PROJECT_ROOT%"

:: 在容器中执行 dotnet build（MainProj.sln）
docker exec "%CONTAINER_NAME%" dotnet build "%SLN_PATH%" -c "%CONFIGURATION%" %RESTORE_FLAG% -nologo

:: ★ 修复：同时编译 FileDriven.sln（FileDriven.UI 等项目在此解决方案中）
set "FILEDRIVEN_SLN_PATH=/workspace/WorkFlowPrj/FileDriven/FileDriven.sln"
docker exec "%CONTAINER_NAME%" dotnet build "%FILEDRIVEN_SLN_PATH%" -c "%CONFIGURATION%" --no-restore -nologo

:: 保存编译结果（取两次编译中较差的）
set "BUILD_RESULT=%ERRORLEVEL%"

echo.
if %BUILD_RESULT% EQU 0 (
    echo [成功] 编译完成！配置: %CONFIGURATION%
) else (
    echo [失败] 编译出错，错误码: %BUILD_RESULT%
)

pause
exit /b %BUILD_RESULT%

:: ============================================
::  显示容器状态
:: ============================================
:show_status
echo.
echo ============================================
echo   容器状态
echo ============================================
echo.
docker ps --filter name=%CONTAINER_NAME% --format "table {{.Names}}\t{{.Status}}\t{{.Image}}"
echo.
docker exec "%CONTAINER_NAME%" dotnet --version 2>nul && (
    echo [信息] 容器内 dotnet SDK 可用
) || (
    echo [信息] 容器内 dotnet SDK 不可用
)
echo.
pause
exit /b 0

:: ============================================
::  显示帮助
:: ============================================
:show_help
echo.
echo ============================================
echo   C# 项目编译脚本 — 帮助
echo ============================================
echo.
echo 用法: %~nx0 [选项] [配置]
echo.
echo 配置:
echo   Debug              Debug 模式编译（默认）
echo   Release            Release 模式编译
echo.
echo 选项:
echo   --restore          先 restore NuGet 包再编译
echo   --no-restore       跳过 restore 直接编译（默认）
echo   --status           查看容器状态
echo   --help             显示此帮助
echo.
echo 示例:
echo   %~nx0                    编译 Debug
echo   %~nx0 Release            编译 Release
echo   %~nx0 Release --restore  编译 Release（先 restore）
echo   %~nx0 --status           查看容器状态
echo.
pause
exit /b 0
