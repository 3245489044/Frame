#requires -Version 5.1
<#
.SYNOPSIS
    DirectoryMap 模块版 —— 与 DirectoryMap.ps1 同源（点源后导出），供 Import-Module 消费。
.DESCRIPTION
    单一实现 = Framework\DirectoryMap.ps1；本 .psm1 仅 dot-source 后显式导出，
    避免「第二实现」分叉（对齐 Baselines\README 的命名唯一性纪律）。
.NOTES
    用法（消费脚本）：
        Import-Module (Join-Path $PSScriptRoot '..\PScriptManager\Framework\DirectoryMap.psm1') -Force
        $root = Get-RepositoryRoot
#>

. (Join-Path $PSScriptRoot 'DirectoryMap.ps1')

Export-ModuleMember -Function @(
    'Get-SuperRoot',
    'Get-RepositoryRoot',
    'Get-FrameRoot',
    'Get-DirectoryMap',
    'Get-MappedPath',
    'Get-DomainPaths',
    'Get-DomainPath',
    'Get-GateDomains',
    'Get-GateScriptOverride',
    'Get-GateScriptLeafName',
    'Resolve-GateScript',
    'Get-GateScriptCell'
)
