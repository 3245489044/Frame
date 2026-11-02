#requires -Version 5.1
<#
.SYNOPSIS
    节点读面单源门禁（NodeReadSingleSource）—— 键集解析单源 / 读轴声明 / 禁自写剥离。
.DESCRIPTION
    判据全部为源码/声明结构事实（不引执行者叙述）：
      R1 task-node 读轴声明闭合：[Gather] task-node 含 read-scope/read-source/read-edge/read-normalize/read-order；
         read-source=edge ⇒ read-edge 必填（负对照：删 read-source 必须判红）。
      R2 端点归一生产调用点 ≤ 基线（唯一实现 = NodeKeySetResolver；树仅剩声明流回退一处）。
      R3 节点键自写剥离（StripPrefix）在框架读面 = 0。
      R4 框架不引上层（Modules.Node 零 using Operation./Share./Agent.）。
      R5 NodeReadProjection 具备显式键集分支（ReadScope.Keys + 互斥校验）。
.PARAMETER Check
    校验模式：违规 → 退出码 1。
.PARAMETER NegativeControl
    合成负对照（必须判红）。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$NegativeControl
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
}

$violations = [System.Collections.Generic.List[string]]::new()
$gatherFile = Join-Path $Root 'Projects\Plug\Agent\Config\nodes\node-gather.ushar'
$nodeDir = Join-Path $Root 'Frame\CsharpPrj\Modules\Node'
$resolverFile = Join-Path $nodeDir 'Read\NodeKeySetResolver.cs'
$projectionFile = Join-Path $nodeDir 'Projection\NodeReadProjection.cs'

function Get-TaskNodeReadBlock {
    param([string]$Text)
    $lines = $Text -split "`r?`n"
    $start = -1
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i].Trim() -eq '[Gather] task-node') { $start = $i; break }
    }
    if ($start -lt 0) { return '' }
    $block = [System.Collections.Generic.List[string]]::new()
    for ($i = $start; $i -lt $lines.Count; $i++) {
        if ($i -gt $start -and $lines[$i] -match '^\[') { break }
        $block.Add($lines[$i])
    }
    return ($block -join "`n")
}

function Test-ReadAxis {
    param([string]$Block)
    $required = @('read-scope', 'read-source', 'read-edge', 'read-normalize', 'read-order')
    foreach ($marker in $required) {
        if ($Block -notmatch "(?m)^\s*$([regex]::Escape($marker))\s+\S+") {
            return "task-node 读轴缺 $marker=（读轴须声明，禁 C# 特判）"
        }
    }
    return $null
}

if (-not (Test-Path $gatherFile)) {
    $violations.Add("缺少聚拢声明：$gatherFile")
} else {
    $block = Get-TaskNodeReadBlock (Get-Content -LiteralPath $gatherFile -Raw)
    if ([string]::IsNullOrWhiteSpace($block)) {
        $violations.Add('缺 [Gather] task-node 声明')
    } elseif ($NegativeControl) {
        $broken = $block -replace '(?m)^\s*read-source\s+.*\r?\n', ''
        $message = Test-ReadAxis $broken
        if ([string]::IsNullOrWhiteSpace($message)) {
            $violations.Add('负对照未判红：删除 read-source 后仍通过（判据失效）')
        }
    } else {
        $message = Test-ReadAxis $block
        if ($message) { $violations.Add($message) }
    }
}

if (-not (Test-Path $resolverFile)) {
    $violations.Add('缺少键集解析单源：Frame\CsharpPrj\Modules\Node\Read\NodeKeySetResolver.cs')
}

$normalizeFiles = @(Get-ChildItem -LiteralPath (Join-Path $Root 'Frame\CsharpPrj') -Recurse -Filter '*.cs' -ErrorAction SilentlyContinue |
    Select-String -Pattern 'TryParseNodeIdentity' |
    Select-Object -ExpandProperty Path -Unique |
    Where-Object { $_ -notmatch 'NodeEndpointIds\.cs$' })
$normalizeBaseline = 2
if ($normalizeFiles.Count -gt $normalizeBaseline) {
    $violations.Add("端点归一生产调用文件数 = $($normalizeFiles.Count) > 基线 $normalizeBaseline（须经 NodeKeySetResolver 单源）")
}

$stripHits = @(Get-ChildItem -LiteralPath $nodeDir -Recurse -Filter '*.cs' -ErrorAction SilentlyContinue |
    Select-String -Pattern 'StripPrefix')
if ($stripHits.Count -gt 0) {
    $violations.Add("框架读面出现自写剥离 StripPrefix：$($stripHits[0].Path):$($stripHits[0].LineNumber)")
}

$upperHits = @(Get-ChildItem -LiteralPath $nodeDir -Recurse -Filter '*.cs' -ErrorAction SilentlyContinue |
    Select-String -Pattern '^\s*using\s+(Operation|Share|Agent|Usharp)\.')
if ($upperHits.Count -gt 0) {
    $violations.Add("Modules.Node 引入上层命名空间：$($upperHits[0].Path):$($upperHits[0].LineNumber)")
}

if (-not (Test-Path $projectionFile)) {
    $violations.Add('缺少读投影：NodeReadProjection.cs')
} else {
    $projection = Get-Content -LiteralPath $projectionFile -Raw
    if ($projection -notmatch 'IReadOnlyList<string>\?\s+Keys') {
        $violations.Add('ReadScope 缺显式键集字段 Keys')
    }
    if ($projection -notmatch 'Keys\s+与\s+KeyPrefix\s+互斥') {
        $violations.Add('缺 Keys/KeyPrefix 互斥校验（禁二义读轴）')
    }
}

# ===== I1 投影生产调用点唯一（FW-3）：ProjectAsync 只允许 NodeReadService 调用 =====
$projectAsyncFiles = @(Get-ChildItem -LiteralPath (Join-Path $Root 'Frame\CsharpPrj\Modules\Node') -Recurse -Filter '*.cs' -ErrorAction SilentlyContinue |
    Select-String -Pattern '\.ProjectAsync\s*\(' |
    Select-Object -ExpandProperty Path -Unique)
$projectionCallSites = $projectAsyncFiles.Count
if ($projectionCallSites -gt 1) {
    $violations.Add("ProjectAsync 生产调用文件数 = $projectionCallSites > 1（唯一入口 = NodeReadService）")
}
$projectionGate = Join-Path $nodeDir 'Projection\NodeReadProjection.cs'
$readServiceFile = Join-Path $nodeDir 'Read\NodeReadService.cs'
if (-not (Test-Path $readServiceFile)) {
    $violations.Add('缺少读门面：Read\NodeReadService.cs')
}

# ===== I2 全量读须显式声明（NodeKeySetScope.Full 出现点 ⊆ 白名单） =====
$fullScopeHits = @(Get-ChildItem -LiteralPath (Join-Path $Root 'Frame\CsharpPrj\Modules\Node') -Recurse -Filter '*.cs' -ErrorAction SilentlyContinue |
    Select-String -Pattern 'NodeKeySetScope\.Full' |
    Select-Object -ExpandProperty Path -Unique)
if ($fullScopeHits.Count -gt 3) {
    $violations.Add("NodeKeySetScope.Full 出现文件数 = $($fullScopeHits.Count) > 3（全量读须显式且白名单）")
}

# ===== I5 原因类型化（禁裸字符串原因） =====
$reasonEnumFile = Join-Path $nodeDir 'Read\NodeReadReason.cs'
if (-not (Test-Path $reasonEnumFile)) {
    $violations.Add('缺少原因注册枚举：Read\NodeReadReason.cs（I5）')
}
$bareReasonHits = @(Get-ChildItem -LiteralPath (Join-Path $Root 'Projects\Plug\Agent\src') -Recurse -Filter '*.cs' -ErrorAction SilentlyContinue |
    Select-String -Pattern '"prefix-undefined"')
if ($bareReasonHits.Count -gt 0) {
    $violations.Add("Agent 侧仍出现裸原因词 prefix-undefined：$($bareReasonHits[0].Path):$($bareReasonHits[0].LineNumber)")
}

# ===== I8 帧载荷解析单源（J2）：FramePayloadReader 生产调用点 ⊆ Share.NodeIo/Payload =====
$frameReaderHits = @(Get-ChildItem -LiteralPath (Join-Path $Root 'Projects\Plug\Agent\src') -Recurse -Filter '*.cs' -ErrorAction SilentlyContinue |
    Select-String -Pattern 'FramePayloadReader\.' |
    Where-Object { $_.Line.TrimStart() -notmatch '^(//|/\*|\*)' })
if ($frameReaderHits.Count -gt 0) {
    $violations.Add("Agent 侧仍直接调用 FramePayloadReader：$($frameReaderHits[0].Path):$($frameReaderHits[0].LineNumber)（唯一解析入口 = Share.NodeIo.NodePayloadReader）")
}

# ===== I7 声明可达（装配期校验器在位） =====
$validatorFile = Join-Path $nodeDir 'Spec\NodeSpecValidator.cs'
if ((Test-Path $validatorFile) -and ((Get-Content -LiteralPath $validatorFile -Raw) -notmatch 'ValidateGather')) {
    $violations.Add('NodeSpecValidator 缺 ValidateGather（声明字段可达性，I7）')
}

if ($violations.Count -gt 0) {
    Write-Host 'NodeReadSingleSource 违规：' -ForegroundColor Red
    foreach ($v in $violations) { Write-Host "  - $v" -ForegroundColor Red }
    if ($Check) { exit 1 }
    return
}

Write-Host 'NodeReadSingleSource：通过（读轴声明 / 归一单源 / 无自写剥离 / 框架无上层依赖）' -ForegroundColor Green
