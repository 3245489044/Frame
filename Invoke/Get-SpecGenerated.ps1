<#
.SYNOPSIS
    【入口】生成物清点（只读 _manifest.json；-Verify 复核 sha256）
.DESCRIPTION
    Frame\Invoke 统一入口 —— 转发到 SpecSourceGen.Cli 的 `generated-list`（loc 位置反转：机制在 Core/Cli）。
    只读 / 零写盘；判据载体 = 文件存在性 + sha256 内容指纹（非执行者叙述）。
.PARAMETER Manifest
    `_manifest.json` 路径（gen --manifest 产物）。
.PARAMETER Verify
    复核每个产物的实际 sha256 是否与清单一致（漂移即违规）。
.PARAMETER Json
    以 JSON 输出清单（供管道消费）。
.EXAMPLE
    .\Get-SpecGenerated.ps1 -Manifest 'Environment\...\_manifest.json' -Verify
#>
param(
    [Parameter(Mandatory = $true)][string]$Manifest,

    [switch]$Verify,

    [switch]$Json
)

$target = Join-Path $PSScriptRoot '..\..\Environment\CentralTest\SpecSourceGen\Cli\SpecSourceGen.Cli.csproj'
$dll = Join-Path $PSScriptRoot '..\..\Environment\CentralTest\BuildOutput\Debug\net10.0\SpecSourceGen.Cli.dll'

$cliArgs = [System.Collections.Generic.List[string]]::new()
$cliArgs.Add('generated-list')
$cliArgs.Add('--manifest'); $cliArgs.Add($Manifest)
if ($PSBoundParameters.ContainsKey('Verify')) { $cliArgs.Add('--verify') }
if ($PSBoundParameters.ContainsKey('Json')) { $cliArgs.Add('--emit-json') }

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
