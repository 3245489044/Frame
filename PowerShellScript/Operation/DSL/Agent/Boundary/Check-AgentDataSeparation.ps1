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
# 基线冻结**单源** = 归属台账 Projects\Plug\Agent\doc\data-ownership.json 的 frozenBaseline
#   （本脚本随台账装载派生，**禁在本脚本内联数值** —— 曾双源漂移：脚本 K10Hits=20 vs 台账 0）。
#   「待迁移」只允许下降，回涨即违规（防基线静默漂移）。
# 模式：报告模式（默认）列出全部清单并 EXIT 0；
#       -Check 模式在「D1–D4 违规 > 0」或「D5 无主数据 > 0」或「待迁移回涨」时 EXIT 1（CI 门禁）。

[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$AllowDataRootLogic
)

$ErrorActionPreference = 'Stop'

# 门禁运行时（根定位/落点/裁决单源；向上查找仓库超根，与目录分层解耦）
$GateRuntimeRoot = $PSScriptRoot
while ($GateRuntimeRoot -and -not ((Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Frame')) -and (Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Projects')))) { $GateRuntimeRoot = Split-Path -Parent $GateRuntimeRoot }
if (-not $GateRuntimeRoot) { throw '仓库超根定位失败（未命中同时含 Frame\ 与 Projects\ 的目录）' }
Import-Module (Join-Path $GateRuntimeRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking

if ([string]::IsNullOrWhiteSpace($Root)) {
    # 仓库超根由门禁运行时解析（与脚本所在目录深度无关）
    $Root = Get-GateRepoRoot -ScriptRoot $PSScriptRoot
}

Import-Module (Join-Path $Root 'Frame\PowerShellScript\Baselines\GateFileIndex.psm1') -Force

$dslDir = Get-MappedPath -Key 'DSL'  # DSL 根（单一来源＝目录映射，禁脚本目录自指）
$agentSrc = Join-Path $Root 'Projects\Plug\Agent\src'
$ledgerPath = Join-Path $Root 'Projects\Plug\Agent\doc\data-ownership.json'
if (-not (Test-Path $agentSrc)) { Write-Error "未找到 Agent 源码目录: $agentSrc"; exit 2 }
if (-not (Test-Path $ledgerPath)) { Write-Error "未找到归属台账（D5 核对源）: $ledgerPath"; exit 2 }

# 冻结基线在下方随归属台账装载派生（单源；见文件头注）—— 待迁移项只允许下降。
# 关键存储文件存在性（D3 前置：脚本默认 Root 已修为四级上溯）
if (-not (Test-Path (Join-Path $Root 'Frame\CsharpPrj\Plate\DataManage\Storage\Sqlite\SqliteRelate.cs'))) {
    Write-Error "未找到 Frame\CsharpPrj\Plate\DataManage\Storage\Sqlite\SqliteRelate.cs —— D3 审计面不完整。"; exit 2
}

# ── 委托执行（捕获退出码 + 输出文本；既有脚本保持唯一权威，本脚本只读其结论） ──
# 注：既有门禁脚本经 Write-Host 输出（PowerShell 信息流 = 流 6），须 6>&1 才能纳入捕获。
# 委托目标按名在 DSL\ 全域递归发现（与 Invoke-GateCheck 同口径；分类到 <Domain>\ 子目录后仍可达）。
function Invoke-DelegateGate {
    param([string]$Script, [hashtable]$Parameters)
    $dslRoot = Get-MappedPath -Key 'DSL'
    $hit = Get-ChildItem -LiteralPath $dslRoot -Recurse -Filter $Script -File -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if (-not $hit) { throw "门禁脚本缺失: $Script（在 $dslRoot 递归未找到）" }
    $lines = & $hit.FullName @Parameters 6>&1 2>&1 | Out-String
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

# 冻结基线派生（单源 = $ledger.frozenBaseline；字段名对齐台账，SWViolation ← SingleWriterViolations）
$fb = $ledger.frozenBaseline
if ($null -eq $fb) { Write-Error "归属台账缺 frozenBaseline（D1–D4 冻结基线单源）: $ledgerPath"; exit 2 }
$frozen = [ordered]@{
    K10Hits     = [int]$fb.K10Hits
    K10Files    = [int]$fb.K10Files
    K9Pending   = [int]$fb.K9Pending
    K9Violation = [int]$fb.K9Violation
    G1Hits      = [int]$fb.G1Hits
    G2Violation = [int]$fb.G2Violation
    SWViolation = [int]$fb.SingleWriterViolations
}

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
$files = @(Get-GateFiles -Root $agentSrc -Extension @('cs') -Exclude '\\(obj|bin|BuildOutput)\\' |
    Where-Object { $_ -notlike '*\_deprecated\*' } |
    ForEach-Object { [System.IO.FileInfo]::new($_) })
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


# ── [D6] 逻辑面不落数据根（框架层判据：声明单源 / 解析单实现 / 数据根零逻辑树 / 开发面隔离） ──
$d6Violations = [System.Collections.Generic.List[string]]::new()
$dataRoot = if (-not [string]::IsNullOrWhiteSpace($env:WF_DATA_ROOT)) { $env:WF_DATA_ROOT } else { Join-Path $Root 'Data' }

# D6a 解析单实现：数据根逻辑目录只允许经层适配器（ConfigPathCandidates/AgentDataRootLayout）访问
$d6aAllowedFiles = @('AgentDataRootLayout.cs', 'ConfigPathCandidates.cs', 'ConfigSourceResolver.cs', 'AgentAssetDeclaration.cs')
foreach ($file in $files) {
    if ($d6aAllowedFiles -contains $file.Name) { continue }
    $rel = $file.FullName.Substring($Root.Length).TrimStart('\')
    $lines = Get-Content $file.FullName -ErrorAction SilentlyContinue
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match 'AgentDataRootLayout\.(ConfigDir|SkillsDir)') {
            $d6Violations.Add(("[D6a] 数据根逻辑目录被旁路访问: {0}:{1}" -f $rel, ($i + 1)))
        }
    }
}

# D6b 数据根零逻辑树（迁移完成后硬判据；迁移期可用 -AllowDataRootLogic 显式降级）
foreach ($logic in @('Config', 'skills')) {
    $logicPath = Join-Path $dataRoot (Join-Path 'Agent' $logic)
    if ((Test-Path -LiteralPath $logicPath) -and -not $AllowDataRootLogic) {
        $d6Violations.Add(("[D6b] 数据根承载逻辑树（真源应为源码仓/发布资产）: {0}" -f $logicPath))
    }
}

# D6c 资产声明一致：plugin.json assets 每项在源码树存在，且覆盖 Config / skills
$manifestPath = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Launcher\plugin.json'
$declaredAssets = @()
if (Test-Path -LiteralPath $manifestPath) {
    try {
        $manifestJson = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        $declaredAssets = @($manifestJson.assets)
    } catch {
        $d6Violations.Add("[D6c] plugin.json 无法解析（资产声明不可判）: $manifestPath")
    }
    if ($declaredAssets.Count -eq 0) {
        $d6Violations.Add("[D6c] plugin.json 未声明 assets（发布态无资产可读）")
    }
    foreach ($asset in $declaredAssets) {
        $name = Split-Path -Leaf ([string]$asset).TrimEnd('/', '\')
        if ([string]::IsNullOrWhiteSpace($name)) { continue }
        $sourceDir = Join-Path $Root (Join-Path 'Projects\Plug\Agent' $name)
        if (-not (Test-Path -LiteralPath $sourceDir)) {
            $d6Violations.Add(("[D6c] 声明资产在源码树缺失: {0}（{1}）" -f $asset, $sourceDir))
        }
    }
    foreach ($required in @('Config', 'skills')) {
        $hit = @($declaredAssets | Where-Object { (Split-Path -Leaf ([string]$_).TrimEnd('/', '\')) -eq $required })
        if ($hit.Count -eq 0) { $d6Violations.Add(("[D6c] plugin.json assets 缺逻辑资产声明: {0}" -f $required)) }
    }
} else {
    $d6Violations.Add("[D6c] 未找到插件清单: $manifestPath")
}

# D6d 开发面隔离：BuildOutput 不得携带 assets（否则开发态读陈旧资产、遮蔽源码仓真源）
$buildOutput = Join-Path $Root 'Projects\Plug\Agent\BuildOutput'
if (Test-Path -LiteralPath $buildOutput) {
    $shadow = @(Get-ChildItem -LiteralPath $buildOutput -Directory -Recurse -Depth 2 -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -eq 'assets' })
    foreach ($s in $shadow) {
        $d6Violations.Add(("[D6d] 开发产物面携带 assets（会遮蔽源码仓真源）: {0}" -f $s.FullName))
    }
}

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
# D6
Write-Host '--- [D6] 逻辑面不落数据根（声明单源 / 解析单实现 / 数据根零逻辑树 / 开发面隔离） ---'
Write-Host ("数据根: {0} | 声明资产: {1} | 违规: {2}" -f $dataRoot, ($declaredAssets -join ', '), $d6Violations.Count)
if ($d6Violations.Count -gt 0) {
    $d6Violations | ForEach-Object { Write-Host ("  {0}" -f $_) }
    $fail.Add("D6 逻辑面违规 $($d6Violations.Count) 处（数据根不得承载逻辑；发布资产须与 plugin.json 声明一致）")
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

