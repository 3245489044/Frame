#requires -Version 7.0
<#
.SYNOPSIS
    视图程序一致性守卫（ViewProgramConsistency）——声明面与编译产物必须逐键一致。

.DESCRIPTION
    C1 产物在位：Config\ui\view-programs.generated.json 可解析且 schema/rootName 合法
    C2 产物不漂移：重跑 Generate-ViewPrograms.ps1 -Stdout 与入库产物规范化后逐键一致
    C3 读取面闭合：UiRegionCatalog 以嵌入资源读取产物（禁第二份视图/容器表）
    C4 负对照：容器键被篡改 ⇒ C2 必须判红
    纯扫描（只读、0 写入；C2 只调生成器 -Stdout）。
#>
[CmdletBinding()]
param([switch]$Check, [switch]$NegativeControl)

$ErrorActionPreference = 'Stop'

# 门禁运行时（根定位/落点/裁决单源；向上查找仓库超根，与目录分层解耦）
$GateRuntimeRoot = $PSScriptRoot
while ($GateRuntimeRoot -and -not ((Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Frame')) -and (Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Projects')))) { $GateRuntimeRoot = Split-Path -Parent $GateRuntimeRoot }
if (-not $GateRuntimeRoot) { throw '仓库超根定位失败（未命中同时含 Frame\ 与 Projects\ 的目录）' }
Import-Module (Join-Path $GateRuntimeRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking

$repo = Get-GateRepoRoot -ScriptRoot $PSScriptRoot
$declaration = Join-Path $repo 'Projects\Plug\Agent\Config\ui\ui-regions.json'
$generated = Join-Path $repo 'Projects\Plug\Agent\Config\ui\view-programs.generated.json'
$generator = Join-Path $repo 'Frame\PowerShellScript\Share\Ui\Generate-ViewPrograms.ps1'
$catalogPath = Join-Path $repo 'Projects\Plug\Agent\src\Agent.Ui\Ui\Views\Declaration\UiRegionCatalog.cs'
$hostLayoutPath = Join-Path $repo 'Projects\Information\DrivenConfig\JsonConfig\AgentRuntimeUI.json'

function Get-Canonical([object]$value) {
    if ($null -eq $value) { return 'null' }
    if ($value -is [string]) { return ($value | ConvertTo-Json -Compress) }
    if ($value -is [bool] -or $value -is [int] -or $value -is [long] -or $value -is [double]) { return ($value | ConvertTo-Json -Compress) }
    if ($value -is [System.Array]) { return '[' + (($value | ForEach-Object { Get-Canonical $_ }) -join ',') + ']' }
    $names = @($value.PSObject.Properties.Name) | Sort-Object
    return '{' + (($names | ForEach-Object { '"' + $_ + '":' + (Get-Canonical $value.$_) }) -join ',') + '}'
}

function Get-Program([string]$json) {
    $parsed = $json | ConvertFrom-Json
    return [pscustomobject]@{ Schema = [string]$parsed.schema; RootName = [string]$parsed.rootName; Canonical = (Get-Canonical $parsed) }
}

$rules = @(
    [pscustomobject]@{ Id = 'C1'; Title = '产物在位且结构合法'
        Ok = {
            if (-not (Test-Path -LiteralPath $generated)) { return $false }
            $program = Get-Program (Get-Content -LiteralPath $generated -Raw)
            return ($program.Schema -eq 'view-programs-v1') -and ($program.RootName -eq 'ui-root') -and $program.Canonical.Contains('"viewKey"')
        } }
    [pscustomobject]@{ Id = 'C2'; Title = '产物 ≡ 声明重生成（逐键）'
        Ok = {
            $declared = Get-Program (Get-Content -LiteralPath $generated -Raw)
            $rebuilt = Get-Program (& $generator -Regions $declaration -Stdout | Out-String)
            return ($declared.Canonical -eq $rebuilt.Canonical)
        } }
    [pscustomobject]@{ Id = 'C3'; Title = '读取面闭合（嵌入产物单源）'
        Ok = {
            $text = Get-Content -LiteralPath $catalogPath -Raw
            return ($text -match 'Agent\.Ui\.view-programs\.generated\.json') -and
                   ($text -match 'UiViewBlueprintCatalog\.FromJson') -and
                   ($text -match 'Blueprints')
        } }
    [pscustomobject]@{ Id = 'C5'; Title = '宿主布局 ⊇ 生成物容器集（第三源不漂移）'
        Ok = {
            if (-not (Test-Path -LiteralPath $hostLayoutPath)) { return $false }
            $hostLayout = Get-Content -LiteralPath $hostLayoutPath -Raw
            $declared = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
            foreach ($m in [regex]::Matches($hostLayout, '"key"\s*:\s*"(region-[a-z0-9-]+)"')) { [void]$declared.Add($m.Groups[1].Value) }
            if ($declared.Count -eq 0) { return $false }
            $program = Get-Content -LiteralPath $generated -Raw | ConvertFrom-Json
            foreach ($view in @($program.views)) {
                foreach ($container in @($view.containers)) {
                    if (-not $declared.Contains([string]$container.key)) { return $false }
                }
            }
            return $true
        } }
)

if ($NegativeControl) {
    $good = [pscustomobject]@{ views = @([pscustomobject]@{ viewKey = 'a'; containers = @([pscustomobject]@{ key = 'region-x' }) }) }
    $bad  = [pscustomobject]@{ views = @([pscustomobject]@{ viewKey = 'a'; containers = @([pscustomobject]@{ key = 'region-y' }) }) }
    if ((Get-Canonical $good) -eq (Get-Canonical $bad)) { throw '负对照失败：C2 判据未能区分容器键变化' }
    if ((Get-Canonical $good) -ne (Get-Canonical $good)) { throw '负对照失败：C2 判据对同一输入不稳定' }
    Write-Host '[OK] 负对照通过：容器键篡改可被 C2 捕获。' -ForegroundColor Green
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
        Write-Host "ViewProgramConsistency: $failed 项违规（产物与声明漂移）。" -ForegroundColor Red
        exit 1
    }
    Write-Host 'ViewProgramConsistency: 全部通过。' -ForegroundColor Green
}

exit 0
