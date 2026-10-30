# Check-KeySpaceInvariants.ps1
# KB6 键空间不变式（三视图共用）：-Kind axis|surface|owner。
[CmdletBinding()]
param(
    [string]$Root = '',
    [ValidateSet('axis', 'surface', 'owner', 'coord', 'policy')]
    [string]$Kind = 'axis',
    [switch]$Check,
    [switch]$NegativeControl
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot '..\..\..\Baselines\GateRuntime.psm1') -Force -DisableNameChecking

if ([string]::IsNullOrWhiteSpace($Root)) { $Root = Get-GateRepoRoot -ScriptRoot $PSScriptRoot }
$declaration = Join-Path $Root 'Projects\Plug\Agent\Config\keyspaces\agent.keyspace.generated.json'
$violations = [System.Collections.Generic.List[string]]::new()

function Get-Placeholders([string]$template) {
    return @([regex]::Matches($template, '\{([A-Za-z_][A-Za-z0-9_]*)(?::[a-z]+)?\}') |
        ForEach-Object { $_.Groups[1].Value } | Select-Object -Unique)
}

function Get-Shape([string]$template) {
    return [regex]::Replace($template, '\{[A-Za-z_][A-Za-z0-9_]*(?::[a-z]+)?\}', '{}')
}

function Get-InvariantViolations {
    param($Families, [string]$View)
    $out = [System.Collections.Generic.List[string]]::new()
    $seenIds = @{}
    $seenShapes = @{}
    foreach ($family in @($Families)) {
        $id = [string]$family.id
        if ([string]::IsNullOrWhiteSpace($id)) { $out.Add('族缺 id'); continue }
        if ($seenIds.ContainsKey($id)) { $out.Add("族 id 重复：$id") }
        $seenIds[$id] = $true

        $canonical = [string]$family.template
        if ([string]::IsNullOrWhiteSpace($canonical)) { $out.Add("$id 缺 template"); continue }
        $canonicalPlaceholders = @(Get-Placeholders $canonical)

        if ($View -eq 'owner') {
            $shape = Get-Shape $canonical
            if ($seenShapes.ContainsKey($shape)) { $out.Add("规范模板形状重复：$id ≡ $($seenShapes[$shape])") }
            else { $seenShapes[$shape] = $id }
        }

        if ($View -eq 'axis') {
            if ($canonicalPlaceholders.Count -eq 0) { $out.Add("$id 规范模板无轴：$canonical") }

            $allTemplates = @([string]$canonical)
            foreach ($surfaceName in @($family.surfaces.PSObject.Properties.Name)) {
                $surface = $family.surfaces.$surfaceName
                $allTemplates += @($surface.templates | ForEach-Object { [string]$_ })
                $allTemplates += @($surface.readOnlyTemplates | ForEach-Object { [string]$_ })
            }

            $declaredAxes = @($family.axes | Where-Object { $_ } | ForEach-Object { [string]$_.name } | Where-Object { $_ })
            $allowed = if ($declaredAxes.Count -gt 0) {
                $declaredAxes
            } else {
                @($allTemplates | ForEach-Object { Get-Placeholders $_ }) | Select-Object -Unique
            }

            $used = @()
            foreach ($template in $allTemplates) {
                foreach ($placeholder in @(Get-Placeholders $template)) {
                    $used += $placeholder
                    if ($allowed -notcontains $placeholder) {
                        $out.Add("$id 轴越界：{$placeholder} 不在声明轴集合（模板：$template）")
                    }
                }
            }

            foreach ($axis in $declaredAxes) {
                $axisNode = @($family.axes | Where-Object { [string]$_.name -eq $axis })
                $isCoordinateAxis = $axisNode.Count -gt 0 -and -not [string]::IsNullOrWhiteSpace([string]$axisNode[0].canonical)
                if ($used -notcontains $axis -and -not $isCoordinateAxis) {
                    $out.Add("$id 声明轴未落地：{$axis} 不在任何模板中")
                }
            }
        }

        if ($View -eq 'surface') {
            foreach ($surfaceName in @($family.surfaces.PSObject.Properties.Name)) {
                if ([string]::IsNullOrWhiteSpace($surfaceName)) { continue }
                $surface = $family.surfaces.$surfaceName
                $templates = @()
                $templates += @($surface.templates | ForEach-Object { [string]$_ })
                $templates += @($surface.readOnlyTemplates | ForEach-Object { [string]$_ })
                if ($templates.Count -lt 2) { continue }
                for ($i = 0; $i -lt $templates.Count; $i++) {
                    for ($j = $i + 1; $j -lt $templates.Count; $j++) {
                        if ((Get-Shape $templates[$i]) -eq (Get-Shape $templates[$j])) {
                            $out.Add("$id/$surfaceName 面模板不可区分：$($templates[$i]) ≡ $($templates[$j])")
                        }
                    }
                }
            }
        }
    }
    return $out
}

# 域级覆盖：[Domain].context 含 container ⇒ 必须声明 container 坐标绑定（禁「容器坐标未声明」假绿）。
function Get-ContainerCoordinateViolations {
    param([string]$Text, [string]$FileName)
    $out = [System.Collections.Generic.List[string]]::new()
    $contextMatch = [regex]::Match($Text, '(?m)^\s*context\s+([^\r\n]+)$')
    if (-not $contextMatch.Success) { return $out }
    $names = @($contextMatch.Groups[1].Value.Split(',') | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    if ($names -notcontains 'container') { return $out }
    if ($Text -notmatch '(?m)^\s*container\s*\|\s*family=') {
        $out.Add("$FileName 的 context 含 container 但缺 coordinates 绑定（容器坐标未声明）")
    }
    return $out
}

# 坐标视图：轴 canonical 引用 / 规则集完备 / 派生前缀可逆性 / 域绑定 ↔ 族轴（结构事实，跨层）。
function Get-CoordinateViolations {
    param($Families, [string]$NodesDir)
    $out = [System.Collections.Generic.List[string]]::new()
    $byFamily = @{}
    foreach ($family in @($Families)) {
        $id = [string]$family.id
        if ([string]::IsNullOrWhiteSpace($id)) { continue }
        $byFamily[$id] = $family
    }

    foreach ($family in @($Families)) {
        $id = [string]$family.id
        if ([string]::IsNullOrWhiteSpace($id)) { continue }

        $canonicalNames = @($family.canonical.PSObject.Properties.Name | Where-Object { $_ })
        $axes = @($family.axes | Where-Object { $_ })
        foreach ($axis in $axes) {
            $axisCanonical = [string]$axis.canonical
            if ([string]::IsNullOrWhiteSpace($axisCanonical)) { continue }
            if ($canonicalNames -notcontains $axisCanonical) {
                $out.Add("$id/$($axis.name) 的 canonical='$axisCanonical' 未在本族声明（悬空引用）")
            }
        }

        foreach ($setName in $canonicalNames) {
            $set = $family.canonical.$setName
            $rules = @($set.rules | Where-Object { $_ })
            if ($rules.Count -eq 0) { $out.Add("$id canonical '$setName' 无规则"); continue }

            $seen = @{}
            foreach ($rule in $rules) {
                $from = [string]$rule.from
                if ([string]::IsNullOrWhiteSpace($from)) { $out.Add("$id canonical '$setName' 规则缺 from"); continue }
                if ($seen.ContainsKey($from)) { $out.Add("$id canonical '$setName' 的 from='$from' 重复") }
                $seen[$from] = $true

                $ruleKind = [string]$rule.rule
                if ($ruleKind -notin @('identity', 'strip-prefix', 'require-family')) {
                    $out.Add("$id canonical '$setName' 的 rule='$ruleKind' 未登记")
                }
                $onMiss = [string]$rule.onMiss
                if (-not [string]::IsNullOrWhiteSpace($onMiss) -and $onMiss -notin @('Fail', 'Keep')) {
                    $out.Add("$id canonical '$setName' 的 on-miss='$onMiss' 非法（Fail|Keep）")
                }
                if ($ruleKind -eq 'strip-prefix' -and $from -eq '*') {
                    $out.Add("$id canonical '$setName' 的 strip-prefix 不得以 * 为 from")
                }
            }

            foreach ($prefix in @($family.derivedPrefixes | Where-Object { $_ })) {
                if (-not $seen.ContainsKey([string]$prefix)) {
                    $out.Add("$id 派生前缀 '$prefix' 缺 canonical 规则（规则集 '$setName'）")
                }
            }
        }

        foreach ($prefix in @($family.derivedPrefixes | Where-Object { $_ })) {
            $reversible = $family.derivedPrefixReversibility.PSObject.Properties[[string]$prefix]
            if ($null -eq $reversible) {
                $out.Add("$id 派生前缀 '$prefix' 缺 reversible 声明（禁默认可逆）")
            }
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($NodesDir) -and (Test-Path -LiteralPath $NodesDir)) {
        $bindingPattern = '(?m)^\s*([A-Za-z_][A-Za-z0-9_-]*)\s*\|\s*family=([^\s|]+)\s*\|\s*axis=([^\s|]+)'
        foreach ($file in @(Get-ChildItem -LiteralPath $NodesDir -Filter '*.ushar' -File)) {
            $text = Get-Content -LiteralPath $file.FullName -Raw
            foreach ($m in [regex]::Matches($text, $bindingPattern)) {
                $name = $m.Groups[1].Value
                $familyId = $m.Groups[2].Value
                $axisName = $m.Groups[3].Value
                if (-not $byFamily.ContainsKey($familyId)) {
                    $out.Add("$($file.Name) 坐标 '$name' 绑定的族 '$familyId' 未在 [KeySpace] 声明")
                    continue
                }
                $axis = @($byFamily[$familyId].axes | Where-Object { [string]$_.name -eq $axisName })
                if ($axis.Count -eq 0) {
                    $out.Add("$($file.Name) 坐标 '$name' 绑定的轴 '$familyId/$axisName' 未声明")
                    continue
                }
                if ([string]::IsNullOrWhiteSpace([string]$axis[0].canonical)) {
                    $out.Add("$($file.Name) 轴 '$familyId/$axisName' 缺 canonical=（坐标值域未声明）")
                }
            }

            $coverage = @(Get-ContainerCoordinateViolations -Text $text -FileName $file.Name)
            if ($coverage.Count -gt 0) { $out.AddRange([string[]]$coverage) }
        }
    }

    return $out
}

# policy 视图：必备策略键 ↔ 已声明值（requiredPolicies ⊆ policies）+ 取值合法。
function Get-PolicyViolations {
    param($Document)
    $out = [System.Collections.Generic.List[string]]::new()
    if ($null -eq $Document) { $out.Add('[policy] 声明不可读'); return $out }

    $policyNames = @($Document.policies.PSObject.Properties.Name | Where-Object { $_ })
    $required = @($Document.requiredPolicies | ForEach-Object { [string]$_ } | Where-Object { $_ })
    $allowed = @('CreateMember', 'Report', 'Fail', 'PassThrough')

    if ($policyNames.Count -eq 0) {
        $out.Add('[policy] 声明面 policies 为空（必备策略无从落地；禁「缺项静默落 Report」）')
    }

    foreach ($key in $required) {
        if ($policyNames -notcontains $key) {
            $out.Add("[policy] required-policy '$key' 缺对应 policy 值")
        }
    }

    foreach ($prop in $Document.policies.PSObject.Properties) {
        if ($allowed -notcontains [string]$prop.Value) {
            $out.Add("[policy] '$($prop.Name)' 取值 '$($prop.Value)' 未登记（∈ CreateMember|Report|Fail|PassThrough）")
        }
    }

    return $out
}

$families = @()
if (-not (Test-Path -LiteralPath $declaration)) {
    $violations.Add("声明文件缺失：$declaration")
} else {
    $document = Get-Content -LiteralPath $declaration -Raw | ConvertFrom-Json
    $families = @($document.families)
    if ($families.Count -eq 0) { $violations.Add('声明族为空') }
    $found = @(Get-InvariantViolations -Families $families -View $Kind)
    if ($found.Count -gt 0) { $violations.AddRange([string[]]$found) }
    if ($Kind -eq 'policy') {
        $policyFound = @(Get-PolicyViolations -Document $document)
        if ($policyFound.Count -gt 0) { $violations.AddRange([string[]]$policyFound) }
    }
    if ($Kind -eq 'coord') {
        $nodesDir = Join-Path $Root 'Projects\Plug\Agent\Config\nodes'
        $coordFound = @(Get-CoordinateViolations -Families $families -NodesDir $nodesDir)
        if ($coordFound.Count -gt 0) { $violations.AddRange([string[]]$coordFound) }
    }
}

if ($NegativeControl) {
    if ($Kind -eq 'policy') {
        $probeRequired = [pscustomobject]@{
            policies = [pscustomobject]@{ 'probe.a' = 'CreateMember' }
            requiredPolicies = @('probe.a', 'probe.b')
        }
        if (@(Get-PolicyViolations -Document $probeRequired).Count -eq 0) {
            $violations.Add('负对照失败：required-policy 缺值样本未命中')
        }

        $probeValue = [pscustomobject]@{
            policies = [pscustomobject]@{ 'probe.a' = 'NotAPolicy' }
            requiredPolicies = @()
        }
        if (@(Get-PolicyViolations -Document $probeValue).Count -eq 0) {
            $violations.Add('负对照失败：非法策略取值样本未命中')
        }
    }
    elseif ($Kind -eq 'coord') {
        $probeCoord = @(
            [pscustomobject]@{
                id = 'probe.task'
                derivedPrefixes = @('probe_prefix_')
                axes = @([pscustomobject]@{ name = 'container'; canonical = 'probe-rule' })
                canonical = [pscustomobject]@{ 'probe-rule' = [pscustomobject]@{ rules = @() } }
                derivedPrefixReversibility = [pscustomobject]@{}
            }
        )
        $probeCoordViolations = @(Get-CoordinateViolations -Families $probeCoord -NodesDir '')
        if ($probeCoordViolations.Count -eq 0) { $violations.Add('负对照失败：coord 合成样本未命中') }

        # 负对照：context 含 container 但未声明坐标绑定 ⇒ 必红；已声明 ⇒ 必绿（正对照防判据空转）。
        $probeMissing = "[Domain] probe`n  identity {container}`n  context container, kind"
        if (@(Get-ContainerCoordinateViolations -Text $probeMissing -FileName 'probe.ushar').Count -eq 0) {
            $violations.Add('负对照失败：容器坐标未绑定的域样本未命中')
        }

        $probeBound = "[Domain] probe`n  identity {container}`n  context container`n  coordinates`n    container | family=probe.task | axis=container | canonical=required"
        if (@(Get-ContainerCoordinateViolations -Text $probeBound -FileName 'probe.ushar').Count -gt 0) {
            $violations.Add('正对照失败：已绑定容器坐标的域样本被误判红')
        }
    }
    else {
        $probe = @(
            [pscustomobject]@{
                id = 'probe.same'
                template = 'x.{a}'
                surfaces = [pscustomobject]@{ Storage = [pscustomobject]@{ templates = @('x.{a}') } }
            },
            [pscustomobject]@{
                id = 'probe.same'
                template = 'x.{a}'
                surfaces = [pscustomobject]@{ Storage = [pscustomobject]@{ templates = @('x.{a}') } }
            }
        )
        $probeViolations = @(Get-InvariantViolations -Families $probe -View $Kind)
        if ($probeViolations.Count -eq 0) { $violations.Add("负对照失败：$Kind 合成样本未命中") }
    }
}

$gateName = switch ($Kind) {
    'axis' { 'KeyAxisCompleteness' }
    'surface' { 'KeySurfaceDistinguishable' }
    'coord' { 'KeyCoordinateBindings' }
    'policy' { 'KeyPolicyDeclared' }
    default { 'KeyOwnerUnique' }
}
Write-GateVerdict -Violations $violations -Check:$Check -Key $gateName
