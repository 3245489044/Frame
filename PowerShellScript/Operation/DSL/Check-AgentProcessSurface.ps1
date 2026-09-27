# Check-AgentProcessSurface.ps1
# Agent 过程面单源守卫（A-1 / A-2 / C-1 的机器判据）
# 判据（三条均为「多处独立编码同一知识」的形态，实际发生过静默漂移）：
#   [P1] 过程键族字面量 "proc." 只允许出现在 Agent.Contract\NodeDomain\ProcessKeys.cs（键文法单源）；
#        其它位置须经 ProcessKeys.FamilyPrefix / PositionKey / IsProcessKey 消费。
#   [P2] 过程事件类别 append 调用不得写字面量 kind（如 ProcessNodeSink.Append(key, "tool", ...)）：
#        须经 ProcessNodeKinds 常量（发射侧与面板读侧共用）。
#   [P3] 禁用 new Progress<T>：其「无同步上下文即线程池投递」破坏到达序（seq 语义），且可能迟到到结算后丢失；
#        须用 Agent.Modules.Threading.SyncProgress<T>。
#   [P4]（K3）过程类别常量须为「词表编译视图」：Agent.Contract\NodeDomain\ProcessNodeKinds.cs 禁自持
#        类别字面量（值的唯一来源 = RecognizedKinds ← 声明面 Config\recognizers.json）；
#        附正对照（镜像须显式引用 RecognizedKinds）与负对照（注入字面量必须被同一判据捕获）。
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

# ---------------- [P4] 过程类别字面量（K3：词表编译视图） ----------------
# 判据核心（纯函数；负对照直接复用 ⇒ 判据不可被"恒真"绕过）：返回 @{ Line; Kind } 清单。
function Get-KindLiteralHits {
    param(
        [string[]]$Lines,
        [string[]]$Kinds
    )
    $found = [System.Collections.Generic.List[object]]::new()
    for ($i = 0; $i -lt $Lines.Count; $i++) {
        $text = [string]$Lines[$i]
        if ([string]::IsNullOrWhiteSpace($text)) { continue }
        $trimmed = $text.Trim()
        if ($trimmed.StartsWith('//') -or $trimmed.StartsWith('/*') -or $trimmed.StartsWith('*')) { continue }
        foreach ($kind in $Kinds) {
            if ($text -match ('"' + [regex]::Escape($kind) + '"')) {
                $found.Add([pscustomobject]@{ Line = $i + 1; Kind = $kind })
            }
        }
    }
    return $found
}

$recognizerSpecPath = Join-Path $Root 'Projects\Plug\Agent\Config\recognizers.json'
$kindMirrorRelative = 'Agent.Contract\NodeDomain\ProcessNodeKinds.cs'
$kindMirrorFull = Join-Path $scanDir 'Agent.Contract\NodeDomain\ProcessNodeKinds.cs'
$declaredKinds = @()
if (Test-Path -LiteralPath $recognizerSpecPath) {
    try {
        $recognizerSpec = Get-Content -LiteralPath $recognizerSpecPath -Raw | ConvertFrom-Json
        $declaredKinds = @($recognizerSpec.kinds | ForEach-Object { [string]$_ } | Where-Object { $_ })
    } catch {
        $hits.Add([pscustomobject]@{ File = 'Config\recognizers.json'; Line = 0; Rule = 'P4 判据前置'; Text = "识别声明解析失败（判据不可判）：$($_.Exception.Message)" })
    }
} else {
    $hits.Add([pscustomobject]@{ File = 'Config\recognizers.json'; Line = 0; Rule = 'P4 判据前置'; Text = '识别声明缺失（类别词表无来源，判据不可判）' })
}

if ($declaredKinds.Count -gt 0) {
    if (Test-Path -LiteralPath $kindMirrorFull) {
        $mirrorLines = @(Get-Content -LiteralPath $kindMirrorFull)
        foreach ($hit in @(Get-KindLiteralHits -Lines $mirrorLines -Kinds $declaredKinds)) {
            $hits.Add([pscustomobject]@{ File = $kindMirrorRelative; Line = $hit.Line; Rule = 'P4 类别字面量'; Text = $hit.Kind })
        }
        # 正对照（防判据空转）：镜像须显式经 RecognizedKinds 取值，否则判据无标的
        if (($mirrorLines -join "`n") -notmatch 'RecognizedKinds\.') {
            $hits.Add([pscustomobject]@{ File = $kindMirrorRelative; Line = 0; Rule = 'P4 正对照失效'; Text = '类别镜像未引用 RecognizedKinds（判据无标的）' })
        }
        # 负对照（防假绿）：注入字面量必须被同一判据捕获
        $probe = @('    public const string Probe = "' + $declaredKinds[0] + '";')
        if (@(Get-KindLiteralHits -Lines $probe -Kinds $declaredKinds).Count -eq 0) {
            $hits.Add([pscustomobject]@{ File = 'P4'; Line = 0; Rule = 'P4 负对照失效'; Text = '注入类别字面量未被判据捕获（判据恒真）' })
        }
    } else {
        $hits.Add([pscustomobject]@{ File = $kindMirrorRelative; Line = 0; Rule = 'P4 镜像缺失'; Text = '类别镜像文件不存在（类别单源缺编译视图）' })
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
Write-Host "  修复口径：键文法 ⇒ ProcessKeys；事件类别 ⇒ ProcessNodeKinds（值须经 RecognizedKinds）；进度接收器 ⇒ SyncProgress<T>"

if ($Check) { exit 1 }
exit 0
