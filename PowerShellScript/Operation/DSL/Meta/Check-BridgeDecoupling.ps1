<#
.SYNOPSIS
    PrjBridge 解耦守卫 —— 桥产物零 Agent/Engine 程序集引用（INV-PB-1）。
.DESCRIPTION
    目标不变量：PrjBridge.dll 的 AssemblyRef 表**不含**任何 Agent.* / GameEditor.* 程序集。
    即「桥只编译自身」—— Agent/Engine 任意变更（函数体/公开签名/程序集增删改名或移动）都不迫使桥重编。
    判据载体 = 桥产物元数据的 AssemblyRef（结构事实），非执行者叙述。
    误报规避：本脚本解析 **AssemblyRef 表**（真正的外部程序集引用），不扫描字符串堆 —— 故桥内
    形如 "Agent.Launcher" 的**字符串字面量**（反射装载名）不会造成误报。
.PARAMETER Check
    -Check 时以退出码裁决（0 = 通过 / 1 = 违约），否则仅打印报告。
.PARAMETER Configuration
    构建配置（默认 Debug）。
.EXAMPLE
    pwsh Check-BridgeDecoupling.ps1 -Check
#>
[CmdletBinding()]
param(
    [switch]$Check,
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'

# 根定位（深度无关）：GateRuntime → DirectoryMap 以「Frame\ + Projects\」marker 上溯（迁 Environment 后不依赖脚本深度）
$repoRoot = $PSScriptRoot
while ($repoRoot -and -not ((Test-Path -LiteralPath (Join-Path $repoRoot 'Frame')) -and (Test-Path -LiteralPath (Join-Path $repoRoot 'Projects')))) { $repoRoot = Split-Path -Parent $repoRoot }
if (-not $repoRoot) { throw '仓库超根定位失败（未命中同时含 Frame\ 与 Projects\ 的目录）' }
Import-Module (Join-Path $repoRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking
$repoRoot = Get-GateRepoRoot -ScriptRoot $PSScriptRoot
$bridgeDll = Join-Path $repoRoot ("Projects\PrjBridge\bin\{0}\net10.0-windows\PrjBridge.dll" -f $Configuration)

$fail = $false

if (-not (Test-Path $bridgeDll)) {
    Write-Host "[BridgeDecoupling] 桥产物缺件（未构建？）：$bridgeDll" -ForegroundColor Yellow
    Write-Host "[BridgeDecoupling] 判定：跳过（未构建不可判；构建后应重跑）。"
    exit 0
}

try {
    # 加载**临时副本**解析元数据（避免锁定真实产物、干扰后续构建）
    $tempCopy = Join-Path $env:TEMP ("PrjBridge.guard.{0}.dll" -f ([guid]::NewGuid().ToString('N')))
    Copy-Item -LiteralPath $bridgeDll -Destination $tempCopy -Force
    $asm = [System.Reflection.Assembly]::LoadFrom($tempCopy)
    $refs = @($asm.GetReferencedAssemblies() | ForEach-Object { $_.Name })
}
catch {
    Write-Host "[BridgeDecoupling] 读取桥产物元数据失败：$($_.Exception.Message)" -ForegroundColor Red
    if ($Check) { exit 1 } else { exit 0 }
}

$offenders = @($refs | Where-Object { $_ -like 'Agent.*' -or $_ -eq 'Agent' -or $_ -like 'GameEditor.*' })

if ($offenders.Count -gt 0) {
    $fail = $true
    Write-Host "[BridgeDecoupling] ✗ 桥产物引用了 Agent/Engine 程序集（违反 INV-PB-1）：" -ForegroundColor Red
    $offenders | ForEach-Object { Write-Host ("    AgentRef: " + $_) -ForegroundColor Red }
    Write-Host "[BridgeDecoupling] 修法：经 AgentHostContract 冻结契约件间接依赖（桥不得直接引用 Agent.* / GameEditor.*）。"
}
else {
    Write-Host "[BridgeDecoupling] ✓ 桥产物零 Agent/Engine 程序集引用（INV-PB-1 成立）。" -ForegroundColor Green
    Write-Host ("[BridgeDecoupling] 引用面：{0}" -f (($refs | Sort-Object) -join ', '))
}

if ($Check -and $fail) { exit 1 }
exit 0
