Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  RunCode 测试 - PS1 文件执行成功！" -ForegroundColor Green
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""
Write-Host " 当前时间: $(Get-Date)"
Write-Host " 当前目录: $(Get-Location)"
Write-Host " 脚本路径: $PSCommandPath"
Write-Host " 脚本目录: $(Split-Path $PSCommandPath -Parent)"
Write-Host ""
Write-Host " [信息] 这个文件是通过 Code Runner 插件"
Write-Host " [信息] 在 VS Code 文件资源管理器中右键执行的。"
Write-Host ""
Write-Host " [信息] 如果看到这个输出，说明 RunCode 插件"
Write-Host " [信息] 的 ps1 文件执行功能正常！"
Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan

Read-Host "按 Enter 键退出"
