@echo off
chcp 65001 >nul
echo ============================================
echo   RunCode 测试 - BAT 文件执行成功！
echo ============================================
echo.
echo  当前时间: %DATE% %TIME%
echo  当前目录: %CD%
echo  脚本路径: %~f0
echo  脚本目录: %~dp0
echo.
echo  [信息] 这个文件是通过 Code Runner 插件
echo  [信息] 在 VS Code 文件资源管理器中右键执行的。
echo.
echo  [信息] 如果看到这个输出，说明 RunCode 插件
echo  [信息] 的 bat 文件执行功能正常！
echo.
echo ============================================
pause
