# Check-AgentDecisionCoreBoundary.ps1
# 确定性核心边界门禁（F-66 边界化，对齐 Frame\DataCenter\Doc\DataCenter-Agent运行时数据接入-第四轮剩余收敛计划.md §2.1 B26-2）
# 判据：Agent 产品侧（Projects\Plug\Agent）不得引用 StateHoldDriven 的确定性核心状态化承载 ——
#       Agent 运行期内核 = AgentTaskItem（内存 ILoopState）；Center 侧审计快照 = DecisionCenter.Record。
#       命中 LoopStateStore / CoreStateView / DataCenter.Extensions.LoopDecision 即失败。
# 语义：LoopStateStore / CoreStateView 的消费方 = StateHoldDriven 宿主线（跨仓消费方须独立取证），
#       Agent 侧接线会形成「确定性核心双承载」（axioms SEP.DeterministicCoreDisjoint：单一确定性写入）。
#   - 报告模式（默认）：列出命中，不设退出码。
#   - 校验模式（-Check）：命中 > 0 → 退出码 1（CI 门禁）。

[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root)) {
    # 脚本位于 Frame\PowerShellScript\Operation\DSL\，上溯四级即仓库根
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
}

$scanDir = Join-Path $Root 'Projects\Plug\Agent'
if (-not (Test-Path $scanDir)) { Write-Error "未找到 Agent 源码目录: $scanDir"; exit 2 }

$files = Get-ChildItem -Path $scanDir -Recurse -File -Include *.cs, *.csproj -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '\\(obj|bin|BuildOutput)\\' -and $_.FullName -notlike '*\_deprecated\*' }

# 模式 → 说明（确定性核心状态化承载的三类符号引用）
$patterns = [ordered]@{
    'LoopStateStore'                       = 'StateHoldDriven 确定性核心状态化宿主（Agent 侧不得接线）'
    'CoreStateView'                        = 'StateHoldDriven 只读投影（Agent 侧不得消费）'
    'DataCenter.Extensions.LoopDecision'   = '上述实现所在程序集（Agent 侧不得引用）'
}

$hits = [System.Collections.Generic.List[object]]::new()
foreach ($file in $files) {
    $lines = Get-Content $file.FullName -ErrorAction SilentlyContinue
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $text = $lines[$i]
        if ([string]::IsNullOrWhiteSpace($text)) { continue }
        $trimmed = $text.Trim()
        if ($trimmed.StartsWith('//') -or $trimmed.StartsWith('/*') -or $trimmed.StartsWith('*')) { continue }
        foreach ($name in $patterns.Keys) {
            if ($text -notmatch [regex]::Escape($name)) { continue }
            $hits.Add([pscustomobject]@{
                File = $file.FullName.Substring($Root.Length).TrimStart('\')
                Line = $i + 1
                Pattern = $name
                Text = $trimmed
            })
            break
        }
    }
}

Write-Host '=== [F-66] Agent 侧不得引用确定性核心状态化承载（LoopStateStore/CoreStateView） ==='
Write-Host ("扫描文件数: {0} | 违规命中: {1}" -f $files.Count, $hits.Count)
if ($hits.Count -gt 0) {
    Write-Host '--- 命中清单（文件:行 符号 片段） ---'
    $hits | ForEach-Object { Write-Host ("  [违规] {0}:{1} [{2}] {3}" -f $_.File, $_.Line, $_.Pattern, $_.Text) }
}

if ($Check -and $hits.Count -gt 0) {
    Write-Host '校验失败：F-66 确定性核心边界被破坏（Agent 侧接线 StateHoldDriven 状态化承载 = 双承载）。'
    exit 1
}
exit 0
