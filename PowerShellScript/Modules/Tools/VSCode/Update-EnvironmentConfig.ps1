<#
.SYNOPSIS
    WorkFlowPrj 环境配置更新与文档生成工具
.DESCRIPTION
    一键完成以下操作：
    1. 更新 .vscode/ 配置（settings.json, launch.json, tasks.json）
    2. 更新 vscode-plugin-framework 启动项配置（entries.config.json）
    3. 更新 CLine MCP 配置（cline_mcp_settings.json）
    4. 自动生成 docs/环境配置指南.md 文档
.PARAMETER WorkspaceRoot
    项目根目录（默认自动检测为脚本所在目录的上级）
.PARAMETER DryRun
    仅预览变更，不实际写入文件
.PARAMETER SkipVscode
    跳过 VS Code 配置更新
.PARAMETER SkipEntries
    跳过启动项配置更新
.PARAMETER SkipClineMCP
    跳过 CLine MCP 配置更新
.PARAMETER SkipDoc
    跳过文档生成
.PARAMETER MCPBaseDir
    MCP 模块存放目录（默认 $env:USERPROFILE\Documents\Cline\MCP）
.EXAMPLE
    # 完整运行（更新所有配置 + 生成文档）
    .\Script\Tools\Update-EnvironmentConfig.ps1

    .EXAMPLE
    # 仅预览变更
    .\Script\Tools\Update-EnvironmentConfig.ps1 -DryRun

    .EXAMPLE
    # 仅生成文档
    .\Script\Tools\Update-EnvironmentConfig.ps1 -SkipVscode -SkipEntries -SkipClineMCP
#>

[CmdletBinding()]
param(
    [string]$WorkspaceRoot = "",
    [switch]$DryRun,
    [switch]$SkipVscode,
    [switch]$SkipEntries,
    [switch]$SkipClineMCP,
    [switch]$SkipDoc,
    [string]$MCPBaseDir = (Join-Path $env:USERPROFILE "Documents\Cline\MCP")
)

# ============================================================
# 初始化
# ============================================================
$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrEmpty($WorkspaceRoot)) {
    # 从 Script\Tools\ 向上三级到项目根
    $detectedPath = Resolve-Path (Join-Path $scriptPath "..\..\..")
    # 检查是否真的是项目根（有 .git 或 .vscode 或 sln）
    $hasSln = Test-Path (Join-Path $detectedPath "WorkFlowPrj.sln")
    $hasGit = Test-Path (Join-Path $detectedPath ".git")
    $hasVscode = Test-Path (Join-Path $detectedPath ".vscode")
    if ($hasSln -or $hasGit -or $hasVscode) {
        $WorkspaceRoot = $detectedPath
    } else {
        # 尝试再向上一级（可能是 WorkFlowPrj 在子目录中）
        $parentPath = Resolve-Path (Join-Path $detectedPath "..")
        $hasSln2 = Test-Path (Join-Path $parentPath "WorkFlowPrj.sln")
        $hasGit2 = Test-Path (Join-Path $parentPath ".git")
        $hasVscode2 = Test-Path (Join-Path $parentPath ".vscode")
        if ($hasSln2 -or $hasGit2 -or $hasVscode2) {
            $WorkspaceRoot = $parentPath
        } else {
            $WorkspaceRoot = $detectedPath
        }
    }
}

Write-Host @"

╔══════════════════════════════════════════════════╗
║     WorkFlowPrj 环境配置更新工具                  ║
║     自动更新配置 + 生成文档                       ║
╚══════════════════════════════════════════════════╝

"@ -ForegroundColor Cyan

Write-Host "[信息] 项目根目录: $WorkspaceRoot" -ForegroundColor White
Write-Host "[信息] 当前用户: $env:USERNAME" -ForegroundColor White
Write-Host "[信息] MCP 目录: $MCPBaseDir" -ForegroundColor White
if ($DryRun) {
    Write-Host "[模式] 预览模式（不会写入任何文件）" -ForegroundColor Yellow
}
Write-Host ""

# ============================================================
# 加载模块
# ============================================================
$modulesPath = Join-Path $scriptPath "Modules"
$modules = @(
    "Update-VscodeConfig.ps1",
    "Update-EntriesConfig.ps1",
    "Update-ClineMCPConfig.ps1",
    "Generate-ConfigDoc.ps1"
)

foreach ($module in $modules) {
    $modulePath = Join-Path $modulesPath $module
    if (Test-Path $modulePath) {
        . $modulePath
        Write-Host "[加载] $module" -ForegroundColor Gray
    } else {
        Write-Warning "[缺失] 模块文件不存在: $modulePath"
    }
}
Write-Host ""

# ============================================================
# 1. 更新 VS Code 配置
# ============================================================
$vscodeResult = $null
if (-not $SkipVscode) {
    if (Get-Command Update-VscodeConfig -ErrorAction SilentlyContinue) {
        $vscodeResult = Update-VscodeConfig -WorkspaceRoot $WorkspaceRoot -DryRun:$DryRun
    } else {
        Write-Warning "[跳过] Update-VscodeConfig 模块未加载"
    }
} else {
    Write-Host "[跳过] VS Code 配置更新" -ForegroundColor Gray
}

# ============================================================
# 2. 更新启动项配置
# ============================================================
$entriesResult = $null
if (-not $SkipEntries) {
    if (Get-Command Update-EntriesConfig -ErrorAction SilentlyContinue) {
        $entriesResult = Update-EntriesConfig -WorkspaceRoot $WorkspaceRoot -DryRun:$DryRun
    } else {
        Write-Warning "[跳过] Update-EntriesConfig 模块未加载"
    }
} else {
    Write-Host "[跳过] 启动项配置更新" -ForegroundColor Gray
}

# ============================================================
# 3. 更新 CLine MCP 配置
# ============================================================
$clineResult = $null
if (-not $SkipClineMCP) {
    if (Get-Command Update-ClineMCPConfig -ErrorAction SilentlyContinue) {
        $clineResult = Update-ClineMCPConfig -WorkspaceRoot $WorkspaceRoot -MCPBaseDir $MCPBaseDir -DryRun:$DryRun
    } else {
        Write-Warning "[跳过] Update-ClineMCPConfig 模块未加载"
    }
} else {
    Write-Host "[跳过] CLine MCP 配置更新" -ForegroundColor Gray
}

# ============================================================
# 4. 生成文档
# ============================================================
if (-not $SkipDoc) {
    if (Get-Command Generate-ConfigDoc -ErrorAction SilentlyContinue) {
        # 文档生成始终需要读取配置，但不写入配置
        Generate-ConfigDoc -WorkspaceRoot $WorkspaceRoot `
                          -VscodeConfig $vscodeResult `
                          -EntriesConfig $entriesResult `
                          -ClineMCPResult $clineResult
    } else {
        Write-Warning "[跳过] Generate-ConfigDoc 模块未加载"
    }
} else {
    Write-Host "[跳过] 文档生成" -ForegroundColor Gray
}

# ============================================================
# 完成
# ============================================================
Write-Host @"

╔══════════════════════════════════════════════════╗
║     完成！                                        ║
╚══════════════════════════════════════════════════╝

"@ -ForegroundColor Green

if ($DryRun) {
    Write-Host "🔍 预览模式已完成，未写入任何文件" -ForegroundColor Yellow
    Write-Host "   移除 -DryRun 参数以实际应用配置" -ForegroundColor Gray
} else {
    Write-Host "✅ 配置已更新" -ForegroundColor Green
    if (-not $SkipDoc) {
        Write-Host "📄 文档已生成: docs\环境配置指南.md" -ForegroundColor Green
    }
}

Write-Host ""
Write-Host "📋 使用提示:" -ForegroundColor White
Write-Host "  • 重新生成文档: .\Script\Tools\Update-EnvironmentConfig.ps1 -SkipVscode -SkipEntries -SkipClineMCP" -ForegroundColor Gray
Write-Host "  • 预览变更: .\Script\Tools\Update-EnvironmentConfig.ps1 -DryRun" -ForegroundColor Gray
Write-Host "  • 自定义 MCP 目录: .\Script\Tools\Update-EnvironmentConfig.ps1 -MCPBaseDir D:\MyMCP" -ForegroundColor Gray
