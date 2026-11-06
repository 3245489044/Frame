<#
.SYNOPSIS
    日志消息目录闭合门禁：源码字面量 msgId ⊆ 目录定义，且两份目录副本键集一致。

.DESCRIPTION
    判据载体 = 结构事实（源码正则提取的字符串字面量 + JSON 键集），不读执行者叙述。
    违规（缺失 id / 副本键集不一致）⇒ 退出码 1。

    - 动态拼接 id（AgentLog.Log(name + "_x")）不在本门禁放行集：提取器只认字面量，
      出现非字面量调用时以「未登记字符串」形式暴露（配合 Check-DeclarationSingleSource 的静态检索）。
#>
[CmdletBinding()]
param(
    [string[]]$CodeRoots = @(
        'Projects\Plug\Agent\src\Agent.Core',
        'Projects\Plug\Agent\src\Agent.Launcher',
        'Projects\Plug\Agent\src\Agent.Axioms',
        'Projects\Plug\Agent\src\Agent.Contract'
    ),
    [string]$Catalog = 'Projects\Plug\Agent\src\Agent.Core\Diagnostics\agent_log_messages.json',
    [string]$RuntimeCatalog = 'Runtime\Config\LogMessages\agent_log_messages.json'
)

$ErrorActionPreference = 'Stop'

# 根定位（深度无关）：GateRuntime → DirectoryMap 以「Frame\ + Projects\」marker 上溯（迁 Environment 后不依赖脚本深度）
$__repoRoot = $PSScriptRoot
while ($__repoRoot -and -not ((Test-Path -LiteralPath (Join-Path $__repoRoot 'Frame')) -and (Test-Path -LiteralPath (Join-Path $__repoRoot 'Projects'))) { $__repoRoot = Split-Path -Parent $__repoRoot }
if (-not $__repoRoot) { throw '仓库超根定位失败（未命中同时含 Frame\ 与 Projects\ 的目录）' }
Import-Module (Join-Path $__repoRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking
$root = Get-GateRepoRoot -ScriptRoot $PSScriptRoot
function Resolve-Repo([string]$Path) {
    if ([System.IO.Path]::IsPathRooted($Path)) { return $Path }
    return (Join-Path $root $Path)
}

$catalogAbs = Resolve-Repo $Catalog
if (-not (Test-Path -LiteralPath $catalogAbs)) { Write-Error "日志目录缺失：$catalogAbs"; exit 2 }

$messages = (Get-Content -LiteralPath $catalogAbs -Raw -Encoding UTF8 | ConvertFrom-Json).messages
$defined = @{}
foreach ($p in $messages.PSObject.Properties) { $defined[$p.Name] = $true }

$pattern = '(?:AgentLog|ModuleLog)\.Log\(\s*"([^"]+)"'
$used = @{}
foreach ($codeRoot in $CodeRoots) {
    $codeAbs = Resolve-Repo $codeRoot
    if (-not (Test-Path -LiteralPath $codeAbs)) { continue }
    foreach ($file in (Get-ChildItem -LiteralPath $codeAbs -Recurse -File -Filter *.cs |
                       Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' })) {
        $text = [System.IO.File]::ReadAllText($file.FullName)
        foreach ($m in [regex]::Matches($text, $pattern)) {
            $used[$m.Groups[1].Value] = $file.FullName
        }
    }
}

$missing = $used.Keys | Where-Object { -not $defined.ContainsKey($_) } | Sort-Object
$violations = 0
foreach ($id in $missing) {
    Write-Host ("[MISSING] {0}  (定义缺失; 首次出现 {1})" -f $id, $used[$id]) -ForegroundColor Red
    $violations++
}

$runtimeAbs = Resolve-Repo $RuntimeCatalog
if (Test-Path -LiteralPath $runtimeAbs) {
    $runtime = (Get-Content -LiteralPath $runtimeAbs -Raw -Encoding UTF8 | ConvertFrom-Json).messages
    $runtimeKeys = @($runtime.PSObject.Properties.Name | Sort-Object)
    $sourceKeys = @($defined.Keys | Sort-Object)  # hashtable 键
    $onlySource = @($sourceKeys | Where-Object { $_ -notin $runtimeKeys })
    $onlyRuntime = @($runtimeKeys | Where-Object { $_ -notin $sourceKeys })
    if ($onlySource.Count -gt 0 -or $onlyRuntime.Count -gt 0) {
        Write-Host ("[DRIFT] 日志目录副本键集不一致：仅源 {0} 条，仅运行期 {1} 条" -f $onlySource.Count, $onlyRuntime.Count) -ForegroundColor Red
        $onlySource | Select-Object -First 10 | ForEach-Object { Write-Host "  仅源: $_" }
        $onlyRuntime | Select-Object -First 10 | ForEach-Object { Write-Host "  仅运行期: $_" }
        $violations++
    }
}

if ($violations -gt 0) { exit 1 }
Write-Host ("[OK] 日志目录闭合：used={0} defined={1}" -f $used.Count, $defined.Count) -ForegroundColor Green
exit 0
