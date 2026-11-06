#requires -Version 7.0
<#
.SYNOPSIS
    空因活性守卫（UPC D-F3）：每个受控空因码必须有生产点与消费点（禁压平、禁死码）。

.DESCRIPTION
    判据载体 = 受控词表（PortContextEmptyCodes）+ 源码文本（结构事实）。
      EL1 生产点：每个空因码在装配器/适配器源码中出现 ≥ 1。
      EL2 消费点：三出口侧存在 EmptyCode 透传（≥1），否则空因无法到达出口。
      EL3 负对照（-NegativeControl）：合成「丢弃 code」样本必须判红。
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
    $ctx = New-GateContext -ScriptRoot $PSScriptRoot -Root $Root -Check:$Check -Key 'EmptyReasonLiveness'
    $Root = $ctx.Root
}

Import-Module (Join-Path $Root 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking

# 受控码表已下沉 Modules\Foundations\PortContext（Share 侧仅留装配器）；判据载体随实现位置，禁指向旧路径。
$framePrj = Join-Path (Get-MappedPath -Key 'Frame') 'CsharpPrj'
$specPath = Join-Path $framePrj 'Modules\Foundations\PortContext\PortContextSpec.cs'
$agentSrc = Get-MappedPath -Key 'AgentSrc'
$producerPaths = @(
    (Join-Path $framePrj 'Share\Frameworks\PortContext\PortContextAssembler.cs'),
    (Join-Path $framePrj 'Share\Frameworks\PortContext\PortContextAssembler.Loaders.cs'),
    (Join-Path $framePrj 'Modules\Foundations\PortContext\PortContextModel.cs'),
    (Join-Path $framePrj 'Modules\Foundations\PortContext\PortContextPage.cs'),
    (Join-Path $framePrj 'Modules\Foundations\PortContext\PortContextPorts.cs'),
    (Join-Path $framePrj 'Modules\Foundations\PortContext\PortContextConsumers.cs'),
    (Join-Path $agentSrc 'Agent.Launcher\Composition\Hosting\Session\PortContextAssembly.cs'),
    (Join-Path $agentSrc 'Agent.Launcher\Composition\Hosting\Session\PortContextAssembly.Adapters.cs')
)
$consumerPaths = @(
    (Join-Path $agentSrc 'Agent.Ui\Core\Projection\HistoryBoardReadModelProjector.cs'),
    (Join-Path $agentSrc 'Agent.Session\Projection\History\HistoryTaskRuntimePromptProjector.cs'),
    (Join-Path $agentSrc 'Agent.Modules\Response\Runtime\HistorySessionSeedContributor.cs')
)

function Read-All([string[]]$paths) {
    $parts = foreach ($path in $paths) {
        if (Test-Path -LiteralPath $path) { [IO.File]::ReadAllText($path) }
    }
    return ($parts -join "`n")
}

function Get-EmptyCodes([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { throw "空因码表缺失：$path（判据不可判定）" }
    $text = [IO.File]::ReadAllText($path)
    $start = $text.IndexOf('class PortContextEmptyCodes', [StringComparison]::Ordinal)
    if ($start -lt 0) { throw '未定位 PortContextEmptyCodes（判据不可判定）' }
    $block = $text.Substring($start)
    return @([regex]::Matches($block, 'public const string (?<name>\w+)\s*=\s*"(?<code>[a-z][a-z0-9-]+)"') |
        ForEach-Object { [pscustomobject]@{ Name = $_.Groups['name'].Value; Code = $_.Groups['code'].Value } } |
        Sort-Object Code -Unique)
}

$codes = Get-EmptyCodes $specPath
$producerText = Read-All $producerPaths
$consumerText = Read-All $consumerPaths
$violations = [System.Collections.Generic.List[string]]::new()

if ($codes.Count -eq 0) { $violations.Add('[EL0] 空因码表为空（判据空转）') }
foreach ($pair in $codes) {
    $bySymbol = "PortContextEmptyCodes\.$($pair.Name)\b"
    if ([regex]::IsMatch($producerText, $bySymbol) -or [regex]::IsMatch($producerText, [regex]::Escape($pair.Code))) {
        Write-Host ("[ok] {0}: 生产点在册" -f $pair.Code) -ForegroundColor Green
    } else {
        $violations.Add("[EL1] 空因码 $($pair.Code) 无生产点（死码）")
    }
}
if (-not [regex]::IsMatch($consumerText, 'EmptyCode|EmptyReason')) {
    $violations.Add('[EL2] 出口侧未见 EmptyCode/EmptyReason 透传（空因被压平）')
}

if ($NegativeControl) {
    $sample = 'if (rows == 0) return "(empty) 未挂载任何 task";'
    if ([regex]::IsMatch($sample, 'EmptyCode|EmptyReason')) { throw '负对照失败：丢弃 code 样本被误判为透传' }
    $positive = 'return $"task.runtime: (empty: {context.EmptyCode})";'
    if (-not [regex]::IsMatch($positive, 'EmptyCode')) { throw '正对照失败：透传样本未被识别' }
    Write-Host '[OK] 负对照通过：空因压平样本可判红。' -ForegroundColor Green
}

if ($Check) {
    Write-GateVerdict -Violations $violations -Check -Key 'EmptyReasonLiveness'
}

exit 0
