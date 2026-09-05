<#
.SYNOPSIS
    增强输出辅助函数模块 — 提供比 OutputHelpers 更丰富的控制台输出格式

.DESCRIPTION
    从 DockerCommon.psm1 和 Install-McpServer.ps1 中提取的纯通用输出函数。
    与 Modules/Core/OutputHelpers.psm1 签名不同，作为补充而非替代。

    包含函数:
        Write-Banner       — 带 ╔═╗ 框的醒目标题
        Write-Section      — 带 ══ 边框的章节标题
        Write-Step         — 带 ━━ 边框的步骤标题
        Write-SubSection   — 带 ── 的子标题
        Write-MenuHeader   — 菜单标题（带 ━━ 边框）
        Write-MenuItem     — 菜单项 [Key] Desc
        Write-Result       — 带图标的通用结果输出
        Pause-Menu         — 按 Enter 继续
        Write-WhatIf       — WhatIf 模式消息

    用法:
        Import-Module .\BasicMethod-Output.psm1
        Write-Banner "标题" "副标题"
        Write-Section "章节"
        Write-Step "步骤"
        Write-Result "OK" "操作成功"
#>

# ============================================
# 增强输出函数
# ============================================

function Write-Banner {
    <#
    .SYNOPSIS
        显示带 ╔═╗ 框的醒目标题
    .PARAMETER Title
        标题文本
    .PARAMETER Subtitle
        副标题文本（可选）
    .PARAMETER Color
        颜色，默认 Cyan
    .EXAMPLE
        Write-Banner "Docker 管理工具"
        Write-Banner "安装工具" "版本 1.0" "Green"
    #>
    param(
        [string]$Title = "标题",
        [string]$Subtitle = "",
        [string]$Color = "Cyan"
    )
    Write-Host "╔══════════════════════════════════════════════════════════════╗" -ForegroundColor $Color
    Write-Host "║     $($Title.PadRight(58))║" -ForegroundColor $Color
    if ($Subtitle) {
        Write-Host "║     $($Subtitle.PadRight(58))║" -ForegroundColor $Color
    }
    Write-Host "╚══════════════════════════════════════════════════════════════╝" -ForegroundColor $Color
    Write-Host ""
}

function Write-Section {
    <#
    .SYNOPSIS
        显示带 ══ 边框的章节标题
    .PARAMETER Title
        章节标题
    .PARAMETER Color
        颜色，默认 Cyan
    .EXAMPLE
        Write-Section "环境检查"
    #>
    param(
        [string]$Title,
        [string]$Color = "Cyan"
    )
    Write-Host "`n═══════════════════════════════════════════════════════════════" -ForegroundColor $Color
    Write-Host "  $Title" -ForegroundColor $Color
    Write-Host "═══════════════════════════════════════════════════════════════" -ForegroundColor $Color
}

function Write-Step {
    <#
    .SYNOPSIS
        显示带 ━━ 边框的步骤标题
    .PARAMETER Title
        步骤标题
    .PARAMETER Color
        颜色，默认 Cyan
    .EXAMPLE
        Write-Step "正在安装..."
    #>
    param(
        [string]$Title,
        [string]$Color = "Cyan"
    )
    Write-Host "`n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor $Color
    Write-Host "  $Title" -ForegroundColor $Color
    Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor $Color
}

function Write-SubSection {
    <#
    .SYNOPSIS
        显示带 ── 的子标题
    .PARAMETER Title
        子标题文本
    .PARAMETER Color
        颜色，默认 White
    .EXAMPLE
        Write-SubSection "详细配置"
    #>
    param(
        [string]$Title,
        [string]$Color = "White"
    )
    Write-Host "`n  ── $Title ──" -ForegroundColor $Color
}

function Write-MenuHeader {
    <#
    .SYNOPSIS
        显示菜单标题（带 ━━ 边框）
    .PARAMETER Title
        菜单标题
    .PARAMETER Color
        颜色，默认 Cyan
    .EXAMPLE
        Write-MenuHeader "请选择操作"
    #>
    param(
        [string]$Title,
        [string]$Color = "Cyan"
    )
    Write-Host "`n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor $Color
    Write-Host "  $Title" -ForegroundColor $Color
    Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor $Color
}

function Write-MenuItem {
    <#
    .SYNOPSIS
        显示菜单项 [Key] Desc
    .PARAMETER Key
        按键标识
    .PARAMETER Desc
        描述文本
    .PARAMETER Color
        颜色，默认 White
    .EXAMPLE
        Write-MenuItem "1" "安装 MCP 服务器"
    #>
    param(
        [string]$Key,
        [string]$Desc,
        [string]$Color = "White"
    )
    Write-Host "  [$Key] $Desc" -ForegroundColor $Color
}

function Write-Result {
    <#
    .SYNOPSIS
        显示带图标的通用结果输出
    .PARAMETER Status
        状态标识: OK / FAIL / INFO / WARN
    .PARAMETER Message
        消息文本
    .EXAMPLE
        Write-Result "OK" "操作成功"
        Write-Result "FAIL" "操作失败"
    #>
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet("OK", "FAIL", "INFO", "WARN")]
        [string]$Status,

        [Parameter(Mandatory = $true)]
        [string]$Message
    )
    $icon = switch ($Status) {
        "OK"   { "✅" }
        "FAIL" { "❌" }
        "INFO" { "ℹ️ " }
        "WARN" { "⚠️ " }
    }
    $color = switch ($Status) {
        "OK"   { "Green" }
        "FAIL" { "Red" }
        "INFO" { "Gray" }
        "WARN" { "Yellow" }
    }
    Write-Host "  $icon [$Status] $Message" -ForegroundColor $color
}

function Pause-Menu {
    <#
    .SYNOPSIS
        暂停并等待用户按 Enter 继续
    .PARAMETER Message
        提示消息（可选，默认 "按 Enter 键返回菜单..."）
    .EXAMPLE
        Pause-Menu
        Pause-Menu "按 Enter 键继续..."
    #>
    param(
        [string]$Message = "  按 Enter 键返回菜单..."
    )
    Write-Host ""
    Write-Host $Message -ForegroundColor Gray
    $null = Read-Host
}

function Write-WhatIf {
    <#
    .SYNOPSIS
        显示 WhatIf 模式消息
    .PARAMETER Msg
        消息文本
    .EXAMPLE
        Write-WhatIf "将安装 context7-mcp"
    #>
    param(
        [string]$Msg
    )
    Write-Host "  [WhatIf] $Msg" -ForegroundColor Magenta
}

# ============================================================
# 便捷输出函数（带 emoji 前缀，适合脚本流程输出）
# ============================================================

function Write-Info {
    <#
    .SYNOPSIS
        显示灰色信息消息（带 ℹ️ 图标）
    .PARAMETER Message
        消息文本
    .EXAMPLE
        Write-Info "正在检查 Docker 状态..."
    #>
    param([string]$Message)
    Write-Host "  ℹ️  $Message" -ForegroundColor Gray
}

function Write-Success {
    <#
    .SYNOPSIS
        显示绿色成功消息（带 ✅ 图标）
    .PARAMETER Message
        消息文本
    .EXAMPLE
        Write-Success "Docker 引擎运行中"
    #>
    param([string]$Message)
    Write-Host "  ✅ $Message" -ForegroundColor Green
}

function Write-Warn {
    <#
    .SYNOPSIS
        显示黄色警告消息（带 ⚠️ 图标）
    .PARAMETER Message
        消息文本
    .EXAMPLE
        Write-Warn "容器未运行"
    #>
    param([string]$Message)
    Write-Host "  ⚠️  $Message" -ForegroundColor Yellow
}

function Write-Error {
    <#
    .SYNOPSIS
        显示红色错误消息（带 ❌ 图标）
    .PARAMETER Message
        消息文本
    .EXAMPLE
        Write-Error "Docker 未安装"
    #>
    param([string]$Message)
    Write-Host "  ❌ $Message" -ForegroundColor Red
}

function Write-Hint {
    <#
    .SYNOPSIS
        显示灰色提示消息
    .PARAMETER Message
        消息文本
    .EXAMPLE
        Write-Hint "使用 -Force 强制重新安装"
    #>
    param([string]$Message)
    Write-Host "  💡 $Message" -ForegroundColor Gray
}

function Write-Header {
    <#
    .SYNOPSIS
        显示带 ╔═╗ 框的脚本标题
    .PARAMETER Text
        标题文本
    .PARAMETER Color
        颜色，默认 Cyan
    .EXAMPLE
        Write-Header "Invoke-CleanAll — 集中清理"
    #>
    param(
        [string]$Text,
        [string]$Color = "Cyan"
    )
    Write-Host ""
    Write-Host "╔════════════════════════════════════════════════╗" -ForegroundColor $Color
    Write-Host "║  $Text" -ForegroundColor $Color
    Write-Host "╚════════════════════════════════════════════════╝" -ForegroundColor $Color
}

function Write-SubHeader {
    <#
    .SYNOPSIS
        显示带 ▶ 的子步骤标题
    .PARAMETER Text
        子标题文本
    .PARAMETER Color
        颜色，默认 Yellow
    .EXAMPLE
        Write-SubHeader "▶ 清理 1/3：BuildOutput 目录"
    #>
    param(
        [string]$Text,
        [string]$Color = "Yellow"
    )
    Write-Host ""
    Write-Host "$Text" -ForegroundColor $Color
}

function Write-StepTitle {
    <#
    .SYNOPSIS
        显示带 ▶ 的步骤标题（与 Write-Step 不同，这是流程步骤）
    .PARAMETER Text
        步骤文本
    .PARAMETER Color
        颜色，默认 Cyan
    .EXAMPLE
        Write-StepTitle "步骤 1/4：验证 .NET SDK 安装"
    #>
    param(
        [string]$Text,
        [string]$Color = "Cyan"
    )
    Write-Host "`n▶ $Text" -ForegroundColor $Color
}

# ============================================================
# 状态图标映射
# ============================================================

function Get-StatusIcon {
    <#
    .SYNOPSIS
        获取状态对应的图标和颜色
    .DESCRIPTION
        统一的状态→图标/颜色映射表，避免各脚本重复定义 switch 语句。
    .PARAMETER Status
        状态标识: OK / FAIL / WARN / INFO / PASS / RUNNING / STOPPED / SKIP
    .PARAMETER IconOnly
        只返回图标，不返回颜色
    .PARAMETER ColorOnly
        只返回颜色，不返回图标
    .OUTPUTS
        默认返回 [hashtable] @{ Icon; Color }
        -IconOnly 返回 [string] 图标
        -ColorOnly 返回 [string] 颜色名
    .EXAMPLE
        $s = Get-StatusIcon "OK"; Write-Host "$($s.Icon) 成功" -ForegroundColor $s.Color
        $icon = Get-StatusIcon "FAIL" -IconOnly
        $color = Get-StatusIcon "WARN" -ColorOnly
    #>
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet("OK", "FAIL", "WARN", "INFO", "PASS", "RUNNING", "STOPPED", "SKIP")]
        [string]$Status,

        [switch]$IconOnly,
        [switch]$ColorOnly
    )

    $map = @{
        "OK"      = @{ Icon = "✅"; Color = "Green" }
        "PASS"    = @{ Icon = "✅"; Color = "Green" }
        "FAIL"    = @{ Icon = "❌"; Color = "Red" }
        "WARN"    = @{ Icon = "⚠️ "; Color = "Yellow" }
        "INFO"    = @{ Icon = "ℹ️ "; Color = "Gray" }
        "RUNNING" = @{ Icon = "🟢"; Color = "Green" }
        "STOPPED" = @{ Icon = "🔴"; Color = "Red" }
        "SKIP"    = @{ Icon = "⏭️ "; Color = "Gray" }
    }

    $entry = $map[$Status]
    if (-not $entry) { $entry = $map["INFO"] }

    if ($IconOnly) { return $entry.Icon }
    if ($ColorOnly) { return $entry.Color }
    return $entry
}

# ============================================================
# 结果表格输出
# ============================================================

function Write-ResultTable {
    <#
    .SYNOPSIS
        以表格形式输出检测结果列表
    .DESCRIPTION
        将 PSCustomObject 数组格式化为对齐的表格输出。
        自动检测列宽，支持状态图标着色。
    .PARAMETER Results
        结果对象数组，每个对象应有 Name/Status/Message 等属性
    .PARAMETER Columns
        要显示的列名数组（默认 @("Name", "Status", "Message")）
    .PARAMETER StatusColumn
        状态列名（默认 "Status"），用于着色
    .PARAMETER Title
        表格标题（可选）
    .PARAMETER Color
        标题颜色（默认 Cyan）
    .EXAMPLE
        $results = @(
            [PSCustomObject]@{ Name="服务A"; Status="OK"; Message="运行正常" }
            [PSCustomObject]@{ Name="服务B"; Status="FAIL"; Message="连接超时" }
        )
        Write-ResultTable -Results $results -Title "服务检测结果"
    #>
    param(
        [Parameter(Mandatory = $true)]
        [array]$Results,

        [string[]]$Columns = @("Name", "Status", "Message"),

        [string]$StatusColumn = "Status",

        [string]$Title = "",

        [string]$Color = "Cyan"
    )

    if ($Title) {
        Write-Host "`n  ── $Title ──" -ForegroundColor $Color
    }

    if ($Results.Count -eq 0) {
        Write-Host "  (无数据)" -ForegroundColor Gray
        return
    }

    # 计算每列最大宽度
    $colWidths = @{}
    foreach ($col in $Columns) {
        $maxLen = $col.Length
        foreach ($item in $Results) {
            $val = if ($item.$col) { $item.$col.ToString() } else { "" }
            $displayLen = [System.Text.Encoding]::UTF8.GetByteCount($val)
            if ($displayLen -gt $maxLen) { $maxLen = $displayLen }
        }
        $colWidths[$col] = $maxLen + 2
    }

    # 表头
    $header = ""
    foreach ($col in $Columns) {
        $header += $col.PadRight($colWidths[$col])
    }
    Write-Host "  $header" -ForegroundColor $Color
    Write-Host "  $("-" * ($header.Length - 2))" -ForegroundColor $Color

    # 数据行
    foreach ($item in $Results) {
        $line = ""
        foreach ($col in $Columns) {
            $val = if ($item.$col) { $item.$col.ToString() } else { "" }
            $line += $val.PadRight($colWidths[$col])
        }

        # 状态列着色
        $statusVal = if ($item.$StatusColumn) { $item.$StatusColumn.ToString() } else { "" }
        $statusEntry = Get-StatusIcon $statusVal
        Write-Host "  $line" -ForegroundColor $statusEntry.Color
    }
}

# ============================================
# 模块导出
# ============================================

Export-ModuleMember -Function @(
    'Write-Banner',
    'Write-Section',
    'Write-Step',
    'Write-SubSection',
    'Write-MenuHeader',
    'Write-MenuItem',
    'Write-Result',
    'Pause-Menu',
    'Write-WhatIf',
    'Write-Info',
    'Write-Success',
    'Write-Warn',
    'Write-Error',
    'Write-Hint',
    'Write-Header',
    'Write-SubHeader',
    'Write-StepTitle',
    'Get-StatusIcon',
    'Write-ResultTable'
)
