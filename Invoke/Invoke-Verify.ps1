<#
.SYNOPSIS
    【入口】验证（构建 + 测试 + 门禁 一次完成）
.DESCRIPTION
    Frame\Invoke 统一入口 —— 转发到 Environment\CentralTest\Invoke-Verify.ps1。
    P2 唯一验证命令（VERIFY_SINGLE_ENTRY 执行体）：内部串 bake→test→gate，agent 面 shell 调用计 1。
    消费 Locate-Build.ps1（Get-VerifyPlan）产出的可冻结清单；验证前置对齐由 Run-CSharpTests.ps1 承担。
    拆分式 build/test/gate 多条调用、原生 dotnet 直呼 ⇒ 违反唯一命令纪律（判 ANTI-MULTI-RUN-VERIFY）。
.PARAMETER Changed
    改动文件（仓库相对路径或绝对路径；多值）。与 -Plan 二选一。
.PARAMETER Plan
    冻结的验证清单（JSON，Locate-Build -OutFile 产出）。与 -Changed 二选一。
.PARAMETER Configuration
    构建配置（默认 Debug）。
.PARAMETER TestSet
    产品改动时须运行的测试矩阵（default/agent/infra；默认 default）。
.EXAMPLE
    .\Invoke-Verify.ps1 -Changed 'Frame\CsharpPrj\Share\X.cs'
.EXAMPLE
    .\Invoke-Verify.ps1 -Plan 'BuildOutput\Debug\verify-plan.json'
#>
param(
    [string[]]$Changed = @(),

    [string]$Plan = '',

    [string]$Configuration = 'Debug',

    [ValidateSet('default', 'agent', 'infra')]
    [string]$TestSet = 'default'
)

$target = Join-Path $PSScriptRoot "..\..\Environment\CentralTest\Invoke-Verify.ps1"

$params = @{}
if ($PSBoundParameters.ContainsKey('Changed') -and $Changed.Count -gt 0) { $params['Changed'] = $Changed }
if ($PSBoundParameters.ContainsKey('Plan') -and $Plan) { $params['Plan'] = $Plan }
if ($PSBoundParameters.ContainsKey('Configuration')) { $params['Configuration'] = $Configuration }
if ($PSBoundParameters.ContainsKey('TestSet'))       { $params['TestSet'] = $TestSet }

$rc = 0
try {
    & $target @params
    $rc = if ($null -eq $LASTEXITCODE) { 2 } else { $LASTEXITCODE }
} catch {
    $rc = 2
    Write-Error $_.Exception.Message
}
exit $rc
