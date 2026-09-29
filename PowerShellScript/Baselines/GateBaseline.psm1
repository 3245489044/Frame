#requires -Version 5.1
<#
GateBaseline.psm1 —— 门禁基线装载与规范校验（纯原语，无判据语义）

设计：
  · 基线单源 = Frame\PowerShellScript\Baselines\<Gate>.psd1（纯数据）；脚本内禁内联数值基线；
  · 每条基线必带 Origin ∈ {Derived,Debt,Verdict}（四分事实，见 Baselines\README.md）；
  · 内联基线的**唯一定义** = Get-InlineBaselineRegex（守卫 U1 与负对照 U5 共用同一正则，防两套口径）。

用法（门禁脚本头部）：
    Import-Module (Join-Path $PSScriptRoot '..\..\Baselines\GateBaseline.psm1') -Force
    $b = Import-GateBaseline -Name <Gate>
#>

$script:BaselinesRoot = $PSScriptRoot

function Get-BaselinesRoot
{
    return $script:BaselinesRoot
}

<#
.SYNOPSIS
    装载单件门禁基线（缺文件即抛：禁隐式空基线导致判据空转）。
#>
function Import-GateBaseline
{
    param(
        [Parameter(Mandatory = $true)][string]$Name
    )

    $path = Join-Path $script:BaselinesRoot "$Name.psd1"
    if (-not (Test-Path -LiteralPath $path))
    {
        throw "门禁基线文件缺失：$path（禁内联基线常量；约定见 Baselines\README.md）"
    }
    return Import-PowerShellDataFile -LiteralPath $path
}

<#
.SYNOPSIS
    列出全部**单件**门禁基线文件（排除 `_` 前缀的清册/登记表）。
#>
function Get-BaselineFiles
{
    # 单根（SR-2a 已并入遗留子根：禁第二基线根 ⇒ 本函数不得再枚举其他根）
    $files = [System.Collections.Generic.List[object]]::new()
    foreach ($f in @(Get-ChildItem -LiteralPath $script:BaselinesRoot -Recurse -Filter '*.psd1' -File -ErrorAction SilentlyContinue))
    {
        if ($f.Name.StartsWith('_')) { continue }
        $files.Add($f)
    }
    return $files.ToArray()
}

<#
.SYNOPSIS
    校验基线条目类型（Origin 必填且受限；Debt/Verdict 必带 DueWhen）。
.OUTPUTS
    违规串列表（空 = 合规）。
#>
function Test-BaselineEntryTyped
{
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)]$Data
    )

    $allowed = @('Derived', 'Debt', 'Verdict')
    $violations = [System.Collections.Generic.List[string]]::new()

    if ($Data -isnot [System.Collections.IDictionary])
    {
        $violations.Add("[U3] $Name：基线根须为哈希表")
        return , $violations
    }

    # 条目 = 值中带 Origin 注记的哈希表（本表约定：每个事实行自带 Origin/DueWhen）
    foreach ($key in $Data.Keys)
    {
        $value = $Data[$key]
        $rows = if ($value -is [System.Collections.IDictionary]) { @($value) }
                elseif ($value -is [System.Collections.IEnumerable] -and $value -isnot [string]) { @($value) }
                else { @() }

        foreach ($row in $rows)
        {
            if ($row -isnot [System.Collections.IDictionary]) { continue }
            if (-not $row.Contains('Origin')) { continue }   # 非事实行（如 Schema/Notes）不计

            $origin = [string]$row['Origin']
            if ($origin -notin $allowed)
            {
                $violations.Add("[U3] $Name.$key：Origin='$origin' 非法（须 ∈ {Derived,Debt,Verdict}）")
                continue
            }

            $due = [string]$row['DueWhen']
            if ($origin -in @('Debt', 'Verdict') -and [string]::IsNullOrWhiteSpace($due))
            {
                $violations.Add("[U3] $Name.$key：Origin=$origin 缺 DueWhen（销账/重裁条件，禁无期债务）")
            }
        }
    }

    return , $violations
}

<#
.SYNOPSIS
    内联数值基线的唯一正则定义（守卫 U1 扫描面 + 负对照 U5 共用）。
.DESCRIPTION
    匹配「名字含 Baseline 的变量被赋十进制字面量」，如 `$k2Baseline = 5` / `$baseline = 0`。
    刻意**不**匹配「比较阈值」（如 `-ge 6`）：后者是判据本体，属判据载体治理（SR-4），不属基线清册。
#>
function Get-InlineBaselineRegex
{
    return '\$(?<name>[A-Za-z_][A-Za-z0-9_]*[Bb]aseline[A-Za-z0-9_]*)\s*=\s*(?<value>\d+)'
}

# 装载契约：**必须** `Import-Module <path> -Force`。
#   实测（SR-W1c 验证批）：点源 `. <path>.psm1` **不会**把函数带进调用方作用域 ⇒ 消费方报
#   「术语 'Get-InlineBaselineRegex' 不会被识别为 cmdlet、函数…」。故消费方一律 Import-Module。
#   因用 Import-Module，本件**禁** `Export-ModuleMember`（无它时函数默认全导出，有它反而需逐名登记）。
