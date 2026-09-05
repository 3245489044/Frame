# _init.ps1 - AI 决策框架模块初始化
# 用法: . .\PowerShellScript\AIDecisionFramework\_init.ps1
# 功能: 导入所有子模块，初始化全局变量

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$BasicMethodDir = Resolve-Path "$ScriptDir\..\BasicMethod"

# 导入通用模块（来自 BasicMethod - v2 子目录结构）
Import-Module "$BasicMethodDir\Core\FileUtils.psm1" -Force -ErrorAction Stop
Import-Module "$BasicMethodDir\FileOps\FileDiff.psm1" -Force -ErrorAction Stop
Import-Module "$BasicMethodDir\FileOps\FileAccess.psm1" -Force -ErrorAction Stop
Import-Module "$BasicMethodDir\Tooling\ToolBridge.psm1" -Force -ErrorAction Stop

# 导入领域特定模块
. "$ScriptDir\TokenAccounting.ps1"
. "$ScriptDir\DecisionEngine.ps1"
. "$ScriptDir\ToolBridge.ps1"

# 全局配置
$Global:AIDecisionFramework = @{
    Version = "1.1.0"
    ScriptDir = $ScriptDir
    ProjectRoot = (Resolve-Path "$ScriptDir\..\..").Path
    HourlyRate = 100.0       # 工程师时薪 ($/小时)
    TokenPrice = 0.0000065   # 每 Token 价格 ($)
    CognitiveRestartSec = 60 # 认知重启恢复时间 (秒)
}

<#
.SYNOPSIS
    获取全局配置值
.DESCRIPTION
    从 $Global:AIDecisionFramework 中读取指定键的值。
    供所有子模块统一使用，避免直接访问全局变量。
.PARAMETER Key
    配置键名
.EXAMPLE
    Get-FrameworkConfig "HourlyRate"
#>
function Get-FrameworkConfig {
    param([string]$Key)
    return $Global:AIDecisionFramework[$Key]
}

Write-Host "[AI决策框架] 模块已加载 v$($Global:AIDecisionFramework.Version)" -ForegroundColor Cyan
