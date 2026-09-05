@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion

:: ============================================
::   run-test.bat — RunCode 插件右键执行功能测试
::   模拟 VS Code 文件资源管理器中右键文件 → Run Code 的完整行为链
::   测试路径与 VS Code 文件资源管理器完全一致
:: ============================================

title RunCode 右键执行测试

set "TEST_DIR=%~dp0"
set "FRAMEWORK_DIR=%TEST_DIR%..\.."
set "TEST_FILES_DIR=%TEST_DIR%test-files"

:: 去除末尾反斜杠
if "%TEST_DIR:~-1%"=="\" set "TEST_DIR=%TEST_DIR:~0,-1%"
if "%FRAMEWORK_DIR:~-1%"=="\" set "FRAMEWORK_DIR=%FRAMEWORK_DIR:~0,-1%"
if "%TEST_FILES_DIR:~-1%"=="\" set "TEST_FILES_DIR=%TEST_FILES_DIR:~0,-1%"

:: ========== 检测工作区根目录（与 VS Code 文件资源管理器路径一致）==========
for /f %%a in ('call "%FRAMEWORK_DIR%\core\path-resolver.bat" --detect-root') do set "WORKSPACE_ROOT=%%a"
echo [信息] 工作区根目录: %WORKSPACE_ROOT%
echo [信息] 测试文件目录: %TEST_FILES_DIR%
echo.

:: ========== 读取当前 VS Code 的 Code Runner 配置 ==========
echo ============================================
echo   第一步：读取当前 Code Runner 配置
echo ============================================
echo.

:: 从 VS Code 用户设置中读取 code-runner 配置
set "VSCODE_SETTINGS=%APPDATA%\Code\User\settings.json"
if exist "%VSCODE_SETTINGS%" (
    echo [配置] VS Code 用户设置: %VSCODE_SETTINGS%
    for /f "tokens=*" %%a in ('type "%VSCODE_SETTINGS%" ^| findstr /i "code-runner"') do (
        echo   %%a
    )
) else (
    echo [配置] 未找到 VS Code 用户设置
)

echo.
echo [配置] 框架 Code Runner 插件配置:
type "%FRAMEWORK_DIR%\plugins\code-runner\plugin.config.json" 2>nul
echo.

:: ========== 第二步：模拟 Code Runner 的 getExecutor() 过程 ==========
echo ============================================
echo   第二步：模拟 getExecutor() — 根据文件扩展名获取执行器
echo ============================================
echo.
echo   Code Runner 源码逻辑:
echo     executorMap["bat"] = "cmd /c"
echo     executorMap["ps1"] = "powershell -ExecutionPolicy ByPass -File"
echo     executorMap["py"]  = "python -u"
echo.

:: ========== 第三步：模拟 getFinalCommandToRunCodeFile() 过程 ==========
echo ============================================
echo   第三步：模拟 getFinalCommandToRunCodeFile() — 生成最终命令
echo ============================================
echo.
echo   Code Runner 源码逻辑:
echo     $dir → 文件所在目录（带引号）
echo     $fullFileName → 完整文件路径（带引号）
echo     $fileName → 文件名（不含路径）
echo     $workspaceRoot → 工作区根目录
echo.

:: ========== 第四步：模拟 executeCommand() 过程 ==========
echo ============================================
echo   第四步：模拟 executeCommand() — 执行命令
echo ============================================
echo.
echo   Code Runner 源码逻辑:
echo     if (runInTerminal) → terminal.sendText(command)
echo     else → child_process.spawn(command, [], { cwd, shell: true })
echo.

:: ========== 开始测试 ==========
:test_menu
cls
echo ============================================
echo   RunCode 插件右键执行功能测试
echo ============================================
echo.
echo  工作区路径: %WORKSPACE_ROOT%
echo  测试文件目录: %TEST_FILES_DIR%
echo.
echo  选择要测试的文件:
echo.
echo   [1] test-hello.bat  — 测试 bat 文件右键执行
echo   [2] test-hello.ps1  — 测试 ps1 文件右键执行
echo   [3] test-hello.py   — 测试 py 文件右键执行
echo.
echo  选择测试方式:
echo   [A] 通过框架 adapter 执行
echo   [B] 通过框架 fallback 执行
echo   [C] 直接 cmd /c 执行（模拟 Code Runner 默认行为）
echo   [D] 全部方式对比测试
echo.
echo   [Q] 退出
echo.

set /p "FILE_CHOICE=请选择文件 (1-3): "

if /i "!FILE_CHOICE!"=="Q" exit /b 0

:: 根据选择设置测试文件
if "!FILE_CHOICE!"=="1" set "TEST_FILE=test-hello.bat" & set "TEST_TYPE=bat" & goto :choose_method
if "!FILE_CHOICE!"=="2" set "TEST_FILE=test-hello.ps1" & set "TEST_TYPE=ps1" & goto :choose_method
if "!FILE_CHOICE!"=="3" set "TEST_FILE=test-hello.py" & set "TEST_TYPE=py" & goto :choose_method
echo 无效选择 & pause & goto :test_menu

:choose_method
set /p "METHOD_CHOICE=请选择测试方式 (A/B/C/D): "

if /i "!METHOD_CHOICE!"=="A" goto :test_adapter
if /i "!METHOD_CHOICE!"=="B" goto :test_fallback
if /i "!METHOD_CHOICE!"=="C" goto :test_direct
if /i "!METHOD_CHOICE!"=="D" goto :test_all
echo 无效选择 & pause & goto :choose_method

:: ========== 测试 A：通过框架 adapter 执行 ==========
:test_adapter
cls
echo ============================================
echo   测试 A：通过框架 adapter 执行
echo ============================================
echo.
echo   测试文件: %TEST_FILE%
echo   测试类型: %TEST_TYPE%
echo   工作目录: %WORKSPACE_ROOT%
echo.
echo   [模拟] Code Runner 右键点击文件 → Run Code
echo   [模拟] 文件路径: %TEST_FILES_DIR%\%TEST_FILE%
echo   [模拟] 工作目录: %WORKSPACE_ROOT%
echo.
echo   [模拟] getExecutor() 返回: 
if /i "%TEST_TYPE%"=="bat" echo     cmd /c
if /i "%TEST_TYPE%"=="ps1" echo     powershell -ExecutionPolicy ByPass -File
if /i "%TEST_TYPE%"=="py" echo     python -u
echo.
echo   [模拟] getFinalCommandToRunCodeFile() 生成:
if /i "%TEST_TYPE%"=="bat" echo     cmd /c "%TEST_FILES_DIR%\%TEST_FILE%"
if /i "%TEST_TYPE%"=="ps1" echo     powershell -ExecutionPolicy ByPass -File "%TEST_FILES_DIR%\%TEST_FILE%"
if /i "%TEST_TYPE%"=="py" echo     python -u "%TEST_FILES_DIR%\%TEST_FILE%"
echo.
echo   [模拟] executeCommand() 方式: runInTerminal=false → spawn
echo.
echo ============================================
echo   开始执行...
echo ============================================
echo.

:: 调用框架 adapter
call "%FRAMEWORK_DIR%\plugins\code-runner\adapter.bat" --type "%TEST_TYPE%" --file "%TEST_FILES_DIR%\%TEST_FILE%" --workdir "%WORKSPACE_ROOT%" --newwindow true

echo.
echo ============================================
echo   测试 A 完成，退出码: %errorlevel%
echo ============================================
pause
goto :test_menu

:: ========== 测试 B：通过框架 fallback 执行 ==========
:test_fallback
cls
echo ============================================
echo   测试 B：通过框架 fallback 执行
echo ============================================
echo.
echo   测试文件: %TEST_FILE%
echo   测试类型: %TEST_TYPE%
echo   工作目录: %WORKSPACE_ROOT%
echo.
echo   [模拟] adapter 失败，自动降级到 fallback
echo   [模拟] fallback 使用 start 命令在新窗口中启动
echo.
echo ============================================
echo   开始执行...
echo ============================================
echo.

:: 调用框架 fallback
call "%FRAMEWORK_DIR%\plugins\code-runner\fallback.bat" --type "%TEST_TYPE%" --file "%TEST_FILES_DIR%\%TEST_FILE%" --workdir "%WORKSPACE_ROOT%" --newwindow true

echo.
echo ============================================
echo   测试 B 完成，退出码: %errorlevel%
echo ============================================
pause
goto :test_menu

:: ========== 测试 C：直接 cmd /c 执行（模拟 Code Runner 默认行为）==========
:test_direct
cls
echo ============================================
echo   测试 C：直接执行（模拟 Code Runner 默认行为）
echo ============================================
echo.
echo   测试文件: %TEST_FILE%
echo   测试类型: %TEST_TYPE%
echo   工作目录: %WORKSPACE_ROOT%
echo.
echo   [模拟] Code Runner 默认 executorMap["bat"] = "cmd /c"
echo   [模拟] 最终命令: cmd /c "%TEST_FILES_DIR%\%TEST_FILE%"
echo   [模拟] 工作目录: %WORKSPACE_ROOT%
echo.
echo ============================================
echo   开始执行...
echo ============================================
echo.

:: 直接使用 cmd /c 执行（与 Code Runner 默认行为一致）
cd /d "%WORKSPACE_ROOT%"

if /i "%TEST_TYPE%"=="bat" (
    echo [执行] cmd /c "%TEST_FILES_DIR%\%TEST_FILE%"
    cmd /c "%TEST_FILES_DIR%\%TEST_FILE%"
) else if /i "%TEST_TYPE%"=="ps1" (
    echo [执行] powershell -ExecutionPolicy ByPass -File "%TEST_FILES_DIR%\%TEST_FILE%"
    powershell -ExecutionPolicy ByPass -File "%TEST_FILES_DIR%\%TEST_FILE%"
) else if /i "%TEST_TYPE%"=="py" (
    echo [执行] python -u "%TEST_FILES_DIR%\%TEST_FILE%"
    python -u "%TEST_FILES_DIR%\%TEST_FILE%"
)

echo.
echo ============================================
echo   测试 C 完成，退出码: %errorlevel%
echo ============================================
pause
goto :test_menu

:: ========== 测试 D：全部方式对比测试 ==========
:test_all
cls
echo ============================================
echo   测试 D：全部方式对比测试
echo ============================================
echo.
echo   测试文件: %TEST_FILE%
echo   测试类型: %TEST_TYPE%
echo.

echo ============================================
echo   [对比 1/3] 框架 adapter 执行
echo ============================================
call "%FRAMEWORK_DIR%\plugins\code-runner\adapter.bat" --type "%TEST_TYPE%" --file "%TEST_FILES_DIR%\%TEST_FILE%" --workdir "%WORKSPACE_ROOT%" --newwindow true
echo   adapter 退出码: %errorlevel%
echo.

echo ============================================
echo   [对比 2/3] 框架 fallback 执行
echo ============================================
call "%FRAMEWORK_DIR%\plugins\code-runner\fallback.bat" --type "%TEST_TYPE%" --file "%TEST_FILES_DIR%\%TEST_FILE%" --workdir "%WORKSPACE_ROOT%" --newwindow true
echo   fallback 退出码: %errorlevel%
echo.

echo ============================================
echo   [对比 3/3] 直接 cmd /c 执行
echo ============================================
cd /d "%WORKSPACE_ROOT%"
if /i "%TEST_TYPE%"=="bat" (
    cmd /c "%TEST_FILES_DIR%\%TEST_FILE%"
) else if /i "%TEST_TYPE%"=="ps1" (
    powershell -ExecutionPolicy ByPass -File "%TEST_FILES_DIR%\%TEST_FILE%"
) else if /i "%TEST_TYPE%"=="py" (
    python -u "%TEST_FILES_DIR%\%TEST_FILE%"
)
echo   直接执行 退出码: %errorlevel%
echo.

echo ============================================
echo   对比测试完成
echo ============================================
pause
goto :test_menu
