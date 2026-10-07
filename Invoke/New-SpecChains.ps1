<#
.SYNOPSIS
    【入口】批量抽链（压回 S2 折叠膨胀；对含同一内联载荷 ≥2 次的套件一次处理）
.DESCRIPTION
    Frame\Invoke 统一入口 —— 转发到 SpecSourceGen.Cli 的 `chains-new`（loc 位置反转：机制在 Core\Authoring）。
    缺省 DryRun（零写盘：只列出将处理的套件）；-Write 落盘。
.PARAMETER CasesRoot
    用例根（缺省 = AgentTest）。
.PARAMETER MinUses
    抽链的最小引用次数（缺省 2）。
.PARAMETER Write
    落盘开关（缺省 DryRun）。
.EXAMPLE
    .\New-SpecChains.ps1
.EXAMPLE
    .\New-SpecChains.ps1 -Write
#>
param(
    [string]$CasesRoot = '',

    [int]$MinUses = 2,

    [switch]$Write
)

$target = Join-Path $PSScriptRoot '..\..\Environment\CentralTest\SpecSourceGen\Cli\SpecSourceGen.Cli.csproj'
$dll = Join-Path $PSScriptRoot '..\..\Environment\CentralTest\BuildOutput\Debug\net10.0\SpecSourceGen.Cli.dll'

if (-not $PSBoundParameters.ContainsKey('CasesRoot') -or [string]::IsNullOrWhiteSpace($CasesRoot)) {
    $CasesRoot = Join-Path $PSScriptRoot '..\..\Environment\CentralTest\AgentTest'
}

$cliArgs = [System.Collections.Generic.List[string]]::new()
$cliArgs.Add('chains-new')
$cliArgs.Add('--cases'); $cliArgs.Add($CasesRoot)
if ($PSBoundParameters.ContainsKey('MinUses')) { $cliArgs.Add('--min-share'); $cliArgs.Add([string]$MinUses) }
if ($PSBoundParameters.ContainsKey('Write')) { $cliArgs.Add('--write') }

$rc = 0
try {
    if (Test-Path -LiteralPath $dll) {
        & dotnet $dll $cliArgs
    } else {
        & dotnet run --project $target -c Debug -- $cliArgs
    }
    $rc = if ($null -eq $LASTEXITCODE) { 2 } else { $LASTEXITCODE }
} catch {
    $rc = 2
    Write-Error $_.Exception.Message
}
exit $rc
