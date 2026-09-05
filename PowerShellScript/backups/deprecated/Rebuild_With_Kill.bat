@echo off
chcp 65001 >nul
title 编译前清理进程并重新编译

echo ============================================
echo  正在检查并关闭占用编译输出的进程...
echo ============================================

:: 关闭已知会锁定 DLL 的进程
echo 正在关闭 FileDriven.Launcher.exe ...
taskkill /F /IM FileDriven.Launcher.exe 2>nul
if %errorlevel% equ 0 (
    echo [成功] FileDriven.Launcher.exe 已终止
) else (
    echo [信息] FileDriven.Launcher.exe 未运行，无需关闭
)

:: 可以在这里添加其他可能锁定 DLL 的进程
:: taskkill /F /IM OtherApp.exe 2>nul

echo.
echo ============================================
echo  等待进程完全退出...
echo ============================================
timeout /t 2 /nobreak >nul

echo.
echo ============================================
echo  开始编译项目...
echo ============================================
echo.

:: 切换到项目根目录
cd /d "%~dp0.."

:: 执行编译
dotnet build WorkFlowPrj.sln -c Debug %*

if %errorlevel% equ 0 (
    echo.
    echo ============================================
    echo  [成功] 编译完成！
    echo ============================================
) else (
    echo.
    echo ============================================
    echo  [失败] 编译出错，请检查上面的错误信息
    echo ============================================
)

echo.
pause
