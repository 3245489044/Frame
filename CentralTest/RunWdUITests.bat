@echo off
chcp 65001 >nul
title WdUI 测试分批运行

set TEST_PROJ=WdUI.Tests\WdUI.Tests.csproj
set SETTINGS=WdUI.Tests\test.runsettings
set NO_RESTORE=--no-restore

echo ============================================================
echo  WdUI 测试分批运行脚本
echo  按功能模块分批，每批运行后等待用户确认继续
echo ============================================================
echo.

:: ============================================================
:: 第 1 批：ControlFactory - 控件工厂测试（11 个 Unit 测试）
:: ============================================================
echo [1/8] ControlFactory - 控件工厂测试
echo ============================================================
dotnet test %TEST_PROJ% --filter "TestCategory=ControlFactory" --settings %SETTINGS% %NO_RESTORE%
if %ERRORLEVEL% NEQ 0 (
    echo ⚠ ControlFactory 测试有失败！
)
echo.
pause
echo.

:: ============================================================
:: 第 2 批：LayoutCalculation - 布局计算测试（~30 个 Unit 测试）
:: ============================================================
echo [2/8] LayoutCalculation - 布局计算测试
echo ============================================================
dotnet test %TEST_PROJ% --filter "TestCategory=LayoutCalculation" --settings %SETTINGS% %NO_RESTORE%
if %ERRORLEVEL% NEQ 0 (
    echo ⚠ LayoutCalculation 测试有失败！
)
echo.
pause
echo.

:: ============================================================
:: 第 3 批：ContainerLayout - 容器布局测试（6 Unit + 3 Integration）
:: ============================================================
echo [3/8] ContainerLayout - 容器布局测试
echo ============================================================
dotnet test %TEST_PROJ% --filter "TestCategory=ContainerLayout" --settings %SETTINGS% %NO_RESTORE%
if %ERRORLEVEL% NEQ 0 (
    echo ⚠ ContainerLayout 测试有失败！
)
echo.
pause
echo.

:: ============================================================
:: 第 4 批：ZOrder - Z轴顺序测试（4 Unit + 2 Integration）
:: ============================================================
echo [4/8] ZOrder - Z轴顺序测试
echo ============================================================
dotnet test %TEST_PROJ% --filter "TestCategory=ZOrder" --settings %SETTINGS% %NO_RESTORE%
if %ERRORLEVEL% NEQ 0 (
    echo ⚠ ZOrder 测试有失败！
)
echo.
pause
echo.

:: ============================================================
:: 第 5 批：Visibility - 可见性测试（6 Unit + 1 Integration）
:: ============================================================
echo [5/8] Visibility - 可见性测试
echo ============================================================
dotnet test %TEST_PROJ% --filter "TestCategory=Visibility" --settings %SETTINGS% %NO_RESTORE%
if %ERRORLEVEL% NEQ 0 (
    echo ⚠ Visibility 测试有失败！
)
echo.
pause
echo.

:: ============================================================
:: 第 6 批：ContainerNesting - 容器嵌套测试（1 Unit + 1 Integration）
:: ============================================================
echo [6/8] ContainerNesting - 容器嵌套测试
echo ============================================================
dotnet test %TEST_PROJ% --filter "TestCategory=ContainerNesting" --settings %SETTINGS% %NO_RESTORE%
if %ERRORLEVEL% NEQ 0 (
    echo ⚠ ContainerNesting 测试有失败！
)
echo.
pause
echo.

:: ============================================================
:: 第 7 批：OverlapDetection + ConfigLoading - 重叠检测与配置加载
::       （1 Unit + 1 Integration）+（1 Integration）
:: ============================================================
echo [7/8] OverlapDetection + ConfigLoading - 重叠检测与配置加载测试
echo ============================================================
echo --- OverlapDetection ---
dotnet test %TEST_PROJ% --filter "TestCategory=OverlapDetection" --settings %SETTINGS% %NO_RESTORE%
if %ERRORLEVEL% NEQ 0 (
    echo ⚠ OverlapDetection 测试有失败！
)
echo.
echo --- ConfigLoading ---
dotnet test %TEST_PROJ% --filter "TestCategory=ConfigLoading" --settings %SETTINGS% %NO_RESTORE%
if %ERRORLEVEL% NEQ 0 (
    echo ⚠ ConfigLoading 测试有失败！
)
echo.
pause
echo.

:: ============================================================
:: 第 8 批：SplitterStretch - 分割容器拉伸测试（10 个 Integration）
::       最重的测试，单独运行
:: ============================================================
echo [8/8] SplitterStretch - 分割容器拉伸测试
echo ============================================================
dotnet test %TEST_PROJ% --filter "TestCategory=SplitterStretch" --settings %SETTINGS% %NO_RESTORE%
if %ERRORLEVEL% NEQ 0 (
    echo ⚠ SplitterStretch 测试有失败！
)
echo.
pause
echo.

:: ============================================================
:: 全部完成
:: ============================================================
echo ============================================================
echo  所有 WdUI 测试运行完毕！
echo ============================================================
pause
