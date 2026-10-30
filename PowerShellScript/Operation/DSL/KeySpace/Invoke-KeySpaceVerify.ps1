# Invoke-KeySpaceVerify.ps1
# keyspace-verify（P4）：-Changed 直传 P2 唯一验证命令；计划缺 KeySpace 门禁时显式列缺口（fail-loud）。
# 只编排：门禁/测试/构建判定全部由 Get-VerifyPlan + Invoke-Verify 承担。
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string[]]$Changed,
    [string]$Configuration = 'Debug',
    [string]$GrantFile = ''
)

$ErrorActionPreference = 'Stop'

# 本件位于 Frame\PowerShellScript\Operation\DSL\KeySpace ⇒ 上溯 5 级 = 仓库根
$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..\..\..\..')).Path
$planScript = Join-Path $repoRoot 'Environment\CentralTest\Internal\Get-VerifyPlan.ps1'
$verifyScript = Join-Path $repoRoot 'Environment\CentralTest\Internal\Invoke-Verify.ps1'
foreach ($p in @($planScript, $verifyScript)) {
    if (-not (Test-Path -LiteralPath $p)) { Write-Error "前置缺失：$p"; exit 2 }
}

$plan = & $planScript -Changed $Changed -Configuration $Configuration -Json | ConvertFrom-Json
$required = @('KeyOpsClosedSet', 'KeyDeclSingleSource', 'KeyRoundTrip', 'KeyCorpusGolden')
$planned = @($plan.Gates)
$missing = @($required | Where-Object { $planned -notcontains $_ })
if ($missing.Count -gt 0) {
    Write-Host ("[GAP] 计划缺 KeySpace 门禁：{0} —— 改动未命中 P1 映射（BuildTargetMap.psd1）" -f ($missing -join ', ')) -ForegroundColor Red
    Write-Host ("      计划 Gates = {0}" -f ($(if ($planned.Count) { $planned -join ', ' } else { '(空)' }))) -ForegroundColor DarkGray
}

$verifyArgs = @{ Changed = $Changed; Configuration = $Configuration }
if ($GrantFile) { $verifyArgs['GrantFile'] = $GrantFile }
& $verifyScript @verifyArgs
exit $(if ($null -eq $LASTEXITCODE) { 2 } else { [int]$LASTEXITCODE })
