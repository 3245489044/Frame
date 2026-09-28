# Check-AgentBuildPoint.ps1
# Agent 装配聚拢点门禁（P4 扩展）—— 对齐 Projects\Plug\Agent\doc\Agent调用链集中化-装配聚拢点(P4)扩展方案.md
# 判据：装配入口唯一 + 根引导唯一 + 绑定先于解析（INV-1/INV-3/INV-4）。
# 检测：
#   [BP1] 唯一装配入口：不得再出现 AgentComposition.StartAsync 调用（一律经 BuildAsync）
#   [BP2] 根引导唯一：AgentRuntimeRootBootstrap.TryBoot 生产调用点 ≤ 1（聚拢点相位 0）
#   [BP3] 承载冻结：AgentRuntimeRoot.Partition 生产调用点 ≤ 2（门面自身 + E 段登记）
#   [BP4] 相位顺序：AgentComposition.BuildAsync 内 Bind( 必须先于 AgentFramework.StartAsync(
#   [BP5] 状态真源解析点收敛：ResolveStateCenter( 生产调用点 ≤ 1（C 段共点；编排器/Runner 已改构造注入必填）
#   [BP6] 取根面直调收敛：AgentRuntimeRoot.Resolve<T>( 生产调用点 = 0（一律经 AgentRuntimeSlots 注册缝）
# 扫描面：Projects\Plug\Agent\src（测试工程不在扫描面）。
#   - 报告模式（默认）：列出命中，不设退出码。
#   - 校验模式（-Check）：任一判据越界 → 退出码 1（CI 门禁）。

[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root)) {
    # 脚本位于 Frame\PowerShellScript\Operation\DSL\，上溯四级即仓库根
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
}

$scanDir = Join-Path $Root 'Projects\Plug\Agent\src'
if (-not (Test-Path $scanDir)) { Write-Error "未找到 Agent 源码目录: $scanDir"; exit 2 }

$files = Get-ChildItem -Path $scanDir -Recurse -Include *.cs |
    Where-Object { $_.FullName -notmatch '\\(?:obj|bin)\\' }

function Get-Hits {
    param([string]$Pattern)
    $files | Select-String -Pattern $Pattern -Encoding UTF8 |
        Where-Object { $_.Line -notmatch '^\s*///' }   # 文档注释不算调用点
}

$failures = @()

Write-Host '--- [BP1] 唯一装配入口（AgentComposition.StartAsync 调用点 = 0） ---'
$bp1 = @(Get-Hits 'AgentComposition\.StartAsync\s*\(')
Write-Host ("命中: {0}" -f $bp1.Count)
$bp1 | ForEach-Object { Write-Host ("  {0}:{1}" -f $_.Path, $_.LineNumber) }
if ($bp1.Count -gt 0) { $failures += "BP1 仍存在 AgentComposition.StartAsync 调用 $($bp1.Count) 处（应改经 BuildAsync）" }

Write-Host '--- [BP2] 根引导唯一（TryBoot 生产调用点 ≤ 1） ---'
$bp2 = @(Get-Hits '\.TryBoot\s*\(\s*\)')
Write-Host ("命中: {0}" -f $bp2.Count)
$bp2 | ForEach-Object { Write-Host ("  {0}:{1}" -f $_.Path, $_.LineNumber) }
if ($bp2.Count -gt 1) { $failures += "BP2 根引导调用点 $($bp2.Count) 处（应仅聚拢点相位 0 一处）" }

Write-Host '--- [BP3] 承载冻结（AgentRuntimeRoot.Partition 生产调用点 ≤ 2） ---'
$bp3 = @(Get-Hits 'AgentRuntimeRoot\.Partition')
Write-Host ("命中: {0}" -f $bp3.Count)
$bp3 | ForEach-Object { Write-Host ("  {0}:{1}" -f $_.Path, $_.LineNumber) }
if ($bp3.Count -gt 2) { $failures += "BP3 AgentRuntimeRoot.Partition 调用点 $($bp3.Count) 处（承载应经 RuntimeSlotScope 冻结，勿新增动态取承载点）" }

Write-Host '--- [BP4] 相位顺序（Bind 先于 StartAsync） ---'
$composition = Join-Path $scanDir 'Agent.Launcher\Composition\Root\AgentComposition.cs'
$bindLine = (Select-String -Path $composition -Pattern 'AgentRuntimeRoot\.Bind\s*\(' -Encoding UTF8 |
        Where-Object { $_.Line -notmatch '^\s*///' } | Select-Object -First 1).LineNumber
$assembleLine = (Select-String -Path $composition -Pattern 'await AgentFramework\.StartAsync\s*\(' -Encoding UTF8 |
        Where-Object { $_.Line -notmatch '^\s*///' } | Select-Object -First 1).LineNumber
Write-Host ("Bind 行: {0} | StartAsync 行: {1}" -f $bindLine, $assembleLine)
if (-not $bindLine -or -not $assembleLine -or $bindLine -gt $assembleLine) {
    $failures += 'BP4 相位顺序破坏：BuildAsync 内绑定必须早于装配（INV-1 绑定先于解析）'
}

Write-Host '--- [BP5] 状态真源解析点收敛（ResolveStateCenter 生产调用点 ≤ 1） ---'
$bp5 = @(Get-Hits 'AgentRuntimeRoot\.ResolveStateCenter\s*\(')
Write-Host ("命中: {0}" -f $bp5.Count)
$bp5 | ForEach-Object { Write-Host ("  {0}:{1}" -f $_.Path, $_.LineNumber) }
if ($bp5.Count -gt 1) { $failures += "BP5 ResolveStateCenter 调用点 $($bp5.Count) 处（状态真源应装配期一次解析并固化）" }

Write-Host '--- [BP6] 取根面直调收敛（AgentRuntimeRoot.Resolve<T> 生产调用点 = 0） ---'
$bp6 = @(Get-Hits 'AgentRuntimeRoot\.Resolve(?:<|\()')
Write-Host ("命中: {0}" -f $bp6.Count)
$bp6 | ForEach-Object { Write-Host ("  {0}:{1}" -f $_.Path, $_.LineNumber) }
if ($bp6.Count -gt 0) { $failures += "BP6 AgentRuntimeRoot.Resolve 生产调用点 $($bp6.Count) 处（应经 AgentRuntimeSlots 注册缝解析，禁静态转发）" }

Write-Host ''
if ($failures.Count -eq 0) {
    Write-Host 'Agent 装配聚拢点门禁通过：BP1–BP6 全部满足。'
    exit 0
}

Write-Host ("越界 {0} 项：" -f $failures.Count)
$failures | ForEach-Object { Write-Host ("  - {0}" -f $_) }
if ($Check) { exit 1 }
exit 0
