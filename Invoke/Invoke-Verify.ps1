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
    测试矩阵（default/agent/infra）。空 = 用计划（局部优先，免授权）；显式给值 = 全量面请求（默认禁止，须授权）。
.PARAMETER GrantFile
    全量面授权单（JSON；machine-readable）。全面测试（矩阵/全部门禁 -All）默认禁止，须持授权
    （授权来源优先级：-GrantFile > 批次授权单 verify 字段 > 宿主令牌；权威 .clinerules\axioms.md §9）。
.EXAMPLE
    .\Invoke-Verify.ps1 -Changed 'Frame\CsharpPrj\Share\X.cs'
.EXAMPLE
    .\Invoke-Verify.ps1 -Plan 'BuildOutput\Debug\verify-plan.json'
#>
param(
    [string[]]$Changed = @(),

    [string]$Plan = '',

    [string]$Configuration = 'Debug',

    # 空 = 用计划产出的测试面（局部优先）；显式给值 = 全量面请求（默认禁止，须授权）
    [string]$TestSet = '',

    # 全量面授权单（JSON，machine-readable；授权来源优先级最高）
    [string]$GrantFile = ''
)

if ($TestSet -and $TestSet -notin @('default', 'agent', 'infra')) {
    Write-Error "TestSet 取值非法：'$TestSet'（合法：default / agent / infra，或留空用计划）"
    exit 2
}

$target = Join-Path $PSScriptRoot "..\..\Environment\CentralTest\Invoke-Verify.ps1"

$params = @{}
if ($PSBoundParameters.ContainsKey('Changed') -and $Changed.Count -gt 0) { $params['Changed'] = $Changed }
if ($PSBoundParameters.ContainsKey('Plan') -and $Plan) { $params['Plan'] = $Plan }
if ($PSBoundParameters.ContainsKey('Configuration')) { $params['Configuration'] = $Configuration }
if ($PSBoundParameters.ContainsKey('TestSet') -and $TestSet)       { $params['TestSet'] = $TestSet }
if ($PSBoundParameters.ContainsKey('GrantFile') -and $GrantFile)   { $params['GrantFile'] = $GrantFile }

$rc = 0
try {
    & $target @params
    $rc = if ($null -eq $LASTEXITCODE) { 2 } else { $LASTEXITCODE }
} catch {
    $rc = 2
    Write-Error $_.Exception.Message
}
exit $rc
