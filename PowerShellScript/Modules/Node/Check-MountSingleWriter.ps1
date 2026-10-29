#requires -Version 5.1
<#
.SYNOPSIS
    UMF 唯一写入口门禁（MountSingleWriter）—— 归属边裸写回归锁。

.DESCRIPTION
    判据（设计单 §9.1 Check-MountSingleWriter 的最小可判定子集）：

      M1  `history_owns_node` 写路径 = 0（历史节点集只能由 HistoryNodes 派生）。
      M2  零依赖层（Share\Foundations\ScopeFramework）不得再产出 `node_to_node` 归属形状。
      M3  Agent Ui/MCP/业务层不得以 `AddReferenceAsync(..., "node_to_node")` 裸写 task→node 归属边。
      M4  生产代码不得出现 `RefType = "node_to_node"` + task→node 组合（归属语义只经 IMountPort）。

    负对照（-NegativeControl）：把合成样本喂给同一判据，必须命中；否则判据空转 ⇒ exit 1。

.PARAMETER Root
    仓库根目录（默认由脚本位置上溯四级推导：Frame\PowerShellScript\Modules\Node → 仓库根）。

.EXAMPLE
    .\Check-MountSingleWriter.ps1 -Check -NegativeControl
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$NegativeControl,
    [switch]$ShowDetail
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
}

$violations = [System.Collections.Generic.List[string]]::new()

Write-Host ''
Write-Host '============================================================' -ForegroundColor Cyan
Write-Host '  UMF MountSingleWriter（归属边裸写回归锁）' -ForegroundColor Cyan
Write-Host '============================================================' -ForegroundColor Cyan

function Get-CodeFiles([string[]]$roots) {
    foreach ($r in $roots) {
        if (-not (Test-Path $r)) { continue }
        Get-ChildItem -Path $r -Recurse -File -Include *.cs -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -notmatch '\\(obj|bin|BuildOutput)\\' }
    }
}

function Test-ForbiddenSample([string]$text) {
    return ($text -match 'AddReferenceAsync\s*\([^\r\n]*node_to_node' -or
            $text -match 'RefType\s*=\s*"node_to_node"')
}

# ---- M1：历史节点集不得存边（代码 + 声明面） ----
$codeFiles = Get-CodeFiles @(
    (Join-Path $Root 'Frame\CsharpPrj'),
    (Join-Path $Root 'Projects\Plug\Agent\src')
)
foreach ($file in $codeFiles) {
    $hits = Select-String -Path $file.FullName -Pattern '\bhistory_owns_node\b' -ErrorAction SilentlyContinue
    foreach ($hit in $hits) {
        # 注释/文档行不算写路径；仅命中字符串字面量或标识符即判红（保守）。
        if ($hit.Line.TrimStart().StartsWith('//') -or $hit.Line.TrimStart().StartsWith('#')) { continue }
        $violations.Add("M1 history_owns_node 写路径：$($file.FullName):$($hit.LineNumber)")
    }
}

$configRoot = Join-Path $Root 'Projects\Plug\Agent\Config'
if (Test-Path $configRoot) {
    foreach ($file in Get-ChildItem -Path $configRoot -Recurse -File -Include *.ushar -ErrorAction SilentlyContinue) {
        $hits = Select-String -Path $file.FullName -Pattern '^\s*(relation|edge|ref)\s*=.*history_owns_node' -ErrorAction SilentlyContinue
        foreach ($hit in $hits) { $violations.Add("M1 声明面 history_owns_node：$($file.FullName):$($hit.LineNumber)") }
    }
}

# ---- M2：零依赖层不得再产出 node_to_node 归属 ----
$scopeRoot = Join-Path $Root 'Frame\CsharpPrj\Share\Foundations\ScopeFramework'
foreach ($file in (Get-CodeFiles @($scopeRoot))) {
    $hits = Select-String -Path $file.FullName -Pattern '"node_to_node"' -ErrorAction SilentlyContinue
    foreach ($hit in $hits) {
        if ($hit.Line.TrimStart().StartsWith('//')) { continue }
        $violations.Add("M2 ScopeFramework 归属形状裸字面量：$($file.FullName):$($hit.LineNumber)")
    }
}

# ---- M3/M4：Agent 侧裸写 task→node 归属 ----
$agentSrc = Join-Path $Root 'Projects\Plug\Agent\src'
foreach ($file in (Get-CodeFiles @($agentSrc))) {
    $hits = Select-String -Path $file.FullName -Pattern 'AddReferenceAsync\s*\([^\r\n]*node_to_node|RefType\s*=\s*"node_to_node"' -ErrorAction SilentlyContinue
    foreach ($hit in $hits) {
        $line = $hit.Line.Trim()
        if ($line.StartsWith('//')) { continue }
        $violations.Add("M3/M4 Agent 裸写归属边：$($file.FullName):$($hit.LineNumber) → $line")
    }
}

# ---- 负对照：同一判据必须能抓到合成样本 ----
if ($NegativeControl) {
    $sample = 'await store.AddReferenceAsync(a, b, "node_to_node", 1.0, ct);'
    if (-not (Test-ForbiddenSample $sample)) {
        $violations.Add('NEG 负对照失效：合成样本未被判据命中（判据空转）')
    }
    $scopeSample = 'RefType = "node_to_node",'
    if (-not (Test-ForbiddenSample $scopeSample)) {
        $violations.Add('NEG 负对照失效：ScopeAction 形状样本未被命中')
    }
    Write-Host '  [负对照] 合成样本已被同一判据命中' -ForegroundColor DarkGray
}

if ($ShowDetail -and $violations.Count -gt 0) {
    $violations | ForEach-Object { Write-Host "  [违规] $_" -ForegroundColor Red }
}

if ($violations.Count -gt 0) {
    Write-Host "  [失败] 违规 $($violations.Count) 项" -ForegroundColor Red
    exit 1
}

Write-Host '  [通过] 归属边裸写 = 0；历史节点集派生模型未被破坏' -ForegroundColor Green
exit 0
