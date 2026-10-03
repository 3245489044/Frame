<#
.SYNOPSIS
    【入口】诊断相位 · 零运行定位（失败测试名 / caseId → 唯一工程）
.DESCRIPTION
    Frame\Invoke 统一入口 —— 转发到 Environment\CentralTest\Run-CSharpTests.ps1 -Locate。
    诊断相位（P0）：只读 / 零裁决 / 不试跑；用于「失败测试落点未知」时**一次定位**，
    禁逐工程试跑（分析文档 R1/A 根治）。诊断运行不产出 pass/fail，不计入验证账（VERIFY_CALL_BUDGET）。
    索引源（唯一真源，零运行）：`AgentTest\**\Cases\**\<suiteId>.specs` 的 caseId 行
      + TestTargets.psd1 别名表 + 各测试工程源码的 class/record 声明。
.PARAMETER Name
    测试类名（可带命名空间）/ caseId / `suiteId.caseId` 显示名 / 别名（多值 = OR）。
.PARAMETER Configuration
    构建配置（默认 Debug，仅用于对齐产物目录，不触发构建）。
.EXAMPLE
    .\Locate-Test.ps1 -Name ConversationPipelineTests
.EXAMPLE
    .\Locate-Test.ps1 -Name core.signal-workflow-runner.run-round-async-without-spec
#>
param(
    [Parameter(Mandatory = $true)]
    [string[]]$Name,

    [string]$Configuration = 'Debug'
)

$target = Join-Path $PSScriptRoot "..\..\Environment\CentralTest\Run-CSharpTests.ps1"

$params = @{ Locate = $Name }
if ($PSBoundParameters.ContainsKey('Configuration')) { $params['Configuration'] = $Configuration }

$rc = 0
try {
    & $target @params
    $rc = if ($null -eq $LASTEXITCODE) { 2 } else { $LASTEXITCODE }
} catch {
    $rc = 2
    Write-Error $_.Exception.Message
}
exit $rc
