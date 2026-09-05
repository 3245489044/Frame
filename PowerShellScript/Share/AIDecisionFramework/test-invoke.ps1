# test-invoke.ps1 — 测试 invoke_ps_script 工具的简单脚本
param(
    [string]$Name = "World"
)

Write-Output "Hello, $Name!"
Write-Output "ScriptPath: $PSCommandPath"
Write-Output "PID: $PID"
