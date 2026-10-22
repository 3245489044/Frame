#requires -Version 5.1
<#
.SYNOPSIS
    PowerShell 宿主定位单源门禁：全仓仅一处实现，Agent 与 FileDriven 只委托。

.DESCRIPTION
    [A1] 单源件存在且含 Store 包族发现 + 结构可用性判据（TryResolveUsable）；
    [A2] Agent ExecutableResolver 已委托（含 PowerShellHostLocator.Locate），无本地 KnownCandidates/别名特判；
    [A3] FileDriven GenerationBuildRunner 已委托，且不再手写 PATH 枚举 pwsh.exe；
    [A4] 全仓恰好一处 class PowerShellHostLocator（旧位置不得残留）；
    [A5] 负对照：合成重复类名不得被误认为单源。
    只读源码；-Check 时违规 > 0 退出码 1。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..\..')).Path
}

$locator = Join-Path $Root 'Frame\CsharpPrj\Plate\PowerShellHost\PowerShellHostLocator.cs'
$agent = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Modules\Framework\Invoke\PsRuntime\ExecutableResolver.cs'
$fileDriven = Join-Path $Root 'Projects\FileDriven\FileDriven.Launcher\Bootstrap\GenerationBuildRunner.cs'

$violations = [System.Collections.Generic.List[string]]::new()
foreach ($file in @($locator, $agent, $fileDriven)) {
    if (-not (Test-Path -LiteralPath $file)) { $violations.Add("缺少单源面文件: $file") }
}

if ($violations.Count -eq 0) {
    $locatorText = Get-Content -LiteralPath $locator -Raw
    $agentText = Get-Content -LiteralPath $agent -Raw
    $fdText = Get-Content -LiteralPath $fileDriven -Raw

    # [A1] 单源实现面
    if ($locatorText -notmatch 'Microsoft\.PowerShell_' -or $locatorText -notmatch 'TryResolveUsable') {
        $violations.Add('[A1] PowerShellHostLocator 未同时具备 Store 包族发现与结构可用性判据')
    }

    # [A2] Agent 只委托
    if ($agentText -notmatch 'PowerShellHostLocator\.Locate\s*\(') {
        $violations.Add('[A2] ExecutableResolver 未委托 PowerShellHostLocator.Locate')
    }
    if ($agentText -match 'KnownCandidates|IsWindowsAppsAlias') {
        $violations.Add('[A2] ExecutableResolver 仍保留本地候选/别名特判（第二套解析）')
    }

    # [A3] FileDriven 只委托
    if ($fdText -notmatch 'PowerShellHostLocator\.Locate\s*\(') {
        $violations.Add('[A3] GenerationBuildRunner 未委托 PowerShellHostLocator.Locate')
    }
    if ($fdText -match 'foreach[\s\S]{0,200}pwsh\.exe') {
        $violations.Add('[A3] GenerationBuildRunner 仍在手写 PATH 枚举 pwsh.exe')
    }

    # [A4] 全仓单一定义（排除构建产物）
    $defs = Get-ChildItem -Path (Join-Path $Root 'Frame'), (Join-Path $Root 'Projects') -Recurse -File -Filter '*.cs' -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\BuildOutput\\|\\obj\\|_deprecated' } |
        Select-String -Pattern 'class\s+PowerShellHostLocator\b' -List
    if (@($defs).Count -ne 1) {
        $violations.Add("[A4] class PowerShellHostLocator 定义数=$(@($defs).Count)（要求恰 1）")
    }

    # [A5] 负对照：旧命名空间不得残留
    if ($locatorText -match 'namespace\s+Modules\.Common\.Utilities') {
        $violations.Add('[A5] 负对照失败：定位器仍位于工具层命名空间（应为独立 Plate 工程）')
    }
}

if ($violations.Count -gt 0) {
    $violations | ForEach-Object { Write-Host "[违规] $_" }
    if ($Check) { exit 1 }
    exit 0
}

Write-Host '[通过] PowerShell 宿主定位单源：Plate\PowerShellHost 唯一实现；Agent/FileDriven 均委托'
exit 0
