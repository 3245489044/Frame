<#
.SYNOPSIS
    【入口】JSON 批量校验
.DESCRIPTION
    Frame\Invoke 统一入口 —— 转发到 Frame\PowerShellScript\Share\Validate\Test-JsonFiles.ps1
    批量校验 JSON/JSONC 文件语法有效性，替代手写 foreach + ConvertFrom-Json。
.PARAMETER Path
    文件或目录（目录递归），必填。
.PARAMETER Filter
    文件过滤，默认 *.json。
.PARAMETER Exclude
    排除路径正则，默认排除 BuildOutput/obj/bin/_deprecated/node_modules/backups/TestResults。
.PARAMETER Schema
    可选 schema 文件（仅做必需键存在性校验）。
.PARAMETER Quiet
    仅输出失败与汇总。
.EXAMPLE
    .\Test-JsonFiles.ps1 -Path .\configs
.EXAMPLE
    .\Test-JsonFiles.ps1 -Path appsettings.json -Quiet
#>
param(
    [Parameter(Mandatory = $true)][string[]]$Path,
    [string]$Filter = '*.json',
    [string]$Exclude = 'BuildOutput|obj|bin|_deprecated|node_modules|backups|TestResults',
    [string]$Schema = '',
    [switch]$Quiet
)

$target = Join-Path $PSScriptRoot "..\PowerShellScript\Share\Validate\Test-JsonFiles.ps1"

$params = @{ Path = $Path }
if ($PSBoundParameters.ContainsKey('Filter'))  { $params['Filter'] = $Filter }
if ($PSBoundParameters.ContainsKey('Exclude')) { $params['Exclude'] = $Exclude }
if ($PSBoundParameters.ContainsKey('Schema'))  { $params['Schema'] = $Schema }
if ($Quiet)   { $params['Quiet'] = $true }

& $target @params
exit $LASTEXITCODE
