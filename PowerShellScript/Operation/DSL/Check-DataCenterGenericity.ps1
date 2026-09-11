# Check-DataCenterGenericity.ps1
# DataCenter 通用化不变式校验器（对齐 Frame\DataCenter\Doc\DataCenter-通用化重构与实施计划.md §2 G1/G2 与 §6 A1/A2/A3）
# 两级检查：
#   [G1 领域词汇零出现] 扫描 DataCenter.Core 源码 + 工程元数据 + schemas，断言无 Loop/Decision/Eval/Budget/Token/Flag/Round/Agent/Engine。
#   [G2 设施端口化]     扫描 DataCenter.Core，断言不自主出现引擎/宿主设施类型
#                       （IStorageCell/StorageCell/BasicMethod.Block/FrameworkAbstractions），并校验 csproj 无设施工程引用。
#                       白名单 = 契约强制点（Compat 适配目录 / 契约签名暴露处），逐条登记理由。
#   - 报告模式（默认）：输出命中清单与白/黑分类，不设退出码（迁移期看板）。
#   - 校验模式（-Check）：存在非白命中时以非零退出码失败（CI 门禁，防领域词汇回流与设施回渗）。
# 语义：领域逻辑经 DataCenter.Extensions.* 单向消费 Core 端口注入，Core 只提供通用状态能力与端口。

[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [string[]]$Words = @('Loop', 'Decision', 'Eval', 'Budget', 'Token', 'Flag', 'Round', 'Agent', 'Engine'),
    [string[]]$Whitelist = @('Dynamic', 'Tokenization', 'Roundtrip')
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root))
{
    # 脚本位于 Frame\PowerShellScript\Operation\DSL\，上溯四级即仓库根
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
}

$coreDir = Join-Path $Root 'Frame\DataCenter\DataCenter.Core'
$schemaDir = Join-Path $Root 'Frame\DataCenter\schemas'
if (-not (Test-Path $coreDir)) { Write-Error "未找到 DataCenter.Core: $coreDir"; exit 2 }

# 扫描面：Core 全部 .cs（排除 obj/bin）+ Core 工程文件（ModuleRule 元数据）+ schemas（跨语言中立性）
$files = @()
$files += Get-ChildItem -Path $coreDir -Recurse -File -Include *.cs -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' -and $_.FullName -notlike '*\_deprecated\*' }
$files += Get-ChildItem -Path $coreDir -Recurse -File -Include *.csproj -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' }
if (Test-Path $schemaDir) {
    $files += Get-ChildItem -Path $schemaDir -Recurse -File -Include *.json -ErrorAction SilentlyContinue
}

$wordPattern = ($Words | ForEach-Object { '\b' + [regex]::Escape($_) }) -join '|'
$whitePattern = if ($Whitelist.Count -gt 0) { ($Whitelist | ForEach-Object { '\b' + [regex]::Escape($_) + '\w*' }) -join '|' } else { $null }

$hits = [System.Collections.Generic.List[object]]::new()
foreach ($file in $files) {
    $lines = Get-Content $file.FullName -ErrorAction SilentlyContinue
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $text = $lines[$i]
        if ([string]::IsNullOrWhiteSpace($text)) { continue }
        # 先抹掉白名单词（Dynamic/Tokenization/Roundtrip 等误报源），再匹配领域词
        $probe = if ($whitePattern) { [regex]::Replace($text, $whitePattern, '', 'IgnoreCase') } else { $text }
        foreach ($m in [regex]::Matches($probe, $wordPattern, 'IgnoreCase')) {
            $hits.Add([pscustomobject]@{
                File = $file.FullName.Substring($Root.Length).TrimStart('\')
                Line = $i + 1
                Word = $m.Value
                Text = $text.Trim()
            })
        }
    }
}

Write-Host "=== [G1] DataCenter 领域词汇零出现 ==="
Write-Host ("扫描文件数: {0} | 词表: {1} | 白名单: {2} | 命中: {3}" -f `
    $files.Count, ($Words -join '/'), ($Whitelist -join '/'), $hits.Count)

if ($hits.Count -gt 0) {
    Write-Host "--- 命中清单（文件:行 词 片段） ---"
    $hits | ForEach-Object { Write-Host ("  {0}:{1} [{2}] {3}" -f $_.File, $_.Line, $_.Word, $_.Text) }
}

# ── [G2] 设施端口化：Core 不自主出现引擎/宿主设施类型（白名单 = 契约强制点） ──
$facilityPatterns = [ordered]@{
    'IStorageCell'          = '\bIStorageCell\b'
    'StorageCell'           = '\bStorageCell\b'
    'BasicMethod.Block'     = 'BasicMethod\.Block'
    'FrameworkAbstractions' = 'FrameworkAbstractions'
}
# 白名单：契约强制点（路径子串 + 模式）——逐条登记理由，不得放宽为整目录泛白
$facilityAllowList = @(
    [pscustomobject]@{ Pattern = 'IStorageCell';          Path = '\DataCenter.Core\Compat\';                      Reason = '契约强制：IStateCenter.Cell → IStorageCell 的适配/视图' }
    [pscustomobject]@{ Pattern = 'StorageCell';           Path = '\DataCenter.Core\Compat\';                      Reason = '契约强制：IStorageCell 契约面适配' }
    [pscustomobject]@{ Pattern = 'IStorageCell';          Path = '\DataCenter.Core\Core\StateCenter.cs';          Reason = '契约强制：IStateCenter.Cell 成员 + 迁移期兼容构造（P8.2 重载并存）' }
    [pscustomobject]@{ Pattern = 'StorageCell';           Path = '\DataCenter.Core\Core\StateCenter.cs';          Reason = '契约强制：上述适配类型引用' }
    [pscustomobject]@{ Pattern = 'IStorageCell';          Path = '\DataCenter.Core\Core\StateStore.cs';           Reason = '契约强制：IStateStore 复用路径 + 迁移期兼容构造（P8.2）' }
    [pscustomobject]@{ Pattern = 'StorageCell';           Path = '\DataCenter.Core\Core\StateStore.cs';           Reason = '契约强制：上述适配类型引用' }
    [pscustomobject]@{ Pattern = 'IStorageCell';          Path = '\DataCenter.Core\Layering\LayeredStoreClient.cs'; Reason = '契约强制：Cell 直通（Agent/Engine 消费 IStorageCell）' }
    [pscustomobject]@{ Pattern = 'BasicMethod.Block';     Path = '\DataCenter.Core\Core\StateCenter.cs';          Reason = '契约强制：IStateOrchestrator.ExecutePipelineAsync → ExecutionPipeline' }
)

$facilityFiles = Get-ChildItem -Path $coreDir -Recurse -File -Include *.cs -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' }
$facilityAllowed = [System.Collections.Generic.List[object]]::new()
$facilityHits = [System.Collections.Generic.List[object]]::new()
$facilityCommentOnly = 0
foreach ($file in $facilityFiles) {
    $rel = $file.FullName.Substring($Root.Length).TrimStart('\')
    foreach ($name in $facilityPatterns.Keys) {
        $pattern = $facilityPatterns[$name]
        $matched = Select-String -Path $file.FullName -Pattern $pattern -ErrorAction SilentlyContinue
        if (-not $matched) { continue }
        $whitelisted = $facilityAllowList | Where-Object { $_.Pattern -eq $name -and $rel -like ('*' + $_.Path + '*') }
        foreach ($m in $matched) {
            $trimmed = $m.Line.Trim()
            # 门禁判据 = 代码依赖；注释内的说明性提及（语义对齐说明）不计入设施回渗
            if ($trimmed.StartsWith('//') -or $trimmed.StartsWith('/*') -or $trimmed.StartsWith('*')) {
                $facilityCommentOnly++
                continue
            }
            $entry = [pscustomobject]@{ File = $rel; Line = $m.LineNumber; Pattern = $name; Text = $trimmed }
            if ($whitelisted) { $facilityAllowed.Add($entry) } else { $facilityHits.Add($entry) }
        }
    }
}

# csproj 依赖闭合：不得引用引擎存储/服务定位器工程（Block 仅契约强制，见工程注释）
$coreCsproj = Join-Path $coreDir 'DataCenter.Core.csproj'
$csprojHits = [System.Collections.Generic.List[string]]::new()
if (Test-Path $coreCsproj) {
    foreach ($forbidden in @('InternalStorage', 'FrameworkAbstractions')) {
        Select-String -Path $coreCsproj -Pattern ('ProjectReference[^\r\n]*' + $forbidden) -ErrorAction SilentlyContinue |
            ForEach-Object { $csprojHits.Add(('DataCenter.Core.csproj:{0} 引用 {1}' -f $_.LineNumber, $forbidden)) }
    }
}

Write-Host ""
Write-Host "=== [G2] DataCenter 设施端口化（Core 不自主依赖引擎/宿主设施） ==="
Write-Host ("白名单命中（契约强制）: {0} | 违规命中: {1} | csproj 违规: {2} | 注释提及（不计入）: {3}" -f `
    $facilityAllowed.Count, $facilityHits.Count, $csprojHits.Count, $facilityCommentOnly)
if ($facilityAllowed.Count -gt 0) {
    Write-Host "--- 白名单（契约强制点） ---"
    $facilityAllowed | ForEach-Object { Write-Host ("  [允许] {0}:{1} [{2}]" -f $_.File, $_.Line, $_.Pattern) }
}
if ($facilityHits.Count -gt 0) {
    Write-Host "--- 违规清单（设施回渗） ---"
    $facilityHits | ForEach-Object { Write-Host ("  [违规] {0}:{1} [{2}] {3}" -f $_.File, $_.Line, $_.Pattern, $_.Text) }
}
if ($csprojHits.Count -gt 0) {
    $csprojHits | ForEach-Object { Write-Host ("  [违规] {0}" -f $_) }
}

$failed = $hits.Count -gt 0 -or $facilityHits.Count -gt 0 -or $csprojHits.Count -gt 0
if ($Check -and $failed) {
    Write-Host "校验失败：G1 领域词汇或 G2 设施依赖未收敛（详见上方违规清单）。"
    exit 1
}
exit 0
