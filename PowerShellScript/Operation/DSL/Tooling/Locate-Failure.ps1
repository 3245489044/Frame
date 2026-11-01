#requires -Version 7.0
<#
.SYNOPSIS
    失败归属查表（Locate-Failure）—— 只读、零运行：回答「这条红是我改出来的，还是本来就红」。

.DESCRIPTION
    治「把归属判定做成复现排查」：失败出现时先查三张静态表，**不允许**用「再跑一次看看」代替判定。

      T1 改动面交集：失败符号/工程 ∩ 改动文件传递闭包 ≠ ∅ ⇒ Mine（候选）
      T2 已知红名单：unit ∈ known-failures.json ⇒ PreExisting(registered)
      T3 基线：无任何改动 ⇒ PreExisting(no-change-surface)
      否则 ⇒ Unknown（只允许 1 次复现，且结论须落 Unresolved，禁继续追）

    纪律：本脚本**不执行** dotnet build/test、不调任何 Invoke-* 执行体、不写盘（-Json 仅输出到 stdout）。
    判据载体 = git 状态 + 声明文件（结构事实），不读执行者叙述。

.PARAMETER Name
    测试名 / 用例名 / 工程名（可多值）。
.PARAMETER Changed
    显式改动文件（缺省 = 从 git status 读取，含子模块）。
.PARAMETER Json
    以 JSON 输出（供上层装配）。
.EXAMPLE
    .\Invoke-Frame.ps1 -Action locate-failure -Name NodeIdentity_RejectsForeignFamilies_AndKeepsEndpointInstance
#>
[CmdletBinding()]
param(
    [string[]]$Name = @(),
    [string[]]$Changed = @(),
    [switch]$Json
)

$ErrorActionPreference = 'Stop'

function Find-RepoRoot {
    $d = $PSScriptRoot
    while ($d -and $d -ne (Split-Path $d -Parent)) {
        if ((Test-Path (Join-Path $d 'Frame')) -and (Test-Path (Join-Path $d 'Projects'))) { return $d }
        $d = Split-Path $d -Parent
    }
    throw '[Locate-Failure] 无法定位仓库根'
}

$root = Find-RepoRoot
$knownFailuresPath = Join-Path $root 'Environment\CentralTest\Manifests\known-failures.json'

# ---- 改动面（结构事实：git 状态；子模块按 ` m`/` M` 行额外展开）----
function Get-ChangedFiles([string]$repoRoot, [string[]]$explicit) {
    if (@($explicit).Count -gt 0) { return @($explicit | ForEach-Object { ($_ -replace '\\', '/') }) }

    $files = [System.Collections.Generic.List[string]]::new()
    $status = & git -C $repoRoot status --porcelain=v1 2>$null
    foreach ($line in @($status)) {
        if ([string]::IsNullOrWhiteSpace($line) -or $line.Length -lt 4) { continue }
        $path = $line.Substring(3).Trim()
        if ($path -match ' -> ') { $path = ($path -split ' -> ')[-1] }
        $files.Add(($path -replace '\\', '/'))
    }

    # 子模块：`m ` 前缀表示子模块内容有改动 ⇒ 展开其内部改动（否则归属判定会漏掉真正的改动面）
    foreach ($sub in @('Frame', 'Projects')) {
        $subPath = Join-Path $repoRoot $sub
        if (-not (Test-Path -LiteralPath $subPath)) { continue }
        $subStatus = & git -C $subPath status --porcelain=v1 2>$null
        foreach ($line in @($subStatus)) {
            if ([string]::IsNullOrWhiteSpace($line) -or $line.Length -lt 4) { continue }
            $path = $line.Substring(3).Trim()
            if ($path -match ' -> ') { $path = ($path -split ' -> ')[-1] }
            $files.Add((($sub + '/' + $path) -replace '\\', '/'))
        }
    }

    return @($files | Sort-Object -Unique)
}

# ---- 已知红名单（声明面）----
function Get-KnownFailures([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { return @() }
    try {
        $doc = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json -Depth 12
    } catch {
        return @()
    }

    $rows = @()
    foreach ($unit in $doc.units.PSObject.Properties) {
        foreach ($name in @($unit.Value.knownFailures)) {
            $rows += [pscustomobject]@{ Unit = $unit.Name; Name = [string]$name }
        }
    }

    return $rows
}

$changedFiles = @(Get-ChangedFiles $root $Changed)
$known = @(Get-KnownFailures $knownFailuresPath)

$results = @()
foreach ($n in @($Name)) {
    if ([string]::IsNullOrWhiteSpace($n)) { continue }

    $hit = @($known | Where-Object { $_.Name -eq $n -or $n.StartsWith($_.Name + '.') })
    $verdict = 'Unknown'
    $evidence = @()

    if ($hit.Count -gt 0) {
        $verdict = 'PreExisting'
        $evidence += ("T2 known-failures: {0} / {1}" -f $hit[0].Unit, $hit[0].Name)
    }
    elseif ($changedFiles.Count -eq 0) {
        $verdict = 'PreExisting'
        $evidence += 'T3 改动面为空（工作区无改动 ⇒ 非本次改动引入）'
    }
    else {
        $candidates = @($changedFiles | Where-Object { $_ -match [regex]::Escape($n) })
        if ($candidates.Count -gt 0) {
            $verdict = 'Mine'
            $evidence += ("T1 改动面命中：{0}" -f ($candidates -join ', '))
        }
        else {
            $evidence += ("T1 改动面 {0} 个文件，均未命中该名称" -f $changedFiles.Count)
            $evidence += '⇒ Unknown：仅当 T1/T2/T3 皆不判时，才允许 1 次复现；结论须落 Unresolved，禁继续追'
        }
    }

    $results += [pscustomobject]@{
        name     = $n
        verdict  = $verdict
        evidence = $evidence
    }
}

if ($Json) {
    [pscustomobject]@{
        schema        = 'failure-attribution-v1'
        changedFiles  = $changedFiles
        knownFailures = $known
        results       = $results
    } | ConvertTo-Json -Depth 12
}
else {
    Write-Host ("=== [Locate-Failure] 改动面 {0} 个文件 · 已知红 {1} 条 ===" -f $changedFiles.Count, $known.Count)
    foreach ($r in $results) {
        Write-Host ("  {0} ⇒ {1}" -f $r.name, $r.verdict)
        foreach ($e in $r.evidence) { Write-Host ("      {0}" -f $e) }
    }
    if ($results.Count -eq 0) { Write-Host '  （未给出 -Name；仅输出改动面与已知红）' }
}

exit 0
