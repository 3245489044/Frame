<#
.SYNOPSIS
    【入口】把匹配用例的内联 invocation 切到既有链（施工卡 2-2 · 写侧）
.DESCRIPTION
    Frame\Invoke 统一入口 —— 转发到 SpecSourceGen.Cli 的 `chain-use`（loc 位置反转：机制在 Core\Authoring）。
    缺省 DryRun（零写盘）；-Write 落盘。三类结构自检（未改写 / 悬空 / 空产物）。
#>
param(
    [Parameter(Mandatory = $true)][string]$Suite,

    [Parameter(Mandatory = $true)][string]$Chain,

    [string]$Case = '',

    [switch]$Write
)

$target = Join-Path $PSScriptRoot '..\..\Environment\CentralTest\SpecSourceGen\Cli\SpecSourceGen.Cli.csproj'
$dll = Join-Path $PSScriptRoot '..\..\Environment\CentralTest\BuildOutput\Debug\net10.0\SpecSourceGen.Cli.dll'

$cliArgs = [System.Collections.Generic.List[string]]::new()
$cliArgs.Add('chain-use')
$cliArgs.Add('--suite'); $cliArgs.Add($Suite)
$cliArgs.Add('--chain'); $cliArgs.Add($Chain)
if ($PSBoundParameters.ContainsKey('Case')) { $cliArgs.Add('--case'); $cliArgs.Add($Case) }
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
