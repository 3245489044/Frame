<#
.SYNOPSIS
    TestBuildFramework 守卫（Gate=TestBuildFramework）—— 测试构建通用框架的结构事实对账。

.DESCRIPTION
    判据载体 = 文件系统 / csproj 元素结构事实（禁执行者叙述；权威: axioms SEP.JudgmentCarrierStructural）：
      T1 输出拓扑单源：CentralTest 测试工程（SpecSourceGen opt-out 除外）不得声明
         <OutputPath>/<IntermediateOutputPath>/<AppendTargetFrameworkToOutputPath>。
      T2 产品面单源：不得用 <Reference HintPath> 指向 Frame\CsharpPrj / Plug\Agent / Plug\Engine /
         FileDriven 权威目录；不得实现 CopyProductClosure（须经 TestProductSurface.props + staging）。
      T3 兼容薄转发：build\props\CsharpPrj.BinaryRefs.props 不得含 ItemGroup/glob，只能声明 surface 选择。
      T4 保留策略：内容戳发布目录 ≤1（_current 指向的一份）；测试树内 *.dmp = 0。

    负对照（-NegativeControl）：合成「含 OutputPath 的 csproj」必判红、「干净 csproj」必判绿（防判据空转）。
    模式：默认报告（不设退出码）；-Check 违规 → 退出码 1；前置缺失 → 2。
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
while ($__repoRoot -and -not ((Test-Path -LiteralPath (Join-Path $__repoRoot 'Frame')) -and (Test-Path -LiteralPath (Join-Path $__repoRoot 'Projects'))) { $__repoRoot = Split-Path -Parent $__repoRoot }
if (-not $__repoRoot) { throw '仓库超根定位失败（未命中同时含 Frame\ 与 Projects\ 的目录）' }
Import-Module (Join-Path $__repoRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking
if ([string]::IsNullOrWhiteSpace($Root)) {
    $ctx = New-GateContext -ScriptRoot $PSScriptRoot -Root $Root -Check:$Check -Key 'TestBuildFramework'
    $Root = $ctx.Root
}
$centralTest = Join-Path $Root 'Environment\CentralTest'

function Get-TestBuildFrameworkViolations {
    param([string]$CentralTest, [string]$Configuration = 'Debug')

    $violations = [System.Collections.Generic.List[string]]::new()
    if (-not (Test-Path -LiteralPath $CentralTest)) {
        $violations.Add("前置缺失：$CentralTest")
        return $violations.ToArray()
    }

    $projectFiles = [System.IO.Directory]::EnumerateFiles($CentralTest, '*.csproj', [System.IO.SearchOption]::AllDirectories)
    foreach ($file in $projectFiles) {
        if ($file -match '\\SpecSourceGen\\') { continue }
        $text = [System.IO.File]::ReadAllText($file)
        $rel = $file.Substring($CentralTest.Length).TrimStart('\', '/')

        foreach ($tag in @('<OutputPath>', '<IntermediateOutputPath>', '<AppendTargetFrameworkToOutputPath>')) {
            if ($text.Contains($tag)) { $violations.Add("[T1] $rel 声明 $tag（输出拓扑须由 TestBuildFramework.props 单源给出）") }
        }
        if ($text -match 'Name\s*=\s*"CopyProductClosure"') {
            $violations.Add("[T2] $rel 自实现 CopyProductClosure（须由 StageTestProductSurface staging）")
        }
        foreach ($authoritative in @('CsharpPrj\BuildOutput', 'Plug\Engine\BuildOutput', 'Plug\Agent\BuildOutput', 'FileDriven\BuildOutput')) {
            if ($text -match ('<HintPath>[^<]*' + [regex]::Escape($authoritative))) {
                $violations.Add("[T2] $rel 以 <Reference HintPath> 直连权威目录（$authoritative）；须经 TestProductSurface 选择")
            }
        }
    }

    $shim = Join-Path $CentralTest 'build\props\CsharpPrj.BinaryRefs.props'
    if (Test-Path -LiteralPath $shim) {
        $shimText = [System.IO.File]::ReadAllText($shim)
        if ($shimText -match '<ItemGroup' -or $shimText -match '\*\.dll') {
            $violations.Add('[T3] CsharpPrj.BinaryRefs.props 仍是引用实现（含 ItemGroup/glob）；须为 surface 选择的薄转发')
        }
        if ($shimText -notmatch 'TestProductSurface') {
            $violations.Add('[T3] CsharpPrj.BinaryRefs.props 未声明 TestProductSurface（薄转发不完整）')
        }
    } else {
        $violations.Add('[T3] 缺 build\props\CsharpPrj.BinaryRefs.props（兼容入口缺失）')
    }

    $publishedRoot = Join-Path $CentralTest ('BuildOutput\{0}\net10.0\published' -f $Configuration)
    if (Test-Path -LiteralPath $publishedRoot) {
        $dirs = @([System.IO.Directory]::GetDirectories($publishedRoot))
        if ($dirs.Count -gt 1) {
            $violations.Add(("[T4] published 版本目录 {0} 份 > 1（保留策略未生效；期望仅 _current 指向的一份）" -f $dirs.Count))
        }
    }
    foreach ($dmp in [System.IO.Directory]::EnumerateFiles($CentralTest, '*.dmp', [System.IO.SearchOption]::AllDirectories)) {
        $violations.Add("[T4] 残留 hangdump：$($dmp.Substring($CentralTest.Length).TrimStart('\', '/'))")
    }

    return $violations.ToArray()
}
Write-Host ''
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host '  TestBuildFramework 守卫（输出拓扑 / 产品面 / 保留策略）' -ForegroundColor Cyan
Write-Host '============================================================' -ForegroundColor Cyan

$violations = @(Get-TestBuildFrameworkViolations -CentralTest $centralTest)
foreach ($v in $violations) { Write-Host ("  [VIOLATION] " + $v) -ForegroundColor Red }
Write-Host ("  违规数: {0}" -f $violations.Count)

$negativeOk = $true
if ($NegativeControl) {
    $probe = Join-Path ([System.IO.Path]::GetTempPath()) ("tbf-gate-" + [guid]::NewGuid().ToString('N'))
    try {
        $bad = Join-Path $probe 'Bad.Tests'
        $good = Join-Path $probe 'Good.Tests'
        New-Item -ItemType Directory -Path $bad, $good -Force | Out-Null
        Set-Content -LiteralPath (Join-Path $bad 'Bad.Tests.csproj') -Encoding UTF8 -Value @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputPath>..\out\</OutputPath>
  </PropertyGroup>
</Project>
'@
        Set-Content -LiteralPath (Join-Path $good 'Good.Tests.csproj') -Encoding UTF8 -Value @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
</Project>
'@
        $badViolations = @(Get-TestBuildFrameworkViolations -CentralTest $bad)
        $goodViolations = @(Get-TestBuildFrameworkViolations -CentralTest $good)
        $detected = @($badViolations | Where-Object { $_ -match '\[T1\]' }).Count -gt 0
        $clean = ($goodViolations | Where-Object { $_ -match '\[T1\]|\[T2\]' }).Count -eq 0
        $negativeOk = $detected -and $clean
        if (-not $negativeOk) { Write-Host '  [FAIL] 负对照失效（判据恒真/恒假）' -ForegroundColor Red }
        else { Write-Host '  [OK] 负对照：合成 OutputPath 必红 / 干净 csproj 必绿' -ForegroundColor Green }
    } finally {
        Remove-Item -LiteralPath $probe -Recurse -Force -ErrorAction SilentlyContinue
    }
}

if (-not $Check) {
    Write-Host '  （报告模式：未设置退出码；加 -Check 校验）' -ForegroundColor DarkGray
    exit 0
}
if ($violations.Count -gt 0 -or -not $negativeOk) { exit 1 }
Write-Host '  [OK] TestBuildFramework 结构事实全部通过' -ForegroundColor Green
exit 0

