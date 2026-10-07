<#
.SYNOPSIS
    【入口】删除命名链（引用守卫：仍有 use / and use 引用即拒绝）
.DESCRIPTION
    Frame\Invoke 统一入口 —— 转发到 SpecSourceGen.Cli 的 `chain-remove`（loc 位置反转：机制在 Core\Authoring）。
    缺省 DryRun（零写盘）；-Write 落盘。自带「引用守卫 / 已删除」结构自检。
.PARAMETER Suite
    目标套件 .specs 路径。
.PARAMETER Chain
    链名（须已声明且无引用）。
.PARAMETER Write
    落盘开关（缺省 DryRun）。
.EXAMPLE
    .\Remove-SpecChain.ps1 -Suite 'Environment\...\x.specs' -Chain probe
#>
param(
    [Parameter(Mandatory = $true)][string]$Suite,

    [Parameter(Mandatory = $true)][string]$Chain,

    [switch]$Write
)

$target = Join-Path $PSScriptRoot '..\..\Environment\CentralTest\SpecSourceGen\Cli\SpecSourceGen.Cli.csproj'
$dll = Join-Path $PSScriptRoot '..\..\Environment\CentralTest\BuildOutput\Debug\net10.0\SpecSourceGen.Cli.dll'

$cliArgs = [System.Collections.Generic.List[string]]::new()
$cliArgs.Add('chain-remove')
$cliArgs.Add('--suite'); $cliArgs.Add($Suite)
$cliArgs.Add('--chain'); $cliArgs.Add($Chain)
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
