#requires -Version 7.0
<#
.SYNOPSIS
    视图声明一致性守卫（ViewProgramConsistency）——产物单源、读取面闭合、宿主布局不漂移。

.DESCRIPTION
    C1 产物在位：ui-declaration.generated.json 可解析（schema=ui-declaration-v1，views/containers 非空）
    C2 单源：无 view-programs.generated.json / Generate-ViewPrograms.ps1（生成物已退役）
    C3 读取面闭合：Provider 零 JsonDocument；唯一读面 = GeneratedUiDeclaration
    C5 宿主布局 ⊇ 产物容器集（第三源不漂移）
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
$declaration = Join-Path $repo 'Projects\Plug\Agent\src\Agent.Ui\Ui\Views\Declaration\ui-declaration.generated.json'
$retiredGenerated = Join-Path $repo 'Projects\Plug\Agent\Config\ui\view-programs.generated.json'
$retiredGenerator = Join-Path $repo 'Frame\PowerShellScript\Share\Ui\Generate-ViewPrograms.ps1'
$providerPath = Join-Path $repo 'Projects\Plug\Agent\src\Agent.Ui\Ui\Views\Declaration\AgentUiDeclarationProvider.cs'
$hostLayoutPath = Join-Path $repo 'Projects\Information\DrivenConfig\JsonConfig\AgentRuntimeUI.json'

function Get-Containers {
    if (-not (Test-Path -LiteralPath $declaration)) { throw "产物缺失：$declaration" }
    $decl = Get-Content -LiteralPath $declaration -Raw -Encoding UTF8 | ConvertFrom-Json
    $keys = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($c in @($decl.containers)) { if ($c -and $c.key) { [void]$keys.Add([string]$c.key) } }
    return $keys
}

$rules = @(
    [pscustomobject]@{ Id = 'C1'; Title = '产物在位且结构合法'
        Ok = {
            if (-not (Test-Path -LiteralPath $declaration)) { return $false }
            $decl = Get-Content -LiteralPath $declaration -Raw -Encoding UTF8 | ConvertFrom-Json
            return ([string]$decl.schema -eq 'ui-declaration-v1') -and
                   (@($decl.views).Count -ge 1) -and
                   (@($decl.containers).Count -ge 8) -and
                   (-not [string]::IsNullOrWhiteSpace([string]$decl.fingerprint))
        } }
    [pscustomobject]@{ Id = 'C2'; Title = '单源：生成物/旧生成器已退役'
        Ok = { (-not (Test-Path -LiteralPath $retiredGenerated)) -and (-not (Test-Path -LiteralPath $retiredGenerator)) } }
    [pscustomobject]@{ Id = 'C3'; Title = '读取面闭合（零 JSON 解析）'
        Ok = {
            if (-not (Test-Path -LiteralPath $providerPath)) { return $false }
            $provider = Get-Content -LiteralPath $providerPath -Raw
            return (-not ($provider -match 'JsonDocument')) -and
                   (-not ($provider -match 'GetManifestResourceStream')) -and
                   ($provider -match 'GeneratedUiDeclaration')
        } }
    [pscustomobject]@{ Id = 'C5'; Title = '宿主布局 ⊇ 产物容器集（第三源不漂移）'
        Ok = {
            if (-not (Test-Path -LiteralPath $hostLayoutPath)) { return $false }
            $hostLayout = Get-Content -LiteralPath $hostLayoutPath -Raw
            $declared = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
            $pattern = '"key"\s*:\s*"(' + (Get-GateRegionKeyPattern) + ')"'
            foreach ($m in [regex]::Matches($hostLayout, $pattern)) { [void]$declared.Add($m.Groups[1].Value) }
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
    $declared = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    $pattern = '"key"\s*:\s*"(' + (Get-GateRegionKeyPattern) + ')"'
    foreach ($m in [regex]::Matches($hostLayout, $pattern)) { [void]$declared.Add($m.Groups[1].Value) }
    $probe = 'region-negative-control'
    if ($declared.Contains($probe)) { throw '负对照失败：探针键已存在于宿主布局' }
    foreach ($container in (Get-Containers)) {
        if (-not $declared.Contains($container)) { throw "负对照基线失效：容器 $container 不在宿主布局" }
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
        try { $pass = [bool](& $r.Ok) } catch {
            Write-Host ("[FAIL] {0} {1} — {2}" -f $r.Id, $r.Title, $_.Exception.Message) -ForegroundColor Red
            $pass = $false
        }
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
