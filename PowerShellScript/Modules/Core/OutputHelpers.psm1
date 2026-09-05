<#
.SYNOPSIS
    输出辅助函数模块 - 统一管理控制台输出格式

.DESCRIPTION
    提供标准化的控制台输出函数，消除各脚本中重复的 Write-Header/Write-Info 等定义。
    所有函数支持 -NoNewline 参数（部分函数）和颜色自定义。

    用法:
        Import-Module OutputHelpers  # 或通过 Import-Modules.ps1 统一加载
        Write-Header "标题"
        Write-Info "标签" "值"
        Write-Success "操作成功"
        Write-Warning "注意..."
        Write-Error "出错了"
        Write-Diff "差异内容" "Yellow"
        Write-SubHeader "子标题"
        Show-FixSummary -summary @("修复了 3 个问题") -backupDir "path"
#>

# ============================================
#  输出函数
# ============================================

function Write-Header {
    <#
    .SYNOPSIS
        显示带边框的标题
    .PARAMETER Text
        标题文本
    .PARAMETER Color
        标题颜色，默认 Cyan
    .PARAMETER Width
        边框宽度，默认 50
    #>
    param(
        [string]$Text,
        [string]$Color = "Cyan",
        [int]$Width = 50
    )
    Write-Host ("=" * $Width) -ForegroundColor $Color
    Write-Host "  $Text" -ForegroundColor $Color
    Write-Host ("=" * $Width) -ForegroundColor $Color
}

function Write-SubHeader {
    <#
    .SYNOPSIS
        显示子标题（带短横线边框）
    .PARAMETER Text
        子标题文本
    .PARAMETER Color
        颜色，默认 Yellow
    #>
    param(
        [string]$Text,
        [string]$Color = "Yellow"
    )
    Write-Host "╔════════════════════════════════════════════════╗" -ForegroundColor $Color
    Write-Host "║  $Text" -ForegroundColor $Color
    Write-Host "╚════════════════════════════════════════════════╝" -ForegroundColor $Color
}

function Write-Info {
    <#
    .SYNOPSIS
        显示标签-值信息行
    .PARAMETER Label
        标签（左对齐，占 16 字符）
    .PARAMETER Value
        值
    .PARAMETER Color
        颜色，默认 Gray
    #>
    param(
        [string]$Label,
        [string]$Value,
        [string]$Color = "Gray"
    )
    Write-Host ("  {0,-16} {1}" -f ($Label + ":"), $Value) -ForegroundColor $Color
}

function Write-Success {
    <#
    .SYNOPSIS
        显示成功消息
    #>
    param([string]$Text)
    Write-Host "  [成功] $Text" -ForegroundColor Green
}

function Write-Warning {
    <#
    .SYNOPSIS
        显示警告消息
    #>
    param([string]$Text)
    Write-Host "  [警告] $Text" -ForegroundColor Yellow
}

function Write-Error {
    <#
    .SYNOPSIS
        显示错误消息
    #>
    param([string]$Text)
    Write-Host "  [错误] $Text" -ForegroundColor Red
}

function Write-Diff {
    <#
    .SYNOPSIS
        显示差异/变更内容
    .PARAMETER Text
        差异文本
    .PARAMETER Color
        颜色，默认 Gray
    #>
    param(
        [string]$Text,
        [string]$Color = "Gray"
    )
    Write-Host "  $Text" -ForegroundColor $Color
}

function Show-FixSummary {
    <#
    .SYNOPSIS
        显示修复摘要信息
    .PARAMETER summary
        修复摘要字符串数组
    .PARAMETER backupDir
        备份目录路径（可选）
    #>
    param(
        [string[]]$summary,
        [string]$backupDir = ""
    )

    if ($summary.Count -eq 0) { return }

    Write-Host ""
    Write-Host "  ── 修复摘要 ──" -ForegroundColor Cyan
    foreach ($item in $summary) {
        Write-Host "    ✓ $item" -ForegroundColor Green
    }
    if ($backupDir -and (Test-Path $backupDir)) {
        Write-Host "    📁 备份目录: $backupDir" -ForegroundColor Gray
    }
    Write-Host ""
}

# ============================================
#  模块导出
# ============================================

Export-ModuleMember -Function @(
    'Write-Header',
    'Write-SubHeader',
    'Write-Info',
    'Write-Success',
    'Write-Warning',
    'Write-Error',
    'Write-Diff',
    'Show-FixSummary'
)
