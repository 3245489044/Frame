#requires -Version 5.1
<#
.SYNOPSIS
    编排内核单一性守卫（OrchestrationKernel）—— Block 单内核 / Pipeline 适配层 / Circuit 描述层。

.DESCRIPTION
    C1 Block 内核零项目引用；C2 BasicMethod 禁反向依赖；
    C3 执行循环/重试唯一；C4 结果类型唯一；
    C5 Pipeline 适配层委托内核；C6 Circuit 走 BlockPipeline；C7 正负对照。

.PARAMETER Check
    校验模式：违规 → 退出码 1；缺省仅报告。
#>
[CmdletBinding()]
param(
    [switch]$Check
)

$ErrorActionPreference = 'Stop'

$repoRoot = $PSScriptRoot
while ($repoRoot -and $repoRoot -ne (Split-Path $repoRoot -Parent)) {
    if ((Test-Path (Join-Path $repoRoot 'CsharpPrj')) -and (Test-Path (Join-Path $repoRoot 'PowerShellScript'))) { break }
    $repoRoot = Split-Path $repoRoot -Parent
}

if (-not $repoRoot -or -not (Test-Path (Join-Path $repoRoot 'CsharpPrj'))) {
    Write-Error '无法定位仓库根（CsharpPrj 不存在）。'
    exit 2
}

$kernelRoot = Join-Path $repoRoot 'CsharpPrj\BasicMethod\Map\Block'
$pipelineRoot = Join-Path $repoRoot 'CsharpPrj\Modules\Composition\Pipeline'
$circuitRoot = Join-Path $repoRoot 'CsharpPrj\Share\Frameworks\CircuitFramework'
$basicRoot = Join-Path $repoRoot 'CsharpPrj\BasicMethod'

$violations = [System.Collections.Generic.List[string]]::new()

function Add-Violation([string]$code, [string]$message) {
    $violations.Add("[$code] $message") | Out-Null
}

function Read-Text([string]$path) {
    if (Test-Path -LiteralPath $path) { return (Get-Content -LiteralPath $path -Raw) }
    return ''
}

function Get-CSharpFiles([string]$root) {
    Get-ChildItem -LiteralPath $root -Recurse -File -Include '*.cs' |
        Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' }
}

# ---------- C1：Block 内核零项目引用 ----------
$blockCsproj = Read-Text (Join-Path $kernelRoot 'Block.csproj')
if ($blockCsproj -match '<ProjectReference') {
    Add-Violation 'C1' 'BasicMethod.Block 必须保持零 ProjectReference（执行内核为叶子层）。'
}

# ---------- C2：BasicMethod 不得反向依赖 Pipeline / Circuit ----------
Get-CSharpFiles $basicRoot | ForEach-Object {
    $text = Get-Content -LiteralPath $_.FullName -Raw
    if ($text -match 'using\s+(Modules\.Pipeline|Share\.CircuitFramework)\s*;') {
        Add-Violation 'C2' "BasicMethod 反向依赖：$($_.FullName)"
    }
}
Get-ChildItem -LiteralPath $basicRoot -Recurse -File -Filter '*.csproj' |
    Where-Object { $_.FullName -notmatch '\\obj\\' } |
    ForEach-Object {
        $text = Get-Content -LiteralPath $_.FullName -Raw
        if ($text -match '\$\(Proj_Pipeline\)|\$\(Proj_CircuitFramework\)|CircuitFramework\.csproj|Modules\\Composition\\Pipeline') {
            Add-Violation 'C2' "BasicMethod csproj 反向引用：$($_.FullName)"
        }
    }

# ---------- C3：执行循环 / 重试唯一 ----------
$runnerText = Read-Text (Join-Path $kernelRoot 'BlockRunner.cs')
if (-not $runnerText) {
    Add-Violation 'C3' '缺少 BlockRunner.cs（唯一执行循环）。'
}
elseif ($runnerText -notmatch 'ExecuteWithPolicyAsync') {
    Add-Violation 'C3' 'BlockRunner 未持有重试实现（ExecuteWithPolicyAsync 缺失）。'
}

if ((Read-Text (Join-Path $kernelRoot 'ExecutionPipeline.cs')) -match 'ExecuteWithRetryAsync') {
    Add-Violation 'C3' 'ExecutionPipeline 仍残留 ExecuteWithRetryAsync（重复重试实现）。'
}
if ((Read-Text (Join-Path $circuitRoot 'Execution\CircuitEngine.cs')) -match 'RunWithPolicyAsync') {
    Add-Violation 'C3' 'CircuitEngine 仍残留 RunWithPolicyAsync（自建重试/退避）。'
}

# ---------- C4：结果类型唯一 ----------
$blockFiles = Get-ChildItem -LiteralPath $kernelRoot -File -Include '*.cs' |
    Where-Object { $_.FullName -notmatch '\\obj\\' }

$blockRunResultDefs = @($blockFiles | Where-Object {
    (Get-Content -LiteralPath $_.FullName -Raw) -match 'record\s+BlockRunResult|class\s+BlockRunResult'
})
if ($blockRunResultDefs.Count -ne 1) {
    Add-Violation 'C4' "BlockRunResult 定义数应为 1，实际 $($blockRunResultDefs.Count)。"
}

$legacyResultDefs = @($blockFiles | Where-Object {
    (Get-Content -LiteralPath $_.FullName -Raw) -match 'record\s+PipelineResult\b|class\s+PipelineResult\b'
})
if ($legacyResultDefs.Count -gt 1) {
    Add-Violation 'C4' "Block 内 PipelineResult 兼容壳定义数应 ≤ 1，实际 $($legacyResultDefs.Count)。"
}

# ---------- C5：Pipeline 适配层委托内核 ----------
$coreText = Read-Text (Join-Path $pipelineRoot 'Core.cs')
if ($coreText -notmatch 'BlockRunner\.Shared') {
    Add-Violation 'C5' 'StagePipeline 未委托 BlockRunner。'
}
if ($coreText -notmatch 'StageBlockAdapter') {
    Add-Violation 'C5' 'StagePipeline 未使用 StageBlockAdapter。'
}
if (-not (Test-Path (Join-Path $pipelineRoot 'Kernel\BlockContextBridge.cs'))) {
    Add-Violation 'C5' '缺少 Pipeline/Kernel/BlockContextBridge.cs。'
}

$staticKernel = Read-Text (Join-Path $pipelineRoot 'StaticPipeline\StaticPipelineKernel.cs')
if ($staticKernel -notmatch 'BlockRunner\.Shared') {
    Add-Violation 'C5' 'StaticPipeline 未委托 BlockRunner。'
}
if ((Read-Text (Join-Path $pipelineRoot 'StaticPipeline\PipelineExecutor.cs')) -match 'foreach\s*\(') {
    Add-Violation 'C5' 'PipelineExecutor 仍含自建 foreach 执行循环。'
}

# ---------- C6：Circuit 走 BlockPipeline ----------
$circuitText = Read-Text (Join-Path $circuitRoot 'Execution\CircuitEngine.cs')
if ($circuitText -notmatch 'BlockPipeline') {
    Add-Violation 'C6' 'CircuitEngine 未使用 BlockPipeline。'
}
if ($circuitText -match 'new\s+StagePipeline<PipelineContext>') {
    Add-Violation 'C6' 'CircuitEngine 仍创建 StagePipeline（未收敛到 BlockPipeline）。'
}

# ---------- C7：正/负对照（防判据恒真/恒假） ----------
$positive = 'using Modules.Pipeline;'
$negative = 'using Modules.PipelineHelpers;'
if ($positive -notmatch 'using\s+(Modules\.Pipeline|Share\.CircuitFramework)\s*;') {
    Add-Violation 'C7' '正向样本未命中（判据失效）。'
}
if ($negative -match 'using\s+(Modules\.Pipeline|Share\.CircuitFramework)\s*;') {
    Add-Violation 'C7' '负向样本误命中（判据过宽）。'
}

if ($violations.Count -eq 0) {
    Write-Host '[OK] OrchestrationKernel：C1–C7 全部通过。' -ForegroundColor Green
    exit 0
}

Write-Host '[FAIL] OrchestrationKernel 违规：' -ForegroundColor Red
$violations | ForEach-Object { Write-Host "  $_" -ForegroundColor Yellow }

if ($Check) { exit 1 }
exit 0
