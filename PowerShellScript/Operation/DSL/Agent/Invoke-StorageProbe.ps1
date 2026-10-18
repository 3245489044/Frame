#requires -Version 5.1
<#
.SYNOPSIS
    Storage 探针 —— 以 `node --import` 预加载 probe.mjs，运行目标脚本并产出 JSON。
.DESCRIPTION
    判据载体 = 探针报告（nodeSqliteLoaded / databaseSyncConstructions / fsWrites）。
    默认目标 = ClineApi/build/engine.js；无参数时仅打印用法。
.PARAMETER Report
    探针报告输出路径。
.PARAMETER ArgumentList
    传给目标脚本的参数（如 cli.js 的 history/ask 子命令）。
.PARAMETER WorkingDirectory
    目标进程工作目录（缺省 = 仓库根）。
.EXAMPLE
    .\Invoke-StorageProbe.ps1 -Target ClineApi\build\cli.js -ArgumentList @('history','--limit','1')
#>
[CmdletBinding()]
param(
    [string]$Target = 'Frame\TypeScriptPrj\TsWorkspace\ClineApi\build\engine.js',
    [string]$Report = 'BuildOutput\storage-probe.json',
    [string]$Node = 'node',
    [string[]]$ArgumentList = @(),
    [string]$WorkingDirectory = ''
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..\..')).Path
if (-not [System.IO.Path]::IsPathRooted($Target)) { $Target = Join-Path $repoRoot $Target }
if (-not [System.IO.Path]::IsPathRooted($Report)) { $Report = Join-Path $repoRoot $Report }
if ([string]::IsNullOrWhiteSpace($WorkingDirectory)) { $WorkingDirectory = $repoRoot }
if (-not (Test-Path -LiteralPath $Target)) { Write-Error "目标不存在: $Target"; exit 2 }

$probe = Join-Path $repoRoot 'Frame\TypeScriptPrj\TsWorkspace\AgentStorage\tools\probe.mjs'
if (-not (Test-Path -LiteralPath $probe)) { Write-Error "探针不存在: $probe"; exit 2 }
# Node 在 Windows 上要求 --import 使用 file:// URL（绝对路径会被当作 d: 协议）。
$probeUrl = ([System.Uri]$probe).AbsoluteUri

$reportDir = Split-Path -Parent $Report
if (-not (Test-Path -LiteralPath $reportDir)) { New-Item -ItemType Directory -Path $reportDir -Force | Out-Null }

$env:STORAGE_PROBE_REPORT = $Report
try {
    Push-Location -LiteralPath $WorkingDirectory
    try {
        & $Node --import $probeUrl $Target @ArgumentList
        $rc = $LASTEXITCODE
    } finally {
        Pop-Location
    }
} finally {
    Remove-Item Env:STORAGE_PROBE_REPORT -ErrorAction SilentlyContinue
}

if (-not (Test-Path -LiteralPath $Report)) {
    Write-Host "StorageProbe: 未生成报告（目标提前退出？）" -ForegroundColor Yellow
    exit 1
}
Write-Host ("StorageProbe: {0}" -f $Report) -ForegroundColor Green
exit $rc
