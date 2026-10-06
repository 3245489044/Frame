# Check-AntiPatternFamily.ps1
# 反模式族守卫（P9 落地）—— 把 clinerules/formula.md §8 的「族 = 一条通用规则 ∧ 判据可回溯至公理」从散文变成机器判据。
# 结构（§8 已合并为「族 = 通用规则」单表）：
#   §8.0 表每行 = 一个族；列 = 族 | 通用规则(权威符号) | 定理 | crit | 严重 | 症状 | covered(违反形态 ID)
# 判据：
#   C1 族唯一且件数 ≥ 基线
#   C2 covered ID 形态合法且全局唯一（每条恰属一族）
#   C3 crit ∈ {macro, micro}
#   C4 每族"通用规则"列非空
#   C5 通用规则（含 covered 引用）符号可解析（«lib:STYP.NAME» 回指已注册定义点；Sec 放行）
#   C6 ID 集合守恒：covered ID ⊇ 基线 ID（只增不减）
#   C7 正/负对照（防判据空转）：族件数/ID 数下限；合成非法 ID 必被拒
# 模式：-Check 违规 exit 1；默认报告模式（不设退出码）。-WriteBaseline 为写操作（聚合器禁用）。
# 依赖方向（硬约束）：本件只读仓库；-WriteBaseline 例外（仅写 Baselines\AntiPatternFamily.psd1）。

param(
    [switch]$Check,
    [switch]$WriteBaseline
)

$ErrorActionPreference = 'Stop'

# 定位仓库根（含 clinerules\ 与 Frame\）—— 相对 $PSScriptRoot 上溯，禁硬编码绝对路径
$__repo = $PSScriptRoot
while ($__repo -and $__repo -ne (Split-Path $__repo -Parent)) {
    if ((Test-Path (Join-Path $__repo 'Frame')) -and ((Test-Path (Join-Path $__repo 'clinerules')) -or (Test-Path (Join-Path $__repo '.clinerules')))) { break }
    $__repo = Split-Path $__repo -Parent
}
$rulesDir = if (Test-Path (Join-Path $__repo 'clinerules')) { Join-Path $__repo 'clinerules' } else { Join-Path $__repo '.clinerules' }
$formulaPath = Join-Path $rulesDir 'formula.md'
$baselinePath = Join-Path $__repo 'Frame\PowerShellScript\Baselines\AntiPatternFamily.psd1'
$violations = [System.Collections.Generic.List[string]]::new()
$families = [ordered]@{}
$allIds = New-Object System.Collections.Generic.HashSet[string]

$idRegex = '^ANTI-[A-Z0-9-]+$'
$familyBaseline = 19
$idBaseline = 69

# ---- 判据 C7b：负对照（ID 形态正则对合成非法样本必须命中拒绝；-cmatch 大小写敏感） ----
if (('NOT-AN-ANTI' -cmatch $idRegex) -or (@('ANTI-', 'ANTI-lower', 'ANTI_MIX') | Where-Object { $_ -cmatch $idRegex }).Count -gt 0) {
    $violations.Add("[负对照] ID 形态正则失效（合成非法样本未被拒）")
}

if (-not (Test-Path -LiteralPath $formulaPath)) {
    $violations.Add("[输入] 缺 $formulaPath")
} else {
    $lines = @(Get-Content -LiteralPath $formulaPath)
    $idx8 = -1; $idxEnd = $lines.Count
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match '^##\s+§八') { $idx8 = $i; continue }
        if ($idx8 -ge 0 -and $lines[$i] -match '^##\s+§[九1-9]') { $idxEnd = $i; break }
    }
    if ($idx8 -lt 0) { $violations.Add("[结构] 未找到 §八 反模式索引标题") }
    for ($i = $idx8 + 1; $i -lt $idxEnd; $i++) {
        $line = $lines[$i]
        if ($line -notmatch '^\s*\|') { continue }
        if ($line -match '^\s*\|[\s\-|:]+\|\s*$') { continue }   # 分隔行
        $cells = @($line.Trim().Trim('|').Split('|') | ForEach-Object { $_.Trim() })
        if ($cells.Count -lt 7) { continue }
        $fam = $cells[0]
        if ($fam -notmatch '^G[0-9]+$') { continue }             # 跳过表头行
        if ($families.Contains($fam)) { $violations.Add("[C1] 族码重复：$fam") }
        $families[$fam] = [PSCustomObject]@{ Rule = $cells[1]; Theorem = $cells[2]; Crit = $cells[3]; Severity = $cells[4]; Symptom = $cells[5]; Covered = $cells[6] }
        if ($cells[3] -notin @('macro', 'micro')) { $violations.Add("[C3] 族 $fam crit 非法：'$($cells[3])'（须 macro|micro）") }
        if ([string]::IsNullOrWhiteSpace($cells[1])) { $violations.Add("[C4] 族 $fam 通用规则列为空") }
        foreach ($m in ($cells[6] -split '·')) {
            $mid = $m.Trim()
            if (-not $mid) { continue }
            if ($mid -notmatch $idRegex) { $violations.Add("[C2] 族 $fam covered 含非法 ID 形态：$mid"); continue }
            if (-not $allIds.Add($mid)) { $violations.Add("[C2] ID 重复归属：$mid") }
        }
    }
    if ($families.Count -lt $familyBaseline) { $violations.Add("[C1] 族件数 $($families.Count) < 基线 $familyBaseline（判据无标的 ⇒ 假绿）") }
    if ($allIds.Count -lt $idBaseline) { $violations.Add("[C7a] covered 条目数 $($allIds.Count) < 基线 $idBaseline") }

    # ---- C5 通用规则符号可解析（回指已注册定义点） ----
    Import-Module (Join-Path $__repo 'Frame\PowerShellScript\Modules\StringTools\StringTools.Clinerules.psm1') -Force -ErrorAction Stop
    $libFiles = @{ 'axioms' = 'axioms.md'; 'formula' = 'formula.md'; 'theorems' = 'theorems.md'; 'logic' = 'logic.md' }
    $defIndex = New-Object System.Collections.Generic.HashSet[string]
    foreach ($lib in $libFiles.Keys) {
        $p = Join-Path $rulesDir $libFiles[$lib]
        if (-not (Test-Path -LiteralPath $p)) { continue }
        foreach ($ln in @(Get-Content -LiteralPath $p)) {
            if ($ln -match '^\s*\|') { continue }          # 表格行非定义
            if ($ln -match '^\s*```') { continue }
            $d = Resolve-DefinitionForm -Line $ln
            if (-not $d) { continue }
            foreach ($nm in $d.Names) { [void]$defIndex.Add("$lib/$($d.Styp)/$nm") }
        }
    }
    foreach ($fam in $families.Keys) {
        foreach ($r in [regex]::Matches($families[$fam].Rule, '«([A-Za-z]+):([A-Za-z0-9_.@-]+)»')) {
            $lib = $r.Groups[1].Value; $refBody = $r.Groups[2].Value -replace '@.*$', ''
            $parts = $refBody -split '\.', 2
            if ($parts.Count -lt 2) { $violations.Add("[C5] $fam 引用形态异常：$($r.Value)"); continue }
            $styp = $parts[0]; $name = $parts[1]
            if ($styp -eq 'Sec') { continue }               # 节引用放行
            $head = ($name -split '\.')[0]
            if (-not $defIndex.Contains("$lib/$styp/$name") -and -not $defIndex.Contains("$lib/$styp/$head")) {
                $violations.Add("[C5] $fam 通用规则不可解析：$($r.Value)")
            }
        }
    }

    # ---- C6 基线（ID 集合守恒：covered ⊇ 基线） ----
    $baselineIds = @()
    if (Test-Path -LiteralPath $baselinePath) {
        $bl = Import-PowerShellDataFile -LiteralPath $baselinePath
        if ($bl.ContainsKey('Ids')) { $baselineIds = @($bl.Ids.Value) }
    } else {
        $violations.Add("[C6] 缺基线：$baselinePath")
    }
    foreach ($id in $baselineIds) {
        if (-not $allIds.Contains($id)) { $violations.Add("[C6] 基线 ID 丢失：$id") }
    }
}

# ---- -WriteBaseline（写操作，聚合器禁用） ----
if ($WriteBaseline -and $families.Count -gt 0) {
    $sorted = @($allIds | Sort-Object)
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine('@{')
    [void]$sb.AppendLine("    Schema = 'gate-baseline-v1'")
    [void]$sb.AppendLine("    Gate   = 'AntiPatternFamily'")
    [void]$sb.AppendLine("    FamilyCount = @{ Value = $($families.Count); Origin = 'Derived'; DueWhen = '' }")
    [void]$sb.AppendLine("    IdCount     = @{ Value = $($allIds.Count); Origin = 'Derived'; DueWhen = '' }")
    [void]$sb.AppendLine("    Ids = @{ Origin = 'Derived'; DueWhen = ''; Value = @(")
    for ($k = 0; $k -lt $sorted.Count; $k++) {
        $tail = if ($k -lt $sorted.Count - 1) { ',' } else { '' }
        [void]$sb.AppendLine("        '$($sorted[$k])'$tail")
    }
    [void]$sb.AppendLine('    ) }')
    [void]$sb.AppendLine('}')
    $dir = Split-Path $baselinePath -Parent
    if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    [System.IO.File]::WriteAllText($baselinePath, $sb.ToString(), (New-Object System.Text.UTF8Encoding($false)))
    Write-Host "基线已写入: $baselinePath" -ForegroundColor Cyan
}

# ---- 报告 ----
Write-Host '=== 反模式族守卫（AntiPatternFamily · P9） ==='
Write-Host '检查项: C1 族唯一/件数  C2 covered 双射  C3 crit  C4 规则非空  C5 通用规则可解析  C6 ID 守恒  C7 正/负对照'
Write-Host ("族数: {0} | covered 条目: {1} | 违规数: {2}" -f $families.Count, $allIds.Count, $violations.Count)

if ($violations.Count -gt 0) {
    Write-Host '--- 违规清单 ---'
    foreach ($v in $violations) { Write-Host "  [FAIL] $v" }
    if ($Check) { Write-Host "校验失败：反模式族守卫发现 $($violations.Count) 项违规"; exit 1 }
    exit 0
}

Write-Host '  [OK] 反模式族守卫通过'
exit 0

