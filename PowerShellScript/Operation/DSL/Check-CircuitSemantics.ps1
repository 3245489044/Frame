#requires -Version 5.1
<#
.SYNOPSIS
    电路语义守卫（CircuitSemantics）—— 防止在 CircuitFramework 内新增平行引用抽象、要求唯一宿主接线。

.DESCRIPTION
    CS1 唯一宿主接线：CircuitFramework.csproj 必须引用 UniversalWorkflowFramework（引用宿主）。
    CS2 禁新增平行抽象（负对照）：CircuitFramework 源码内禁止定义 `*ReferenceManager / *ReferenceService /
        *RelationshipService / *GraphService` 之类接口/类（唯一宿主铁律）。
    CS3 引用符号在册（正对照）：`ReferenceRelation` 五种引用语义（Owns/Calls/Depends/Provides/Consumes）
        在 ReferenceSymbol.cs 中齐备。

    模式：默认报告模式；-Check 校验模式（违归 → 退出码 1）。纯结构读检查，无外部基线依赖。

.PARAMETER Root
    仓库根目录（默认由本脚本位置上溯四级推导）。
.PARAMETER Check
    校验模式：违归 → 退出码 1。
.PARAMETER ShowDetail
    输出逐命中点明细。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$ShowDetail
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root))
{
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
}

$violations = [System.Collections.Generic.List[string]]::new()

Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  电路语义守卫（唯一宿主接线 / 禁平行引用抽象 / 符号在册）" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan

$cfDir = Join-Path $Root 'Frame\CsharpPrj\Share\Frameworks\CircuitFramework'
if (-not (Test-Path $cfDir))
{
    Write-Host "[FAIL] 未找到 CircuitFramework 工程目录：$cfDir" -ForegroundColor Yellow
    if ($Check) { exit 1 }
    exit 0
}

# ---------- CS1 唯一宿主接线 ----------
$csproj = Join-Path $cfDir 'CircuitFramework.csproj'
$csprojText = Get-Content -LiteralPath $csproj -Raw -Encoding UTF8
$cs1Ok = $csprojText -match 'UniversalWorkflowFramework'
Write-Host ("  CS1 唯一宿主接线（引用 UniversalWorkflowFramework）：{0}" -f $(if ($cs1Ok) { 'OK' } else { '缺失' })) -ForegroundColor Gray
if (-not $cs1Ok)
{
    $violations.Add("[CS1] CircuitFramework.csproj 未引用 UniversalWorkflowFramework（引用唯一宿主未接线）")
}

# ---------- CS2 禁新增平行引用抽象（负对照） ----------
$allCs = @(Get-ChildItem -Path $cfDir -Recurse -Filter *.cs -File -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' })
$paraPattern = '(?i)(interface|class)\s+(I?[A-Za-z]*(ReferenceManager|ReferenceService|RelationshipService|GraphService))\b'
$cs2Hits = @($allCs | ForEach-Object {
    $m = Select-String -Path $_.FullName -Pattern $paraPattern -AllMatches -ErrorAction SilentlyContinue
    if ($m) { $m }
    } | Select-Object -Unique Path)
$cs2Actual = $cs2Hits.Count
Write-Host ("  CS2 平行引用抽象定义（负对照）：{0} 处（须为 0）" -f $cs2Actual) -ForegroundColor Gray
if ($cs2Actual -gt 0)
{
    $violations.Add("[CS2] 在 CircuitFramework 内定义平行引用抽象 $cs2Actual 处（唯一宿主铁律：关系必须经 UniversalReferenceProjector→ReferenceSymbol，禁止 I*ReferenceManager 等平行门面）")
}
if ($ShowDetail) { foreach ($h in $cs2Hits) { Write-Host ("    {0}" -f ($h -replace [regex]::Escape($Root + '\'), '')) -ForegroundColor DarkGray } }

# ---------- CS3 引用符号在册（正对照） ----------
$refSym = Join-Path $cfDir 'Symbols\ReferenceSymbol.cs'
if (-not (Test-Path $refSym))
{
    $violations.Add("[CS3] 缺失 Symbols\ReferenceSymbol.cs（引用符号未在册）")
}
else
{
    $refText = Get-Content -LiteralPath $refSym -Raw -Encoding UTF8
    $expectedRels = 'ReferenceRelation.Owns','ReferenceRelation.Calls','ReferenceRelation.Depends',
                    'ReferenceRelation.Provides','ReferenceRelation.Consumes'
    foreach ($r in $expectedRels)
    {
        if ($refText -match [regex]::Escape($r)) { continue }
        $violations.Add("[CS3] ReferenceSymbol 缺失引用关系语义定义：$r")
    }
    if ($violations.Count -eq 0) { Write-Host "  CS3 引用符号在册（5 种引用关系）：OK" -ForegroundColor Gray }
}

if ($violations.Count -eq 0)
{
    Write-Host ""
    Write-Host "[OK] 电路语义守卫：0 违归（唯一宿主已接线 / 无平行引用抽象 / 引用符号齐备）" -ForegroundColor Green
    exit 0
}

Write-Host ""
Write-Host "[FAIL] 电路语义守卫命中 $($violations.Count) 处：" -ForegroundColor Yellow
foreach ($v in $violations) { Write-Host "  - $v" -ForegroundColor Yellow }
if ($Check) { exit 1 }
exit 0