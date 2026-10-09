#requires -Version 5.1
<#
.SYNOPSIS
    节点键地址守卫（NodeKeyAddress）—— 「位置键字段 ⊆ 身份坐标 ∪ 位置 local 坐标」的机器判据。

.DESCRIPTION
    扫描面：Projects\Plug\Agent\Config\nodes\*.json（节点域声明：identity / context / sequence / placements）
            + Config\instances\nodes.json（实例位置 id 清单：与域声明交叉一致）。

    判据（权威实现 = Frame\CsharpPrj\Modules\Node\Spec\NodeSpecValidator.Validate；本脚本为**可独立运行的等价门**，
    该判据此前在生产装配路径零调用 —— 本轮已由 AgentNodeDomains.TryCreate 接线为装配期 fail-fast）：
      A1 位置键字段全域：每个 placement.key 的 {占位符} ⊆ identity 占位符 ∪ placement.local（缺一即违规）
      A2 local 自洽：local 项不得与身份占位符重名（同坐标两名）；声明却未出现在键模板 ⇒ 违规（死声明）
      A3 身份坐标声明齐备：identity 的非序数占位符须列入 context；sequence 项须出现在 identity（隐式坐标禁入）
      A4 同域键模板可区分：占位符匿名化后两两不同（同形不同名 = 同一键空间 ⇒ 违规）
      A5 单写者多落点（提示）：同域 writer 复用时可见化（非违规；「位置 → 写者唯一」由 PlacementId 主键保证）
      A6 实例清单一致：Config\instances\nodes.json 的 placements[] 全部落在域声明的位置 id 并集内
      A7 正对照（防假绿）：位置声明数 ≥ 基线（声明被删而门禁恒绿 = 失效）
      A8 跨域键可区分（**可见化，只报不拦**）：不同域的位置键模板不得逐字同形（同形 ⇒ 同一 NodeKey
         被两域声明占用 = 后写覆盖 + NodeType 漂移）；当前 ERR-3 三项为已知处置中，列待消除不阻断

    模式：默认报告模式（列命中，不设退出码）；-Check 校验模式（违规或正对照失效 → 退出码 1）。

.PARAMETER Root
    仓库根目录（缺省由本脚本位置上溯四级推导：Frame\PowerShellScript\Modules\Node → 仓库根）。

.PARAMETER Check
    校验模式：存在违规（A1–A6）或正对照失效（A7）→ 退出码 1。

.PARAMETER ShowDetail
    输出逐项实测计数（含达标项）。

.EXAMPLE
    .\Check-NodeKeyAddress.ps1 -ShowDetail

.EXAMPLE
    .\Check-NodeKeyAddress.ps1 -Check
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

# U9：域声明真源已上移 .ushar（Config\nodes），原 JSON 迁移为**冻结基线**；本门禁对冻结基线执行同一判据。
$declarationDir = Join-Path $Root 'Environment\CentralTest\UsharpTests\Usharp.Emit.Tests\Fixtures\baselines'
$instanceFile   = Join-Path $Root 'Projects\Plug\Agent\Config\instances\nodes.json'

$violations = [System.Collections.Generic.List[string]]::new()
$pending    = [System.Collections.Generic.List[string]]::new()

Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  节点键地址守卫（位置键字段域 / local 自洽 / 同域可区分 / writer 一对一）" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan

if (-not (Test-Path -LiteralPath $declarationDir)) {
    Write-Host "[FAIL] 未找到节点域声明目录：$declarationDir" -ForegroundColor Yellow
    if ($Check) { exit 1 }
    exit 0
}
# ---------------- 解析 helpers ----------------
# 占位符提取（容忍 {name:shape} 形态；唯一词法权威 = Modules.Parsing.Keys.KeyTemplate）
function Get-Placeholders {
    param([string]$Template)
    if ([string]::IsNullOrEmpty($Template)) { return @() }
    $found = [regex]::Matches($Template, '\{([A-Za-z_][A-Za-z0-9_]*)(?::[A-Za-z]+)?\}')
    return @($found | ForEach-Object { $_.Groups[1].Value })
}

# 形状匿名化（占位符 → *）：同形不同名判为不可区分（与 KeyTemplates.IsDistinguishable 同口径）
function Get-ShapePattern {
    param([string]$Template)
    if ([string]::IsNullOrEmpty($Template)) { return '' }
    return [regex]::Replace($Template, '\{[A-Za-z_][A-Za-z0-9_]*(?::[A-Za-z]+)?\}', '*')
}

function Get-Text {
    param($Node, [string]$Name)
    if ($null -eq $Node) { return $null }
    if ($Node.PSObject.Properties.Name -notcontains $Name) { return $null }
    $value = $Node.$Name
    if ($null -eq $value) { return $null }
    if ($value -is [string]) { return $value }
    return [string]$value
}

# local 声明读取：字符串数组或逗号/分号分隔字符串
function Get-LocalCoordinates {
    param($Placement)
    if ($Placement.PSObject.Properties.Name -notcontains 'local') { return @() }
    $value = $Placement.local
    if ($null -eq $value) { return @() }
    if ($value -is [string]) {
        if ([string]::IsNullOrWhiteSpace($value)) { return @() }
        return @($value -split '[,;]' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    }
    return @($value | ForEach-Object { [string]$_ } | Where-Object { $_ })
}
# ---------------- 逐声明校验（A1–A5） ----------------
$files = @(Get-ChildItem -LiteralPath $declarationDir -Filter *.json -File | Sort-Object Name)
$placementCount = 0
$domainCount = 0
$declaredPlacementIds = [System.Collections.Generic.HashSet[string]]::new()
# A8 跨域键形状收集（可见化用；跨域同形 = 同一 NodeKey 被两域声明占用 ⇒ ERR-3）
$keyShapes = [System.Collections.Generic.List[object]]::new()

foreach ($file in $files) {
    $doc = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
    $domain = Get-Text $doc 'domain'
    $identity = Get-Text $doc 'identity'
    if ([string]::IsNullOrWhiteSpace($domain) -or [string]::IsNullOrWhiteSpace($identity)) {
        $violations.Add("[A0] $($file.Name)：缺少 domain 或 identity（身份模板是地址声明的第一要素）")
        continue
    }

    $domainCount++
    $identityFields = @(Get-Placeholders $identity)
    $sequenceFields = @()
    if ($doc.PSObject.Properties.Name -contains 'sequence' -and $null -ne $doc.sequence) {
        $sequenceFields = @($doc.sequence | ForEach-Object { [string]$_ })
    }
    $contextFields = @()
    if ($doc.PSObject.Properties.Name -contains 'context' -and $null -ne $doc.context) {
        $contextFields = @($doc.context | ForEach-Object { [string]$_ })
    }

    # A3 身份坐标声明齐备（非序数占位符须在 context；sequence 项须出现在 identity）
    foreach ($field in $identityFields) {
        if ($sequenceFields -contains $field) { continue }
        if ($contextFields -notcontains $field) {
            $violations.Add("[A3] $($file.Name)：身份坐标 '$field' 未列入 context（隐式坐标禁入）")
        }
    }
    foreach ($field in $sequenceFields) {
        if ($identityFields -notcontains $field) {
            $violations.Add("[A3] $($file.Name)：sequence 项 '$field' 未出现在 identity")
        }
    }

    $placements = @($doc.placements)
    foreach ($placement in $placements) {
        $placementCount++
        $placementId = Get-Text $placement 'id'
        $keyTemplate = Get-Text $placement 'key'
        if ([string]::IsNullOrWhiteSpace($placementId)) { continue }
        [void]$declaredPlacementIds.Add($placementId)
        if ([string]::IsNullOrWhiteSpace($keyTemplate)) {
            $violations.Add("[A1] $($file.Name)：位置 '$placementId' 缺少 key")
            continue
        }

        $locals = @(Get-LocalCoordinates $placement)
        $keyFields = @(Get-Placeholders $keyTemplate)

        # A8 跨域可区分性取数（形状匿名化后跨域比对；判定在文件循环后）
        $keyShapes.Add([pscustomobject]@{
            Domain = $domain
            Id     = $placementId
            Key    = $keyTemplate
            Shape  = (Get-ShapePattern $keyTemplate)
        })

        # A1 位置键字段全域 = 身份占位符 ∪ local
        foreach ($field in $keyFields) {
            if (($identityFields -contains $field) -or ($locals -contains $field)) { continue }
            $violations.Add("[A1] $($file.Name)：位置 '$placementId' 键模板字段 '$field' 未声明（既非身份占位符，也未列入 local）")
        }

        # A2 local 自洽
        foreach ($local in $locals) {
            if ($identityFields -contains $local) {
                $violations.Add("[A2] $($file.Name)：位置 '$placementId' 的 local '$local' 与身份占位符重名（局部坐标不得遮蔽身份坐标）")
            }
            elseif ($keyFields -notcontains $local) {
                $violations.Add("[A2] $($file.Name)：位置 '$placementId' 的 local '$local' 未出现在键模板（死声明）")
            }
        }
    }

    # A4 同域键模板可区分（形状匿名化后两两不同）
    for ($i = 0; $i -lt $placements.Count; $i++) {
        for ($j = $i + 1; $j -lt $placements.Count; $j++) {
            $left  = Get-Text $placements[$i] 'key'
            $right = Get-Text $placements[$j] 'key'
            if ([string]::IsNullOrWhiteSpace($left) -or [string]::IsNullOrWhiteSpace($right)) { continue }
            if ((Get-ShapePattern $left) -eq (Get-ShapePattern $right)) {
                $leftId = Get-Text $placements[$i] 'id'
                $rightId = Get-Text $placements[$j] 'id'
                $violations.Add("[A4] $($file.Name)：位置 '$leftId' / '$rightId' 键模板不可区分（同形 ⇒ 同一键空间）")
            }
        }
    }

    # A5 单写者多落点（**提示**，非违规）：同域 writer 复用 = 一次物化的多个落点（段域既有设计：
    #    segment-node / segment-context 共用 writer-segment）；「位置 → 写者唯一」由 PlacementId 主键保证。
    #    权威判据 = NodeSpecValidator 的 spec.writer-shared（Warning，不阻断装配）。
    $writers = @($placements | ForEach-Object { Get-Text $_ 'writer' } | Where-Object { $_ })
    foreach ($group in @($writers | Group-Object | Where-Object { $_.Count -gt 1 })) {
        $pending.Add("[A5] $($file.Name)：writer '$($group.Name)' 被 $($group.Count) 个位置共用（单写者多落点，须同属一次物化）")
    }

    if ($ShowDetail) {
        Write-Host ("  {0,-18} identity={1,-30} 位置={2}  identity=({3}) context=({4}) seq=({5})" -f `
            $file.Name, $identity, $placements.Count, ($identityFields -join ','), ($contextFields -join ','), ($sequenceFields -join ',')) -ForegroundColor Gray
    }
}
# ---------------- A8 跨域键可区分（**可见化，只报不拦**） ----------------
# 同域可区分由 A4 拦（NodeKey 幂等 ⇒ 同键覆盖）。**跨域**同形（域不同而键模板逐字同形）⇒ 两个域各自写
# 同一个 NodeKey（后写覆盖 + NodeType 漂移）。当前 = 已知处置中（ERR-3：session-view / session-message /
# segment-context 三者同形，收敛归 W4 / `session-message` 退役），故只列待消除（$pending）不阻断（$violations）；
# 一旦**越界新增**跨域同形，本项立即可见（防静默扩散）。
for ($i = 0; $i -lt $keyShapes.Count; $i++) {
    for ($j = $i + 1; $j -lt $keyShapes.Count; $j++) {
        $left  = $keyShapes[$i]
        $right = $keyShapes[$j]
        if ($left.Domain -eq $right.Domain) { continue }
        if ($left.Shape -ne $right.Shape) { continue }
        $pending.Add("[A8] 跨域键同形：'$($left.Domain)/$($left.Id)' 与 '$($right.Domain)/$($right.Id)' 键模板逐字同形（$($left.Key)）—— 同一 NodeKey 跨域塌缩（ERR-3，归 W4 收敛）")
    }
}

# ---------------- A6 实例清单一致 ----------------
if (Test-Path -LiteralPath $instanceFile) {
    $instances = Get-Content -LiteralPath $instanceFile -Raw | ConvertFrom-Json
    $idList = @()
    if ($instances.PSObject.Properties.Name -contains 'placements' -and $null -ne $instances.placements) {
        $idList = @($instances.placements | ForEach-Object { [string]$_ })
    }
    foreach ($id in $idList) {
        if (-not $declaredPlacementIds.Contains($id)) {
            $violations.Add("[A6] instances\nodes.json：位置 id '$id' 未在任何域声明（悬空位置引用）")
        }
    }
    if ($ShowDetail) {
        Write-Host ("  {0,-18} 位置引用={1}（域声明并集={2}）" -f 'instances\nodes.json', $idList.Count, $declaredPlacementIds.Count) -ForegroundColor Gray
    }
}

# ---------------- A7 正对照（防假绿） ----------------
# 基线单源 = Baselines\NodeKeyAddress.psd1（禁内联数值基线）
Import-Module (Join-Path $PSScriptRoot '..\..\Baselines\GateBaseline.psm1') -Force
$g = Import-GateBaseline -Name NodeKeyAddress
$a7Baseline = [int]$g.PlacementCount.Value   # 2026-09-27 实测：5 域 9 位置
if ($placementCount -lt $a7Baseline) {
    $violations.Add("[A7] 位置声明数 $placementCount < 基线 $a7Baseline（声明被删或未装载 ⇒ 判据失效，禁假绿）")
}
elseif ($placementCount -gt $a7Baseline) {
    $pending.Add("[A7] 位置声明数 $placementCount > 基线 $a7Baseline（新增位置须同步上调基线）")
}

# ---------------- 汇总 ----------------
Write-Host ""
Write-Host ("  实测：域 {0} / 位置 {1} / 位置 id 并集 {2}" -f $domainCount, $placementCount, $declaredPlacementIds.Count) -ForegroundColor Gray

if ($ShowDetail -and $pending.Count -gt 0) {
    Write-Host "[NKA] --- 基线待调整（无新增即通过） ---" -ForegroundColor DarkGray
    foreach ($item in $pending) { Write-Host "  - $item" -ForegroundColor DarkGray }
}

if ($violations.Count -eq 0) {
    Write-Host "[OK] 节点键地址守卫：0 违规（位置键字段全部落在「身份坐标 ∪ local」内）" -ForegroundColor Green
    exit 0
}

Write-Host "[FAIL] 节点键地址守卫命中 $($violations.Count) 处：" -ForegroundColor Yellow
foreach ($item in $violations) { Write-Host "  - $item" -ForegroundColor Yellow }
Write-Host "  修复口径：位置键字段须为身份占位符或显式声明 placement.local；identity 坐标须列入 context；" -ForegroundColor Gray
Write-Host "            权威实现 = Modules\Node\Spec\NodeSpecValidator.Validate（装配期 fail-fast，见 AgentNodeDomains.TryCreate）。" -ForegroundColor Gray

if ($Check) { exit 1 }
exit 0



