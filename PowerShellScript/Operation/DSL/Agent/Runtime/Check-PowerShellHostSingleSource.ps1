#requires -Version 5.1
<#
.SYNOPSIS
    PowerShell 宿主定位单源门禁：全仓仅一处实现，Agent 与 FileDriven 只委托。

.DESCRIPTION
    [A1] 单源件存在且含 Store 包族发现 + 结构可用性判据（TryResolveUsable）；
    [A2] Agent ExecutableResolver 已委托（含 PowerShellHostLocator.Locate），无本地 KnownCandidates/别名特判；
    [A3] FileDriven GenerationBuildRunner 已委托（RunFileAsync），且不再手写 PATH 枚举 pwsh.exe / ProcessStartInfo；
    [A4] 全仓恰好一处 class PowerShellHostLocator（旧位置不得残留）；
    [A5] 负对照：合成重复类名不得被误认为单源；
    [A6] 非单源面（Agent/FileDriven）不得出现 PS ProcessStartInfo；
    [A7] "-File" 字面量仅允许出现在 Plate\PowerShellHost 的 Runner/Session/Probe；
    [A8] 正/负对照：合成重复启动代码必须被拦截，合法框架调用不得被误判。
    只读源码；-Check 时违规 > 0 退出码 1。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check
)

$ErrorActionPreference = 'Stop'

# 门禁运行时（根定位/落点/裁决单源；向上查找仓库超根，与目录分层解耦）
$GateRuntimeRoot = $PSScriptRoot
while ($GateRuntimeRoot -and -not ((Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Frame')) -and (Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Projects')))) { $GateRuntimeRoot = Split-Path -Parent $GateRuntimeRoot }
if (-not $GateRuntimeRoot) { throw '仓库超根定位失败（未命中同时含 Frame\ 与 Projects\ 的目录）' }
Import-Module (Join-Path $GateRuntimeRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking

if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = Get-GateRepoRoot -ScriptRoot $PSScriptRoot
}

$locator = Join-Path $Root 'Frame\CsharpPrj\Plate\PowerShellHost\PowerShellHostLocator.cs'
$resolver = Join-Path $Root 'Frame\CsharpPrj\Plate\PowerShellHost\PowerShellHostResolver.cs'
$agent = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Modules\Framework\Invoke\PsRuntime\ExecutableResolver.cs'
$hosting = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Modules\Framework\Invoke\PsRuntime\PsRuntimeHosting.cs'
$fileDriven = Join-Path $Root 'Projects\FileDriven\FileDriven.Launcher\Bootstrap\GenerationBuildRunner.cs'
$hostScript = Join-Path $Root 'Frame\PowerShellScript\Modules\PsRuntime\Binding\PsRuntime-Host.ps1'

$violations = [System.Collections.Generic.List[string]]::new()
foreach ($file in @($locator, $resolver, $agent, $hosting, $fileDriven, $hostScript)) {
    if (-not (Test-Path -LiteralPath $file)) { $violations.Add("缺少单源面文件: $file") }
}

if ($violations.Count -eq 0) {
    $locatorText = Get-Content -LiteralPath $locator -Raw
    $resolverText = Get-Content -LiteralPath $resolver -Raw
    $agentText = Get-Content -LiteralPath $agent -Raw
    $hostingText = Get-Content -LiteralPath $hosting -Raw
    $fdText = Get-Content -LiteralPath $fileDriven -Raw
    $hostText = Get-Content -LiteralPath $hostScript -Raw

    # [A1] 单源实现面（B10：发现/物化在 Resolver，Locator 保留兼容门面）
    $singleSourceText = $locatorText + "`n" + $resolverText
    if ($singleSourceText -notmatch 'Microsoft\.PowerShell_' -or $locatorText -notmatch 'TryResolveUsable') {
        $violations.Add('[A1] PowerShellHost 未同时具备 Store 包族发现/物化与结构可用性判据')
    }

    # [A2] Agent 只委托
    if ($agentText -notmatch 'PowerShellHostLocator\.Locate\s*\(') {
        $violations.Add('[A2] ExecutableResolver 未委托 PowerShellHostLocator.Locate')
    }
    if ($agentText -match 'KnownCandidates|IsWindowsAppsAlias') {
        $violations.Add('[A2] ExecutableResolver 仍保留本地候选/别名特判（第二套解析）')
    }

    # [A3] FileDriven 只委托（B9：一次性脚本执行统一经 PowerShellScriptRunner）
    if ($fdText -notmatch 'PowerShellScriptRunner\.RunFileAsync\s*\(' -and
        $fdText -notmatch 'PowerShellHostLocator\.Locate\s*\(') {
        $violations.Add('[A3] GenerationBuildRunner 未委托 Plate.PowerShellHost（RunFileAsync/Locate）')
    }
    if ($fdText -match 'foreach[\s\S]{0,200}pwsh\.exe') {
        $violations.Add('[A3] GenerationBuildRunner 仍在手写 PATH 枚举 pwsh.exe')
    }
    if ($fdText -match 'new\s+ProcessStartInfo') {
        $violations.Add('[A3] GenerationBuildRunner 仍内联 ProcessStartInfo（应经 PowerShellScriptRunner）')
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

    # [A9] reparse/AppExecLink 语义只允许在 Plate\PowerShellHost（WindowsAppExecLink 单源）
    if ($locatorText -notmatch 'PowerShellHostMaterializer\.TryMaterialize') {
        $violations.Add('[A9] 定位器未经物化器（0 字节 alias 可能被当作可用路径）')
    }

    # [A10] 能力失败必须保留框架稳定码（禁按异常类型折叠为 attach_failed）
    if ($hostingText -notmatch 'is\s+PowerShellHostException') {
        $violations.Add('[A10] PsRuntimeHosting.ClassifyFailure 未优先消费 PowerShellHostException.Code')
    }

    # [A6]/[A7] 进程启动面单源（B9）：PS ProcessStartInfo 与 -File 字面量只允许出现在 Plate\PowerShellHost
    $allowedLiteralFiles = @(
        'Frame\CsharpPrj\Plate\PowerShellHost\PowerShellScriptRunner.cs',
        'Frame\CsharpPrj\Plate\PowerShellHost\PowerShellStdioSession.cs',
        'Frame\CsharpPrj\Plate\PowerShellHost\PowerShellHostProbe.cs'
    )
    $scanRoots = @(
        (Join-Path $Root 'Frame\CsharpPrj\Plate\PowerShellHost'),
        (Join-Path $Root 'Projects\Plug\Agent\src'),
        (Join-Path $Root 'Projects\FileDriven')
    ) | Where-Object { Test-Path -LiteralPath $_ }

    $processStartPattern = 'new\s+ProcessStartInfo'
    $fileLiteralPattern = '"-File"'

    foreach ($scanRoot in $scanRoots) {
        $codeFiles = Get-ChildItem -Path $scanRoot -Recurse -File -Filter '*.cs' -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -notmatch '\\BuildOutput\\|\\obj\\|_deprecated' }
        foreach ($file in $codeFiles) {
            $rel = [System.IO.Path]::GetRelativePath($Root, $file.FullName)
            $inPlate = $rel -like 'Frame\CsharpPrj\Plate\PowerShellHost\*'
            $text = Get-Content -LiteralPath $file.FullName -Raw

            if (-not $inPlate -and $text -match $processStartPattern -and $text -match '(?i)\bpwsh\b|powershell') {
                $violations.Add("[A6] 非单源面出现 PS ProcessStartInfo：$rel")
            }
            if ($text -match $fileLiteralPattern -and ($allowedLiteralFiles -notcontains $rel)) {
                $violations.Add("[A7] `"-File`" 字面量出现在非单源面：$rel")
            }

            # [A11] reparse/WindowsApps 目录语义只允许在 Plate\PowerShellHost（发现机制单源）
            if (-not $inPlate -and $text -match '0x8000001B|FSCTL_GET_REPARSE_POINT|IO_REPARSE_TAG_APPEXECLINK|AppExecLink') {
                $violations.Add("[A11] reparse/AppExecLink 语义出现在非单源面：$rel")
            }
            if ($text -match 'Directory\.(GetDirectories|EnumerateDirectories)' -and
                $text -match 'WindowsApps' -and
                $rel -notlike '*PowerShellHostResolver.cs') {
                $violations.Add("[A11] WindowsApps 目录枚举出现在非解析器：$rel")
            }
        }
    }

    # [A12] 协议载荷单通道：必须经 plain payload；禁止深递归 ConvertTo-Json（>8）回归挂起。
    if ($hostText -notmatch 'ConvertTo-PlainPayload') {
        $violations.Add('[A12] PsRuntime-Host.ps1 未使用 plain payload（PS* 元数据可能进入协议）')
    }
    foreach ($depthMatch in [regex]::Matches($hostText, 'ConvertTo-Json\s+-Depth\s+(\d+)')) {
        $depth = [int]$depthMatch.Groups[1].Value
        if ($depth -gt 8) {
            $violations.Add("[A12] ConvertTo-Json -Depth $depth 超上限 8（plain payload 已消除环，超限即回归风险）")
        }
    }
    if ('ConvertTo-Json -Depth 32 -Compress' -notmatch 'ConvertTo-Json\s+-Depth\s+(\d+)' -or
        [int]([regex]::Match('ConvertTo-Json -Depth 32 -Compress', 'ConvertTo-Json\s+-Depth\s+(\d+)').Groups[1].Value) -le 8) {
        $violations.Add('[A12] 负对照失败：合成超深 ConvertTo-Json 未被规则命中')
    }
    if ('ConvertTo-Json -Depth 8 -Compress' -match 'ConvertTo-Json\s+-Depth\s+(\d+)' -and
        [int]([regex]::Match('ConvertTo-Json -Depth 8 -Compress', 'ConvertTo-Json\s+-Depth\s+(\d+)').Groups[1].Value) -gt 8) {
        $violations.Add('[A12] 正对照失败：合法深度 8 被误判')
    }

    # [A8] 正/负对照：合成样本必须被拦截，合法调用不得被误判
    $syntheticDuplicate = 'var psi = new ProcessStartInfo { FileName = "pwsh" }; psi.ArgumentList.Add("-File");'
    if ($syntheticDuplicate -notmatch $processStartPattern -or $syntheticDuplicate -notmatch $fileLiteralPattern) {
        $violations.Add('[A8] 负对照失败：合成重复启动代码未被 [A6]/[A7] 规则命中')
    }
    $syntheticClean = 'var result = await PowerShellScriptRunner.RunFileAsync(script, args, options);'
    if ($syntheticClean -match $processStartPattern -or $syntheticClean -match $fileLiteralPattern) {
        $violations.Add('[A8] 正对照失败：合法框架调用被误判为重复实现')
    }
}

if ($violations.Count -gt 0) {
    $violations | ForEach-Object { Write-Host "[违规] $_" }
    if ($Check) { exit 1 }
    exit 0
}

Write-Host '[通过] PowerShell 宿主定位单源：Plate\PowerShellHost 唯一实现；Agent/FileDriven 均委托'
exit 0
