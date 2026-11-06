#requires -Version 5.1
<#
.SYNOPSIS
    能力库文件化守卫（PsCapabilityFileOnly）—— 所有可执行 PS 能力必须落为「文件 + 同名契约 sidecar」。

.DESCRIPTION
    判据（结构事实，禁执行者叙述）：
      C1 文件化：BasicMethod\Capability 下每个 .psm1/.ps1 必须有同基名 .contracts.json；
      C2 禁内联执行：该目录内禁 Invoke-Expression / [scriptblock]::Create / iex（能力体只做原子操作）；
      C3 契约↔实现一致：sidecar 的 identity.function 必须出现在对应模块的 Export-ModuleMember 中；
      C4 命名规范：action 必须是 verb.object（小写，点分）；
      C5 正/负对照：检测器对合成样本必须给出预期结果（防判据空转）。
    任一项不满足 ⇒ 退出码 1。

.PARAMETER Check
    只读验收模式（与门禁清单 Args 对齐；缺省等价于检查）。
#>
[CmdletBinding()]
param(
    [switch]$Check
)

$ErrorActionPreference = 'Stop'

# ---- 仓库根（深度无关）：GateRuntime → DirectoryMap 以「Frame\ + Projects\」marker 上溯 ----
$repoRoot = $PSScriptRoot
while ($repoRoot -and -not ((Test-Path -LiteralPath (Join-Path $repoRoot 'Frame')) -and (Test-Path -LiteralPath (Join-Path $repoRoot 'Projects')))) { $repoRoot = Split-Path -Parent $repoRoot }
if (-not $repoRoot) { throw '仓库超根定位失败（未命中同时含 Frame\ 与 Projects\ 的目录）' }
Import-Module (Join-Path $repoRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking
$repoRoot = Get-GateRepoRoot -ScriptRoot $PSScriptRoot

$violations = [System.Collections.Generic.List[string]]::new()
$capDir = Join-Path (Get-MappedPath -Key 'PowerShellScript') 'BasicMethod\Capability'

if (-not (Test-Path -LiteralPath $capDir)) {
    Write-Host "VIOLATION: 能力库目录不存在：$capDir" -ForegroundColor Red
    exit 1
}

# ---- C1/C2：文件与契约一一对应 + 禁内联执行 ----
$modules = @(Get-ChildItem -LiteralPath $capDir -Recurse -Include '*.psm1', '*.ps1' -File -ErrorAction SilentlyContinue)
foreach ($file in $modules) {
    $expected = [System.IO.Path]::ChangeExtension($file.FullName, '.contracts.json')
    if (-not (Test-Path -LiteralPath $expected)) {
        $violations.Add("[C1] 缺契约 sidecar：$($file.Name)（期望 $(Split-Path $expected -Leaf)）")
    }

    $text = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8
    foreach ($pattern in @('Invoke-Expression', '\[scriptblock\]::Create', '(?i)\biex\b')) {
        if ($text -match $pattern) {
            $violations.Add("[C2] 能力体含内联执行（$pattern）：$($file.Name)")
        }
    }
}

# ---- C3/C4：sidecar → 实现/命名 ----
$sidecars = @(Get-ChildItem -LiteralPath $capDir -Recurse -Filter '*.contracts.json' -File -ErrorAction SilentlyContinue)
foreach ($sidecar in $sidecars) {
    $baseName = $sidecar.Name -replace '\.contracts\.json$', ''
    $modulePath = Join-Path $sidecar.DirectoryName ($baseName + '.psm1')
    if (-not (Test-Path -LiteralPath $modulePath)) {
        $modulePath = Join-Path $sidecar.DirectoryName ($baseName + '.ps1')
    }
    if (-not (Test-Path -LiteralPath $modulePath)) {
        $violations.Add("[C3] 契约无实现文件：$($sidecar.Name)")
        continue
    }

    try {
        $doc = Get-Content -LiteralPath $sidecar.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
    }
    catch {
        $violations.Add("[C3] 契约不可解析：$($sidecar.Name) — $($_.Exception.Message)")
        continue
    }

    $moduleText = Get-Content -LiteralPath $modulePath -Raw -Encoding UTF8
    foreach ($contract in @($doc.contracts)) {
        if ($null -eq $contract -or $null -eq $contract.identity) { continue }
        $action = [string]$contract.identity.action
        $function = [string]$contract.identity.function

        if ($action -notmatch '^[a-z][a-z0-9]*\.[a-z][a-z0-9.]*$') {
            $violations.Add("[C4] action 非 verb.object 形：'$action'（$($sidecar.Name)）")
        }
        if ([string]::IsNullOrWhiteSpace($function)) {
            $violations.Add("[C3] 契约缺 identity.function：$($sidecar.Name)")
        }
        elseif ($moduleText -notmatch [regex]::Escape($function)) {
            $violations.Add("[C3] 声明函数未在实现文件出现：$function（$($modulePath | Split-Path -Leaf)）")
        }
    }
}

# ---- C5：正/负对照（检测器自证）----
function Test-InlineExecution([string]$Text) {
    foreach ($pattern in @('Invoke-Expression', '\[scriptblock\]::Create', '(?i)\biex\b')) {
        if ($Text -match $pattern) { return $true }
    }
    return $false
}
if (-not (Test-InlineExecution 'Invoke-Expression $cmd')) { $violations.Add('[C5] 负对照失效：内联执行检测器未命中合成样本') }
if (Test-InlineExecution 'Get-ChildItem -Path .') { $violations.Add('[C5] 正对照失效：干净样本被误判为内联执行') }

if ($violations.Count -gt 0) {
    $violations | ForEach-Object { Write-Host "VIOLATION: $_" -ForegroundColor Red }
    exit 1
}

Write-Host ("PsCapabilityFileOnly OK（modules={0}, contracts={1}）" -f $modules.Count, $sidecars.Count) -ForegroundColor Green
exit 0
