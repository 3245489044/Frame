@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion

:: ============================================
::   run-test-auto.bat — RunCode 自动化测试（无需交互）
::   自动执行所有测试并输出结果
::   用法：run-test-auto.bat [bat|ps1|py|all]
:: ============================================

title RunCode 自动化测试

set "TEST_DIR=%~dp0"
set "FRAMEWORK_DIR=%TEST_DIR%..\.."
set "TEST_FILES_DIR=%TEST_DIR%test-files"

if "%TEST_DIR:~-1%"=="\" set "TEST_DIR=%TEST_DIR:~0,-1%"
if "%FRAMEWORK_DIR:~-1%"=="\" set "FRAMEWORK_DIR=%FRAMEWORK_DIR:~0,-1%"
if "%TEST_FILES_DIR:~-1%"=="\" set "TEST_FILES_DIR=%TEST_FILES_DIR:~0,-1%"

:: 检测工作区根目录
for /f %%a in ('call "%FRAMEWORK_DIR%\core\path-resolver.bat" --detect-root') do set "WORKSPACE_ROOT=%%a"

set "TEST_TYPE=%~1"
if "%TEST_TYPE%"=="" set "TEST_TYPE=all"

echo ============================================
echo   RunCode 插件右键执行功能 - 自动化测试
echo ============================================
echo.
echo  工作区路径: %WORKSPACE_ROOT%
echo  测试文件目录: %TEST_FILES_DIR%
echo  测试类型: %TEST_TYPE%
echo.

:: ========== 测试信息收集 ==========
echo ============================================
echo   测试环境信息
echo ============================================
echo.
echo  [环境] 操作系统: %OS%
echo  [环境] 计算机名: %COMPUTERNAME%
echo  [环境] 用户名: %USERNAME%
echo  [环境] 当前目录: %CD%
echo  [环境] 工作区根目录: %WORKSPACE_ROOT%
echo  [环境] 测试文件目录: %TEST_FILES_DIR%
echo.

:: 读取 VS Code 配置
echo  [配置] VS Code Code Runner 设置:
if exist "%APPDATA%\Code\User\settings.json" (
    type "%APPDATA%\Code\User\settings.json" | findstr /i "code-runner" 2>nul
)
echo.

:: 读取框架插件配置
echo  [配置] 框架 Code Runner 插件配置:
type "%FRAMEWORK_DIR%\plugins\code-runner\plugin.config.json" 2>nul
echo.

:: ========== 执行测试 ==========
set "ALL_PASS=1"

if /i "%TEST_TYPE%"=="bat" goto :test_bat
if /i "%TEST_TYPE%"=="ps1" goto :test_ps1
if /i "%TEST_TYPE%"=="py" goto :test_py
if /i "%TEST_TYPE%"=="all" goto :test_all
goto :usage

:usage
echo 用法: run-test-auto.bat [bat^|ps1^|py^|all]
exit /b 1

:test_all
echo ============================================
echo   开始全部测试...
echo ============================================
echo.
call :test_single bat "test-hello.bat"
call :test_single ps1 "test-hello.ps1"
call :test_single py "test-hello.py"
goto :summary

:test_bat
call :test_single bat "test-hello.bat"
goto :summary

:test_ps1
call :test_single ps1 "test-hello.ps1"
goto :summary

:test_py
call :test_single py "test-hello.py"
goto :summary

:test_single
set "FILE_TYPE=%~1"
set "FILE_NAME=%~2"
set "FILE_PATH=%TEST_FILES_DIR%\%FILE_NAME%"

echo --------------------------------------------------
echo  测试文件: %FILE_NAME% (%FILE_TYPE%)
echo --------------------------------------------------
echo.

:: 测试 A：框架 adapter
echo [测试 A] 框架 adapter 执行...
call "%FRAMEWORK_DIR%\plugins\code-runner\adapter.bat" --type "%FILE_TYPE%" --file "%FILE_PATH%" --workdir "%WORKSPACE_ROOT%" --newwindow false
set "ADAPTER_EXIT=!errorlevel!"
echo [结果] adapter 退出码: !ADAPTER_EXIT!
echo.

:: 测试 B：框架 fallback
echo [测试 B] 框架 fallback 执行...
call "%FRAMEWORK_DIR%\plugins\code-runner\fallback.bat" --type "%FILE_TYPE%" --file "%FILE_PATH%" --workdir "%WORKSPACE_ROOT%" --newwindow false
set "FALLBACK_EXIT=!errorlevel!"
echo [结果] fallback 退出码: !FALLBACK_EXIT!
echo.

:: 测试 C：直接 cmd /c 执行（模拟 Code Runner 默认行为）
echo [测试 C] 直接执行（模拟 Code Runner 默认行为）...
cd /d "%WORKSPACE_ROOT%"
if /i "!FILE_TYPE!"=="bat" (
    cmd /c "!FILE_PATH!"
) else if /i "!FILE_TYPE!"=="ps1" (
    powershell -ExecutionPolicy ByPass -File "!FILE_PATH!"
) else if /i "!FILE_TYPE!"=="py" (
    python -u "!FILE_PATH!"
)
set "DIRECT_EXIT=!errorlevel!"
echo [结果] 直接执行 退出码: !DIRECT_EXIT!
echo.

:: 汇总
echo --------------------------------------------------
echo  测试结果汇总:
echo    adapter:  !ADAPTER_EXIT! ^(0=成功^)
echo    fallback: !FALLBACK_EXIT! ^(0=成功^)
echo    direct:   !DIRECT_EXIT! ^(0=成功^)
echo --------------------------------------------------
echo.

if !ADAPTER_EXIT! neq 0 set "ALL_PASS=0"
if !FALLBACK_EXIT! neq 0 set "ALL_PASS=0"
if !DIRECT_EXIT! neq 0 set "ALL_PASS=0"

exit /b 0

:summary
echo ============================================
echo   测试完成
echo ============================================
echo.
if "!ALL_PASS!"=="1" (
    echo  [结果] 全部测试通过 ✓
) else (
    echo  [结果] 部分测试失败 ✗
)
echo.
echo  详细结果请查看上方输出
echo ============================================

if "!ALL_PASS!"=="1" (exit /b 0) else (exit /b 1)
