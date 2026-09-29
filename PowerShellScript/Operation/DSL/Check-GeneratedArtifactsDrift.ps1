#requires -Version 5.1
<#
.SYNOPSIS
    生成物漂移守卫（GeneratedArtifactsDrift）—— 生成物 = 产物而非手写；手改/漂移即红。

.DESCRIPTION
    登记表 = Baselines\_generated-artifacts.psd1（产物 ↔ 生成器 ↔ 易变字段）。
    本仓已有生成器（Generate-ModulePaths / Build-FunctionIndex / Build-ContractIndex），
    但此前**无漂移判据**（重跑是否等价、产物是否被手改，无人看守）。本件把它们变成 fail-closed 判据。

    每件产物三段判据：
      D1 存在：产物文件在位（缺失 ⇒ 违规 + 修复命令）。
      D2 哨兵：产物内含登记表声明的哨兵串（手改/覆盖即失守）。
      D3 等价：以临时输出路径复跑生成器 → 归一（剔除易变字段/行）→ 逐字比对 ⇒ 漂移即红。

    模式：默认报告模式；-Check 校验模式（任一违规 → 退出码 1）。

.PARAMETER Root
    仓库根目录（默认由本脚本位置上溯四级推导）。

.PARAMETER Check
    校验模式：违规 → 退出码 1。

.PARAMETER ShowDetail
    输出逐件实测明细与差异行。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$ShowDetail
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root))
{
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
}

$violations = [System.Collections.Generic.List[string]]::new()

Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  生成物漂移守卫（存在 / 哨兵 / 等价）" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan

$registryPath = Join-Path $Root 'Frame\PowerShellScript\Baselines\_generated-artifacts.psd1'
if (-not (Test-Path -LiteralPath $registryPath))
{
    Write-Host "[FAIL] 生成物登记表缺失：Frame\PowerShellScript\Baselines\_generated-artifacts.psd1" -ForegroundColor Yellow
    if ($Check) { exit 1 }
    exit 0
}
$registry = Import-PowerShellDataFile -LiteralPath $registryPath
$artifacts = @($registry.Artifacts)
if ($artifacts.Count -lt 1)
{
    Write-Host "[FAIL] 生成物登记表登记数 0（判据无标的 ⇒ 假绿）" -ForegroundColor Yellow
    if ($Check) { exit 1 }
    exit 0
}

$tempDir = Join-Path ([IO.Path]::GetTempPath()) ('sr-drift-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tempDir -Force | Out-Null

<#
.SYNOPSIS
    读取并归一化产物/复算结果（剔除易变字段），供逐字比对。
#>
function Get-NormalizedContent
{
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Kind,
        [string[]]$IgnoreLinePatterns = @(),
        [string[]]$IgnoreFields = @()
    )

    if ($Kind -eq 'Json')
    {
        $obj = Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
        foreach ($field in $IgnoreFields)
        {
            if ($null -ne $obj.PSObject.Properties[$field]) { $obj.PSObject.Properties.Remove($field) }
        }
        return ($obj | ConvertTo-Json -Depth 20 -Compress)
    }

    $lines = @([System.IO.File]::ReadAllLines($Path))
    $kept = [System.Collections.Generic.List[string]]::new()
    foreach ($line in $lines)
    {
        $skip = $false
        foreach ($p in $IgnoreLinePatterns) { if ($line -match $p) { $skip = $true; break } }
        if (-not $skip) { $kept.Add($line.TrimEnd()) }
    }
    return (($kept -join "`n").TrimEnd())
}

$pass = 0
foreach ($a in $artifacts)
{
    $name = [string]$a.Name
    $artifactPath = Join-Path $Root ([string]$a.Artifact)
    $generatorPath = Join-Path $Root ([string]$a.Generator)
    $fix = [string]$a.FixCommand

    # D1 存在
    if (-not (Test-Path -LiteralPath $artifactPath))
    {
        $violations.Add("[D1] $name 产物缺失：$($a.Artifact)（修复：$fix）")
        continue
    }

    # D2 哨兵
    $sentinel = [string]$a.SentinelPattern
    if (-not [string]::IsNullOrWhiteSpace($sentinel))
    {
        $raw = Get-Content -LiteralPath $artifactPath -Raw -Encoding UTF8
        if ($raw -notmatch [regex]::Escape($sentinel))
        {
            $violations.Add("[D2] $name 哨兵缺失（期望含 '$sentinel'）⇒ 产物疑被手改/覆盖：$($a.Artifact)（修复：$fix）")
        }
    }

    # D3 等价（复算到 temp）
    if (-not (Test-Path -LiteralPath $generatorPath))
    {
        $violations.Add("[D3] $name 登记生成器不存在：$($a.Generator)")
        continue
    }
    $tempFile = Join-Path $tempDir ($name + '.out')
    $argList = @{}
    foreach ($k in $a.GeneratorArgs.Keys)
    {
        $v = $a.GeneratorArgs[$k]
        if ($v -is [string] -and $v -eq '{TEMP}') { $v = $tempFile }
        $argList[$k] = $v
    }
    # 判据基座 = **产物是否落盘**（而非退出码）：部分生成器正常路径不显式 exit，
    # $LASTEXITCODE 会残留上一次原生命令的值 ⇒ 按退出码判会产生假红。temp 目录每次唯一 ⇒ 缺产物即失败。
    $genOut = @(& $generatorPath @argList 2>&1)
    if (-not (Test-Path -LiteralPath $tempFile))
    {
        $rc = if ($null -eq $LASTEXITCODE) { 0 } else { $LASTEXITCODE }
        $violations.Add("[D3] $name 复算未产出文件（退出码 $rc，未写 {TEMP}）：$($a.Generator) 输出尾部：$(($genOut | Select-Object -Last 3) -join ' / ')")
        continue
    }

    $ignoreLines = @()
    if ($a.ContainsKey('IgnoreLinePatterns')) { $ignoreLines = @($a.IgnoreLinePatterns) }
    $ignoreFields = @()
    if ($a.ContainsKey('IgnoreJsonFields')) { $ignoreFields = @($a.IgnoreJsonFields) }

    $actual = Get-NormalizedContent -Path $artifactPath -Kind $a.Kind -IgnoreLinePatterns $ignoreLines -IgnoreFields $ignoreFields
    $expected = Get-NormalizedContent -Path $tempFile -Kind $a.Kind -IgnoreLinePatterns $ignoreLines -IgnoreFields $ignoreFields

    if ($actual -ne $expected)
    {
        $violations.Add("[D3] $name 漂移：产物 ≠ 复算结果（$($a.Artifact)）（修复：$fix）")
        if ($ShowDetail)
        {
            $aLines = @($actual -split "`n")
            $eLines = @($expected -split "`n")
            $shown = 0
            for ($i = 0; $i -lt [Math]::Max($aLines.Count, $eLines.Count) -and $shown -lt 5; $i++)
            {
                $av = if ($i -lt $aLines.Count) { $aLines[$i] } else { '<缺行>' }
                $ev = if ($i -lt $eLines.Count) { $eLines[$i] } else { '<缺行>' }
                if ($av -ne $ev)
                {
                    Write-Host ("    [diff] 行 {0}" -f ($i + 1)) -ForegroundColor DarkYellow
                    Write-Host ("      产物: {0}" -f $av) -ForegroundColor DarkYellow
                    Write-Host ("      复算: {0}" -f $ev) -ForegroundColor DarkYellow
                    $shown++
                }
            }
        }
        continue
    }

    $pass++
    if ($ShowDetail) { Write-Host ("  [OK] {0} 存在 ∧ 哨兵 ∧ 等价" -f $name) -ForegroundColor Gray }
}

Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "  登记 $($artifacts.Count) 件；通过 $pass 件" -ForegroundColor Gray

if ($violations.Count -eq 0)
{
    Write-Host ""
    Write-Host "[OK] 生成物漂移守卫：0 违规（全部产物存在、哨兵在位、复算等价）" -ForegroundColor Green
    exit 0
}

Write-Host ""
Write-Host "[FAIL] 生成物漂移守卫命中 $($violations.Count) 处：" -ForegroundColor Yellow
foreach ($v in $violations) { Write-Host "  - $v" -ForegroundColor Yellow }
if ($Check) { exit 1 }
exit 0

