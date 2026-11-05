#requires -Version 5.1
<#
.SYNOPSIS
    keyspace 声明镜像守卫（Data / 部署代 必须与仓库声明事实一致）。
.DESCRIPTION
    A2/D1：键空间事实**只由 C# 产出**（Usharp.Bundle keyspace-facts / keyspace-facts-json）。
    本脚本不解析 .ushar、不解析 keyspace JSON 字段、不复制常量；只做三次 Ordinal 文本对拍：
      1) 仓库声明复算 facts  vs  已入库 agent.keyspace.generated.json（生成产物是否过期）
      2) 仓库声明复算 facts  vs  Data\Agent\Config\keyspaces\agent.keyspace.json（部署镜像）
      3) 仓库声明复算 facts  vs  Runtime 代目录 keyspace（若 current 指针存在）
    事实字形 = families 按 id、ports 按 name 的 Ordinal 排序；derivedPrefixes 保留声明序；
    ports 保留「同名异向」全部条目（旧 PS 用 Dictionary 会静默覆盖，已修）。

    Data 缺失 = 干净 checkout：输出 [SKIPPED_NO_TARGET]（未验证），不计入通过。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$RequireData
)

$ErrorActionPreference = 'Stop'

$GateRuntimeRoot = $PSScriptRoot
while ($GateRuntimeRoot -and -not ((Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Frame')) -and (Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Projects')))) { $GateRuntimeRoot = Split-Path -Parent $GateRuntimeRoot }
if (-not $GateRuntimeRoot) { throw '仓库超根定位失败（未命中同时含 Frame\ 与 Projects\ 的目录）' }
Import-Module (Join-Path $GateRuntimeRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking
Import-Module (Join-Path $GateRuntimeRoot 'Frame\PowerShellScript\Baselines\GateFacts.psm1') -Force -DisableNameChecking

if ([string]::IsNullOrWhiteSpace($Root)) {
    $probe = Get-GateRepoRoot -ScriptRoot $PSScriptRoot
    if (-not $probe) { throw 'KeyspaceMirror gate：未找到仓库根（Projects\Plug\Agent）' }
    $Root = $probe
}

$bundleCandidates = @(
    (Join-Path $Root 'Frame\Usharp\BuildOutput\Debug\Usharp.Bundle.exe'),
    (Join-Path $Root 'Frame\Usharp\BuildOutput\Release\Usharp.Bundle.exe')
)
$bundle = $bundleCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $bundle) {
    Write-Host '[FAIL] 无法执行：Usharp.Bundle 未构建（键空间事实不可产出）' -ForegroundColor Red
    exit 2
}

$repoConfig = Join-Path $Root 'Projects\Plug\Agent\Config'
$repoGenerated = Join-Path $repoConfig 'keyspaces\agent.keyspace.generated.json'
$dataPath = Join-Path $Root 'Data\Agent\Config\keyspaces\agent.keyspace.json'
$violations = New-Object System.Collections.Generic.List[string]

$repoFacts = Get-GateKeyspaceFactsText -BundlePath $bundle -InputPath $repoConfig -Mode 'keyspace-facts'

# 生成产物必须与声明复算一致：第二处「读生成文件但可能过期」的载体（与 ITransferKeyStore.cs 空壳同类）。
if (-not (Test-Path -LiteralPath $repoGenerated)) {
    $violations.Add("缺仓库键空间产物：$repoGenerated")
} else {
    $committedFacts = Get-GateKeyspaceFactsText -BundlePath $bundle -InputPath $repoGenerated -Mode 'keyspace-facts-json'
    if (-not (Test-GateFactTextEqual -Expected $repoFacts -Actual $committedFacts)) {
        $violations.Add('仓库声明 vs agent.keyspace.generated.json 键空间事实漂移（Ordinal 文本不等；按 families/ports 规范化后比较）')
    }
}

$dataAvailable = Test-Path -LiteralPath $dataPath
if ($dataAvailable) {
    $dataFacts = Get-GateKeyspaceFactsText -BundlePath $bundle -InputPath $dataPath -Mode 'keyspace-facts-json'
    if (-not (Test-GateFactTextEqual -Expected $repoFacts -Actual $dataFacts)) {
        $violations.Add('仓库声明 vs Data\Agent\Config 键空间事实漂移（Ordinal 文本不等；按 families/ports 规范化后比较）')
    }
}

$currentPtr = Join-Path $Root 'Runtime\Plugins\Agent\current'
if (Test-Path -LiteralPath $currentPtr) {
    $generation = (Get-Content -LiteralPath $currentPtr -Raw).Trim()
    if (-not [string]::IsNullOrWhiteSpace($generation)) {
        $genPath = Join-Path $Root "Runtime\Plugins\Agent\Generations\$generation\assets\Config\keyspaces\agent.keyspace.json"
        if (Test-Path -LiteralPath $genPath) {
            $genFacts = Get-GateKeyspaceFactsText -BundlePath $bundle -InputPath $genPath -Mode 'keyspace-facts-json'
            if (-not (Test-GateFactTextEqual -Expected $repoFacts -Actual $genFacts)) {
                $violations.Add("仓库声明 vs Gen($generation) 键空间事实漂移（Ordinal 文本不等；按 families/ports 规范化后比较）")
            }
        }
        else {
            $violations.Add("Gen($generation) 缺 keyspace 声明：$genPath")
        }
    }
}

if ($violations.Count -gt 0) {
    $violations | ForEach-Object { Write-Host "VIOLATION: $_" -ForegroundColor Red }
    if ($Check) { exit 1 }
    exit 0
}

if (-not $dataAvailable) {
    Write-GateSkip -Key 'KeyspaceMirror' -Reason 'Data\Agent\Config\keyspaces\agent.keyspace.json 不存在（无对照对象）'
    if ($RequireData) { exit 1 }
    Write-Host '[PASS] KeyspaceMirror 仓库声明面通过（生成产物未过期）；Data/代目录镜像未验证' -ForegroundColor Green
    exit 0
}

Write-Host '[PASS] KeyspaceMirror gate 通过（仓库声明、生成产物、Data、代目录均 Ordinal 一致）' -ForegroundColor Green
exit 0
