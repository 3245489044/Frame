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

$probeDir = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), ('keyspace-' + [guid]::NewGuid().ToString('N')))
New-Item -ItemType Directory -Force -Path $probeDir | Out-Null

function Invoke-Facts([string]$ModeName, [string]$InputPath, [string]$OutFile) {
    & $bundle $ModeName $InputPath $OutFile | Out-Null
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $OutFile)) {
        throw "Usharp.Bundle $ModeName 失败（输入：$InputPath）"
    }
    # 生成器刚写完的产物可能被索引/杀软短暂占用：重试读取（不改内容）。
    for ($attempt = 0; $attempt -lt 5; $attempt++) {
        try { return [System.IO.File]::ReadAllText($OutFile) }
        catch { Start-Sleep -Milliseconds 50 }
    }
    throw "无法读取事实产物：$OutFile"
}

function Compare-Facts([string]$Label, [string]$Expected, [string]$Actual) {
    if (-not [string]::Equals($Expected, $Actual, [System.StringComparison]::Ordinal)) {
        $violations.Add("$Label 键空间事实漂移（Ordinal 文本不等；按 families/ports 规范化后比较）")
    }
}

try {
    $repoFacts = Invoke-Facts 'keyspace-facts' $repoConfig (Join-Path $probeDir 'repo.json')

    # 生成产物必须与声明复算一致：第二处「读生成文件但可能过期」的载体（与 ITransferKeyStore.cs 空壳同类）。
    if (-not (Test-Path -LiteralPath $repoGenerated)) {
        $violations.Add("缺仓库键空间产物：$repoGenerated")
    } else {
        $committedFacts = Invoke-Facts 'keyspace-facts-json' $repoGenerated (Join-Path $probeDir 'committed.json')
        Compare-Facts '仓库声明 vs agent.keyspace.generated.json' $repoFacts $committedFacts
    }

    $dataAvailable = Test-Path -LiteralPath $dataPath
    if ($dataAvailable) {
        $dataFacts = Invoke-Facts 'keyspace-facts-json' $dataPath (Join-Path $probeDir 'data.json')
        Compare-Facts '仓库声明 vs Data\Agent\Config' $repoFacts $dataFacts
    }

    $currentPtr = Join-Path $Root 'Runtime\Plugins\Agent\current'
    if (Test-Path -LiteralPath $currentPtr) {
        $generation = (Get-Content -LiteralPath $currentPtr -Raw).Trim()
        if (-not [string]::IsNullOrWhiteSpace($generation)) {
            $genPath = Join-Path $Root "Runtime\Plugins\Agent\Generations\$generation\assets\Config\keyspaces\agent.keyspace.json"
            if (Test-Path -LiteralPath $genPath) {
                $genFacts = Invoke-Facts 'keyspace-facts-json' $genPath (Join-Path $probeDir 'generation.json')
                Compare-Facts "仓库声明 vs Gen($generation)" $repoFacts $genFacts
            }
            else {
                $violations.Add("Gen($generation) 缺 keyspace 声明：$genPath")
            }
        }
    }
}
finally {
    if (Test-Path -LiteralPath $probeDir) { Remove-Item -LiteralPath $probeDir -Recurse -Force }
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
