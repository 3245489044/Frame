# CapabilityResolver.psm1 — 归一化（别名/verb 补默认 object）+ 两层检索（object 过滤 → action 精确匹配）
# 复用 C# CommandParser.Normalize + NormalizationRegistry 思想

$script:_ActionAliases = @{}

function Register-PsActionAlias {
    param([Parameter(Mandatory)][string]$Alias, [Parameter(Mandatory)][string]$Action)
    $script:_ActionAliases[$Alias] = $Action
}

function Normalize-PsAction {
    # 对应 C# CommandParser.Normalize：别名优先 → 已是 verb.object 原样 → verb 补默认 object
    param(
        [Parameter(Mandatory)][AllowEmptyString()][string]$Action,
        [string]$DefaultObject
    )
    # 1. 别名优先归一化
    if ($script:_ActionAliases.ContainsKey($Action)) { return $script:_ActionAliases[$Action] }
    # 2. 已是 verb.object 原样返回
    if ($Action -match '\.') { return $Action }
    # 3. 仅 verb：有默认 object 则补全，否则原样交由 resolve 判 no_capability
    if ($DefaultObject) { return "$Action.$DefaultObject" }
    return $Action
}

function Resolve-PsCapability {
    param(
        [array]$Contracts,
        [string]$Action,      # 动词优先动作 ID，如 read.file
        [string]$Verb,
        [string]$Object,
        [string]$Category     # 兼容：等同 object
    )
    # null/空 contracts 边界
    if (-not $Contracts) { return $null }

    # 第一层：object/category 过滤
    $obj = if ($Object) { $Object } elseif ($Category) { $Category }
    $cands = $Contracts
    if ($Action) {
        $parts = $Action -split '\.'
        if (-not $Verb) { $Verb = $parts[0] }
        if (-not $obj) { $obj = ($parts | Select-Object -Skip 1) -join '.' }
    }
    if ($obj) { $cands = @($cands | Where-Object { $_.identity.object -eq $obj }) }

    if ($cands.Count -eq 0) { return $null }

    # 第二层：精确匹配
    if ($Action) {
        $hit = @($cands | Where-Object { $_.identity.action -eq $Action })
        if ($hit.Count -gt 0) { return $hit[0] }
    }
    if ($Verb) {
        $hit = @($cands | Where-Object { $_.identity.verb -eq $Verb })
        if ($hit.Count -eq 1) { return $hit[0] }
        if ($hit.Count -gt 1) { return $hit }   # 多候选交由调用方决断
    }
    return $cands
}

Export-ModuleMember -Function Register-PsActionAlias, Normalize-PsAction, Resolve-PsCapability
