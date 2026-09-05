@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion

:: ============================================
::   path-resolver.bat — 路径解析器
::   自动检测工作区根目录，统一处理路径
::   接口：path-resolver.bat [--detect-root | --resolve <相对路径>]
:: ============================================

set "CMD=%~1"

if "%CMD%"=="--detect-root" goto :detect_root
if "%CMD%"=="--resolve" goto :resolve_path
goto :usage

:usage
echo 用法: path-resolver.bat [选项]
echo.
echo 选项:
echo   --detect-root             检测工作区根目录
echo   --resolve ^<相对路径^>       解析相对路径为绝对路径
echo.
exit /b 1

:detect_root
:: 从当前目录向上查找，直到找到标记文件
set "ROOT=%~dp0"
:: 从当前目录开始向上查找
set "SEARCH_DIR=%CD%"

:find_root_loop
if exist "%SEARCH_DIR%\.git" goto root_found
if exist "%SEARCH_DIR%\.vscode" goto root_found
if exist "%SEARCH_DIR%\*.sln" goto root_found

:: 获取父目录
for %%F in ("%SEARCH_DIR%") do set "PARENT=%%~dpF"
if "%PARENT%"=="%SEARCH_DIR%" goto root_not_found
set "SEARCH_DIR=%PARENT%"
goto :find_root_loop

:root_found
echo %SEARCH_DIR%
exit /b 0

:root_not_found
:: 找不到标记文件，使用启动器所在目录的父目录
echo %~dp0..\
exit /b 0

:resolve_path
:: 解析相对路径
set "REL_PATH=%~2"
if "%REL_PATH%"=="" (
    echo 错误: 请提供相对路径
    exit /b 1
)

:: 替换 %WORKSPACE_ROOT% 变量
set "RESOLVED=%REL_PATH:%%WORKSPACE_ROOT%%=%WORKSPACE_ROOT%"

:: 如果是相对路径，基于工作区根目录解析
if not "%RESOLVED%"=="%REL_PATH%" goto output_resolved
set "RESOLVED=%WORKSPACE_ROOT%%REL_PATH%"

:output_resolved
echo %RESOLVED%
exit /b 0
