# Check-AgentProcessSurface.ps1
# Agent 过程面单源守卫（A-1 / A-2 / C-1 的机器判据）
# 判据（三条均为「多处独立编码同一知识」的形态，实际发生过静默漂移）：
#   [P1] 过程键族字面量 "proc." 只允许出现在 Agent.Contract\NodeDomain\ProcessKeys.cs（键文法单源）；
#        其它位置须经 ProcessKeys.FamilyPrefix / PositionKey / IsProcessKey 消费。
#   [P2] 过程事件类别 append 调用不得写字面量 kind（如 ProcessNodeSink.Append(key, "tool", ...)）：
#        须经 ProcessNodeKinds 常量（发射侧与面板读侧共用）。
#   [P3] 禁用 new Progress<T>：其「无同步上下文即线程池投递」破坏到达序（seq 语义），且可能迟到到结算后丢失；
#        须用 Agent.Modules.Threading.SyncProgress<T>。
# 说明：仅扫「代码行」（跳过注释/XML 文档行）；测试工程不在扫描面（在 Environment\CentralTest 下）。
#   - 报告模式（默认）：列出命中，不设退出码。
#   - 校验模式（-Check）：命中 > 0 → 退出码 1（门禁）。

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

$scanDir = Join-Path $Root 'Projects\Plug\Agent\src'
if (-not (Test-Path $scanDir)) { Write-Error "未找到 Agent 源码目录: $scanDir"; exit 2 }

# 白名单（唯一单源文件）：键文法常量本体
$keyGrammarFile = 'Agent.Contract\NodeDomain\ProcessKeys.cs'

$files = Get-ChildItem -Path $scanDir -Recurse -File -Include *.cs -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' -and $_.FullName -notlike '*\_deprecated\*' }

$hits = [System.Collections.Generic.List[object]]::new()
foreach ($file in $files) {
    $rel = $file.FullName.Substring($Root.Length).TrimStart('\')
    $lines = Get-Content $file.FullName -ErrorAction SilentlyContinue
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $text = $lines[$i]
        if ([string]::IsNullOrWhiteSpace($text)) { continue }
        $trimmed = $text.Trim()
        # 注释/文档行不参与判据（守卫对象是代码）
        if ($trimmed.StartsWith('//') -or $trimmed.StartsWith('/*') -or $trimmed.StartsWith('*')) { continue }

        if ($text -match '"proc\.' -and ($rel -notlike ('*' + $keyGrammarFile))) {
            $hits.Add([pscustomobject]@{ File = $rel; Line = $i + 1; Rule = 'P1 过程键族字面量'; Text = $trimmed })
        }

        if ($text -match '(ProcessNodeSink|ProcessRunNodes)\.Append\([^,]*,\s*"') {
            $hits.Add([pscustomobject]@{ File = $rel; Line = $i + 1; Rule = 'P2 过程类别字面量'; Text = $trimmed })
        }

        if ($text -match 'new\s+Progress\s*<') {
            $hits.Add([pscustomobject]@{ File = $rel; Line = $i + 1; Rule = 'P3 Progress<T> 异步投递'; Text = $trimmed })
        }
    }
}

if ($hits.Count -eq 0) {
    Write-Host "[OK] Agent 过程面单源守卫：0 命中（键文法/类别/进度接收器均走单源）" -ForegroundColor Green
    exit 0
}

Write-Host "[FAIL] Agent 过程面单源守卫命中 $($hits.Count) 处：" -ForegroundColor Yellow
foreach ($hit in $hits) {
    Write-Host ("  [{0}] {1}:{2}  {3}" -f $hit.Rule, $hit.File, $hit.Line, $hit.Text)
}
Write-Host "  修复口径：键文法 ⇒ ProcessKeys；事件类别 ⇒ ProcessNodeKinds；进度接收器 ⇒ SyncProgress<T>"

if ($Check) { exit 1 }
exit 0
