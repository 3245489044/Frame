#requires -Version 5.1
<#
GateCriteria.psm1 —— 门禁判据（criteria）装载与规则求值（纯原语，无指标语义）

设计：
  · 判据单源 = 门禁自己的 JSON（如 Presentation\Fixtures\ui-glass-criteria.json）；脚本内禁内联区间字面量；
  · 规则只有三种：range（闭区间）/ min（≥）/ max（≤），指标名与原样输出键一致；
  · 缺文件、schema 非法、缺必需指标、规则字段非数值 ⇒ 一律 throw（fail-closed，禁静默兜底）。

用法（门禁脚本内）：
    Import-Module (Join-Path $repoRoot 'Frame\PowerShellScript\Baselines\GateCriteria.psm1') -Force
    $criteria = Import-GateCriteria -Path $criteriaPath -RequiredMetrics @('panelToPage')
    $row = Test-GateCriteriaValue -Id 'panelToPage' -Rule $criteria.Metrics['panelToPage'] -Value 1.5

契约：本模块**不**读写基线（棘轮基线走 GateBaseline），只负责「目标区间/门限」这一层。
#>

$script:AllowedRules = @('range', 'min', 'max')

function Get-CriteriaRequiredFields
{
    param([string]$Rule)

    switch ($Rule)
    {
        'range' { return @('min', 'max') }
        'min' { return @('min') }
        'max' { return @('max') }
        default { return @() }
    }
}

function Assert-CriteriaNumber
{
    param([string]$Id, [string]$Field, $Value)

    if ($null -eq $Value -or $Value -is [bool] -or $Value -isnot [ValueType])
    {
        throw "判据 $Id 的 $Field 非数值：$Value"
    }
}

function Assert-CriteriaRule
{
    param([string]$Id, $Rule)

    if ($null -eq $Rule)
    {
        throw "判据 $Id 缺失（规则须为对象）"
    }

    $ruleProperty = $Rule.PSObject.Properties['rule']
    if ($null -eq $ruleProperty)
    {
        throw "判据 $Id 缺 rule（须 ∈ range/min/max）"
    }

    $kind = [string]$ruleProperty.Value
    if ($script:AllowedRules -notcontains $kind)
    {
        throw "判据 $Id 的 rule='$kind' 非法（须 ∈ range/min/max）"
    }

    foreach ($field in (Get-CriteriaRequiredFields $kind))
    {
        $property = $Rule.PSObject.Properties[$field]
        if ($null -eq $property)
        {
            throw "判据 $Id（rule=$kind）缺 $field"
        }
        Assert-CriteriaNumber -Id $Id -Field $field -Value $property.Value
    }

    if ($kind -eq 'range' -and [double]$Rule.min -ge [double]$Rule.max)
    {
        throw "判据 $Id 区间非法：min=$($Rule.min) >= max=$($Rule.max)"
    }
}

<#
.SYNOPSIS
    装载判据 JSON 并做结构校验（缺证/非法即 throw）。
.DESCRIPTION
    schema 必须为 gate-criteria-v1；metrics 下每个指标按 rule 校验字段；
    RequiredMetrics 中的指标缺失 ⇒ throw（禁让门禁在「无目标」状态下空转）。
.OUTPUTS
    [pscustomobject]@{ Path; Sha256; Schema; Metrics（有序哈希表 id→规则）; Data（原始对象） }
#>
function Import-GateCriteria
{
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [string[]]$RequiredMetrics = @()
    )

    if (-not (Test-Path -LiteralPath $Path))
    {
        throw "判据文件缺失：$Path（缺证即违规）"
    }

    $raw = Get-Content -LiteralPath $Path -Raw
    try
    {
        $data = $raw | ConvertFrom-Json
    }
    catch
    {
        throw "判据文件无法解析：$Path（$($_.Exception.Message)）"
    }

    $schema = [string]$data.schema
    if ($schema -ne 'gate-criteria-v1')
    {
        throw "判据 schema 非法：'$schema'（期望 gate-criteria-v1）"
    }

    if ($null -eq $data.metrics)
    {
        throw "判据缺 metrics 段（判据无标的）"
    }

    $metrics = [ordered]@{}
    foreach ($property in @($data.metrics.PSObject.Properties))
    {
        Assert-CriteriaRule -Id $property.Name -Rule $property.Value
        $metrics[$property.Name] = $property.Value
    }
    if ($metrics.Count -eq 0)
    {
        throw "判据 metrics 为空（判据无标的）"
    }

    foreach ($required in @($RequiredMetrics))
    {
        if (-not $metrics.Contains($required))
        {
            throw "判据缺必需指标：$required（$Path）"
        }
    }

    return [pscustomobject]@{
        Path    = (Resolve-Path -LiteralPath $Path).Path
        Sha256  = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
        Schema  = $schema
        Metrics = $metrics
        Data    = $data
    }
}

<#
.SYNOPSIS
    按规则求值单条指标（唯一求值实现；门禁不得各写一套比较）。
.OUTPUTS
    [pscustomobject]@{ Id; Value; Rule; Ok; Range }（Range 为输出用文本）
#>
function Test-GateCriteriaValue
{
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Id,
        [Parameter(Mandatory = $true)]$Rule,
        [Parameter(Mandatory = $true)][double]$Value
    )

    $kind = [string]$Rule.rule
    switch ($kind)
    {
        'range' {
            $ok = ($Value -ge [double]$Rule.min -and $Value -le [double]$Rule.max)
            $range = ('{0}–{1}' -f $Rule.min, $Rule.max)
        }
        'min' {
            $ok = ($Value -ge [double]$Rule.min)
            $range = ('≥{0}' -f $Rule.min)
        }
        'max' {
            $ok = ($Value -le [double]$Rule.max)
            $range = ('≤{0}' -f $Rule.max)
        }
        default {
            throw "判据 $Id 的 rule='$kind' 非法（须 ∈ range/min/max）"
        }
    }

    return [pscustomobject]@{
        Id    = $Id
        Value = $Value
        Rule  = $kind
        Ok    = [bool]$ok
        Range = $range
    }
}

<#
.SYNOPSIS
    取单条指标的区间边界（无该边界返回 $null；棘轮/诊断复用，避免脚本直接摸 JSON 形状）。
#>
function Get-GateCriteriaBound
{
    param(
        [Parameter(Mandatory = $true)]$Rule,
        [Parameter(Mandatory = $true)][ValidateSet('min', 'max')][string]$Name
    )

    $property = $Rule.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return [double]$property.Value
}

# 装载契约与 GateBaseline.psm1 一致：消费方必须 `Import-Module <path> -Force`；
# 本件禁 Export-ModuleMember（有它反而需逐名登记，函数默认全部导出）。
