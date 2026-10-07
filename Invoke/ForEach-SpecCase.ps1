<#
.SYNOPSIS
    【入口】按选择器批量遍历用例（只读 · 施工卡 2-2）
.DESCRIPTION
    Frame\Invoke 统一入口 —— 转发到 Environment\CentralTest\Tools\ForEach-SpecCase.ps1。
    只读 / 幂等 / 零写盘；标注每个用例的调用形态（inline / use / when / none）。
#>
param(
    [string]$CasesRoot = '',

    [string]$SuitePattern = '',

    [string]$CasePattern = '',

    [string]$Shape = '',

    [string]$Out = ''
)

$target = Join-Path $PSScriptRoot '..\..\Environment\CentralTest\Tools\ForEach-SpecCase.ps1'

$params = @{}
if ($PSBoundParameters.ContainsKey('CasesRoot')) { $params['CasesRoot'] = $CasesRoot }
if ($PSBoundParameters.ContainsKey('SuitePattern')) { $params['SuitePattern'] = $SuitePattern }
if ($PSBoundParameters.ContainsKey('CasePattern')) { $params['CasePattern'] = $CasePattern }
if ($PSBoundParameters.ContainsKey('Shape')) { $params['Shape'] = $Shape }
if ($PSBoundParameters.ContainsKey('Out')) { $params['Out'] = $Out }

$rc = 0
try {
    & $target @params
    $rc = if ($null -eq $LASTEXITCODE) { 2 } else { $LASTEXITCODE }
} catch {
    $rc = 2
    Write-Error $_.Exception.Message
}
exit $rc
