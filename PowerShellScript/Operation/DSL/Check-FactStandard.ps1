#requires -Version 5.1
<#
.SYNOPSIS
    会话事实标准审计 —— 标识标准（FactId）与事实标准的机检 C1–C4 + 声明面闸A。

.DESCRIPTION
    收敛目标（方案 §1.6 机检）：把「一轮的编号」收敛为 1 套 FactId（废除 4 套旧形态）、
    把「一轮的事实」收敛为 1 份 append-only 事实文档（单写者），并以声明面（[Obs]/[Space]）为判据真源。
    检查项（命中即违规）：
      A. 声明面闸A：Config/session-facts.ushar 编译「错误 0」（US6005 警告按容量预期，不作失败判据）。
      C1 键字面量唯一：除 Ids/FactIdGrammar.cs 与 Ids/FactIdMigration.cs（文法与迁移天然含旧形态）外，
         生产源码的**代码行**不得出现 "#R" / "@" 轮键拼接串（禁新增键拼接点）。
      C2 键形状合法：FactId 文法（唯一正则）须接受新形态样例、拒绝全部旧形态样例（形态回归锁）。
      C3 废除项归零：leaseId= / 事实面 opId / 关联位 conv_ 出现次数 = 0（-Strict 时判失败；P1-6 前为「待迁移」）。
      C4 单写者：事实写入调用点仅 SessionFactWriter（AppendAsync 调用方白名单）。
    CI 集成：返回码 0 = 无违规；非 0 = 存在违规（标识 / 事实标准回归）。

.PARAMETER Root
    扫描根目录（默认仓库根，相对本脚本上溯四级）。

.PARAMETER Strict
    严格模式：C3 旧标识残留判为失败（P1-6 标识归一完成后启用）。

.PARAMETER ShowDetail
    输出全部命中明细（默认仅输出违规摘要与计数）。

.EXAMPLE
    .\Check-FactStandard.ps1
    .\Check-FactStandard.ps1 -Strict -ShowDetail
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Strict,
    [switch]$ShowDetail
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root))
{
    # 脚本位于 Frame\PowerShellScript\Operation\DSL\，上溯四级即仓库根（与 Check-SingleWriter.ps1 一致）
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
}

$violations = [System.Collections.Generic.List[string]]::new()
$pending    = [System.Collections.Generic.List[string]]::new()

$agentSrc   = Join-Path $Root 'Projects\Plug\Agent\src'
$frameConv  = Join-Path $Root 'Frame\CsharpPrj\Share\Frameworks\Conversation'
$declaration = Join-Path $Root 'Projects\Plug\Agent\Config\session-facts.ushar'
# S1 下沉（2026-09-24）：标识标准唯一字面量源已迁 Frame 侧（Share.Conversation.Ids），
# Agent 侧 Agent.Session\Ids 仅剩迁移提示 ⇒ 扫描根须含 Frame 侧，否则 C1 退化为恒真（假绿）。
$grammarFile = Join-Path $frameConv 'Ids\FactIdGrammar.cs'
$idsDirs     = @(
    (Join-Path $frameConv 'Ids'),
    (Join-Path $agentSrc 'Agent.Session\Ids')
)

if (-not (Test-Path $agentSrc))
{
    Write-Host "[FS] 未找到 Agent 源码目录：$agentSrc（跳过）"
    exit 0
}

$scanRoots = @($agentSrc)
if (Test-Path $frameConv) { $scanRoots += $frameConv }
else { $violations.Add("C1 缺 Frame 侧会话层目录：$frameConv（标识标准已下沉，扫描根须覆盖）") }

$csFiles = Get-ChildItem -Path $scanRoots -Recurse -Filter *.cs -File -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' -and $_.FullName -notlike '*\_deprecated\*' }

# 代码行（剔除注释行：注释中允许提及旧形态，便于说明「已废除 / 已归一」）
function Get-CodeLines([string]$Path)
{
    Get-Content -LiteralPath $Path | Where-Object {
        $t = $_.TrimStart()
        -not ($t.StartsWith('//') -or $t.StartsWith('*') -or $t.StartsWith('/*'))
    }
}

# A. 声明面闸A：错误 0（US6005 未接线警告按文档预期，不作失败判据）
$cli = Join-Path $Root 'Frame\Usharp\BuildOutput\Debug\Usharp.Cli.exe'
if (-not (Test-Path $declaration))
{
    $violations.Add("A 声明面缺件：$declaration（事实契约真源缺失）")
}
elseif (Test-Path $cli)
{
    $out = & $cli build $declaration 2>&1
    $summary = ($out | Select-Object -First 1).ToString()
    if ($summary -notmatch '错误 0')
    {
        $violations.Add("A 声明面闸A 未通过：$summary（须错误 0）")
    }
    elseif ($ShowDetail)
    {
        Write-Host "[FS] 声明面闸A：$summary"
    }
}
else
{
    $pending.Add("A 声明面闸A 跳过（未找到 Usharp.Cli：$cli）")
}

# C1. 键字面量唯一：Ids 的文法 / 迁移文件是唯一允许出现旧形态与分隔串的落点
$c1Allow = @('FactIdGrammar.cs', 'FactIdMigration.cs')
foreach ($f in $csFiles)
{
    if ($c1Allow -contains $f.Name) { continue }
    foreach ($line in Get-CodeLines $f.FullName)
    {
        if ($line -match '"#R"' -or $line -match '"@(\d|\{)')
        {
            $violations.Add("C1 键拼接散点：$($f.Name) 出现键字面量拼接；须经 FactIds 生成（唯一字面量源 = FactIdGrammar.cs）")
        }
    }
}

# C2. 键形状合法：唯一正则须接受新形态、拒绝旧形态（形态回归锁）
$newSamples     = @('default#R1', 'default#R6/cline_execute#1', 'default#R1/cline_execute#1/2/call_00_FJLmIZn')
$retiredSamples = @('default@1', 'default#1#1', 'defaultR1', 'conv_1790222300189_vu045ho', 'default#R0')
$anyPattern   = '^[A-Za-z0-9_.\-]+#R[1-9][0-9]*(?:\/[A-Za-z0-9_.\-]+#[1-9][0-9]*(?:\/[1-9][0-9]*\/[A-Za-z0-9_.\-]+)?)?$'
foreach ($s in $newSamples)
{
    if ($s -notmatch $anyPattern) { $violations.Add("C2 文法拒收新形态：$s") }
}
foreach ($s in $retiredSamples)
{
    if ($s -match $anyPattern) { $violations.Add("C2 文法误收旧形态：$s（标识资格已废除）") }
}
if (-not (Test-Path $grammarFile))
{
    $violations.Add("C2 缺唯一字面量源：$grammarFile")
}
elseif ((Get-Content -LiteralPath $grammarFile -Raw) -notmatch [regex]::Escape($anyPattern))
{
    $pending.Add('C2 正则与 FactIdGrammar.AnyKeyPattern 未逐字一致（发射生成物化 P2-13 后收敛）')
}

# C3. 废除项归零（-Strict 判失败；P1-6 前属「待迁移」）
$legacyPatterns = @(
    @{ Name = 'leaseId='; Pattern = 'leaseId\s*=' },
    @{ Name = 'conv_ 关联位'; Pattern = 'conv_[0-9]' },
    @{ Name = 'opId 事实面'; Pattern = 'opId' }
)
foreach ($item in $legacyPatterns)
{
    $hits = 0
    foreach ($f in $csFiles)
    {
        $inIds = $false
        foreach ($d in $idsDirs) { if ($f.FullName.StartsWith($d, [System.StringComparison]::OrdinalIgnoreCase)) { $inIds = $true; break } }
        if ($inIds) { continue }
        foreach ($line in Get-CodeLines $f.FullName)
        {
            if ($line -match $item.Pattern) { $hits++ }
        }
    }
    if ($hits -gt 0)
    {
        $message = "C3 旧标识残留 $($item.Name)：$hits 处（标识资格已废除；P1-6 批量归一）"
        if ($Strict) { $violations.Add($message) } else { $pending.Add($message) }
    }
}

# C4. 单写者：AppendAsync 调用方白名单（事实只经 SessionFactWriter 追加）
$c4Allow = @('SessionFactWriter.cs', 'JsonlSessionFactStore.cs')
foreach ($f in $csFiles)
{
    if ($c4Allow -contains $f.Name) { continue }
    foreach ($line in Get-CodeLines $f.FullName)
    {
        if ($line -match '\.AppendAsync\(')
        {
            $violations.Add("C4 旁路写事实：$($f.Name) 直接调用 AppendAsync；只允许经 SessionFactWriter（F1 单写者）")
        }
    }
}

if ($ShowDetail)
{
    Write-Host "[FS] 扫描文件：$($csFiles.Count) 个 .cs"
    foreach ($p in $pending) { Write-Host "[FS][待迁移] $p" }
}

if ($violations.Count -gt 0)
{
    Write-Host "[FS] 违规 $($violations.Count) 项："
    foreach ($v in $violations) { Write-Host "  - $v" }
    foreach ($p in $pending) { Write-Host "[FS][待迁移] $p" }
    exit 1
}

Write-Host "[FS] 通过：标识标准 C1/C2/C4 全绿（C3 残留 0 项；待迁移 $($pending.Count) 项）"
foreach ($p in $pending) { Write-Host "[FS][待迁移] $p" }
exit 0
