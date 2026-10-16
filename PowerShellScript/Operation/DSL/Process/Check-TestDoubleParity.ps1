#requires -Version 5.1
<#
.SYNOPSIS
    测试替身一致性守卫（TestDoubleParity）—— 测试侧引用的产品类型成员必须存在于产品类型定义。

.DESCRIPTION
    根因（框架级缺陷 · 因6）：测试侧手写替身/夹具与产品模型**无一致性判据** —— 产品模型演进
    （成员改名/移除/改形）后，测试仍按旧面构造，漂移只能等**编译期/运行期**才炸，且暴露成本被
    运行器放大（全量扫描 + 排队）。本件把「测试侧对产品表面的引用」与「产品表面」做机器对账，
    使漂移在**门禁期**可见。

    判据（静态对账；只读仓库，零运行）：
      D0 正/负对照：判据载体（成员差集判定）对合成样本行为正确（防判据恒真/恒假）。
      D1 成员存在性：测试侧 `new <Type> { Member = … }` 用到的成员须 ∈ 产品类型 `<Type>` 的成员集。
          仅判「产品面能定位到该类型声明 ∧ 该声明无基类子句（继承面不可见）∧ 解析到 ≥1 成员」的类型
          —— 判不了即跳过（禁猜测，防假红）。
      D2 不静默空转：标的（替身构造点数）须 ≥ 基线下限（否则判据无标的 ⇒ 假绿）。
      D3 可见化：违规明细须列出「类型 / 缺失成员 / 命中文件」。

    模式：默认报告模式；-Check 校验模式（违规 → 退出码 1）。

.PARAMETER Check
    校验模式：存在违规 → 退出码 1。

.PARAMETER ShowDetail
    输出逐项实测（类型 / 成员 / 命中数）。
#>
[CmdletBinding()]
param(
    [switch]$Check,
    [switch]$ShowDetail
)

$ErrorActionPreference = 'Stop'

$repoRoot = $PSScriptRoot
while ($repoRoot -and $repoRoot -ne (Split-Path $repoRoot -Parent)) {
    if ((Test-Path (Join-Path $repoRoot 'Projects')) -and (Test-Path (Join-Path $repoRoot 'Frame'))) { break }
    $repoRoot = Split-Path $repoRoot -Parent
}

$productRoots = @($repoRoot)
$productExcludeDirs = @('BuildOutput', 'obj', 'bin', '_deprecated', 'node_modules', '.git', 'Docs', 'crash', '.VSCodeCounter')
$productExcludePaths = @('/Environment/CentralTest/')
$testRoots = @(
    (Join-Path $repoRoot 'Environment\CentralTest\AgentTest'),
    (Join-Path $repoRoot 'Environment\CentralTest\CSharpTest'),
    (Join-Path $repoRoot 'Environment\CentralTest\UsharpTests'),
    (Join-Path $repoRoot 'Environment\CentralTest\NarrativeTests')
)

$violations = [System.Collections.Generic.List[string]]::new()
$notes = [System.Collections.Generic.List[string]]::new()

# 路径排除判据（唯一定义点；产品面 / 测试面共用）—— 段口径（`-like`），避开正则转义
function Test-ExcludedPath([string]$Path, [switch]$AsProduct) {
    $norm = ($Path -replace '\\', '/')
    foreach ($seg in $productExcludeDirs) { if ($norm -like "*/$seg/*") { return $true } }
    if ($AsProduct) { foreach ($p in $productExcludePaths) { if ($norm -like "*$p*") { return $true } } }
    return $false
}

# 继承面不可见 ⇒ 这些成员名一律豁免（防假红）
$universalAllow = @('Item', 'Count', 'Length', 'Value', 'Key', 'Name', 'Equals', 'GetHashCode', 'ToString')
$minProbes = 5    # D2 下限（标的过少 ⇒ 判据空转）

# ── 判据载体：成员差集（唯一定义点；D0 与 D1 共用同一判定函数）──
function Get-MissingMember([System.Collections.Generic.HashSet[string]]$Members, [System.Collections.Generic.HashSet[string]]$Used, [string[]]$Allow) {
    $missing = New-Object System.Collections.Generic.List[string]
    foreach ($m in $Used) {
        if ([string]::IsNullOrWhiteSpace($m)) { continue }
        if ($Members.Contains($m)) { continue }
        if ($Allow -contains $m) { continue }
        $missing.Add($m)
    }
    return ($missing | Sort-Object)
}

# ── 判据载体：初始化体 → 顶层成员名（剥离嵌套块，防「子初始化器成员误归因父类型」）──
function Get-InitializerMember([string]$Body) {
    if ([string]::IsNullOrWhiteSpace($Body)) { return @() }
    $flat = $Body
    $prev = $null
    while ($prev -ne $flat) { $prev = $flat; $flat = [regex]::Replace($flat, '\{[^{}]*\}', ' ') }
    $names = @([regex]::Matches($flat, '(?:^|,|\{|;)\s*(?<m>[A-Za-z_][A-Za-z0-9_]*)\s*=(?![=>])') |
        ForEach-Object { $_.Groups['m'].Value } | Where-Object { $_ })
    return $names
}

# ── D0 正/负对照 ──
$cmpMembers = [System.Collections.Generic.HashSet[string]]::new([string[]]@('A', 'B'))
$cmpUsedOk = [System.Collections.Generic.HashSet[string]]::new([string[]]@('A'))
$cmpUsedBad = [System.Collections.Generic.HashSet[string]]::new([string[]]@('A', 'C'))
if ((@(Get-MissingMember -Members $cmpMembers -Used $cmpUsedOk -Allow @())).Count -ne 0) {
    $violations.Add('[D0] 负对照失效：子集使用未判 0 缺失（判据载体恒真）')
}
if ((@(Get-MissingMember -Members $cmpMembers -Used $cmpUsedBad -Allow @())).Count -ne 1) {
    $violations.Add('[D0] 正对照失效：越界成员未判 1 缺失（判据载体被改坏）')
}
if ((@(Get-MissingMember -Members $cmpMembers -Used $cmpUsedBad -Allow @('C'))).Count -ne 0) {
    $violations.Add('[D0] 豁免面失效：Allow 内的成员仍被判缺失')
}
$flatProbe = @(Get-InitializerMember -Body 'A = 1, B = new C { D = 2 }, E = x => y')
if ($flatProbe.Count -ne 3 -or $flatProbe -notcontains 'A' -or $flatProbe -notcontains 'B' -or $flatProbe -notcontains 'E' -or $flatProbe -contains 'D') {
    $violations.Add("[D0] 初始化体解析失效：期望 {A,B,E}（D 属子初始化器 ⇒ 剥离），实测 {$($flatProbe -join ',')}")
}

# ── 1) 测试面：收集 `new <Type> { Member = … }` 用法（含首个 `{` 的括号配对体）──
$usage = @{}       # type -> HashSet[member]
$usageFiles = @{}  # type -> List[file]
$testDeclared = @{} # type -> bool（测试面自身声明的类型 ⇒ 同名歧义 ⇒ 不可判）
$probeCount = 0
$rxNew = [regex]'new\s+(?<t>[A-Z][A-Za-z0-9_]*)\s*\{'
$rxTestDecl = [regex]('(?m)^\s*(?:public|internal|protected|private|sealed|abstract|static|partial|readonly|unsafe|new|\s)*\b(?:class|record|struct)\s+(?<n>[A-Za-z_][A-Za-z0-9_]*)\b')
foreach ($root in $testRoots) {
    if (-not (Test-Path -LiteralPath $root)) { continue }
    foreach ($f in @(Get-ChildItem -Path $root -Recurse -File -Filter '*.cs' -ErrorAction SilentlyContinue)) {
        if (Test-ExcludedPath -Path $f.FullName) { continue }
        $text = $null
        try { $text = Get-Content -LiteralPath $f.FullName -Raw } catch { continue }
        if (-not $text) { continue }
        foreach ($dm in $rxTestDecl.Matches($text)) { $testDeclared[$dm.Groups['n'].Value] = $true }
        foreach ($m in $rxNew.Matches($text)) {
            $type = $m.Groups['t'].Value
            $open = $text.IndexOf('{', $m.Index + $m.Length - 1)
            if ($open -lt 0) { continue }
            $depth = 0; $end = -1
            for ($i = $open; $i -lt $text.Length; $i++) {
                $c = $text[$i]
                if ($c -eq '{') { $depth++ }
                elseif ($c -eq '}') { $depth--; if ($depth -eq 0) { $end = $i; break } }
            }
            if ($end -lt 0) { continue }
            $body = $text.Substring($open + 1, $end - $open - 1)
            $members = @(Get-InitializerMember -Body $body)
            if ($members.Count -eq 0) { continue }
            $probeCount++
            if (-not $usage.ContainsKey($type)) {
                $usage[$type] = [System.Collections.Generic.HashSet[string]]::new()
                $usageFiles[$type] = New-Object System.Collections.Generic.List[string]
            }
            foreach ($mm in $members) { [void]$usage[$type].Add($mm) }
            $rel = $f.FullName.Substring($repoRoot.Length + 1)
            if (-not $usageFiles[$type].Contains($rel)) { $usageFiles[$type].Add($rel) }
        }
    }
}

if ($ShowDetail) { Write-Host ("  测试面：替身构造点 {0} 个 / 涉及类型 {1} 个" -f $probeCount, $usage.Count) -ForegroundColor DarkGray }

# ── D2 标的数量 ──
if ($probeCount -lt $minProbes) {
    $violations.Add("[D2] 正对照失效：替身构造点 $probeCount < 基线下限 $minProbes（判据无标的 ⇒ 假绿）")
}
# ── 2) 产品面：只读「含目标类型名」的文件，解析声明与成员（继承子句 ⇒ 跳过判定）──
$targetTypes = @($usage.Keys | Sort-Object)
if ($targetTypes.Count -eq 0) {
    $notes.Add('未发现测试侧替身构造点（无标的，判据按 D2 处理）')
}
$typeMembers = @{}    # type -> HashSet[member]
$typeJudged = @{}     # type -> bool（可判：定位到声明 ∧ 无基类子句）
$typeDeclCount = @{}  # type -> int（产品面声明出现次数；>1 = 同名多类型 ⇒ 有歧义 ⇒ 不可判）

if ($targetTypes.Count -gt 0) {
    $typeAlt = ($targetTypes | ForEach-Object { [regex]::Escape($_) }) -join '|'
    $rxTypeAlt = [regex]("\b(?:class|record|struct)\s+(?:$typeAlt)\b")
    $rxDecl = [regex]('(?m)^\s*(?:public|internal|protected|private|sealed|abstract|static|partial|readonly|unsafe|new|\s)*\b(?:class|record|struct)\s+(?<n>[A-Za-z_][A-Za-z0-9_]*)\b(?<rest>[^\r\n{]*)')
    $rxMember = [regex]('(?m)^\s*(?:public|internal|protected)\b[^\r\n;{()]*\b(?<m>[A-Za-z_][A-Za-z0-9_]*)\s*(?:\{|;|=)')

    foreach ($root in $productRoots) {
        if (-not (Test-Path -LiteralPath $root)) { continue }
        foreach ($f in @(Get-ChildItem -Path $root -Recurse -File -Filter '*.cs' -ErrorAction SilentlyContinue)) {
            if (Test-ExcludedPath -Path $f.FullName -AsProduct) { continue }
            $text = $null
            try { $text = Get-Content -LiteralPath $f.FullName -Raw } catch { continue }
            if (-not $text -or $text.Length -lt 16) { continue }
            if (-not $rxTypeAlt.IsMatch($text)) { continue }   # 快速预筛：不含任何目标类型名即跳过
            # 本文件声明的目标类型（先声明后取成员：避免 O(成员 × 目标类型) 的乘积扫描）
            $declared = New-Object System.Collections.Generic.List[string]
            foreach ($dm in $rxDecl.Matches($text)) {
                $tn = $dm.Groups['n'].Value
                if (-not $usage.ContainsKey($tn)) { continue }
                if (-not $declared.Contains($tn)) { $declared.Add($tn) }
                if (-not $typeDeclCount.ContainsKey($tn)) { $typeDeclCount[$tn] = 0 }
                $typeDeclCount[$tn]++
                if (-not $typeJudged.ContainsKey($tn)) { $typeJudged[$tn] = $true }
                if (-not $typeMembers.ContainsKey($tn)) { $typeMembers[$tn] = [System.Collections.Generic.HashSet[string]]::new() }

                $rest = [string]$dm.Groups['rest'].Value
                if ($rest -match ':') { $typeJudged[$tn] = $false }   # 继承面不可见 ⇒ 不可判（防假红）
                $pm = [regex]::Match($rest, '\((?<params>[^)]*)\)')
                if ($pm.Success) {
                    foreach ($p in ($pm.Groups['params'].Value -split ',')) {
                        $pname = ($p -replace '\s*=\s*.*$', '').Trim()
                        $pname = ($pname -split '\s+')[-1]
                        if ($pname -match '^[A-Za-z_][A-Za-z0-9_]*$') { [void]$typeMembers[$tn].Add($pname) }
                    }
                }
            }
            if ($declared.Count -eq 0) { continue }   # 本文件不声明目标类型 ⇒ 成员解析无归属

            foreach ($mm in $rxMember.Matches($text)) {
                $mn = $mm.Groups['m'].Value
                foreach ($tn in $declared) { [void]$typeMembers[$tn].Add($mn) }
            }
        }
    }
}

# ── 3) 判定（仅可判类型：定位到声明 ∧ 无继承子句 ∧ 同名唯一）──
# 基线棘轮：Baselines\TestDoubleParity.psd1 的 Known 为「已知/已报告」集合 ⇒ 在表不算新违规（仍打印）；
# 表外即红（新增漂移）。基线缺失 ⇒ fail-closed 报错（禁隐式空基线：判据不得空转）。
$knownIds = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
Import-Module (Join-Path $repoRoot 'Frame\PowerShellScript\Baselines\GateBaseline.psm1') -Force
$baseline = Import-GateBaseline -Name TestDoubleParity
foreach ($e in @($baseline.Known)) {
    if ($e -is [System.Collections.IDictionary] -and $e.Contains('Value')) { [void]$knownIds.Add([string]$e.Value) }
}
if ($knownIds.Count -eq 0) {
    $violations.Add('[D2] 基线为空（Baselines\TestDoubleParity.psd1 无 Known 条目）——棘轮不得空转')
}

$judgedCount = 0
$skippedAmbiguous = 0
$knownHits = New-Object System.Collections.Generic.List[string]
foreach ($t in $targetTypes) {
    if (-not $typeJudged.ContainsKey($t) -or -not $typeJudged[$t]) { continue }
    if ($typeDeclCount.ContainsKey($t) -and $typeDeclCount[$t] -gt 1) { $skippedAmbiguous++; continue }   # 同名多类型 ⇒ 歧义 ⇒ 跳过
    if ($testDeclared.ContainsKey($t)) { $skippedAmbiguous++; continue }   # 测试面亦声明同名类型 ⇒ 歧义 ⇒ 跳过
    if (-not $typeMembers.ContainsKey($t) -or $typeMembers[$t].Count -eq 0) { continue }
    $judgedCount++
    $missing = @(Get-MissingMember -Members $typeMembers[$t] -Used $usage[$t] -Allow $universalAllow)
    if ($ShowDetail) {
        Write-Host ("  D1 {0}：产品成员 {1} / 测试使用 {2} / 缺失 {3}" -f $t, $typeMembers[$t].Count, $usage[$t].Count, $missing.Count) -ForegroundColor DarkGray
    }
    foreach ($m in $missing) {
        $files = if ($usageFiles.ContainsKey($t)) { @($usageFiles[$t]) -join ', ' } else { '' }
        $id = "$t.$m"
        if ($knownIds.Contains($id)) {
            $knownHits.Add("[已知] $id（命中：$files）")
            continue
        }
        $violations.Add("[D1] 测试引用了产品类型 $t 不存在的成员 '$m'（命中：$files）——测试替身/夹具与产品模型漂移")
    }
}

foreach ($k in $knownHits) { Write-Host ("  {0}" -f $k) -ForegroundColor DarkYellow }

if ($judgedCount -eq 0 -and $targetTypes.Count -gt 0) {
    $notes.Add("目标类型 $($targetTypes.Count) 个均不可判（未定位声明 / 含继承子句 / 零成员）⇒ 已跳过（禁猜测）")
}

foreach ($n in $notes) { Write-Host "  ! $n" -ForegroundColor Yellow }
Write-Host ("  可判类型 {0} / 目标类型 {1}（同名歧义跳过 {2}）；替身构造点 {3}" -f $judgedCount, $targetTypes.Count, $skippedAmbiguous, $probeCount) -ForegroundColor Gray

if ($violations.Count -eq 0) {
    Write-Host ''
    Write-Host '[OK] TestDoubleParity：0 违规（测试侧引用 ⊆ 产品类型成员面；正/负对照绿）' -ForegroundColor Green
    exit 0
}

Write-Host ''
Write-Host "[FAIL] TestDoubleParity 命中 $($violations.Count) 处：" -ForegroundColor Yellow
foreach ($v in (@($violations) | Select-Object -First 40)) { Write-Host "  - $v" -ForegroundColor Yellow }
if ($violations.Count -gt 40) { Write-Host ("  … 其余 {0} 处（-ShowDetail 或直接跑门禁脚本查看全量）" -f ($violations.Count - 40)) -ForegroundColor Yellow }
Write-Host '  修复口径：测试侧引用的产品类型成员须与产品定义一致；漂移应改测试（替身/夹具随模型演进），或改由 Agent.TestShared 提供单源工厂。' -ForegroundColor Gray
if ($Check) { exit 1 }
exit 0