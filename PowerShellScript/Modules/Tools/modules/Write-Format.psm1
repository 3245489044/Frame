<#
.SYNOPSIS
    格式化输出模块 — 提供统一的 Banner/Step/Info/Success/Warning/Error/WhatIf 输出函数
.DESCRIPTION
    所有 Tools 脚本共享的格式化输出函数，消除重复代码。
    提供统一的颜色方案和输出格式。

    用法:
        Import-Module .\Write-Format.psm1 -Force
        Write-Banner -Title "My Tool" -Color "Cyan"
        Write-Step "步骤 1/3: 环境检查"
        Write-Info "正在处理..."
        Write-Success "完成"
        Write-Warning "注意"
        Write-Error "失败"
        Write-WhatIf "预览操作"
#>

function Write-Banner {
    <#
    .SYNOPSIS
        显示工具标题横幅
    .PARAMETER Title
        标题文本（可选，默认使用通用标题）
    .PARAMETER Subtitle
        副标题文本（可选）
    .PARAMETER Color
        颜色（默认 Cyan）
    .PARAMETER Width
        横幅宽度（默认 60）
    #>
    param(
        [string]$Title = "",
        [string]$Subtitle = "",
        [string]$Color = "Cyan",
        [int]$Width = 60
    )

    $line = "═" * ($Width - 2)
    Write-Host "`n╔$line╗" -ForegroundColor $Color
    if ($Title) {
        $padding = " " * [math]::Max(0, ($Width - 2 - $Title.Length - 2) / 2)
        Write-Host "║$padding  $Title  $padding║" -ForegroundColor $Color
    }
    if ($Subtitle) {
        $padding = " " * [math]::Max(0, ($Width - 2 - $Subtitle.Length - 2) / 2)
        Write-Host "║$padding  $Subtitle  $padding║" -ForegroundColor $Color
    }
    Write-Host "╚$line╝" -ForegroundColor $Color
    Write-Host ""
}

function Write-Step {
    <#
    .SYNOPSIS
        显示步骤标题
    .PARAMETER Title
        步骤标题
    .PARAMETER Color
        颜色（默认 Cyan）
    #>
    param(
        [string]$Title,
        [string]$Color = "Cyan"
    )
    Write-Host "`n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor $Color
    Write-Host "  $Title" -ForegroundColor $Color
    Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor $Color
}

function Write-Section {
    <#
    .SYNOPSIS
        显示章节标题（带等号线）
    .PARAMETER Title
        章节标题
    #>
    param([string]$Title)
    Write-Host ""
    Write-Host ("=" * 60) -ForegroundColor Cyan
    Write-Host "  $Title" -ForegroundColor White
    Write-Host ("=" * 60) -ForegroundColor Cyan
}

function Write-SubSection {
    <#
    .SYNOPSIS
        显示子章节标题
    .PARAMETER Title
        子章节标题
    #>
    param([string]$Title)
    Write-Host "`n  ── $Title ──" -ForegroundColor White
}

function Write-Info {
    <#
    .SYNOPSIS
        显示信息消息
    #>
    param([string]$Msg)
    Write-Host "  ℹ️  $Msg" -ForegroundColor Gray
}

function Write-Success {
    <#
    .SYNOPSIS
        显示成功消息
    #>
    param([string]$Msg)
    Write-Host "  ✅ $Msg" -ForegroundColor Green
}

function Write-Warning {
    <#
    .SYNOPSIS
        显示警告消息
    #>
    param([string]$Msg)
    Write-Host "  ⚠️  $Msg" -ForegroundColor Yellow
}

function Write-Error {
    <#
    .SYNOPSIS
        显示错误消息
    #>
    param([string]$Msg)
    Write-Host "  ❌ $Msg" -ForegroundColor Red
}

function Write-WhatIf {
    <#
    .SYNOPSIS
        显示 WhatIf 预览消息
    #>
    param([string]$Msg)
    Write-Host "  [WhatIf] $Msg" -ForegroundColor Magenta
}

function Write-Color {
    <#
    .SYNOPSIS
        通用彩色输出
    .PARAMETER Text
        文本内容
    .PARAMETER ForegroundColor
        前景色
    .PARAMETER BackgroundColor
        背景色
    #>
    param(
        [string]$Text,
        [string]$ForegroundColor = "White",
        [string]$BackgroundColor = "Black"
    )
    Write-Host $Text -ForegroundColor $ForegroundColor -BackgroundColor $BackgroundColor
}

# 导出所有函数
Export-ModuleMember -Function Write-Banner, Write-Step, Write-Section, Write-SubSection,
    Write-Info, Write-Success, Write-Warning, Write-Error, Write-WhatIf, Write-Color
