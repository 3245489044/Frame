#requires -Version 5.1
<#
.SYNOPSIS
    RecognizedKinds 生成视图（声明 → 代码，P1-5）—— 词表镜像的唯一来源 = 声明面。

.DESCRIPTION
    单源：`Projects\Plug\Agent\Config\agents\cline\recognizers.json` 的 `kinds[]`
    产物：`Projects\Plug\Agent\src\Agent.Contract\NodeDomain\RecognizedKinds.cs`

    模式：
      默认 / -Verify：比对「声明面 kind 集」与「镜像字面量集」（双向）——不一致 ⇒ 退出码 1。
      -Emit        ：按声明面重写镜像文件（常量名 = PascalCase(kind)）。

    纪律（W-2）：kind 字面量只允许出现在声明面与本镜像（生成物），TS 侧零字面量（R5 守卫）。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Emit
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root))
{
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..\..')).Path
}

$specPath   = Join-Path $Root 'Projects\Plug\Agent\Config\agents\cline\recognizers.json'
$mirrorPath = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Contract\NodeDomain\RecognizedKinds.cs'

if (-not (Test-Path -LiteralPath $specPath))
{
    Write-Host "[FAIL] 声明面缺失：$specPath" -ForegroundColor Yellow
    exit 1
}

$spec = Get-Content -LiteralPath $specPath -Raw | ConvertFrom-Json
$kinds = @($spec.kinds)

function ConvertTo-ConstName([string]$kind)
{
    $name = ($kind -split '[-_]' | ForEach-Object { if ($_.Length -gt 0) { $_.Substring(0, 1).ToUpper() + $_.Substring(1) } }) -join ''
    if ($name -match '^\d') { $name = "K$name" }
    return $name
}

if ($Emit)
{
    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add('namespace Agent.Contract.NodeDomain;')
    $lines.Add('')
    $lines.Add('/// <summary>')
    $lines.Add('/// 识别词表 C# 镜像 —— **生成物**（生成器：Frame\PowerShellScript\Operation\DSL\Agent\Generate-RecognizedKinds.ps1）。')
    $lines.Add('/// <para>纪律（W-1/W-2）：本类型只是声明面的镜像；新增/改名识别类别 = 只改声明面后重跑生成器。')
    $lines.Add('/// 门禁 Check-RecognizerVocabulary（R8/R11）逐 kind 对账；手写副本的退役条件 = 本文件由生成器产出且对拍恒绿。</para>')
    $lines.Add('/// </summary>')
    $lines.Add('public static class RecognizedKinds')
    $lines.Add('{')
    foreach ($kind in $kinds)
    {
        $lines.Add("    /// <summary>$kind</summary>")
        $lines.Add("    public const string $(ConvertTo-ConstName $kind) = `"$kind`";")
        $lines.Add('')
    }
    $lines.Add('    /// <summary>全部受控类别（编译视图的唯一来源；ProcessNodeKinds.All 派生自本表）。</summary>')
    $values = ($kinds | ForEach-Object { ConvertTo-ConstName $_ }) -join ', '
    $lines.Add("    public static readonly string[] All = { $values };")
    $lines.Add('}')

    ($lines -join "`n") | Set-Content -LiteralPath $mirrorPath -Encoding utf8NoBOM
    Write-Host "[OK] 已生成：$mirrorPath（$($kinds.Count) kinds）" -ForegroundColor Green
    exit 0
}

# ---------------- Verify（默认） ----------------
if (-not (Test-Path -LiteralPath $mirrorPath))
{
    Write-Host "[FAIL] 镜像缺失：$mirrorPath（跑 -Emit 生成）" -ForegroundColor Yellow
    exit 1
}

$mirrorText = Get-Content -LiteralPath $mirrorPath -Raw
$mirrorKinds = @([regex]::Matches($mirrorText, '(?m)^\s*public const string \w+\s*=\s*"([a-z][a-z0-9-]*)"') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
$declared = @($kinds | Sort-Object -Unique)

$missing = @($declared | Where-Object { $mirrorKinds -notcontains $_ })
$extra   = @($mirrorKinds | Where-Object { $declared -notcontains $_ })

Write-Host ""
Write-Host "  声明面：$($declared.Count) kinds | 镜像：$($mirrorKinds.Count) kinds" -ForegroundColor Gray
if ($missing.Count -eq 0 -and $extra.Count -eq 0)
{
    Write-Host "[OK] RecognizedKinds 生成视图与声明面一致（$($declared.Count) kinds）" -ForegroundColor Green
    exit 0
}

if ($missing.Count -gt 0) { Write-Host "  - 镜像缺：$($missing -join ', ')" -ForegroundColor Yellow }
if ($extra.Count -gt 0)   { Write-Host "  - 镜像多：$($extra -join ', ')" -ForegroundColor Yellow }
Write-Host "[FAIL] 声明面与镜像漂移（跑 -Emit 重生成，或修声明面）" -ForegroundColor Yellow
exit 1
