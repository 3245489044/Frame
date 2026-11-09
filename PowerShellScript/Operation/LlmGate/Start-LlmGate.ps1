<#
.SYNOPSIS
    启动 LlmGate（LLM API 观测与拦截代理）—— 薄转发到 CallCenter.Host --llm-gate。
.DESCRIPTION
    唯一入口 = Frame\Invoke\Invoke-Frame.ps1 -Action llm-gate。
    客户端把 OpenAI 兼容 BaseUrl 指向 http://127.0.0.1:8790，即可被逐调用观测与拦截。
    阈值单源：Config\llm-gate\gate.json；价格表：Config\llm-gate\pricing.json。
.EXAMPLE
    Frame\Invoke\Invoke-Frame.ps1 -Action llm-gate -Mode enforce
#>
[CmdletBinding()]
param(
    [int]$Port = 0,
    [string]$Listen = '',
    [string]$Upstream = '',
    [string]$Mode = '',
    [string]$Config = '',
    [string]$DataRoot = '',
    [string]$Pricing = ''
)

$ErrorActionPreference = 'Stop'

function Find-RepoRoot {
    $dir = New-Object System.IO.DirectoryInfo $PSScriptRoot
    while ($null -ne $dir) {
        if ((Test-Path -LiteralPath (Join-Path $dir.FullName 'Frame')) -and
            (Test-Path -LiteralPath (Join-Path $dir.FullName 'Projects'))) {
            return $dir.FullName
        }
        $dir = $dir.Parent
    }
    return $null
}

try {
    $repoRoot = Find-RepoRoot
    if (-not $repoRoot) { throw '无法定位仓库根（需同时含 Frame 与 Projects）。' }

    $configuration = if ($env:WF_BUILD_CONFIGURATION) { $env:WF_BUILD_CONFIGURATION } else { 'Debug' }
    $hostExe = Join-Path $repoRoot ("Frame\CallCenter\BuildOutput\{0}\CallCenter.Host.exe" -f $configuration)
    if (-not (Test-Path -LiteralPath $hostExe)) {
        throw "未找到 $hostExe。请先构建 CallCenter（Invoke-Frame.ps1 -Action invoke-verify -Changed Frame\CallCenter\）。"
    }

    $argList = @('--llm-gate')
    if ($Port -gt 0) { $argList += @('--port', [string]$Port) }
    if ($Listen) { $argList += @('--listen', $Listen) }
    if ($Upstream) { $argList += @('--upstream', $Upstream) }
    if ($DataRoot) { $argList += @('--data-root', $DataRoot) }
    if ($Config) { $argList += @('--config', $Config) }
    if ($Pricing) { $argList += @('--pricing', $Pricing) }

    # dev/locked 属其它动作的 -Mode 取值；对 llm-gate 视为「未指定」，交 gate.json。
    $effectiveMode = $Mode
    if ($effectiveMode -in @('dev', 'locked')) { $effectiveMode = '' }
    if ($effectiveMode) { $argList += @('--mode', $effectiveMode) }

    Write-Host ("[llm-gate] 启动：{0} {1}" -f $hostExe, ($argList -join ' ')) -ForegroundColor Cyan
    & $hostExe @argList
    exit $LASTEXITCODE
}
catch {
    Write-Error ("[llm-gate] 启动失败：{0}" -f $_.Exception.Message)
    exit 2
}
