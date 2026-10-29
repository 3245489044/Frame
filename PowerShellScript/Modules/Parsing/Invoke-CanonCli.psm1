#requires -Version 5.1
<#
.SYNOPSIS
    canon-replay CLI 单实现（D-4）—— 「调 CLI + 抽 JSON」的唯一落点。
.DESCRIPTION
    判据载体 = CLI 进程与其 stdout 文本；本模块不识别、不判定（判定归各门禁）。
    RoundShape / CanonicalForm / CanonReplay 三个门禁只经本模块，禁各自内联正则抽取（CLI 输出形态一改即三处同时坏）。
#>

function Resolve-CanonCliPath
{
    param([Parameter(Mandatory)][string]$Root)

    $path = Join-Path $Root 'Frame\TypeScriptPrj\TsWorkspace\ClineApi\build\cli.js'
    if (Test-Path -LiteralPath $path) { return $path }
    return $null
}

function ConvertFrom-CanonCliOutput
{
    param([string]$Raw)

    if ([string]::IsNullOrWhiteSpace($Raw)) { return $null }
    $start = $Raw.IndexOf('{')
    if ($start -lt 0) { return $null }
    try { return ($Raw.Substring($start) | ConvertFrom-Json) } catch { return $null }
}

function Invoke-CanonReplay
{
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$Agent,
        [Parameter(Mandatory)][string]$RoundFile,
        [string]$SpecPath,
        [string]$NodeExe
    )

    $cli = Resolve-CanonCliPath -Root $Root
    if ($null -eq $cli) { return $null }

    if ([string]::IsNullOrWhiteSpace($NodeExe)) {
        $NodeExe = if ($env:CLINE_NODE) { $env:CLINE_NODE } else { 'node' }
    }

    $cliArgs = @($cli, 'canon-replay', '--agent', $Agent, '--round', $RoundFile)
    if (-not [string]::IsNullOrWhiteSpace($SpecPath)) { $cliArgs += @('--spec', $SpecPath) }

    $raw = & $NodeExe @cliArgs 2>$null | Out-String
    return ConvertFrom-CanonCliOutput -Raw $raw
}

Export-ModuleMember -Function Resolve-CanonCliPath, ConvertFrom-CanonCliOutput, Invoke-CanonReplay
