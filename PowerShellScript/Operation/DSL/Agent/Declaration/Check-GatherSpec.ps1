#requires -Version 5.1
<#
.SYNOPSIS
    聚拢声明守卫（GatherSpec）—— 声明面合格性 + 组合折叠 + 装配期闭合（G1）。
.DESCRIPTION
    唯一实现 = C# `Modules.Gather.GatherSpecLoader`（Validate + ResolveInheritance），
    本脚本只做 CLI 接线、违规汇总与正负对照，**不再保留第二份声明解析/折叠逻辑**。

    规则号以 C# 为准（V1–V15）：
      V1  schema = gather-spec-v1 / 结构合法
      V2  view 的 JSON 键与视图名一致
      V3  视图非空；每视图有 stages（折叠后）
      V4  stage 名唯一
      V5  stage.deps 指向存在的 stage 且无环（拓扑可解）
      V6  stage.needs ⊆ 视图 needs（映射闭合）
      V7  produces 名唯一
      V8  produce.crit ∈ {micro,macro}
      V9  crit=macro 必带 recompute
      V10 produce.retention 合法
      V11 命名合格性（视图/stage/need/produce 须匹配 ^[a-z][a-z0-9._-]*$）
      V12 extends 环
      V13 extends / excludeProduces 悬空
      V14 禁过程语义键（step/order/sequence/then/next）
      V15 extends 名合法

    历史漂移（合并收益，已按 C# 重对齐）：
      旧 PS 头注只写到 S8，实现另有 S9（extends 环/悬空）、S10（排除悬空）、S11（effective 复检）；
      C# 对应的规则为 V12 / V13，另补出 PS 从未覆盖的 V2、V15。
      运行期错误码（schema 非法 / JSON 装载失败）统一记为 LOAD（C# 未单独编号）。

    模式：默认报告（不设退出码）；-Check 违规 → 1；前置缺失（LedgerForge 未构建）→ 2。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$ShowDetail
)

$ErrorActionPreference = 'Stop'

# 门禁运行时（根定位/落点/裁决单源；向上查找仓库超根，与目录分层解耦）
$GateRuntimeRoot = $PSScriptRoot
while ($GateRuntimeRoot -and -not ((Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Frame')) -and (Test-Path -LiteralPath (Join-Path $GateRuntimeRoot 'Projects')))) { $GateRuntimeRoot = Split-Path -Parent $GateRuntimeRoot }
if (-not $GateRuntimeRoot) { throw '仓库超根定位失败（未命中同时含 Frame\ 与 Projects\ 的目录）' }
Import-Module (Join-Path $GateRuntimeRoot 'Frame\PowerShellScript\Baselines\GateRuntime.psm1') -Force -DisableNameChecking

if ([string]::IsNullOrWhiteSpace($Root))
{
    $Root = Get-GateRepoRoot -ScriptRoot $PSScriptRoot
}

$violations = [System.Collections.Generic.List[string]]::new()

Write-Host ''
Write-Host '============================================================'
Write-Host '  聚拢声明守卫（GatherSpec）'
Write-Host '============================================================'

# 唯一实现入口（A1/D10）：LedgerForge gather-validate → Modules.Gather.GatherSpecLoader
$forgeCandidates = @(
    (Join-Path $GateRuntimeRoot 'Frame\CsharpPrj\Plate\DeclarativeLedger.Tools\LedgerForge\BuildOutput\Debug\LedgerForge.exe'),
    (Join-Path $GateRuntimeRoot 'Frame\CsharpPrj\Plate\DeclarativeLedger.Tools\LedgerForge\BuildOutput\Release\LedgerForge.exe')
)
$ledgerForge = $forgeCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $ledgerForge) {
    Write-Host '[FAIL] 无法执行：LedgerForge 未构建（缺 BuildOutput\Debug|Release\LedgerForge.exe）' -ForegroundColor Red
    exit 2
}

function Invoke-GatherValidate([string]$Directory, [string]$OutFile) {
    & $ledgerForge gather-validate --dir $Directory --out $OutFile
    return $LASTEXITCODE
}

function Read-GatherIssues([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return @() }
    $payload = Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
    return @($payload.issues)
}

$declDir = Join-Path $Root 'Projects\Plug\Agent\Config\gather'
$files = @(Get-ChildItem -LiteralPath $declDir -Filter '*.json' -File -ErrorAction SilentlyContinue)
$totalViews = 0
if ($files.Count -eq 0) {
    $violations.Add('[V1] 聚拢声明缺失：*.json（缺失即判红，禁回退 C# 常量）')
} else {
    $probeOut = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), ('gather-' + [guid]::NewGuid().ToString('N') + '.json'))
    try {
        $code = Invoke-GatherValidate -Directory $declDir -OutFile $probeOut
        if ($code -eq 2) {
            Write-Host '[FAIL] 无法执行：gather-validate 声明目录不可读' -ForegroundColor Red
            exit 2
        }
        if ($code -notin @(0, 1)) {
            Write-Host "[FAIL] 无法执行：gather-validate 退出码 $code" -ForegroundColor Red
            exit 2
        }
        foreach ($issue in (Read-GatherIssues $probeOut)) {
            $violations.Add("$(Split-Path -Leaf $issue.path) [$($issue.code)] $($issue.message)")
        }
        foreach ($file in $files) {
            $spec = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
            $totalViews += @($spec.views.PSObject.Properties).Count
        }
    }
    finally {
        if (Test-Path -LiteralPath $probeOut) { Remove-Item -LiteralPath $probeOut -Force }
    }
}

# ---------------- 正负对照（同一 CLI，防判据被「恒真」绕过） ----------------
# 一次调用覆盖 1 正 + 7 负；每个样本必须命中对应 V 规则，正样本必须 0 issue。
$controlDir = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), ('gather-controls-' + [guid]::NewGuid().ToString('N')))
$controlOut = [System.IO.Path]::Combine($controlDir, 'result.json')
$samples = [ordered]@{
    'good.json'           = '{"schema":"gather-spec-v1","views":{"base":{"stages":[{"name":"s","needs":["d"],"deps":[]}],"needs":[{"domain":"d","scope":"x"}],"produces":[{"name":"p","kind":"k","retention":"c","crit":"micro"}]},"child":{"extends":"base"}}}'
    'missing-need.json'   = '{"schema":"gather-spec-v1","views":{"v":{"stages":[{"name":"s","needs":["ghost"],"deps":[]}],"needs":[{"domain":"a","scope":"x"}],"produces":[]}}}'
    'cycle.json'          = '{"schema":"gather-spec-v1","views":{"v":{"stages":[{"name":"a","needs":["d"],"deps":["b"]},{"name":"b","needs":["d"],"deps":["a"]}],"needs":[{"domain":"d","scope":"x"}],"produces":[]}}}'
    'step.json'           = '{"schema":"gather-spec-v1","step":1,"views":{"v":{"stages":[],"needs":[],"produces":[]}}}'
    'camel.json'          = '{"schema":"gather-spec-v1","views":{"v":{"stages":[{"name":"capture","needs":["rawRound"],"deps":[]}],"needs":[{"domain":"rawRound","scope":"round"}],"produces":[]}}}'
    'extends-ghost.json'  = '{"schema":"gather-spec-v1","views":{"child":{"extends":"ghost"}}}'
    'exclude-ghost.json'  = '{"schema":"gather-spec-v1","views":{"base":{"stages":[{"name":"s","needs":["d"],"deps":[]}],"needs":[{"domain":"d","scope":"x"}],"produces":[]},"child":{"extends":"base","excludeProduces":["nope"]}}}'
    'illegal-schema.json' = '{"schema":"gather-spec-v0","views":{"v":{"stages":[],"needs":[],"produces":[]}}}'
}
$expect = @{
    'good.json' = @(); 'missing-need.json' = @('V6'); 'cycle.json' = @('V8'); 'step.json' = @('V14');
    'camel.json' = @('V11'); 'extends-ghost.json' = @('V13'); 'exclude-ghost.json' = @('V13');
    'illegal-schema.json' = @('LOAD')
}
try {
    New-Item -ItemType Directory -Force -Path $controlDir | Out-Null
    foreach ($name in $samples.Keys) {
        [System.IO.File]::WriteAllText((Join-Path $controlDir $name), $samples[$name])
    }
    $controlCode = Invoke-GatherValidate -Directory $controlDir -OutFile $controlOut
    $actual = @{}
    foreach ($issue in (Read-GatherIssues $controlOut)) {
        $leaf = Split-Path -Leaf $issue.path
        if (-not $actual.ContainsKey($leaf)) { $actual[$leaf] = [System.Collections.Generic.List[string]]::new() }
        $actual[$leaf].Add([string]$issue.code)
    }
    foreach ($name in $expect.Keys) {
        $got = @(if ($actual.ContainsKey($name)) { $actual[$name] })
        $want = @($expect[$name])
        if ($want.Count -eq 0) {
            if ($got.Count -ne 0) { $violations.Add("[controls] 正对照失效：$name 被误判（$($got -join ',')）") }
            continue
        }
        foreach ($rule in $want) {
            if ($got -cnotcontains $rule) { $violations.Add("[controls] 负对照失效：$name 未命中 $rule（实际：$($got -join ',')）") }
        }
    }
    if ($controlCode -ne 1) { $violations.Add("[controls] 负样本集未判红（退出码 $controlCode）") }
}
finally {
    if (Test-Path -LiteralPath $controlDir) { Remove-Item -LiteralPath $controlDir -Recurse -Force }
}

if ($ShowDetail) {
    Write-Host "  声明文件：$($files.Count) 个 / 视图：$totalViews 个" -ForegroundColor Gray
    Write-Host "  唯一实现：$ledgerForge gather-validate" -ForegroundColor Gray
}

if ($violations.Count -eq 0) {
    Write-Host '[PASS] 聚拢声明守卫：0 违规（C# 唯一实现 + 正负对照在位）' -ForegroundColor Green
    exit 0
}

Write-Host "[FAIL] 聚拢声明守卫命中 $($violations.Count) 处：" -ForegroundColor Yellow
foreach ($item in $violations) { Write-Host "  - $item" -ForegroundColor Yellow }

if ($Check) { exit 1 }
exit 0
