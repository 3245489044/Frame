#requires -Version 7.0
<#
.SYNOPSIS
    视图黄金样本守卫（ViewGolden）：改声明/改渲染前必须能离线对拍（输入 → 期望 ViewSnapshot/DOM 指纹）。
.DESCRIPTION
    G1 样本矩阵：Goldens\<view>\<scenario>\ 三视图各 >= 2 场景（空库 + 单行）。
    G2 文件齐备：每个场景必须有 input.json / expected.snapshot.json / expected.dom.html。
    G3 结构可复算：expected.snapshot.json 可解析且含 schema/view/rows 字段；expected.dom.html 非空。
    G4 唯一性：同一视图内场景目录名唯一；snapshot.view 必须等于所在视图目录名。
    纯扫描（只读、0 写入）。默认报告；-Check 违规 → 退出码 1。
#>
[CmdletBinding()]
param([switch]$Check, [switch]$NegativeControl)

$ErrorActionPreference = 'Stop'

$GateRuntimeRoot = $PSScriptRoot
while ($GateRuntimeRoot -and -not ((Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Frame')) -and (Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Projects')))) { $GateRuntimeRoot = Split-Path -Parent $GateRuntimeRoot }
if (-not $GateRuntimeRoot) { throw '仓库超根定位失败（未命中同时含 Frame\ 与 Projects\ 的目录）' }
Import-Module (Join-Path $GateRuntimeRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking

$Root = Get-GateRepoRoot -ScriptRoot $PSScriptRoot
$goldens = Join-Path $Root 'Environment\CentralTest\AgentTest\Agent.Launcher.Tests\Goldens'
$viewsRequired = @('history', 'task', 'node')
$casesRequired = 2

$violations = [System.Collections.Generic.List[string]]::new()

function Test-GoldenCase([string]$dir, [string]$view) {
    $errors = [System.Collections.Generic.List[string]]::new()
    foreach ($file in @('input.json', 'expected.snapshot.json', 'expected.dom.html')) {
        if (-not (Test-Path -LiteralPath (Join-Path $dir $file))) { $errors.Add(("[G2] 缺件 {0}\\{1}" -f $dir, $file)) }
    }

    $snapshotPath = Join-Path $dir 'expected.snapshot.json'
    if (Test-Path -LiteralPath $snapshotPath) {
        try {
            $snap = Get-Content -LiteralPath $snapshotPath -Raw | ConvertFrom-Json -Depth 16
            foreach ($key in @('schema', 'view', 'rows')) {
                if ($null -eq $snap.PSObject.Properties[$key]) { $errors.Add(("[G3] snapshot 缺字段 '{0}'：{1}" -f $key, $snapshotPath)) }
            }
            if ($snap.PSObject.Properties['view'] -and [string]$snap.view -ne $view) {
                $errors.Add(("[G4] snapshot.view '{0}' != 目录视图 '{1}'" -f $snap.view, $view))
            }
        } catch {
            $errors.Add(("[G3] snapshot 不可解析：{0}" -f $snapshotPath))
        }
    }

    $domPath = Join-Path $dir 'expected.dom.html'
    if ((Test-Path -LiteralPath $domPath) -and (Get-Item -LiteralPath $domPath).Length -eq 0) {
        $errors.Add(("[G3] expected.dom.html 为空：{0}" -f $domPath))
    }
    return $errors
}

if ($NegativeControl) {
    $tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("golden-neg-" + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $tmp | Out-Null
    try {
        Set-Content -LiteralPath (Join-Path $tmp 'input.json') -Value '{}'
        Set-Content -LiteralPath (Join-Path $tmp 'expected.snapshot.json') -Value '{"schema":"view-snapshot-v1","view":"history","rows":[]}'
        if ((Test-GoldenCase $tmp 'history').Count -eq 0) { throw '负对照失败：缺 expected.dom.html 未被捕获' }
        Set-Content -LiteralPath (Join-Path $tmp 'expected.dom.html') -Value '<div></div>'
        if ((Test-GoldenCase $tmp 'task').Count -eq 0) { throw '负对照失败：snapshot.view 错配未被捕获' }
        if ((Test-GoldenCase $tmp 'history').Count -ne 0) { throw '负对照失败：合法样本被误报' }
        Write-Host '[OK] 负对照通过：缺件 / 视图错配均被捕获。' -ForegroundColor Green
    } finally {
        Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue
    }
    return
}

Write-Host '=== [ViewGolden] 视图黄金样本 ==='
foreach ($view in $viewsRequired) {
    $viewDir = Join-Path $goldens $view
    if (-not (Test-Path -LiteralPath $viewDir)) {
        $violations.Add(("[G1] 缺视图样本目录：{0}" -f $viewDir))
        continue
    }

    $cases = @(Get-ChildItem -LiteralPath $viewDir -Directory -ErrorAction SilentlyContinue)
    if ($cases.Count -lt $casesRequired) {
        $violations.Add(("[G1] 视图 '{0}' 场景数 {1} < {2}" -f $view, $cases.Count, $casesRequired))
    }

    foreach ($case in $cases) {
        foreach ($e in (Test-GoldenCase $case.FullName $view)) { $violations.Add($e) }
    }
}

$totalCases = 0
if (Test-Path -LiteralPath $goldens) {
    $totalCases = @(Get-ChildItem -LiteralPath $goldens -Directory | ForEach-Object { Get-ChildItem -LiteralPath $_.FullName -Directory -ErrorAction SilentlyContinue }).Count
}
Write-Host ("视图: {0} | 场景: {1} | 违规命中: {2}" -f ($viewsRequired -join '/'), $totalCases, $violations.Count)
if ($violations.Count -gt 0) {
    $violations | ForEach-Object { Write-Host ("  [违规] {0}" -f $_) }
    if ($Check) { Write-Host '校验失败：黄金样本不闭合（改声明无离线对拍）。'; exit 1 }
}

if ($violations.Count -eq 0) { Write-Host '结果: 通过' -ForegroundColor Green }
exit 0
