#requires -Version 5.1
<#
.SYNOPSIS
    FsSink 入口/数据根不变式审计（对齐 2026-09-12-FsSink拦截失效根因与入口收拢-分析与实施方案.md 的 I1/I4 及其收拢判据）。

.DESCRIPTION
    校验「拦截为什么失效」两个根因的防复发锁（命中即违规）：
      A. preload 静态闭包洁净（I1）：preload / 入口 bootstrap / 路径解析模块的**静态导入**不得引用
         node:fs / fs / node:fs/promises（只允许 createRequire 或动态 import）。
         理由：ESM 命名绑定是 facade 首次求值时的快照；静态 fs 消费者会在 patch 之前建立 facade，
         使 patch 对 SDK 的 `import { x } from "node:fs"` 不可见 → 拦截静默失效（根因 A）。
      B. 绑定自检在位（I2 载体）：sink-preload.ts 必须调用 verifyBindings（非空即 fail-fast）。
      C. 数据根单点权威（I4）：`CLINE_HOME` 全仓（*.ts/*.cs）零残留；`CLINE_DIR` 的**读取**
         （process.env.CLINE_DIR / GetEnvironmentVariable("CLINE_DIR")）只允许出现在两个权威解析器
         （TS: ClineApi/src/runtime/cline-data-root.ts；C#: CallCenter.Core/Cline/ClineDataRoot.cs）。
      D. 构造单点：`new AgentSinkHost(` 只允许出现在 preload（生产入口唯一）；DbSink 同理
         （`new DbSink(` 只允许出现在 preload）。
      E. db 落盘面（DbSink 文档 P0-1/P0-4）：仓库树内不得出现 `sessions.db*`（原生 SQLite，fs 层拦不住；
         落到仓库树即「状态根未注册」症状）。默认 WARN；-StrictDbResidue 时升级为违规。
         E3：`CLINE_DB_SINK=memory` 时该断言恒为硬承诺（零落盘），无需 -StrictDbResidue。
    CI 集成：返回码 0 = 无违规；非 0 = 存在违规（拦截链路回归）。

.PARAMETER Root
    扫描根目录（默认仓库根，相对本脚本上溯四级）。

.PARAMETER ShowDetail
    输出全部检查详情（默认仅输出违规摘要）。

.PARAMETER StrictDbResidue
    把 E 段的「仓库树内 sessions.db* 残留」从 WARN 升级为违规（用于清理完成后的棘轮锁定）。

.EXAMPLE
    .\Check-FsSinkEntry.ps1
    .\Check-FsSinkEntry.ps1 -Root d:\Project\Main
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$ShowDetail,
    [switch]$StrictDbResidue
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root))
{
    # 脚本位于 Frame\PowerShellScript\Operation\DSL\，上溯四级即仓库根（与 Check-SingleWriter.ps1 一致）
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
}
$violations = [System.Collections.Generic.List[string]]::new()

# 扫描面：TS/C# 源文件（排除产物与第三方）
$tsFiles = Get-ChildItem -Path $Root -Recurse -Filter *.ts -File -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '\\(node_modules|build|dist|obj|_deprecated)\\' }
$csFiles = Get-ChildItem -Path $Root -Recurse -Filter *.cs -File -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '\\(obj|bin|BuildOutput|_deprecated)\\' }

$clineApiSrc = Join-Path $Root 'Frame\TypeScriptPrj\TsWorkspace\ClineApi\src'
$fsSinkSrc = Join-Path $Root 'Frame\TypeScriptPrj\TsWorkspace\FsSink\src'
$preloadFile = Join-Path $clineApiSrc 'preload\sink-preload.ts'
$bootstrapFiles = @(
    (Join-Path $clineApiSrc 'engine.ts'),
    (Join-Path $clineApiSrc 'cli.ts')
)

# ── A. preload 静态图洁净（I1） ──
# DbSink（模块层 sqlite shim）与其声明式 provider 同属 preload 静态闭包，一并纳管（I1 扩展）。
$cleanScope = @($preloadFile) + $bootstrapFiles + @(Join-Path $clineApiSrc 'runtime\cline-data-root.ts') + @(
    (Join-Path $fsSinkSrc 'db\db-sink.ts'),
    (Join-Path $fsSinkSrc 'db\db-route-provider.ts'),
    (Join-Path $fsSinkSrc 'cline\cline-db-provider.ts')
)
$staticFsPattern = '^\s*import\s+[^;]*from\s+["''](node:)?fs(/promises)?["'']'
foreach ($f in $cleanScope)
{
    if (-not (Test-Path -LiteralPath $f)) { $violations.Add("preload 洁净面缺文件（被删/改名？）: $f"); continue }
    $hit = Select-String -Path $f -Pattern $staticFsPattern -ErrorAction SilentlyContinue
    foreach ($h in $hit)
    {
        $violations.Add("preload 静态图含 node:fs 消费者（I1）$($h.Path):$($h.LineNumber) —— patch 时序将被提前破坏")
    }
}
if ($ShowDetail) { Write-Host "A: 已检 preload 洁净面 $($cleanScope.Count) 个文件。" }

# ── B. 绑定自检在位（I2 载体） ──
if (Test-Path -LiteralPath $preloadFile)
{
    if (-not (Select-String -Path $preloadFile -Pattern 'verifyBindings\s*\(' -ErrorAction SilentlyContinue))
    {
        $violations.Add("sink-preload.ts 未调用 verifyBindings（绑定自检缺失，A 类失效将静默）")
    }
}

# ── C. 数据根单点权威（I4） ──
$staleHits = @($tsFiles) + @($csFiles) | Select-String -Pattern 'CLINE_HOME' -ErrorAction SilentlyContinue
foreach ($h in $staleHits)
{
    $violations.Add("残留 CLINE_HOME（SDK 不识别，I4）$($h.Path):$($h.LineNumber)")
}

# CLINE_DIR / CLINE_DB_DATA_DIR 字面量白名单：
#   两个权威解析器（读）+ ClineDaemonHost（向子进程注入写）+ 测试；注释/文档行不算使用。
$envLiteralAllowlist = @('ClineDataRoot.cs', 'cline-data-root.ts', 'ClineDaemonHost.cs')
$envLiteralHits = @($tsFiles) + @($csFiles) |
    Select-String -Pattern 'CLINE_DIR|CLINE_DB_DATA_DIR' -ErrorAction SilentlyContinue
foreach ($h in $envLiteralHits)
{
    $trimmed = $h.Line.Trim()
    if ($trimmed.StartsWith('//') -or $trimmed.StartsWith('///') -or $trimmed.StartsWith('*') -or $trimmed.StartsWith('/*'))
    {
        continue # 注释/文档提及不算使用
    }
    $name = Split-Path -Path $h.Path -Leaf
    $isTest = $h.Path -match '\\CentralTest\\' -or $name -like '*.test.ts'
    if ($isTest) { continue }
    if ($envLiteralAllowlist -notcontains $name)
    {
        $violations.Add("CLINE_DIR 越权使用（数据根非单点，I4）$($h.Path):$($h.LineNumber) —— 应经 ClineDataRoot / resolveClineDataRoot")
    }
}

# ── D. 构造单点：new AgentSinkHost / new DbSink 只允许出现在 preload ──
$ctorHits = $tsFiles | Select-String -Pattern 'new\s+AgentSinkHost\s*\(|new\s+DbSink\s*\(' -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -notmatch '\\FsSink\\(src|test)\\' }
foreach ($h in $ctorHits)
{
    if ($h.Path -ne $preloadFile)
    {
        $violations.Add("AgentSinkHost/DbSink 越权构造（入口非单点）$($h.Path):$($h.LineNumber) —— 生产入口唯一 = sink-preload")
    }
}

foreach ($f in $bootstrapFiles)
{
    if (-not (Test-Path -LiteralPath $f)) { continue }
    if (-not (Select-String -Path $f -Pattern 'import\("\./preload/sink-preload\.js"\)' -ErrorAction SilentlyContinue))
    {
        $violations.Add('入口未走 preload bootstrap（H1）: ' + $f + ' 缺少动态 import("./preload/sink-preload.js")')
    }
}

# ── E. db 落盘面（DbSink 文档 P0-1/P0-4） ──
# E1：仓库树内不得出现 sessions.db*（sqlite 为原生写盘，fs 层原理上拦不住 → 落到仓库树即「状态根未注册」症状，
#     见 RepositoryPaths.ResolveClineDaemonDataDir 的显性告警 P0-3）。默认 WARN，保持既有退出码语义。
$dbResidue = @(Get-ChildItem -Path $Root -Recurse -File -Filter 'sessions.db*' -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '\\(node_modules|BuildOutput|obj|bin|_deprecated|TestResults|\.git)\\' })
foreach ($f in $dbResidue)
{
    $msg = "db 落盘残留（仓库树内，P0-4）: $($f.FullName)"
    if ($StrictDbResidue) { $violations.Add($msg) } else { Write-Host "  [WARN] $msg" -ForegroundColor Yellow }
}

# E2：默认根（~/.cline/data/db）落点观测（信息性；真值判据 = 仓库树无残留 ∧ 隔离根下出现写入）。
if (-not [string]::IsNullOrWhiteSpace($env:USERPROFILE))
{
    $defaultDb = Join-Path $env:USERPROFILE '.cline\data\db'
    if (Test-Path -LiteralPath $defaultDb)
    {
        $latest = Get-ChildItem -LiteralPath $defaultDb -File -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if ($latest) { Write-Host "E2: 默认根最新写入 = $($latest.Name) @ $($latest.LastWriteTime)" }
    }
}
if ($ShowDetail) { Write-Host "E: db 落盘面已检（仓库树残留 $($dbResidue.Count) 处；严格模式=$StrictDbResidue）。" }

# E3：DbSink.memory 形态即「零落盘」硬承诺 —— 仓库树残留直接违规（无需 -StrictDbResidue）。
$dbSinkMode = ''
if (-not [string]::IsNullOrWhiteSpace($env:CLINE_DB_SINK)) { $dbSinkMode = $env:CLINE_DB_SINK.Trim().ToLower() }
if (-not $StrictDbResidue -and $dbSinkMode -eq 'memory')
{
    foreach ($f in $dbResidue)
    {
        $violations.Add("CLINE_DB_SINK=memory 下仓库树仍有 db 残留（零落盘承诺）: $($f.FullName)")
    }
}

if ($violations.Count -eq 0)
{
    Write-Host "Check-FsSinkEntry: OK —— preload 静态图洁净 + 绑定自检在位 + 数据根单点（CLINE_DIR/CLINE_HOME 收敛）。" -ForegroundColor Green
    exit 0
}

Write-Host "Check-FsSinkEntry: FAIL —— 发现 $($violations.Count) 处违规：" -ForegroundColor Red
$violations | ForEach-Object { Write-Host "  - $_" -ForegroundColor Yellow }
exit 1
