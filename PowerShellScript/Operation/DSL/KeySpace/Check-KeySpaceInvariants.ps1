# Check-KeySpaceInvariants.ps1
# KB6 键空间不变式（三视图共用）：-Kind axis|surface|owner。
[CmdletBinding()]
param(
    [string]$Root = '',
    [ValidateSet('axis', 'surface', 'owner')]
    [string]$Kind = 'axis',
    [switch]$Check,
    [switch]$NegativeControl
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot '..\..\..\Baselines\GateRuntime.psm1') -Force -DisableNameChecking

if ([string]::IsNullOrWhiteSpace($Root)) { $Root = Get-GateRepoRoot -ScriptRoot $PSScriptRoot }
$declaration = Join-Path $Root 'Projects\Plug\Agent\Config\keyspaces\agent.keyspace.json'
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
            foreach ($surfaceName in @($family.surfaces.PSObject.Properties.Name)) {
                foreach ($template in @($family.surfaces.$surfaceName.templates)) {
                    foreach ($placeholder in @(Get-Placeholders ([string]$template))) {
                        if ($canonicalPlaceholders -notcontains $placeholder) {
                            $out.Add("$id/$surfaceName 轴越界：{$placeholder} 不在规范模板")
                        }
                    }
                }
            }
        }

        if ($View -eq 'surface') {
            $templates = @()
            foreach ($surfaceName in @($family.surfaces.PSObject.Properties.Name)) {
                $templates += @($family.surfaces.$surfaceName.templates | ForEach-Object { [string]$_ })
            }
            for ($i = 0; $i -lt $templates.Count; $i++) {
                for ($j = $i + 1; $j -lt $templates.Count; $j++) {
                    if ((Get-Shape $templates[$i]) -eq (Get-Shape $templates[$j])) {
                        $out.Add("$id 面模板不可区分：$($templates[$i]) ≡ $($templates[$j])")
                    }
                }
            }
        }
    }
    return $out
}

$families = @()
if (-not (Test-Path -LiteralPath $declaration)) {
    $violations.Add("声明文件缺失：$declaration")
} else {
    $families = @((Get-Content -LiteralPath $declaration -Raw | ConvertFrom-Json).families)
    if ($families.Count -eq 0) { $violations.Add('声明族为空') }
    $found = @(Get-InvariantViolations -Families $families -View $Kind)
    if ($found.Count -gt 0) { $violations.AddRange([string[]]$found) }
}

if ($NegativeControl) {
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

$gateName = switch ($Kind) {
    'axis' { 'KeyAxisCompleteness' }
    'surface' { 'KeySurfaceDistinguishable' }
    default { 'KeyOwnerUnique' }
}
Write-GateVerdict -Violations $violations -Check:$Check -Key $gateName
