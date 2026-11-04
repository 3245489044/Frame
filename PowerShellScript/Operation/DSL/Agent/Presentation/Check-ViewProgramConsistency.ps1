#requires -Version 7.0
<#
.SYNOPSIS
    视图声明一致性守卫（ViewProgramConsistency）——声明面单源、读取面闭合、宿主布局不漂移。

.DESCRIPTION
    C1 声明在位：Config\ui\ui-regions.json 可解析且 schema/regions/views 合法
    C2 单源：无 view-programs.generated.json（生成物已退役；容器由声明派生）
    C3 读取面闭合：UiRegionCatalog 零 JSON 解析；唯一解析点 = AgentUiDeclarationProvider
    C5 宿主布局 ⊇ 声明派生容器集（第三源不漂移）
    C6 负对照：容器键被篡改 ⇒ C5 必须判红
    纯扫描（只读、0 写入）。
#>
[CmdletBinding()]
param([switch]$Check, [switch]$NegativeControl)

$ErrorActionPreference = 'Stop'

$GateRuntimeRoot = $PSScriptRoot
while ($GateRuntimeRoot -and -not ((Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Frame')) -and (Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Projects')))) { $GateRuntimeRoot = Split-Path -Parent $GateRuntimeRoot }
if (-not $GateRuntimeRoot) { throw '仓库超根定位失败（未命中同时含 Frame\ 与 Projects\ 的目录）' }
Import-Module (Join-Path $GateRuntimeRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking

$repo = Get-GateRepoRoot -ScriptRoot $PSScriptRoot
$declaration = Join-Path $repo 'Projects\Plug\Agent\Config\ui\ui-regions.json'
$retiredGenerated = Join-Path $repo 'Projects\Plug\Agent\Config\ui\view-programs.generated.json'
$retiredGenerator = Join-Path $repo 'Frame\PowerShellScript\Share\Ui\Generate-ViewPrograms.ps1'
$catalogPath = Join-Path $repo 'Projects\Plug\Agent\src\Agent.Ui\Ui\Views\Declaration\UiRegionCatalog.cs'
$providerPath = Join-Path $repo 'Projects\Plug\Agent\src\Agent.Ui\Ui\Views\Declaration\AgentUiDeclarationProvider.cs'
$hostLayoutPath = Join-Path $repo 'Projects\Information\DrivenConfig\JsonConfig\AgentRuntimeUI.json'

function Get-Containers {
    $decl = Get-Content -LiteralPath $declaration -Raw | ConvertFrom-Json
    $keys = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($slot in @('region-seg', 'region-head', 'region-subhead')) { [void]$keys.Add($slot) }
    foreach ($r in @($decl.regions)) { if ($null -ne $r -and $r.key) { [void]$keys.Add([string]$r.key) } }
    return $keys
}

$rules = @(
    [pscustomobject]@{ Id = 'C1'; Title = '声明在位且结构合法'
        Ok = {
            if (-not (Test-Path -LiteralPath $declaration)) { return $false }
            $decl = Get-Content -LiteralPath $declaration -Raw | ConvertFrom-Json
            return ([string]$decl.schema -eq 'ui-regions-v1') -and (@($decl.regions).Count -ge 8) -and (@($decl.views).Count -ge 1)
        } }
    [pscustomobject]@{ Id = 'C2'; Title = '单源：生成物/生成器已退役'
        Ok = { (-not (Test-Path -LiteralPath $retiredGenerated)) -and (-not (Test-Path -LiteralPath $retiredGenerator)) } }
    [pscustomobject]@{ Id = 'C3'; Title = '读取面闭合（唯一解析点）'
        Ok = {
            if (-not (Test-Path -LiteralPath $catalogPath)) { return $false }
            if (-not (Test-Path -LiteralPath $providerPath)) { return $false }
            $catalog = Get-Content -LiteralPath $catalogPath -Raw
            $provider = Get-Content -LiteralPath $providerPath -Raw
            return (-not ($catalog -match 'JsonDocument')) -and
                   ($provider -match 'JsonDocument\.Parse') -and
                   ($provider -match 'ui-regions\.json')
        } }
    [pscustomobject]@{ Id = 'C5'; Title = '宿主布局 ⊇ 声明派生容器集（第三源不漂移）'
        Ok = {
            if (-not (Test-Path -LiteralPath $hostLayoutPath)) { return $false }
            $hostLayout = Get-Content -LiteralPath $hostLayoutPath -Raw
            $declared = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
            foreach ($m in [regex]::Matches($hostLayout, '"key"\s*:\s*"(region-[a-z0-9-]+)"')) { [void]$declared.Add($m.Groups[1].Value) }
            if ($declared.Count -eq 0) { return $false }
            foreach ($container in (Get-Containers)) {
                if (-not $declared.Contains($container)) { return $false }
            }
            return $true
        } }
)

if ($NegativeControl) {
    if (-not (Test-Path -LiteralPath $hostLayoutPath)) { throw '负对照失败：缺宿主布局（判据不可判）' }
    $hostLayout = Get-Content -LiteralPath $hostLayoutPath -Raw
    $declared = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($m in [regex]::Matches($hostLayout, '"key"\s*:\s*"(region-[a-z0-9-]+)"')) { [void]$declared.Add($m.Groups[1].Value) }
    $probe = 'region-negative-control'
    if ($declared.Contains($probe)) { throw '负对照失败：探针键已存在于宿主布局' }
    foreach ($container in (Get-Containers)) {
        if (-not $declared.Contains($container)) { throw "负对照基线失效：声明容器 $container 不在宿主布局" }
    }
    Write-Host '[OK] 负对照通过：篡改容器键可被 C5 捕获。' -ForegroundColor Green
}

if (-not $Check -and -not $NegativeControl) {
    Write-Host '用法: -Check（检查） / -NegativeControl（判据自检）' -ForegroundColor Yellow
    return
}

if ($Check) {
    $failed = 0
    foreach ($r in $rules) {
        $pass = $false
        try { $pass = [bool](& $r.Ok) } catch { $pass = $false }
        $mark = if ($pass) { 'PASS' } else { 'FAIL' }
        if (-not $pass) { $failed++ }
        Write-Host ("[{0}] {1} {2}" -f $mark, $r.Id, $r.Title) -ForegroundColor $(if ($pass) { 'Green' } else { 'Red' })
    }
    if ($failed -gt 0) {
        Write-Host "ViewProgramConsistency: $failed 项违规（声明/读取面漂移）。" -ForegroundColor Red
        exit 1
    }
    Write-Host 'ViewProgramConsistency: 全部通过。' -ForegroundColor Green
}

exit 0
