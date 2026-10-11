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
         + 行 dbToggle；负对照（缺钮必被捕获）。
      G5 显示增强：AgentRuntimeUI.json 主题含 .db-* 类（.db-table-view/.db-toolbar/.db-btn/.db-row/.db-edit）
         且渲染侧含 class="title"/class="updated"；负对照（缺类必被捕获）。

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

$viewService = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Launcher\Modules\Libraries\ConversationViewService.cs'
# C7：数据库页渲染已拆为部分类（ConversationViewService.Db.cs）——判据须合并两文件为同一读侧面。
$viewServiceDb = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Launcher\Modules\Libraries\ConversationViewService.Db.cs'
$convLib     = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Launcher\Modules\Libraries\AgentUiLibraryConversation.cs'

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
# 入口注册可落在 Libraries 任一库文件（如 AgentUiLibraryBatch.cs）——扫整个 Libraries 目录。
$libDir = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Launcher\Modules\Libraries'
if (Test-Path $libDir) {
    $libText = [string]::Join("`n", @(Get-ChildItem -Path $libDir -Filter '*.cs' -File -ErrorAction SilentlyContinue | ForEach-Object { [System.IO.File]::ReadAllText($_.FullName) }))
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
if ((Test-Path -LiteralPath $viewService) -or (Test-Path -LiteralPath $viewServiceDb)) {
    $viewText = Read-ViewServiceText
    foreach ($m in (Get-MissingDbCmds $viewText)) {
        $violations.Add("[G4] 数据库页交互未闭合：缺 data-cmd '$m'（工具条五钮 + 行 dbToggle 缺一不可）")
    }
    # C11：行身份闭合 —— 行/按钮须含 data-type（行来源域），否则域无法回传（域丢失根因）。
    if ($viewText -notmatch [regex]::Escape('data-type')) {
        $violations.Add('[G4] 行/按钮缺 data-type（行来源域无法回传，读身份≠写删身份）')
    }
    # 负对照：仅含两钮（缺 dbBatchDelete/dbRefresh/分页/详情/保存 + dbToggle）的合成样本必须被捕获（防判据空转）。
    $probeDb = 'data-cmd="dbSelectAll" data-cmd="dbInvert"'
    if ((Get-MissingDbCmds $probeDb).Count -eq 0) {
        $violations.Add('[G4] 负对照失效：缺钮的合成样本未被交互闭合判据捕获')
    }
}

# ---------------- G5 显示增强（主题 .db-* 类 + 行 Title/Updated；负对照防判据空转） ----------------
$runtimeUi = Join-Path $Root 'Projects\Information\DrivenConfig\JsonConfig\AgentRuntimeUI.json'
$requiredDbClasses = @('.db-table-view', '.db-toolbar', '.db-btn', '.db-row', '.db-edit')
function Get-MissingDbClasses([string]$Text) {
    $missing = [System.Collections.Generic.List[string]]::new()
    foreach ($c in $requiredDbClasses) {
        if ($Text -notmatch [regex]::Escape($c)) { $missing.Add($c) }
    }
    return $missing
}
if (Test-Path -LiteralPath $runtimeUi) {
    $uiText = [System.IO.File]::ReadAllText($runtimeUi)
    foreach ($m in (Get-MissingDbClasses $uiText)) {
        $violations.Add("[G5] 数据库页主题缺 .db-* 样式类 '$m'（显示优化未落地）")
    }
    # 负对照：缺类的合成样本必须被捕获。
    if ((Get-MissingDbClasses '.db-table-view').Count -eq 0) {
        $violations.Add('[G5] 负对照失效：缺类的合成样本未被显示增强判据捕获')
    }
} else {
    $violations.Add("[G5] 缺失 AgentRuntimeUI.json（判据不可判）：$runtimeUi")
}
if ((Test-Path -LiteralPath $viewService) -or (Test-Path -LiteralPath $viewServiceDb)) {
    $viewText5 = Read-ViewServiceText
    if ($viewText5 -notmatch 'class=\\"title\\"') { $violations.Add('[G5] 渲染行缺 Title 展示（class="title"）') }
    if ($viewText5 -notmatch 'class=\\"updated\\"') { $violations.Add('[G5] 渲染行缺 Updated 展示（class="updated"）') }
}

# ---------------- G6 去字符串猜域（C4：Launcher 无 NormalizeDomain 猜域；负对照防判据空转） ----------------
$libDirG6 = Join-Path $Root 'Projects\Plug\Agent\src\Agent.Launcher\Modules\Libraries'
function Get-DomainGuessHits([string]$Text) {
    return @([regex]::Matches($Text, 'NormalizeDomain'))
}
if (Test-Path -LiteralPath $libDirG6) {
    foreach ($f in (Get-ChildItem -LiteralPath $libDirG6 -Filter '*.cs' -File)) {
        $t = [System.IO.File]::ReadAllText($f.FullName)
        if ((Get-DomainGuessHits $t).Count -gt 0) {
            $violations.Add("[G6] 字符串猜域残留：$($f.Name) 含 NormalizeDomain（应经域清单/端口分派，禁 Contains 猜域）")
        }
    }
    if ((Get-DomainGuessHits 'static string NormalizeDomain(string db) { if (db.Contains("task")) ... }').Count -eq 0) {
        $violations.Add('[G6] 负对照失效：合成样本（NormalizeDomain）未被猜域判据捕获')
    }
} else {
    $violations.Add("[G6] 缺失 Libraries 目录（判据不可判）：$libDirG6")
}

Write-Host ("  扫描读路径方法体: {0} 个" -f $checkedBodies) -ForegroundColor Gray

if ($violations.Count -eq 0) {
    Write-Host '  [成功] G1/G2/G3/G4/G5/G6 全部通过（零存储域 + 聚拢点唯一 + 交互闭合 + 显示增强 + 去猜域 + 正负对照绿）' -ForegroundColor Green
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
