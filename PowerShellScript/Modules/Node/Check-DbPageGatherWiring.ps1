#requires -Version 5.1
<#
.SYNOPSIS
    数据库页读侧统一守卫（DbPageGatherWiring）—— 数据库页路径零存储域 / 聚拢点为唯一读侧面。

.DESCRIPTION
    判据（数据库页 R1/R5）：
      G1 零存储域：数据库页新读路径（ConversationViewService 的 RenderDbTableHtmlAsync /
         RenderNodesViewAsync 方法体）内 `SELECT *` / `ur_nodes` / `ur_tasks` / `ur_histories` /
         `CellMaxLength` 命中 = 0。
      G2 唯一读侧 DTO：数据库页路径出现 `NodePanelModel`（域中立 DTO），且两个入口函数
         （AgentUi_DbTableHtml / AgentUi_DbTableMessageDispatch）已注册。
      G3 正/负对照：合成样本命中（含 `SELECT *`）必被 G1 捕获（防判据空转）。
      G4 交互闭合：ConversationViewService 含工具条 data-cmd（dbSelectAll/dbInvert/dbBatchDelete/
         dbRefresh/dbPrevPage/dbNextPage/dbNodeDetail/dbRecordSave/dbRevert/dbApplyFilter/dbFilterClear/dbSort）
         + 行 dbToggle + 行身份 data-type（行来源域）/data-source（真键回写，字面量在框架 HtmlDataRow.cs）；负对照（缺钮/缺身份必被捕获）。
      G5 显示增强：主题**资产包化**后判据面 = 源声明（themeSpec.componentPack + templatePacks）∧ 资产包产物
         `Assets\**\*.css` 含 .db-* 类（.db-table-view/.db-toolbar/.db-btn/.db-row/.db-edit）；
         渲染侧声明 Title/Updated 单元格（HtmlDataCell("title")/("updated")，经 Plate 通用行构建器产出 class="title"/class="updated"）；负对照（缺类必被捕获）。
      G6 去字符串猜域：Agent.Launcher 无 NormalizeDomain 字符串猜域（应经域清单/端口分派）；负对照（合成样本必被捕获）。
      G7 写身份闭合：渲染侧以写身份落桥（row.WriteDomain → data-type；row.SourceKey → data-session）∧ 展开判定用真键
         （ExpandedKey == sourceKey）∧ 写端口注册表 AgentWriteSources 四存储键齐全 ∧ 单键删除先查注册表；正/负对照。

    模式：默认报告；-Check 时违规 → 退出码 1。

.PARAMETER Root
    仓库根（默认为本脚本位置上溯四级）。
.PARAMETER Check
    校验模式。
.PARAMETER ShowDetail
    输出全部检查详情。
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [switch]$Check,
    [switch]$ShowDetail
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..\..')).Path
}

$violations = [System.Collections.Generic.List[string]]::new()

Write-Host ''
Write-Host '============================================================'
Write-Host '  数据库页读侧统一守卫（DbPageGatherWiring：零存储域 / 聚拢点唯一）'
Write-Host '============================================================'

$viewService = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Ui\Ui\Views\ConversationViewService.cs'
# C7：数据库页渲染已拆为部分类（ConversationViewService.Db.cs）——判据须合并两文件为同一读侧面。
$viewServiceDb = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Ui\Ui\Views\ConversationViewService.Db.cs'
$convLib     = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Ui\Core\Functions\Conversation\AgentUiLibraryConversation.cs'
# 行标记已下沉 Plate 通用行构建器（HtmlDataRowView）——行身份 data-type/data-source 字面量随之下沉，判据读集须并入。
$rowComponent = Join-Path $Root 'Frame\CsharpPrj\Plate\HtmlViewer\Html\Render\HtmlDataRow.cs'

function Read-ViewServiceText {
    $parts = [System.Collections.Generic.List[string]]::new()
    foreach ($f in @($viewService, $viewServiceDb)) {
        if (Test-Path -LiteralPath $f) { $parts.Add([System.IO.File]::ReadAllText($f)) }
    }
    return [string]::Join("`n", $parts)
}

# ---------------- 工具：抽取一个方法体（从签名字符串到下一个“    /// ”或“    private ”或“    internal ”或“    }”边界） ----------------
function Get-MethodBody {
    param([string]$Text, [string]$Signature, [string]$EndMarker)
    $start = $Text.IndexOf($Signature)
    if ($start -lt 0) { return $null }
    $end = $Text.IndexOf($EndMarker, $start)
    if ($end -lt 0) { return $Text.Substring($start) }
    return $Text.Substring($start, $end - $start)
}

$storageTokens = @('SELECT \*', 'ur_nodes', 'ur_tasks', 'ur_histories', 'CellMaxLength')

# ---------------- G1 零存储域（数据库页新读路径方法体） ----------------
$checkedBodies = 0
if ((Test-Path -LiteralPath $viewService) -or (Test-Path -LiteralPath $viewServiceDb)) {
    $text = Read-ViewServiceText
    $bodies = @(
        (Get-MethodBody -Text $text -Signature 'RenderDbTableHtmlAsync' -EndMarker "`n    /// <summary>聚拢面板模型"),
        (Get-MethodBody -Text $text -Signature 'RenderNodesViewAsync' -EndMarker "`n    /// <summary>数据库页（② 数据库）")
    )
    foreach ($body in $bodies) {
        if ($null -eq $body) { continue }
        # 兼容回退段（Unavailable 时才走的旧直读）不计入主路径存储域判据（设计允许的唯一回退面）。
        $cut = $body.IndexOf('兼容回退')
        if ($cut -ge 0) { $body = $body.Substring(0, $cut) }
        $checkedBodies++
        $code = @($body -split "`r?`n" | Where-Object { $_.TrimStart() -notmatch '^(//|/\*|\*)' })
        $codeText = [string]::Join("`n", $code)
        foreach ($tok in $storageTokens) {
            $hits = [regex]::Matches($codeText, $tok)
            foreach ($h in $hits) {
                $violations.Add("[G1] 数据库页读路径含存储域 token '$tok'（须经聚拢点 NodePanelModel，禁 SQL 直读）")
            }
        }
    }
} else {
    $violations.Add("[G1] 缺失 ConversationViewService.cs（判据不可判）")
}
if ($checkedBodies -eq 0) {
    $violations.Add('[G1] 未定位到数据库页读路径方法体（RenderDbTableHtmlAsync / RenderNodesViewAsync）')
}

# ---------------- G2 唯一读侧 DTO + 入口注册 ----------------
if (Test-Path -LiteralPath $viewService) {
    $text = [System.IO.File]::ReadAllText($viewService)
    if ($text -notmatch 'NodePanelModel') {
        $violations.Add('[G2] 数据库页路径未使用 NodePanelModel（域中立 DTO 缺位）')
    }
}
if (Test-Path -LiteralPath $convLib) {
    $text = [System.IO.File]::ReadAllText($convLib)
}
else {
    $text = ''
}
# 入口注册可落在 Modules 任一库文件（如 Assembly\Registration\AgentUiLibraryBatch.Registration.cs）——扫整个 Modules 目录。
$libDir = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Ui\Core'
if (Test-Path $libDir) {
    $libText = [string]::Join("`n", @(Get-ChildItem -Path $libDir -Filter '*.cs' -File -Recurse -ErrorAction SilentlyContinue | ForEach-Object { [System.IO.File]::ReadAllText($_.FullName) }))
    foreach ($fn in @('AgentUi_DbTableHtml', 'AgentUi_DbTableMessageDispatch')) {
        if ($libText -notmatch [regex]::Escape("`"$fn`"")) {
            $violations.Add("[G2] 数据库页入口函数未注册: $fn")
        }
    }
}
else {
    $violations.Add("[G2] 缺失 Libraries 目录（判据不可判）：$libDir")
}

# ---------------- G3 正/负对照（防判据空转） ----------------
$probe = 'SELECT * FROM ur_nodes LIMIT 10'
$probeHit = $false
foreach ($tok in $storageTokens) { if ([regex]::IsMatch($probe, $tok)) { $probeHit = $true } }
if (-not $probeHit) {
    $violations.Add('[G3] 负对照失效：合成样本（SELECT * FROM ur_nodes）未被存储域判据捕获')
}

# ---------------- G4 交互闭合（U5：工具条 data-cmd 五钮 + 行 dbToggle；负对照防判据空转） ----------------
$requiredDbCmds = @('dbSelectAll', 'dbInvert', 'dbBatchDelete', 'dbRefresh', 'dbPrevPage', 'dbNextPage', 'dbFirstPage', 'dbLastPage', 'dbGoPage', 'dbNodeDetail', 'dbRecordSave', 'dbRevert', 'dbApplyFilter', 'dbFilterClear', 'dbSort')
function Get-MissingDbCmds([string]$Text) {
    $missing = [System.Collections.Generic.List[string]]::new()
    foreach ($cmd in $requiredDbCmds) {
        if ($Text -notmatch [regex]::Escape($cmd)) { $missing.Add($cmd) }
    }
    if ($Text -notmatch [regex]::Escape('dbToggle')) { $missing.Add('dbToggle') }
    return $missing
}
# C11：行身份闭合判据（data-type=行来源域 / data-source=真键回写）——命中缺失项返回非空（供正判据 + 负对照）。
function Get-MissingRowIdentity([string]$Text) {
    $missing = [System.Collections.Generic.List[string]]::new()
    foreach ($attr in @('data-type', 'data-source')) {
        if ($Text -notmatch [regex]::Escape($attr)) { $missing.Add($attr) }
    }
    return $missing
}
if ((Test-Path -LiteralPath $viewService) -or (Test-Path -LiteralPath $viewServiceDb)) {
    $viewText = Read-ViewServiceText
    foreach ($m in (Get-MissingDbCmds $viewText)) {
        $violations.Add("[G4] 数据库页交互未闭合：缺 data-cmd '$m'（工具条五钮 + 行 dbToggle 缺一不可）")
    }
    # C11：行须含 data-type（行来源域，否则域无法回传）+ data-source（真键回写；Stream 域=聚合真行）。
    # 行标记现由 Plate 通用行构建器产出（字面量在框架 HtmlDataRow.cs），故行身份判据读集 = 领域渲染面 + 框架行构建器。
    $identityText = $viewText
    if (Test-Path -LiteralPath $rowComponent) { $identityText += "`n" + [System.IO.File]::ReadAllText($rowComponent) }
    foreach ($attr in (Get-MissingRowIdentity $identityText)) {
        $violations.Add("[G4] 行/按钮缺 $attr（行身份未闭合：data-type=行来源域 / data-source=真键回写；读身份≠写删身份）")
    }
    # 负对照：仅含两钮（缺 dbBatchDelete/dbRefresh/分页/详情/保存 + dbToggle）的合成样本必须被捕获（防判据空转）。
    $probeDb = 'data-cmd="dbSelectAll" data-cmd="dbInvert"'
    if ((Get-MissingDbCmds $probeDb).Count -eq 0) {
        $violations.Add('[G4] 负对照失效：缺钮的合成样本未被交互闭合判据捕获')
    }
    # 负对照：缺 data-type/data-source 的合成样本必须被行身份判据捕获（防判据空转）。
    if ((Get-MissingRowIdentity 'data-cmd="dbToggle"').Count -eq 0) {
        $violations.Add('[G4] 负对照失效：缺 data-type/data-source 的合成样本未被行身份判据捕获')
    }
}

# ---------------- G5 显示增强（样式真源归一 + 行 Title/Updated；负对照防判据空转） ----------------
# 判据面（G2 真源归一的**终态**）：样式真源 = **框架 CSS 资产**（`HtmlAssetRegistry.DefaultCss` /
#   `FrameworkComponentCss`），经 `HtmlDocumentRenderer` 合成入文档 —— 单源、仓内、零副本。
#   应用域**不得**再引入：① 无消费者的死声明（themeSpec / templatePacks）；② 渲染零命中的死样式
#   （`.db-*` 类：渲染侧实际用通用类 toolbar/row/code-input mono）；③ 构建产物里的资产副本。
$runtimeUi = Join-Path $Root 'Projects\Information\DrivenConfig\JsonConfig\AgentRuntimeUI.json'
$assetRegistry = Join-Path $Root 'Frame\CsharpPrj\Plate\HtmlViewer\Html\Assets\HtmlAssetRegistry.cs'
$docRenderer = Join-Path $Root 'Frame\CsharpPrj\Plate\HtmlViewer\HtmlDocumentRenderer.cs'
function Get-DeadWeightHits([string]$Text) {
    $hits = [System.Collections.Generic.List[string]]::new()
    if ($Text -match '"themeSpec"') { $hits.Add('themeSpec（无消费者的死声明）') }
    if ($Text -match '"templatePacks"') { $hits.Add('templatePacks（无消费者的死声明）') }
    if ($Text -match '\.db-(table-view|toolbar|btn|row|edit)\b') { $hits.Add('.db-* 死样式（渲染零命中：实际用 toolbar/row/code-input mono）') }
    return $hits
}
if (-not (Test-Path -LiteralPath $runtimeUi)) {
    $violations.Add("[G5] 缺失 AgentRuntimeUI.json（判据不可判）：$runtimeUi")
} else {
    foreach ($h in (Get-DeadWeightHits ([System.IO.File]::ReadAllText($runtimeUi)))) {
        $violations.Add("[G5] 冗余残留：$h")
    }
    # 负对照：合成样本（含死声明 + 死样式）必须被捕获（防判据空转）。
    if ((Get-DeadWeightHits '{"themeSpec":{},"templatePacks":[]} /* .db-row{} */').Count -eq 0) {
        $violations.Add('[G5] 负对照失效：冗余残留的合成样本未被判据捕获')
    }
    # 样式真源在位（框架资产），且确被合成入文档（单源生效面）。
    if (-not (Test-Path -LiteralPath $assetRegistry)) {
        $violations.Add('[G5] 框架样式真源缺失：HtmlAssetRegistry.cs')
    }
    if ((Test-Path -LiteralPath $docRenderer) -and
        ([System.IO.File]::ReadAllText($docRenderer) -notmatch 'FrameworkComponentCss')) {
        $violations.Add('[G5] 框架组件样式未合成入文档（HtmlDocumentRenderer 未见 FrameworkComponentCss）')
    }
}
if ((Test-Path -LiteralPath $viewService) -or (Test-Path -LiteralPath $viewServiceDb)) {
    $viewText5 = Read-ViewServiceText
    if ($viewText5 -notmatch 'HtmlDataCell\(\s*"title"') { $violations.Add('[G5] 渲染行缺 Title 单元格（HtmlDataCell("title")；行标记经 Plate 通用行构建器产出 class="title"）') }
    if ($viewText5 -notmatch 'HtmlDataCell\(\s*"updated"') { $violations.Add('[G5] 渲染行缺 Updated 单元格（HtmlDataCell("updated")；行标记经 Plate 通用行构建器产出 class="updated"）') }
}

# ---------------- G6 去字符串猜域（C4/ERR-02：Launcher 全域无「归一函数内 Contains 子串猜域」；负对照防判据空转） ----------------
$launcherDirG6 = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Ui'
function Get-DomainGuessHits([string]$Text) {
    $hits = [System.Collections.Generic.List[string]]::new()
    if ($Text -match 'NormalizeDomain') { $hits.Add('NormalizeDomain') }
    # 结构式判据（ERR-02 补强）：Normalize* 归一函数体窗口内出现 .Contains( ⇒ 子串猜域（如 db.Contains("ur_nodes")）。
    foreach ($m in [regex]::Matches($Text, 'string\?\s+Normalize\w*\s*\([^)]*\)\s*\{')) {
        $window = $Text.Substring($m.Index, [Math]::Min(2000, $Text.Length - $m.Index))
        if ($window -match '\.Contains\(') { $hits.Add('Normalize*.Contains'); break }
    }
    return $hits
}
if (Test-Path -LiteralPath $launcherDirG6) {
    foreach ($f in (Get-ChildItem -LiteralPath $launcherDirG6 -Filter '*.cs' -File -Recurse -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -notmatch '\\(bin|obj|BuildOutput)\\' })) {
        $t = [System.IO.File]::ReadAllText($f.FullName)
        if ((Get-DomainGuessHits $t).Count -gt 0) {
            $violations.Add("[G6] 字符串猜域残留：$($f.Name)（应经域清单/端口分派，禁 Contains 子串猜域）")
        }
    }
    if ((Get-DomainGuessHits 'static string NormalizeDomain(string db) { if (db.Contains("task")) return "x"; return null; }').Count -eq 0) {
        $violations.Add('[G6] 负对照失效：合成样本（Normalize*+Contains）未被猜域判据捕获')
    }
} else {
    $violations.Add("[G6] 缺失 Agent.Launcher 目录（判据不可判）：$launcherDirG6")
}

# ---------------- G7 写身份闭合（读身份 ≠ 写/删身份；正/负对照防判据空转） ----------------
# 判据（收口不变量）：① 渲染侧以写身份落桥（row.WriteDomain → data-type；row.SourceKey → data-session）；
#   ② 展开判定用真键（ExpandedKey 与 row.SourceKey 比较，而非 row.Key 显示键）；
#   ③ 写域 → 写端口注册表在位（AgentWriteSources，四存储键齐全）；
#   ④ 单键删除先查写端口注册表（禁 switch 猜域兜底）。
$writeSources = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Ui\Core\Functions\Batch\AgentWriteSources.cs'
$batchFile = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Ui\Core\Functions\Batch\AgentUiLibraryBatch.cs'
function Get-WriteIdentityMisses([string]$ViewText, [string]$BatchText, [string]$SourcesText) {
    $misses = [System.Collections.Generic.List[string]]::new()
    if ($ViewText -notmatch 'row\.WriteDomain') { $misses.Add('渲染行未声明写域（row.WriteDomain）') }
    if ($ViewText -notmatch 'Session:\s*sourceKey') { $misses.Add('桥命令键未用真键（Session: sourceKey）') }
    if ($ViewText -notmatch 'summary\)\s*=\s*sourceKey' -and $ViewText -notmatch '\["session"\]\s*=\s*sourceKey') { $misses.Add('摘要点击未用真键（data-session=sourceKey）') }
    if ($ViewText -notmatch 'state\.ExpandedKey,\s*sourceKey') { $misses.Add('展开判定未用真键（ExpandedKey == sourceKey）') }
    if ($SourcesText -notmatch 'DbStoreKeys\.Nodes' -or $SourcesText -notmatch 'DbStoreKeys\.Tasks' -or
        $SourcesText -notmatch 'DbStoreKeys\.Histories' -or $SourcesText -notmatch 'DbStoreKeys\.Graph') {
        $misses.Add('写端口注册表缺存储键（Nodes/Tasks/Histories/Graph 四行）')
    }
    if ($BatchText -notmatch 'AgentWriteSources\.TryGet') { $misses.Add('单键删除未先查写端口注册表（DeleteOneAsync）') }
    return $misses
}
$viewText7 = ''
if ((Test-Path -LiteralPath $viewService) -or (Test-Path -LiteralPath $viewServiceDb)) { $viewText7 = Read-ViewServiceText }
$batchText7 = if (Test-Path -LiteralPath $batchFile) { [System.IO.File]::ReadAllText($batchFile) } else { '' }
$sourcesText7 = if (Test-Path -LiteralPath $writeSources) { [System.IO.File]::ReadAllText($writeSources) } else { '' }
if (-not (Test-Path -LiteralPath $writeSources)) {
    $violations.Add("[G7] 缺件：写端口注册表（$writeSources）")
}
if ($viewText7 -and $batchText7 -and $sourcesText7) {
    foreach ($m in (Get-WriteIdentityMisses $viewText7 $batchText7 $sourcesText7)) {
        $violations.Add("[G7] 写身份未闭合：$m")
    }
    # 负对照：把写身份摘掉的合成样本必须被捕获（防判据空转）。
    $probeMisses = Get-WriteIdentityMisses 'Session: row.Key' 'switch (domain)' ''
    if ($probeMisses.Count -eq 0) {
        $violations.Add('[G7] 负对照失效：写身份缺失的合成样本未被判据捕获')
    }
}

Write-Host ("  扫描读路径方法体: {0} 个" -f $checkedBodies) -ForegroundColor Gray

if ($violations.Count -eq 0) {
    Write-Host '  [成功] G1/G2/G3/G4/G5/G6/G7 全部通过（零存储域 + 聚拢点唯一 + 交互闭合 + 显示增强（资产包面）+ 去猜域 + 写身份闭合 + 正负对照绿）' -ForegroundColor Green
    Write-Host ''
    exit 0
}

Write-Host ""
Write-Host "  [违规] $($violations.Count) 项：" -ForegroundColor Red
foreach ($v in $violations) { Write-Host "    - $v" -ForegroundColor Red }
Write-Host '  结果: 失败' -ForegroundColor Red
Write-Host ''
if ($Check) { exit 1 }
exit 0
