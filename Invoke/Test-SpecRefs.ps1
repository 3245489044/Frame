<#
.SYNOPSIS
    【入口】引用完整性（悬空 use / 未解析引用）
.DESCRIPTION
    Frame\Invoke 统一入口 —— 转发到 SpecSourceGen.Cli 的 `refs`（loc 位置反转：机制在 Core\Authoring）。
    只读 / 零写盘；-Check 自检（正/负对照），违规 exit 1。
.PARAMETER CasesRoot
    用例根（缺省 = AgentTest）。
.PARAMETER Check
    自检模式（正/负对照，防解析器空转）；违规 exit 1。
.EXAMPLE
    .\Test-SpecRefs.ps1 -Check
#>
param(
    [string]$CasesRoot = '',

    [switch]$Check
)

$target = Join-Path $PSScriptRoot '..\..\Environment\CentralTest\SpecSourceGen\Cli\SpecSourceGen.Cli.csproj'
$dll = Join-Path $PSScriptRoot '..\..\Environment\CentralTest\BuildOutput\Debug\net10.0\SpecSourceGen.Cli.dll'

if (-not $PSBoundParameters.ContainsKey('CasesRoot') -or [string]::IsNullOrWhiteSpace($CasesRoot)) {
    $CasesRoot = Join-Path $PSScriptRoot '..\..\Environment\CentralTest\AgentTest'
}

$cliArgs = [System.Collections.Generic.List[string]]::new()
$cliArgs.Add('refs')
$cliArgs.Add('--cases'); $cliArgs.Add($CasesRoot)
if ($PSBoundParameters.ContainsKey('Check')) { $cliArgs.Add('--check') }

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
