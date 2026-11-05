<#
.SYNOPSIS
    唯一启动执行体（launch）—— 闭包前置 → 构建（launch profile）→ 冒烟（可选）→ 启动。
.DESCRIPTION
    单一入口契约（权威: Docs\analysis\2026\11\11.5\2026-11-05-启动面装配闭包与布局单源-框架级缺陷治理施工单.md）：
      退出码 0 = 启动成功并已连接/正常结束；2 = 前置不满足；3 = 装配闭包不完整；
              4 = 构建失败；5 = 占用未解除；6 = 启动未连接（冒烟失败）。
    硬约束：「未执行任何动作」不得为 0（旧「未进 run 分支即 exit 0」已废除）。
    外部 bat 只做薄转发：Invoke-Frame.ps1 -Action launch [-Interactive]。
#>
[CmdletBinding()]
param(
    [switch]$Interactive,
    [switch]$NoBuild,
    [switch]$Smoke,
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
while ($root -and -not ((Test-Path -LiteralPath (Join-Path $root 'Frame')) -and (Test-Path -LiteralPath (Join-Path $root 'Projects')))) {
    $parent = Split-Path -Parent $root
    if ($parent -eq $root) { break }
    $root = $parent
}
if (-not $root) { Write-Host '[launch] 仓库超根定位失败' -ForegroundColor Red; exit 2 }

# 启动入口是唯一强制装配闭包的地方（库默认保持灰色降级）。
$env:FILEDRIVEN_REQUIRE_ASSEMBLY_CLOSURE = '1'
if ($env:WF_LAUNCH_NOBUILD -eq '1') { $NoBuild = $true }
if ($env:WF_LAUNCH_INTERACTIVE -eq '1') { $Interactive = $true }
if ($env:WF_LAUNCH_SMOKE -eq '1') { $Smoke = $true }

Write-Host ''
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host ("  FileDriven 启动（唯一入口）  配置={0}  交互={1}  构建={2}" -f $Configuration, [bool]$Interactive, (-not $NoBuild)) -ForegroundColor Cyan
Write-Host '============================================================' -ForegroundColor Cyan

# 1) 装配闭包前置断言（结构事实；缺件即停，给出资产/清单/根）
$closureGate = Join-Path $root 'Frame\PowerShellScript\Operation\DSL\Meta\Check-AssemblyClosure.ps1'
& $closureGate -Check
if ($LASTEXITCODE -ne 0) {
    Write-Host '[launch] exit 3：装配闭包不完整（见上方缺件清单）' -ForegroundColor Red
    exit 3
}

# 2) 构建（启动必需闭包 = BuildProfiles.launch；增量）
if (-not $NoBuild) {
    $build = Join-Path $root 'Projects\BuildFramework\Entry\Build-Product.ps1'
    & $build -All -Configuration $Configuration -Wait
    if ($LASTEXITCODE -ne 0) {
        Write-Host '[launch] exit 4：构建失败（见上方 / BuildOutput\.build-gate）' -ForegroundColor Red
        exit 4
    }
}

# 3) 冒烟（macro 判据；显式开启，默认不在每次启动时付 90s）
if ($Smoke) {
    $smokeGate = Join-Path $root 'Frame\PowerShellScript\Operation\DSL\Meta\Check-LaunchSmoke.ps1'
    & $smokeGate -Check -TimeoutSeconds 90
    if ($LASTEXITCODE -ne 0) {
        Write-Host '[launch] exit 6：启动冒烟未通过（未越过 bootstrap）' -ForegroundColor Red
        exit 6
    }
}

# 4) 启动（交互 = 测试运行器；默认 = GUI）
$argLine = [string]$env:WF_LAUNCH_ARGS
$forward = @()
if (-not [string]::IsNullOrWhiteSpace($argLine)) {
    $forward = @($argLine -split '\s+' | Where-Object { $_ })
}

if ($Interactive) {
    $runner = Join-Path $root 'Frame\PowerShellScript\FileDriven\FileDriven\运行测试GUI模式.ps1'
    if (-not (Test-Path -LiteralPath $runner)) { Write-Host '[launch] 交互运行器缺失' -ForegroundColor Red; exit 2 }
    & $runner @forward
    exit $LASTEXITCODE
}

$launcher = Join-Path $root ("Projects\FileDriven\BuildOutput\{0}\FileDriven.Launcher.exe" -f $Configuration)
if (-not (Test-Path -LiteralPath $launcher)) {
    Write-Host ("[launch] exit 3：启动器缺失 {0}" -f $launcher) -ForegroundColor Red
    exit 3
}
& $launcher @forward
$rc = $LASTEXITCODE
if ($null -eq $rc) { $rc = 0 }
exit $rc
