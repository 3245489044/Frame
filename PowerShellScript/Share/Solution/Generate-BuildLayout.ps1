<#
.SYNOPSIS
    生成产物布局声明件（Frame\generated\BuildLayout.generated.props）。
.DESCRIPTION
    单一生成器：任何产品 props 不得自表达「求值期 Configuration 默认」这一事实。
    生成物被自包含 props（Engine / DataProcess）Import，保证 OutputPath 求值前 Configuration 非空
    （否则落成扁平 BuildOutput\，装载面按 <Config> 探测即失配）。
    守卫 = Operation\DSL\Meta\Check-BuildLayout.ps1。
    用法：.\Generate-BuildLayout.ps1 [-WhatIf]
#>
[CmdletBinding()]
param([switch]$WhatIf)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$outPath = Join-Path $root 'Frame\generated\BuildLayout.generated.props'

$stamp = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
$content = @"
<Project>

  <!-- ============================================================
       产物布局生成件（单一真源；自动生成，请勿手改）
       生成器：Frame\PowerShellScript\Share\Solution\Generate-BuildLayout.ps1
       生成时间: $stamp

       不变量：在自包含 props（Engine / DataProcess 等不 Import 上游 props 的工程）
       求值 OutputPath 之前，`$(Configuration) 必须已有值；否则
       BuildOutput\`$(Configuration)\ 会固化为扁平 BuildOutput\（产物分裂、装载面失配）。
       守卫：Check-BuildLayout.ps1（L1 配置层 / L2 必须 Import 本件 / L3 禁扁平层）
       ============================================================ -->
  <PropertyGroup Label="BuildLayoutGenerated">
    <BuildLayoutGenerated>true</BuildLayoutGenerated>
    <Configuration Condition="'`$(Configuration)' == ''">Debug</Configuration>
    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
  </PropertyGroup>

</Project>
"@

Write-Host ("[BuildLayout] 输出: {0}" -f $outPath) -ForegroundColor Cyan
if ($WhatIf) {
    Write-Host '[BuildLayout] -WhatIf：仅报告，不写盘' -ForegroundColor Yellow
    exit 0
}

$dir = Split-Path -Parent $outPath
if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
Set-Content -LiteralPath $outPath -Value $content -Encoding UTF8
Write-Host '[BuildLayout] 已生成（单一真源）' -ForegroundColor Green
exit 0
