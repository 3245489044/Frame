#requires -Version 5.1
<#
.SYNOPSIS
    WorkFlowMethod 层 4 键语义守卫（KS4/KS2 在层 4 的机检面）。

.DESCRIPTION
    KeySpaceCoverage 只扫描 Modules.KeySpace（层 2），不覆盖层 4 的键处理代码；
    本门禁把「键文法唯一 / 禁同步等待 / 禁策略兜底」固化到 WFM 的真实目录。

      WM1 键目录零自建 Regex（NodeProcess\KeyMapping / Execution / Context）
      WM2 键目录零 Split(',')（词法唯一 = Modules.Parsing.Keys）
      WM3 Output 目录零 Values.First()（禁任意策略静默执行）
      WM4 NodeProcessPipelineBuilder 零 GetAwaiter().GetResult()（禁同步阻塞）
      WM0 负对照：合成样本必须被同一判据捕获

    模式：默认报告；-Check 违规 → 退出码 1。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$NegativeControl
)

$ErrorActionPreference = 'Stop'
$__gateRoot = $PSScriptRoot
while ($__gateRoot -and -not (Test-Path -LiteralPath (Join-Path $__gateRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1'))) { $__gateRoot = Split-Path -Parent $__gateRoot }
if (-not $__gateRoot) { throw '未定位 GateRuntime（Frame\PowerShellScript\Baselines\GateRuntime.psm1）' }
Import-Module (Join-Path $__gateRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking

if ([string]::IsNullOrWhiteSpace($Root)) { $Root = Get-GateRepoRoot -ScriptRoot $PSScriptRoot }

$workflowDir = Join-Path $Root 'Frame\CsharpPrj\Operation\WorkFlowMethod'
$violations = [System.Collections.Generic.List[string]]::new()

if (-not (Test-Path -LiteralPath $workflowDir)) {
    $violations.Add('WM0 工程目录不存在：Frame\CsharpPrj\Operation\WorkFlowMethod')
}

function Get-CsFiles([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { return @() }
    return @(Get-ChildItem -LiteralPath $path -Recurse -Filter *.cs -File)
}

function Test-KeySemantics {
    param([string[]]$Paths, [string]$OutputDir, [string]$BuilderFile)

    $hits = [System.Collections.Generic.List[string]]::new()
    $keyFiles = @()
    foreach ($path in $Paths) { $keyFiles += Get-CsFiles $path }

    function Get-CodeText([string]$path) {
        $lines = Get-Content -LiteralPath $path | Where-Object {
            $_.TrimStart() -notmatch '^(//|///|\*|/\*)'
        }
        return ($lines -join "`n")
    }

    foreach ($file in $keyFiles) {
        $text = Get-CodeText $file.FullName
        $relative = $file.FullName.Substring($Root.Length + 1)
        if ($text -match 'Regex\.(Match|Replace)') {
            $hits.Add("WM1 键目录自建 Regex：$relative")
        }
        if ($text -match '\.Split\(\s*'',''\s*\)') {
            $hits.Add("WM2 键目录自建 Split(','')：$relative")
        }
    }

    foreach ($file in (Get-CsFiles $OutputDir)) {
        if ((Get-CodeText $file.FullName) -match 'Values\.First\(\)') {
            $hits.Add("WM3 Output 策略兜底 Values.First()：$($file.Name)")
        }
    }

    if (Test-Path -LiteralPath $BuilderFile) {
        if ((Get-CodeText $BuilderFile) -match 'GetAwaiter\(\)\.GetResult\(\)') {
            $hits.Add('WM4 管线同步等待 GetAwaiter().GetResult()：NodeProcessPipelineBuilder.cs')
        }
    }

    return , $hits
}

$hits = Test-KeySemantics `
    -Paths @(
        (Join-Path $workflowDir 'NodeProcess\KeyMapping'),
        (Join-Path $workflowDir 'NodeProcess\Execution'),
        (Join-Path $workflowDir 'Context')
    ) `
    -OutputDir (Join-Path $workflowDir 'Output') `
    -BuilderFile (Join-Path $workflowDir 'NodeProcess\NodeProcessPipelineBuilder.cs')

foreach ($hit in $hits) { $violations.Add($hit) }

if ($NegativeControl) {
    # 负对照：合成样本必须命中 WM1/WM2（判据非空转）。
    $synthetic = "var m = Regex.Match(key, `"x`"); var parts = key.Split(',')"
    $syntheticHits = [System.Collections.Generic.List[string]]::new()
    if ($synthetic -match 'Regex\.(Match|Replace)') { $syntheticHits.Add('WM1') }
    if ($synthetic -match '\.Split\(\s*'',''\s*\)') { $syntheticHits.Add('WM2') }
    if ($syntheticHits.Count -lt 2) {
        $violations.Add('WM0 负对照失败：合成样本未被判据捕获（判据空转）')
    }
}

$scope = 'WorkFlowMethodKeySemantics'
if ($violations.Count -eq 0) {
    Write-Host "  [PASS] $scope：0 违规" -ForegroundColor Green
} else {
    Write-Host "  [FAIL] $scope：$($violations.Count) 违规" -ForegroundColor Red
    foreach ($violation in $violations) { Write-Host "    - $violation" -ForegroundColor Yellow }
}

if ($Check -and $violations.Count -gt 0) { exit 1 }
exit 0
