param(
    [string]$Source = 'Projects\Plug\Agent\Config\keyspaces\agent.keyspace.json',
    [string]$Destination = 'Projects\Plug\Agent\Config\keyspaces\agent.keyspace.ushar'
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
$src = if ([IO.Path]::IsPathRooted($Source)) { $Source } else { Join-Path $root $Source }
$dst = if ([IO.Path]::IsPathRooted($Destination)) { $Destination } else { Join-Path $root $Destination }

$j = Get-Content -LiteralPath $src -Raw -Encoding UTF8 | ConvertFrom-Json
$sb = [System.Text.StringBuilder]::new()

function Add-Line([string]$text) { [void]$sb.AppendLine('  ' + $text) }
function NonEmpty([object]$value) { return $null -ne $value -and "$value".Length -gt 0 }

[void]$sb.AppendLine('# 键空间声明（[KeySpace]）——由 agent.keyspace.json 机械转换；本文件为主源，生成物禁止手改。')
[void]$sb.AppendLine("[KeySpace] $($j.id): domains=$(@($j.domains) -join ',')")

foreach ($f in @($j.families)) {
    $parts = @("family $($f.id)", "template=$($f.template)")
    if (NonEmpty $f.familyPrefix) { $parts += "prefix=$($f.familyPrefix)" }
    if (NonEmpty $f.relation) { $parts += "relation=$($f.relation)" }
    if (NonEmpty $f.cardinality) { $parts += "cardinality=$($f.cardinality)" }
    if (NonEmpty $f.businessKeyTemplate) { $parts += "businessKey=$($f.businessKeyTemplate)" }
    if (NonEmpty $f.domainId) { $parts += "domain=$($f.domainId)" }
    if (@($f.aliases).Count -gt 0) { $parts += "aliases=$(@($f.aliases) -join ',')" }
    if (@($f.orderKeys).Count -gt 0) { $parts += "orderKeys=$(@($f.orderKeys) -join ',')" }
    if (@($f.identityKeys).Count -gt 0) { $parts += "identity=$(@($f.identityKeys) -join ',')" }
    if (@($f.derivedPrefixes).Count -gt 0) { $parts += "derivedPrefixes=$(@($f.derivedPrefixes) -join ',')" }
    Add-Line ($parts -join ' | ')

    if ($f.derivedPrefixRoles) {
        foreach ($role in $f.derivedPrefixRoles.PSObject.Properties) {
            Add-Line ("derived $($f.id)/$($role.Name)=$($role.Value)")
        }
    }

    if ($f.surfaces) {
        foreach ($surface in $f.surfaces.PSObject.Properties) {
            $s = $surface.Value
            $sp = @("surface $($f.id)/$($surface.Name)")
            if (@($s.templates).Count -gt 0) { $sp += "templates=$(@($s.templates) -join ',')" }
            if (NonEmpty $s.familyPrefix) { $sp += "prefix=$($s.familyPrefix)" }
            if (@($s.readOnlyTemplates).Count -gt 0) { $sp += "readOnly=$(@($s.readOnlyTemplates) -join ',')" }
            Add-Line ($sp -join ' | ')
        }
    }

    foreach ($a in @($f.axes)) {
        if ($null -eq $a) { continue }
        $ap = @("axis $($f.id)/$($a.name)")
        if (NonEmpty $a.shape) { $ap += "shape=$($a.shape)" }
        $ap += "required=$($a.required)"
        if (@($a.allowedValues).Count -gt 0) { $ap += "allowed=$(@($a.allowedValues) -join ',')" }
        if (@($a.disallowInfix).Count -gt 0) { $ap += "disallow=$(@($a.disallowInfix) -join ',')" }
        Add-Line ($ap -join ' | ')
    }
}

foreach ($p in @($j.ports)) {
    $pp = @("port $($p.name)", "direction=$($p.direction)", "family=$($p.familyId)", "required=$($p.required)")
    if (NonEmpty $p.cardinality) { $pp += "cardinality=$($p.cardinality)" }
    if (NonEmpty $p.defaultValue) { $pp += "default=$($p.defaultValue)" }
    Add-Line ($pp -join ' | ')
}

foreach ($policy in $j.policies.PSObject.Properties) {
    Add-Line ("policy $($policy.Name) | value=$($policy.Value)")
}

Add-Line 'derive task | chain=taskKey,sessionKey,declaredScopeKey'
Add-Line 'derive session | chain=sessionKey,taskKey,declaredScopeKey'
Add-Line 'derive scope | chain=declaredScopeKey,taskKey,sessionKey'

[IO.File]::WriteAllText($dst, $sb.ToString(), (New-Object System.Text.UTF8Encoding $false))
Write-Host "[OK] $dst" -ForegroundColor Green
