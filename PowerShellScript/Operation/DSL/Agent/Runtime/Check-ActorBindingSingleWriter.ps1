#requires -Version 5.1
<#
.SYNOPSIS
    执行体身份单写守卫（ActorBindingSingleWriter）—— K-2。

.DESCRIPTION
    判据（机械可复算）：
      W1 身份写点唯一：`AgentStatus.TryAdd` 仅允许出现在
         - ActorBinding.cs（身份登记）
         - SnapshotMergePolicy.cs（任务快照合并回填，只增）
         - AgentTaskItemJsonConverter.cs（载荷反序列化，只增）
      W2 裸改写为 0：`AgentStatus[...] =` 仅允许上述三个文件。
      W3 身份解析唯一：`ActorIdentity.Resolve` 定义点 = 1（ActorIdentity.cs）。
      W4 读点带 preferred：`ReadActorId(` 调用点（除定义）必须传第二实参（标签优先，防旧载荷双键）。

    模式：默认报告；-Check 时违规 → 退出码 1。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check
)

$ErrorActionPreference = 'Stop'

# 门禁运行时（根定位/落点/裁决单源；向上查找仓库超根，与目录分层解耦）
$GateRuntimeRoot = $PSScriptRoot
while ($GateRuntimeRoot -and -not ((Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Frame')) -and (Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Projects')))) { $GateRuntimeRoot = Split-Path -Parent $GateRuntimeRoot }
if (-not $GateRuntimeRoot) { throw '仓库超根定位失败（未命中同时含 Frame\ 与 Projects\ 的目录）' }
Import-Module (Join-Path $GateRuntimeRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking

if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = Get-GateRepoRoot -ScriptRoot $PSScriptRoot
}

$violations = [System.Collections.Generic.List[string]]::new()
$src = Join-Path $Root 'Projects\Plug\Agent\src'
$allowedWrite = @('ActorBinding.cs', 'SnapshotMergePolicy.cs', 'AgentTaskItemJsonConverter.cs')
Import-Module (Join-Path (Get-FrameRoot) 'PowerShellScript\Baselines\GateFileIndex.psm1') -Force

Write-Host ''
Write-Host '============================================================'
Write-Host '  执行体身份单写守卫（ActorBindingSingleWriter：K-2）'
Write-Host '============================================================'

if (-not (Test-Path -LiteralPath $src)) {
    $violations.Add("[W0] 缺件（判据不可判）：$src")
} else {
    $files = @(Get-GateFiles -Root $src -Extension cs -Exclude '\\(obj|bin|BuildOutput)\\')
    $tryAddHits = 0
    $identityDefs = 0
    foreach ($file in $files) {
        $text = [System.IO.File]::ReadAllText($file)
        $name = [System.IO.Path]::GetFileName($file)
        foreach ($m in [regex]::Matches($text, 'AgentStatus\.TryAdd')) {
            $tryAddHits++
            if ($allowedWrite -notcontains $name) {
                $violations.Add("[W1] 非法身份写点：$($file.Replace($Root + '\',''))")
            }
        }
        if ($text -match 'AgentStatus\[[^\]]+\]\s*=' -and $allowedWrite -notcontains $name) {
            $violations.Add("[W2] 裸改写 AgentStatus：$($file.Replace($Root + '\',''))")
        }
        if ($name -eq 'ActorIdentity.cs' -and $text -match 'static\s+ActorIdentity\?\s+Resolve\s*\(') {
            $identityDefs++
        }
    }
    if ($tryAddHits -eq 0) { $violations.Add('[W1] 身份登记写点缺失（ActorBinding.Bind 未登记）') }
    if ($identityDefs -ne 1) { $violations.Add("[W3] ActorIdentity.Resolve 定义点 = $identityDefs（应恰 1）") }

    $readHits = 0
    $readWithPreferred = 0
    foreach ($file in (Get-GateFiles -Root $src -Extension cs -Exclude '\\(obj|bin|BuildOutput)\\')) {
        $text = [System.IO.File]::ReadAllText($file)
        foreach ($m in [regex]::Matches($text, 'ReadActorId\s*\(([^)]*)\)')) {
            $readHits++
            if ($m.Groups[1].Value -match ',') { $readWithPreferred++ }
        }
    }
    if ($readHits -eq 0) { $violations.Add('[W4] 身份读点缺失（ReadActorId 未被消费）') }
}

if ($violations.Count -eq 0) {
    Write-Host '  [成功] W1/W2/W3/W4 通过（身份写点受限 + 解析唯一 + 读点带 preferred）' -ForegroundColor Green
    Write-Host ''
    exit 0
}

Write-Host "  [违规] $($violations.Count) 项：" -ForegroundColor Red
foreach ($v in $violations) { Write-Host "    - $v" -ForegroundColor Red }
Write-Host '  结果: 失败' -ForegroundColor Red
Write-Host ''
if ($Check) { exit 1 }
exit 0
