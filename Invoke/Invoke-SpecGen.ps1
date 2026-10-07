#requires -Version 5.1
<#
.SYNOPSIS
    spec 生成入口（PS 可调用）：调用 SpecSourceGen.Cli 的 `gen`（复用内核同管线，产物落生成根）。

.DESCRIPTION
    R1 目标：把「生成」变成一个 PS 能调、产物能清点的动作。**零第二套生成器** ——
    逐字复用宿主 A/B 的发射器（`.spec.json` / 壳类 `*.g.cs` / `SpecPorts.g.cs`），故与既有装配产物一致。
    产物落生成根（缺省 `BuildOutput\Debug\SpecGen`），不入源码树（守卫 SpecBoundary）。

    退出码：0 绿 / 1 声明源或生成红 / 2 用法或环境错误。

.PARAMETER Project
    工程名（壳类命名空间 / 生成根工程分层；必填）。
.PARAMETER Source
    紧凑源文件或目录（可多值；递归 `*.specs`，跳过 obj/bin/BuildOutput）。
.PARAMETER Config
    工程接线文件（`projects.specs`；缺省自源目录上溯）。
.PARAMETER Ports
    端口声明目录（可选；收集 `SpecPorts.g.cs`）。
.PARAMETER Out
    生成根（缺省 `BuildOutput\Debug\SpecGen`）。
.PARAMETER Manifest
    另出 `_manifest.json`（源 → 产物 + sha256）。
.PARAMETER DryRun
    只列清单，零写盘。

.EXAMPLE
    .\Invoke-SpecGen.ps1 -Project Agent.Core.Tests -Source 'Environment\CentralTest\AgentTest\Agent.Core.Tests' -Manifest
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Project,
    [Parameter(Mandatory = $true)][string[]]$Source,
    [string]$Config = '',
    [string]$Ports = '',
    [string]$Out = '',
    [switch]$Manifest,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

try {
    # ── 仓库根：锚定「含 Tools\lib\SpecPaths.psm1」的超级工程根（Frame 是子模块，.git 会锚错，故不单用 .git 哨兵）──
    $repo = $PSScriptRoot
    while ($repo -and -not (Test-Path -LiteralPath (Join-Path $repo 'Environment\CentralTest\Tools\lib\SpecPaths.psm1'))) {
        $repo = Split-Path -Parent $repo
    }
    if (-not $repo) { throw '未能定位仓库根（应含 Environment\CentralTest\Tools\lib\SpecPaths.psm1）' }

    Import-Module (Join-Path $repo 'Environment\CentralTest\Tools\lib\SpecPaths.psm1') -Force -DisableNameChecking

    $cli = Join-Path (Get-SpecCentralTestRoot) 'SpecSourceGen\Cli\SpecSourceGen.Cli.csproj'
    if (-not (Test-Path -LiteralPath $cli)) { throw "CLI 工程不存在：$cli" }

    $cliArgs = [System.Collections.Generic.List[string]]::new()
    foreach ($token in @('run', '--project', $cli, '-c', 'Debug', '--')) { $cliArgs.Add($token) }
    foreach ($token in @('gen', '--project', $Project)) { $cliArgs.Add($token) }
    foreach ($s in $Source) { $cliArgs.Add('--source'); $cliArgs.Add($s) }
    if ($Config) { $cliArgs.Add('--config'); $cliArgs.Add($Config) }
    if ($Ports) { $cliArgs.Add('--ports'); $cliArgs.Add($Ports) }
    if ($Out) { $cliArgs.Add('--out'); $cliArgs.Add($Out) }
    if ($Manifest) { $cliArgs.Add('--manifest') }
    if ($DryRun) { $cliArgs.Add('--dry-run') }

    & dotnet $cliArgs.ToArray()
    $rc = $LASTEXITCODE
} catch {
    Write-Error $_
    $rc = 2
}

exit $rc
