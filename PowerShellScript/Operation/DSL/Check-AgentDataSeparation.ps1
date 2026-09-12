# Check-AgentDataSeparation.ps1
# Agent「数据与代码分离」聚合门禁（D1–D5）
#   权威: Projects\Plug\Agent\doc\Agent数据与代码分离-架构整理与通用化方案.md §1（判定式）/ §6.2（判据体系）/ §7 Batch 0
# 设计: 只做聚合与归属核对，不重复实现既有扫描 ——
#   [D1] 逻辑面零静态可变状态 → 委托 Check-AgentRuntimeSlots.ps1（K10 段）
#   [D2] 逻辑面零造根         → 委托 Check-DataCenterGenericity.ps1（K9 段）
#   [D4] 通用层零领域词汇     → 同上 G1/G2 段（同一次调用取两份结论，不重复起进程）
#   [D3] 同一事实单写者       → 委托 Check-SingleWriter.ps1
#   [D5] 每类数据登记归属     → 扫描「疑似数据持有」逐条核对 doc\data-ownership.json（本脚本独有）
# 疑似数据持有 = K10 的 S1/S2（静态可变槽）∪ K9 的 new StateCenter( / static … IRootProvider；
#   未在 data-ownership.json 登记 → 无主数据违规（D5 判据）。
# 基线冻结（Batch 0 实测 2026-09-12）：K10 命中 22 / 16 文件；K9 待迁移 2；单写者告警 12；G1/G2 违规 0。
#   「待迁移」只允许下降，回涨即违规（防基线静默漂移）。
# 模式：报告模式（默认）列出全部清单并 EXIT 0；
#       -Check 模式在「D1–D4 违规 > 0」或「D5 无主数据 > 0」或「待迁移回涨」时 EXIT 1（CI 门禁）。

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

$dslDir   = $PSScriptRoot
$agentSrc = Join-Path $Root 'Projects\Plug\Agent\src'
$ledgerPath = Join-Path $Root 'Projects\Plug\Agent\doc\data-ownership.json'
if (-not (Test-Path $agentSrc)) { Write-Error "未找到 Agent 源码目录: $agentSrc"; exit 2 }
if (-not (Test-Path $ledgerPath)) { Write-Error "未找到归属台账（D5 核对源）: $ledgerPath"; exit 2 }

# 冻结基线（Batch 0 实测；归零后由 Batch 7 清空）—— 待迁移项只允许下降
$frozen = [ordered]@{
    K10Hits     = 22   # K10 存量基线命中（对象槽逐项收敛 → 最终 0）
    K10Files    = 16   # K10 存量基线涉及文件数
    K9Pending   = 0    # K9 待迁移（Batch 1b 已归零：产品侧兜底造根移除）
    K9Violation = 0
    G1Hits      = 0
    G2Violation = 0
    SWViolation = 0
}

# 关键存储文件存在性（D3 前置：脚本默认 Root 已修为四级上溯）
if (-not (Test-Path (Join-Path $Root 'Frame\CsharpPrj\Plate\DataManage\Storage\Sqlite\SqliteRelate.cs'))) {
    Write-Error "未找到 Frame\CsharpPrj\Plate\DataManage\Storage\Sqlite\SqliteRelate.cs —— D3 审计面不完整。"; exit 2
}

# ── 委托执行（捕获退出码 + 输出文本；既有脚本保持唯一权威，本脚本只读其结论） ──
# 注：既有门禁脚本经 Write-Host 输出（PowerShell 信息流 = 流 6），须 6>&1 才能纳入捕获。
function Invoke-DelegateGate {
    param([string]$Script, [hashtable]$Parameters)
    $path = Join-Path $dslDir $Script
    if (-not (Test-Path $path)) { throw "门禁脚本缺失: $path" }
    $lines = & $path @Parameters 6>&1 2>&1 | Out-String
    $code  = $LASTEXITCODE
    return [pscustomobject]@{ Exit = $code; Text = $lines }
}

function Get-CountByMark {
    param([string]$Text, [string]$Mark)
    if ([string]::IsNullOrWhiteSpace($Text)) { return 0 }
    return @($Text -split "`r?`n" | Where-Object { $_ -match ('^\s*\[' + [regex]::Escape($Mark) + '\]') }).Count
}

# ── [D1] 零静态可变状态（委托 K10） ──
$k10 = Invoke-DelegateGate -Script 'Check-AgentRuntimeSlots.ps1' -Parameters @{ Check = $true; Root = $Root }
$k10Pending = Get-CountByMark -Text $k10.Text -Mark '待迁移'
$k10Hits    = Get-CountByMark -Text $k10.Text -Mark '违规'

# ── [D2]+[D4] 零造根 + 通用层零领域词汇（委托 K9 + G1/G2，单次调用） ──
$gen = Invoke-DelegateGate -Script 'Check-DataCenterGenericity.ps1' -Parameters @{ Check = $true; Root = $Root }
$k9Pending   = Get-CountByMark -Text $gen.Text -Mark '待迁移'
$k9Violation = Get-CountByMark -Text $gen.Text -Mark '违规'
$g1Hits = if ($gen.Text -match '扫描文件数:\s*\d+[^\r\n]*?命中:\s*(\d+)') { [int]$Matches[1] } else { 0 }
$g2Violation = if ($gen.Text -match '违规命中:\s*(\d+)\s*\|\s*csproj 违规:\s*(\d+)') { [int]$Matches[1] + [int]$Matches[2] } else { 0 }

# ── [D3] 同一事实单写者（委托 Check-SingleWriter） ──
$sw = Invoke-DelegateGate -Script 'Check-SingleWriter.ps1' -Parameters @{ Root = $Root }
$swViolation = if ($sw.Text -match '发现\s*(\d+)\s*处违规') { [int]$Matches[1] } else { 0 }
$swWarnings  = if ($sw.Text -match '告警\s*(\d+)\s*条') { [int]$Matches[1] } else { 0 }

# ── [D5] 无主数据：疑似数据持有逐条核对归属台账 ──
$ledger = Get-Content $ledgerPath -Raw -Encoding UTF8 | ConvertFrom-Json
$entries = @($ledger.entries)
if ($entries.Count -eq 0) { Write-Error "归属台账为空: $ledgerPath"; exit 2 }

$holdPatterns = [ordered]@{
    'S1 静态可变字段'          = '^\s*(?:public|internal|private|protected)\s+[^;]*\bstatic\b(?![^;]*\b(?:readonly|const)\b)[^;]*;\s*$'
    'S2 静态可变属性'          = '\bstatic\b[^;{]*\{\s*get;\s*set;'
    'K9 兜底造根'              = 'new\s+StateCenter\s*\('
    'K9 静态持有根'            = 'static\s+[^;\r\n=]*\bIRootProvider\b'
}

function Test-HolderLine {
    param([string]$Line)
    $t = $Line.Trim()
    if ([string]::IsNullOrWhiteSpace($t)) { return $null }
    if ($t.StartsWith('//') -or $t.StartsWith('/*') -or $t.StartsWith('*')) { return $null }
    if ($t -match '\b(?:partial|class|struct|record|interface|enum)\b') { return $null }
    foreach ($name in $holdPatterns.Keys) {
        if ($t -notmatch $holdPatterns[$name]) { continue }
        # 与 K10 对齐：只读/常量/类型声明/方法/表达式体 不构成"静态可变槽"
        if ($name -like 'S1*' -and ($t -match '\(|\{|=>')) { continue }
        # 与 K9 对齐：仅"静态持有根"适用豁免（static readonly / 仅 get 属性 / 方法签名 / 表达式体）；
        # "兜底造根"（new StateCenter(）本身含 '(' 故不得套用该豁免
        if ($name -eq 'K9 静态持有根' -and ($t -match '\bstatic\s+readonly\b' -or $t -match '\{\s*get;\s*\}')) { continue }
        if ($name -eq 'K9 静态持有根' -and ($t -match '=>|\(')) { continue }
        return $name
    }
    return $null
}

# 台账条目匹配：pattern 为 '/' 分隔的路径片段（'|' 可并列多条），命中任一即视为已登记
function Test-LedgerMatch {
    param([string]$RelNorm)
    foreach ($e in $entries) {
        foreach ($frag in ($e.pattern -split '\|')) {
            $f = $frag.Trim()
            if ($f -and $RelNorm -like ('*' + $f + '*')) { return $e }
        }
    }
    return $null
}

$holders = [System.Collections.Generic.List[object]]::new()
$unowned = [System.Collections.Generic.List[object]]::new()
$pendingEntries = [System.Collections.Generic.List[object]]::new()
$files = Get-ChildItem -Path $agentSrc -Recurse -File -Include *.cs -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' -and $_.FullName -notlike '*\_deprecated\*' }
foreach ($file in $files) {
    $rel = $file.FullName.Substring($Root.Length).TrimStart('\')
    $relNorm = $rel -replace '\\', '/'
    $lines = Get-Content $file.FullName -ErrorAction SilentlyContinue
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $kind = Test-HolderLine -Line $lines[$i]
        if (-not $kind) { continue }
        $entry = Test-LedgerMatch -RelNorm $relNorm
        $rec = [pscustomobject]@{
            File = $rel; Line = $i + 1; Kind = $kind
            Entry = if ($entry) { $entry.id } else { '' }
            Class = if ($entry) { $entry.class } else { '' }
            Status = if ($entry) { $entry.status } else { 'unowned' }
        }
        $holders.Add($rec)
        if (-not $entry) { $unowned.Add($rec) }
    }
}
foreach ($e in $entries) { if ($e.status -eq 'pending') { $pendingEntries.Add($e) } }


# ── 报告 ──
Write-Host '=== [DataSeparation] Agent 数据与代码分离聚合门禁（D1–D5） ==='
Write-Host ("根目录: {0} | 台账: {1} 条 | 疑似数据持有: {2}" -f $Root, $entries.Count, $holders.Count)
Write-Host ''

$fail = [System.Collections.Generic.List[string]]::new()

# D1
Write-Host '--- [D1] 逻辑面零静态可变状态（委托 K10） ---'
Write-Host ("K10 退出码: {0} | 存量基线命中: {1}（冻结 {2}）| 违规: {3}" -f $k10.Exit, $k10Pending, $frozen.K10Hits, $k10Hits)
if ($k10Hits -gt 0) { $fail.Add("D1 静态可变运行时槽违规 $k10Hits 处（应改经运行时槽分区或 AmbientSlot 承载）") }
if ($k10Pending -gt $frozen.K10Hits) { $fail.Add("D1 存量基线回涨：$k10Pending > 冻结 $($frozen.K10Hits)（只允许下降）") }

# D2 / D4
Write-Host '--- [D2] 逻辑面零造根 / [D4] 通用层零领域词汇（委托 K9 + G1/G2） ---'
Write-Host ("G1 领域词汇命中: {0}（冻结 {1}）| G2 设施违规: {2}（冻结 {3}）" -f $g1Hits, $frozen.G1Hits, $g2Violation, $frozen.G2Violation)
Write-Host ("K9 待迁移: {0}（冻结 {1}）| 违规: {2}" -f $k9Pending, $frozen.K9Pending, $k9Violation)
if ($k9Violation -gt 0) { $fail.Add("D2 产品侧兜底造根 / 静态持有根违规 $k9Violation 处") }
if ($k9Pending -gt $frozen.K9Pending) { $fail.Add("D2 待迁移回涨：$k9Pending > 冻结 $($frozen.K9Pending)（只允许下降）") }
if ($g1Hits -gt $frozen.G1Hits) { $fail.Add("D4 DataCenter 领域词汇回流 $g1Hits 处") }
if ($g2Violation -gt $frozen.G2Violation) { $fail.Add("D4 DataCenter 设施回渗 $g2Violation 处") }

# D3
Write-Host '--- [D3] 同一事实单写者（委托 Check-SingleWriter） ---'
Write-Host ("退出码: {0} | 违规: {1}（冻结 {2}）| 告警: {3}" -f $sw.Exit, $swViolation, $frozen.SWViolation, $swWarnings)
if ($swViolation -gt $frozen.SWViolation) { $fail.Add("D3 单写者违规 $swViolation 处（跨介质双写同一事实）") }

# D5
Write-Host '--- [D5] 无主数据（核对 data-ownership.json） ---'
Write-Host ("已登记持有: {0} | 无主数据: {1} | 台账待迁移条目: {2}" -f ($holders.Count - $unowned.Count), $unowned.Count, $pendingEntries.Count)
if ($unowned.Count -gt 0) {
    Write-Host '--- 无主数据清单（未登记归属 = 违规） ---'
    $unowned | ForEach-Object { Write-Host ("  [无主] {0}:{1} [{2}]" -f $_.File, $_.Line, $_.Kind) }
    $fail.Add("D5 无主数据 $($unowned.Count) 处（须在 data-ownership.json 登记归属）")
}
if ($Check) {
    Write-Host ''
    Write-Host '--- 台账待迁移条目（status=pending；随批次收敛） ---'
    $pendingEntries | ForEach-Object { Write-Host ("  [待迁移] {0} | class={1} | {2}" -f $_.id, $_.class, $_.target) }
}

Write-Host ''
if ($fail.Count -gt 0) {
    Write-Host "校验失败：数据与代码分离门禁未通过，$($fail.Count) 项："
    $fail | ForEach-Object { Write-Host "  - $_" }
    if ($Check) { exit 1 }
    Write-Host '（报告模式：不设退出码；加 -Check 进入门禁判定）'
    exit 0
}

Write-Host 'Check-AgentDataSeparation: OK —— D1–D5 全部通过（无新增违规 / 无无主数据 / 待迁移未回涨）。'
exit 0

