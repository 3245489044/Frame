@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion

:: ============================================
::   dotnet-test/adapter.bat — .NET 测试适配器
::   标准接口：adapter.bat --type <类型> --file <文件> --workdir <目录> [--newwindow <true/false>]
::   返回值：0=成功, 1=失败, 2=插件不可用
:: ============================================

:: 解析参数
:parse_args
if "%~1"=="" goto :execute
if /i "%~1"=="--type" set "FILE_TYPE=%~2" & shift & shift & goto :parse_args
if /i "%~1"=="--file" set "FILE_PATH=%~2" & shift & shift & goto :parse_args
if /i "%~1"=="--workdir" set "WORK_DIR=%~2" & shift & shift & goto :parse_args
if /i "%~1"=="--newwindow" set "NEW_WINDOW=%~2" & shift & shift & goto :parse_args
shift & goto :parse_args

:execute
echo [.NET Test] 执行 .NET 测试...
echo [.NET Test] 工作目录: %WORK_DIR%

:: 检查 dotnet 是否可用
where dotnet >nul 2>nul
if !errorlevel! neq 0 (
    echo [.NET Test] 错误: dotnet CLI 不可用
    exit /b 2
)

cd /d "%WORK_DIR%"

if /i "%FILE_PATH%"=="test" (
    :: 运行所有测试
    dotnet test
) else (
    :: 运行指定项目测试
    dotnet test "%FILE_PATH%"
)

if !errorlevel! equ 0 (
    echo [.NET Test] 测试通过
    exit /b 0
) else (
    echo [.NET Test] 测试失败
    exit /b 1
)
