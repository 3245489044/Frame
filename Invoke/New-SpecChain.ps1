<#
.SYNOPSIS
    【入口】由内联 invocation 抽取命名链并改写引用（施工卡 2-2 · 写侧）
.DESCRIPTION
    Frame\Invoke 统一入口 —— 转发到 SpecSourceGen.Cli 的 `chain-new`（loc 位置反转：机制在 Core\Authoring）。
    缺省 DryRun（零写盘）；-Write 落盘。自带「未改写 / 悬空 / 空产物」三类结构自检。
.PARAMETER Suite
    目标套件 .specs 路径。
.PARAMETER Name
    链名（套件内唯一）。
.PARAMETER From
    提供载荷来源的 caseId（缺省 = 首个内联 invocation）。
.PARAMETER Write
    落盘开关（缺省 DryRun）。
.EXAMPLE
    .\New-SpecChain.ps1 -Suite 'Environment\...\x.specs' -Name probe
.EXAMPLE
    .\New-SpecChain.ps1 -Suite 'Environment\...\x.specs' -Name probe -Write
#>
param(
    [Parameter(Mandatory = $true)][string]$Suite,

    [Parameter(Mandatory = $true)][string]$Name,

    [string]$From = '',

    [switch]$Write
)

$target = Join-Path $PSScriptRoot '..\..\Environment\CentralTest\SpecSourceGen\Cli\SpecSourceGen.Cli.csproj'
$dll = Join-Path $PSScriptRoot '..\..\Environment\CentralTest\BuildOutput\Debug\net10.0\SpecSourceGen.Cli.dll'

$cliArgs = [System.Collections.Generic.List[string]]::new()
$cliArgs.Add('chain-new')
$cliArgs.Add('--suite'); $cliArgs.Add($Suite)
$cliArgs.Add('--name'); $cliArgs.Add($Name)
if ($PSBoundParameters.ContainsKey('From')) { $cliArgs.Add('--from'); $cliArgs.Add($From) }
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
