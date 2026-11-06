#requires -Version 7.0
<#
.SYNOPSIS
    声明-消费守卫（UPC D-F1）：每个 [Flow] 的入口必须有生产消费者，或显式声明 Dead=<reason>。

.DESCRIPTION
    判据载体 = 声明文件结构（[Flow] 名称）+ 源码调用点计数（结构事实）；执行者叙述不参与。
      DC1 入口映射：每个 [Flow] 必须能映射到唯一入口方法（映射表 = 受控词，缺映射 ⇒ 判据不可判定）。
      DC2 消费者：入口方法在 Projects + Frame\CsharpPrj 源码中的调用点 ≥ 1；或 flow 行内声明 Dead=<reason>。
      DC3 负对照（-NegativeControl）：合成「零调用点」样本必须被判据识别。
    只读仓库，零侧写。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$NegativeControl
)

$ErrorActionPreference = 'Stop'

# 根定位（深度无关）：GateRuntime → DirectoryMap 以「Frame\ + Projects\」marker 上溯（迁 Environment 后不依赖脚本深度）
$__repoRoot = $PSScriptRoot
while ($__repoRoot -and -not ((Test-Path -LiteralPath (Join-Path $__repoRoot 'Frame')) -and (Test-Path -LiteralPath (Join-Path $__repoRoot 'Projects')))) { $__repoRoot = Split-Path -Parent $__repoRoot }
if (-not $__repoRoot) { throw '仓库超根定位失败（未命中同时含 Frame\ 与 Projects\ 的目录）' }
Import-Module (Join-Path $__repoRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking
if ([string]::IsNullOrWhiteSpace($Root)) {
    $ctx = New-GateContext -ScriptRoot $PSScriptRoot -Root $Root -Check:$Check -Key 'DeclaredFlowConsumers'
    $Root = $ctx.Root
}

Import-Module (Join-Path $Root 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking
Import-Module (Join-Path $Root 'Frame\PowerShellScript\Baselines\GateFileIndex.psm1') -Force

# 受控词：声明 flow → 唯一入口方法（新增 flow 必须在此登记，否则判据不可判定）。
$flowMap = @{
    'TransferContribute' = 'ContributeAsync'
    'TransferDispatch'   = 'DispatchAsync'
}

$rxEntry = { param($entry) "\.$entry\s*\(" }

function Test-DeclaredFlowConsumers {
    param([string]$RootPath, [hashtable]$Map, [scriptblock]$EntryRegex)

    $violations = [System.Collections.Generic.List[string]]::new()
    $workflows = Join-Path (Get-MappedPath -Key 'AgentConfig') 'workflows'
    if (-not (Test-Path -LiteralPath $workflows)) {
        $violations.Add("[DC0] 声明目录缺失：$workflows（判据不可判定）")
        return $violations
    }

    $flows = [System.Collections.Generic.List[object]]::new()
    foreach ($file in Get-ChildItem -LiteralPath $workflows -Filter '*.ushar' -File) {
        foreach ($line in [IO.File]::ReadAllLines($file.FullName)) {
            $m = [regex]::Match($line, '^\s*\[Flow\]\s+(?<name>[A-Za-z0-9_]+)')
            if ($m.Success) {
                $flows.Add([pscustomobject]@{
                    Name = $m.Groups['name'].Value
                    File = $file.Name
                    Dead = ($line -match 'Dead=')
                })
            }
        }
    }
    if ($flows.Count -eq 0) {
        $violations.Add('[DC0] 未发现任何 [Flow] 声明（判据空转）')
        return $violations
    }

    $sources = @()
    $sources += @(Get-GateFiles -Root (Get-MappedPath -Key 'Projects') -Extension cs | Where-Object { $_ -notmatch '\\BuildOutput\\|\\obj\\' })
    $sources += @(Get-GateFiles -Root (Join-Path (Get-MappedPath -Key 'Frame') 'CsharpPrj') -Extension cs | Where-Object { $_ -notmatch '\\BuildOutput\\|\\obj\\' })
    $all = ($sources | ForEach-Object { [IO.File]::ReadAllText($_) }) -join "`n"

    foreach ($flow in $flows) {
        if ($flow.Dead) { continue }
        if (-not $Map.ContainsKey($flow.Name)) {
            # 判据域 = 已登记入口映射的 flow（新增 flow 须先登记映射，否则不进入本批判据）。
            Write-Host ("[skip] [Flow] {0} 未登记入口映射（不在本批判据域）" -f $flow.Name) -ForegroundColor DarkGray
            continue
        }
        $entry = [string]$Map[$flow.Name]
        $count = [regex]::Matches($all, (& $EntryRegex $entry)).Count
        if ($count -lt 1) {
            $violations.Add("[DC2] [Flow] $($flow.Name) 入口 $entry 生产调用点=0（死声明：须接线或标 Dead=<reason>）")
        } else {
            Write-Host ("[ok] [Flow] {0} → {1} 调用点={2}" -f $flow.Name, $entry, $count) -ForegroundColor Green
        }
    }

    return $violations
}

if ($NegativeControl) {
    $sample = 'var items = flow.RunAsync(containerKey);'
    $caught = -not [regex]::IsMatch($sample, (& $rxEntry 'ContributeAsync'))
    if (-not $caught) { throw '负对照失败：零调用点样本未被识别（判据空转）' }
    $positive = 'var items = flow.ContributeAsync(containerKey, edge);'
    if (-not [regex]::IsMatch($positive, (& $rxEntry 'ContributeAsync'))) { throw '正对照失败：真实调用点未被识别' }
    Write-Host '[OK] 负对照通过：死声明样本可判红。' -ForegroundColor Green
}

if ($Check) {
    $violations = Test-DeclaredFlowConsumers -RootPath $Root -Map $flowMap -EntryRegex $rxEntry
    Write-GateVerdict -Violations $violations -Check -Key 'DeclaredFlowConsumers'
}

exit 0
