#requires -Version 5.1
<#
.SYNOPSIS
    节点层级引用声明守卫（NodeHierarchyRefs）—— 「归属边事实只在声明面」的机器判据（W1）。

.DESCRIPTION
    沿《节点域全量工作流化（声明驱动）总体方案》§5 W1：归属边（task↔node / history→task / session→node /
    task→sub-task）的「边类型事实」只住 `Config\nodes\hierarchy-refs.ushar`；代码只算不记（写侧按声明白名单准入）。

    扫描面：
      Projects\Plug\Agent\Config\nodes\hierarchy-refs.ushar      （声明面：边类型集 + 逐条 [Ref]）
      Frame\CsharpPrj\...\Abstractions\ReferenceTypes.cs            （边类型常量唯一字面量源，A1）
      Frame\CsharpPrj\...\Core\ReferenceGrounding.cs             （写边唯一 owner 实测）

    判据：
      E1 闸A 编译：声明面经 Usharp.Cli build ⇒ 错误 0（未声明坐标 / 悬空引用 / 形状非法即拦）
      E2 边类型集合**双向对账**：声明 `edge=` 集 ⇄ 代码实测用到集（唯一字面量源 `ReferenceTypes` 常量值映射；
                           `CrossDbReferenceTypes` 已退化为别名门面，不再承载字面量）
                           E2a 声明 ⊆ 代码（无死声明：声明的边必须有实现）
                           E2b 代码 ⊆ 声明（无未声明边：实现的边必须声明；对齐 W-1 / W-3）
      E2c 退役常量债务基线（只减不增）：`HistoryOwnsNode` 写用法 ≤ 基线（退役 = 清理存留写点后删本行）
      E3 `[Ref]` **字段白名单**：字段键 ∈ {space,key,src,tgt,intent,edge,scope}
                           （治「未注册字段被编译器静默忽略 ⇒ 假声明」，见 R3 实测）
      E4 正对照（防假绿）：声明边类型数 ≥ 基线 ∧ 常量数 ≥ 基线 ∧ 字段面命中数 ≥ 基线
      E5 负对照（防假绿）：① 未注册字段 `loc=` 必须被 E3 判据命中（判据非恒绿）；
                           ② 闸A 对 `loc=` 报 **0 错**（锁死 R3 实测：闸A 不拦未注册字段 ⇒ E3 是唯一防线）

    模式：默认报告模式（列命中，不设退出码）；-Check 校验模式（违规或正对照失效 → 退出码 1）。

.PARAMETER Root
    仓库根目录（缺省由本脚本位置上溯四级推导：Frame\PowerShellScript\Modules\Node → 仓库根）。

.PARAMETER Check
    校验模式：存在违规（E1–E5）→ 退出码 1。

.PARAMETER ShowDetail
    输出逐项实测计数（含达标项）。

.EXAMPLE
    .\Check-NodeHierarchyRefs.ps1 -ShowDetail

.EXAMPLE
    .\Check-NodeHierarchyRefs.ps1 -Check
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$ShowDetail
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
}

# ---------------- 基线（只减不增；变更须给出理由并同步本表） ----------------
$declarationFile = Join-Path $Root 'Projects\Plug\Agent\Config\nodes\hierarchy-refs.ushar'
# 边类型常量**唯一字面量源**（A1）：`ReferenceTypes` 承载字面量；`CrossDbReferenceTypes` / `WorkflowReferenceTypes` /
#   `NodeResourceTypes` / LoopFoldMethod.Reference.Model.ReferenceTypes 均为其别名门面（const 转发，无字面量）。
$constsFile      = Join-Path $Root 'Frame\CsharpPrj\Operation\DataStoreDriven\Abstractions\ReferenceTypes.cs'
$groundingFile   = Join-Path $Root 'Frame\CsharpPrj\Operation\DataStoreDriven\Core\Domain\ReferenceGrounding.cs'   # 目录分组（DS-P4a-pre）：判据不变，仅路径改指
$cliExe          = Join-Path $Root 'Frame\Usharp\BuildOutput\Debug\Usharp.Cli.exe'
$cliProj         = Join-Path $Root 'Frame\Usharp\src\Usharp.Cli\Usharp.Cli.csproj'

# 基线单源 = Baselines\NodeHierarchyRefs.psd1（禁内联数值基线）
Import-Module (Join-Path $PSScriptRoot '..\..\Baselines\GateBaseline.psm1') -Force
$g = Import-GateBaseline -Name NodeHierarchyRefs
# 声明面边类型数下限（W1 实测收敛 = 4；追加须同批更新本基线并给出理由）
$DeclaredEdgeCountBaseline = [int]$g.DeclaredEdgeCount.Value
# 边类型常量数下限（防常量文件被清空 ⇒ 对账恒真）
$ConstQuantityBaseline     = [int]$g.ConstQuantity.Value
# `[Ref]` 字段白名单（= UsharpLanguage 已注册字段；未注册字段被编译器静默忽略，禁写）
$RefFieldWhitelist = @('space', 'key', 'src', 'tgt', 'intent', 'edge', 'scope')
# 退役常量的写用法债务基线（只减不增）：常量名 → 允许的写用法计数。
# W1 已清零：`HistoryOwnsNode` 写点（ReferenceGrounding.AttachHistoryNodeAsync）已退役（改为抛 + 派生视图）
# ⇒ 不设债务项；新增退役项须在此登记并在清理后删除该行（禁「永续并存」）。
$DeprecatedWriteDebt = @{}

$violations = [System.Collections.Generic.List[string]]::new()

Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  节点层级引用声明守卫（W1：归属边事实只在声明面）" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan

if (-not (Test-Path -LiteralPath $declarationFile)) {
    Write-Host "[FAIL] 未找到归属边声明面：$declarationFile" -ForegroundColor Yellow
    if ($Check) { exit 1 }
    exit 0
}

# ---------------- 编译取数（唯一实现 = Usharp 语言编译器；本脚本不复制闸A） ----------------
function Invoke-UsharpBuild {
    param([string]$Path)
    if (Test-Path -LiteralPath $cliExe) {
        return (& $cliExe build $Path 2>&1 | Out-String)
    }
    if (Test-Path -LiteralPath $cliProj) {
        return (& dotnet run --project $cliProj -- build $Path 2>&1 | Out-String)
    }
    throw "未找到 Usharp 编译器（$cliExe 或 $cliProj）：闸A 判据 E1/E5 不可判，禁静默通过"
}

# ---------------- 字段扫描（E3/E5 共用；返回 @{ Line; Key }[]） ----------------
function Get-RefFieldNames {
    param([string]$Path)
    $fields = [System.Collections.Generic.List[object]]::new()
    $index = 0
    foreach ($raw in (Get-Content -LiteralPath $Path)) {
        $index++
        $line = $raw.Trim()
        if (-not $line.StartsWith('[Ref]')) { continue }
        foreach ($m in [regex]::Matches($line, '\|\s*(?<k>[A-Za-z_][A-Za-z0-9_]*)\s*=')) {
            $fields.Add([pscustomobject]@{ Line = $index; Key = $m.Groups['k'].Value })
        }
    }
    return $fields
}

# ---------------- 声明面取数（边类型集 + 字段面） ----------------
$declaredEdges = [System.Collections.Generic.List[string]]::new()
$fieldHits = [System.Collections.Generic.List[object]]::new()
$index = 0
foreach ($raw in (Get-Content -LiteralPath $declarationFile)) {
    $index++
    $line = $raw.Trim()
    if ($line.StartsWith('#')) { continue }
    foreach ($m in [regex]::Matches($line, '\|\s*edge\s*=\s*(?<v>[^|\s]+)')) {
        $value = $m.Groups['v'].Value.Trim()
        if (-not $declaredEdges.Contains($value)) { $declaredEdges.Add($value) }
    }
    foreach ($m in [regex]::Matches($line, '\|\s*(?<k>[A-Za-z_][A-Za-z0-9_]*)\s*=')) {
        $fieldHits.Add([pscustomobject]@{ Line = $index; Key = $m.Groups['k'].Value })
    }
}

# ---------------- E1 闸A 编译 ----------------
$buildText = ''
try {
    $buildText = Invoke-UsharpBuild -Path $declarationFile
} catch {
    $violations.Add("[E1] 闸A 不可执行：$($_.Exception.Message)")
}
$errorCount = -1
if ($buildText -match '错误\s*(?<n>\d+)') { $errorCount = [int]$Matches['n'] }
if ($errorCount -lt 0) {
    $violations.Add('[E1] 闸A 未取得编译错误计数（声明面不可判，禁静默通过）')
} elseif ($errorCount -ne 0) {
    $violations.Add("[E1] 声明面闸A 编译报 $errorCount 处错误（未声明坐标 / 悬空引用 / 形状非法）")
}

# ---------------- E2 边类型集合双向对账 ----------------
$constMap = @{}
if (-not (Test-Path -LiteralPath $constsFile)) {
    $violations.Add("[E2] 未找到边类型常量单源：$constsFile")
} else {
    foreach ($m in [regex]::Matches((Get-Content -LiteralPath $constsFile -Raw),
            'public\s+const\s+string\s+(?<n>\w+)\s*=\s*"(?<v>[^"]+)"')) {
        $constMap[$m.Groups['n'].Value] = $m.Groups['v'].Value
    }
}

$usedByConst = @{}
if (-not (Test-Path -LiteralPath $groundingFile)) {
    $violations.Add("[E2] 未找到写边唯一 owner：$groundingFile")
} else {
    foreach ($m in [regex]::Matches((Get-Content -LiteralPath $groundingFile -Raw),
            'CrossDbReferenceTypes\.(?<n>\w+)')) {
        $name = $m.Groups['n'].Value
        if (-not $usedByConst.ContainsKey($name)) { $usedByConst[$name] = 0 }
        $usedByConst[$name]++
    }
}

$codeEdges = [System.Collections.Generic.List[string]]::new()
foreach ($name in $usedByConst.Keys) {
    if ($DeprecatedWriteDebt.ContainsKey($name)) { continue }   # 退役常量不入对账（见 E2c）
    if ($constMap.ContainsKey($name)) {
        if (-not $codeEdges.Contains($constMap[$name])) { $codeEdges.Add($constMap[$name]) }
    } else {
        $violations.Add("[E2] 写边用到未登记常量 CrossDbReferenceTypes.$name（常量单源缺项）")
    }
}

foreach ($edge in $declaredEdges) {
    if (-not $codeEdges.Contains($edge)) {
        $violations.Add("[E2a] 声明了边类型 '$edge' 但写边实测未用到（死声明：声明的边必须有实现）")
    }
}
foreach ($edge in $codeEdges) {
    if (-not $declaredEdges.Contains($edge)) {
        $violations.Add("[E2b] 写边实测用到边类型 '$edge' 但声明面未声明（未声明边：禁静默建边）")
    }
}

# ---------------- E2c 退役常量债务基线（只减不增） ----------------
foreach ($name in $DeprecatedWriteDebt.Keys) {
    $actual = if ($usedByConst.ContainsKey($name)) { $usedByConst[$name] } else { 0 }
    $limit = $DeprecatedWriteDebt[$name]
    if ($actual -gt $limit) {
        $violations.Add("[E2c] 退役常量 $name 写用法 $actual 处 > 债务基线 $limit（只减不增；退役须清理写点）")
    }
}

# ---------------- E3 `[Ref]` 字段白名单 ----------------
foreach ($hit in $fieldHits) {
    if ($RefFieldWhitelist -notcontains $hit.Key) {
        $violations.Add("[E3] 声明面第 $($hit.Line) 行使用了未注册的 [Ref] 字段 '$($hit.Key)='" +
                        "（未注册字段被编译器**静默忽略** ⇒ 假声明；白名单 = $($RefFieldWhitelist -join ',')）")
    }
}

# ---------------- E4 正对照（防假绿） ----------------
if ($declaredEdges.Count -lt $DeclaredEdgeCountBaseline) {
    $violations.Add("[E4] 正对照失效：声明边类型 $($declaredEdges.Count) < 基线 $DeclaredEdgeCountBaseline" +
                    "（声明被删而门禁恒绿 = 失效）")
}
if ($constMap.Count -lt $ConstQuantityBaseline) {
    $violations.Add("[E4] 正对照失效：边类型常量 $($constMap.Count) 个 < 基线 $ConstQuantityBaseline" +
                    "（常量单源被清空 ⇒ 对账恒真）")
}
if ((Get-RefFieldNames -Path $declarationFile).Count -lt $DeclaredEdgeCountBaseline) {
    $violations.Add('[E4] 正对照失效：声明面未取到字段面（`| k=` 命中数低于基线 ⇒ 字段判据 E3 恒绿）')
}

# ---------------- E5 负对照（防假绿） ----------------
$negativePath = Join-Path ([System.IO.Path]::GetTempPath()) 'usharp-neg-ref-edge-unregistered-field.ushar'
@'
[Space] HierarchyRef: kind=keys
  history_owns_task

[Ref] HistoryOwnsTask: HierarchyRef:history_owns_task | edge=history_owns_task | loc=agent-round
'@ | Set-Content -LiteralPath $negativePath -Encoding utf8

# ① E3 判据必须命中未注册字段（否则 E3 恒绿）
$negativeFields = Get-RefFieldNames -Path $negativePath
if (-not ($negativeFields | Where-Object { $_.Key -eq 'loc' })) {
    $violations.Add('[E5] 负对照失效：未注册字段 `loc=` 未被 E3 判据命中（判据恒绿）')
}

# ② 闸A 对未注册字段报 0 错（锁死 R3 实测结论：闸A 不拦未注册字段 ⇒ E3 是唯一防线）
$negativeText = ''
try {
    $negativeText = Invoke-UsharpBuild -Path $negativePath
} catch {
    $violations.Add("[E5] 负对照不可执行：$($_.Exception.Message)")
}
if ($negativeText -match '错误\s*(?<n>\d+)') {
    if ([int]$Matches['n'] -ne 0) {
        $violations.Add('[E5] 实测口径漂移：闸A 竟拦下未注册字段 `loc=` ⇒ 复核 R3 结论与 E3 必要性（勿留冗余判据）')
    }
} elseif ($negativeText) {
    $violations.Add('[E5] 负对照未取得编译错误计数（禁假绿）')
}
Remove-Item -LiteralPath $negativePath -Force -ErrorAction SilentlyContinue

# ---------------- 汇总 ----------------
Write-Host ""
Write-Host (("  实测：声明边类型 {0} / 代码实测边类型 {1} / 边类型常量 {2} / [Ref] 字段命中 {3}" -f `
    $declaredEdges.Count, $codeEdges.Count, $constMap.Count, $fieldHits.Count)) -ForegroundColor Gray

if ($ShowDetail) {
    Write-Host ("  声明边类型     : {0}" -f ($declaredEdges -join ',')) -ForegroundColor Gray
    Write-Host ("  代码实测边类型 : {0}" -f ($codeEdges -join ',')) -ForegroundColor Gray
    foreach ($name in $DeprecatedWriteDebt.Keys) {
        $actual = if ($usedByConst.ContainsKey($name)) { $usedByConst[$name] } else { 0 }
        Write-Host ("  退役债务（只减不增）: {0} = {1}/{2}" -f $name, $actual, $DeprecatedWriteDebt[$name]) -ForegroundColor DarkYellow
    }
}

if ($violations.Count -eq 0) {
    Write-Host ""
    Write-Host "[OK] 节点层级引用声明守卫：0 违规（归属边事实只在声明面；边类型集合双向对账一致）" -ForegroundColor Green
    exit 0
}

Write-Host ""
foreach ($item in $violations) { Write-Host "  $item" -ForegroundColor Yellow }
Write-Host ""
Write-Host "  修复口径：归属边真相 = Config\nodes\hierarchy-refs.ushar（[Ref] + edge=）；" -ForegroundColor Gray
Write-Host "            代码只算不记；未声明边类型 ⇒ 拒写 + 可见化（禁静默建边）。" -ForegroundColor Gray
Write-Host ""
Write-Host "[FAIL] 节点层级引用声明守卫：$($violations.Count) 处违规" -ForegroundColor Red
if ($Check) { exit 1 }
exit 0
