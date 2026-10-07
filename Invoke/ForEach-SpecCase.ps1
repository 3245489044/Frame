<#
.SYNOPSIS
    【入口】按选择器批量遍历用例（只读 · 施工卡 2-2）
.DESCRIPTION
    Frame\Invoke 统一入口 —— 转发到 SpecSourceGen.Cli 的 `case-list`（loc 位置反转：机制在 Core\Authoring）。
    只读 / 幂等 / 零写盘；标注每个用例的调用形态（inline / use / when / none）。
#>
param(
    [string]$CasesRoot = '',

    [string]$SuitePattern = '',

    [string]$CasePattern = '',

    [string]$Shape = '',

    [string]$Out = ''
)

$target = Join-Path $PSScriptRoot '..\..\Environment\CentralTest\SpecSourceGen\Cli\SpecSourceGen.Cli.csproj'
$dll = Join-Path $PSScriptRoot '..\..\Environment\CentralTest\BuildOutput\Debug\net10.0\SpecSourceGen.Cli.dll'

if (-not $PSBoundParameters.ContainsKey('CasesRoot') -or [string]::IsNullOrWhiteSpace($CasesRoot)) {
    $CasesRoot = Join-Path $PSScriptRoot '..\..\Environment\CentralTest\AgentTest'
}

$cliArgs = [System.Collections.Generic.List[string]]::new()
$cliArgs.Add('case-list')
$cliArgs.Add('--cases'); $cliArgs.Add($CasesRoot)
if ($PSBoundParameters.ContainsKey('SuitePattern')) { $cliArgs.Add('--suite-pattern'); $cliArgs.Add($SuitePattern) }
if ($PSBoundParameters.ContainsKey('CasePattern')) { $cliArgs.Add('--case-pattern'); $cliArgs.Add($CasePattern) }
if ($PSBoundParameters.ContainsKey('Shape')) { $cliArgs.Add('--shape'); $cliArgs.Add($Shape) }
if ($PSBoundParameters.ContainsKey('Out')) { $cliArgs.Add('--out'); $cliArgs.Add($Out) }

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
