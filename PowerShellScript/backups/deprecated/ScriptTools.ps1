<#
.SYNOPSIS
    Script 工具集启动器 - 统一运行 Script 目录下的工具脚本

.DESCRIPTION
    提供 Script 目录下所有 .ps1 工具的统一启动入口。
    支持 --list/--info/--show/--help 等标准参数，以及交互选择界面。
    不传参进入交互选择界面。

    用法:
        ScriptTools.ps1 <工具名>             运行指定工具
        ScriptTools.ps1 --list               列出可用工具
        ScriptTools.ps1 --info               显示配置信息
        ScriptTools.ps1 --show <工具名>      显示工具内容
        ScriptTools.ps1 --help               显示帮助
        ScriptTools.ps1                      进入交互选择界面

    示例:
        ScriptTools.ps1 CleanupExtraBuildOutput.ps1
        ScriptTools.ps1 --list
        ScriptTools.ps1 --show CleanupExtraBuildOutput.ps1
#>

param(
    [Parameter(Position=0)]
    [string]$ToolName = "",

    [switch]$List,
    [switch]$Info,
    [string]$Show = "",
    [switch]$Dirs,
    [switch]$Help
)

# ============================================
#  配置
# ============================================

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$modulesDir = Join-Path $scriptDir "Modules"

# 加载 ScriptRunner 框架
. "$modulesDir\FileDriven\FileDrivenTestUtils.ps1"
. "$modulesDir\FileDriven\ScriptRunner.ps1"

# ============================================
#  获取工具列表（排除 Modules\ 子目录和自身）
# ============================================

function Get-ScriptTools {
    <#
    .SYNOPSIS
        获取 Script 目录下的可用工具列表
    #>
    $selfName = Split-Path -Leaf $MyInvocation.ScriptName
    $tools = Get-ChildItem -Path $scriptDir -Filter "*.ps1" -File |
        Where-Object { $_.Name -ne $selfName } |
        Sort-Object Name
    return $tools
}

# ============================================
#  工具描述映射
# ============================================

$toolDescriptions = @{
    "CleanupExtraBuildOutput.ps1" = "构建输出清理与项目配置修复"
    "fix_coderunner_config.ps1"   = "修复 VS Code Code Runner 配置"
    "register_entry.ps1"          = "注册 BatTool 启动项到 entries 配置"
    "sync_vscode_entries.ps1"     = "同步更新 entries 配置中的 workDir 路径"
}


# ============================================
#  构建配置（兼容 ScriptRunner 框架）
# ============================================

$config = @{
    ProjectRoot   = Resolve-Path (Join-Path $scriptDir "..")
    ScriptsDir    = $scriptDir
    FileDrivenDir = ""
    Aliases       = @{}
}

# ============================================
#  定义运行逻辑
# ============================================

$runAction = {
    param([string]$ToolFileName)

    $toolPath = Join-Path $scriptDir $ToolFileName

    if (-not (Test-Path $toolPath)) {
        Write-Host "[错误] 工具不存在: $toolPath"
        Write-Host ""
        Write-Host "可用的工具:"
        $tools = Get-ScriptTools
        $tools | ForEach-Object { Write-Host "  $($_.Name)" }
        Read-Host "按回车键退出"
        exit 1
    }

    Write-Header "Script 工具集"
    Write-Host ""
    Write-Info "工具" $ToolFileName
    Write-Info "路径" $toolPath
    if ($toolDescriptions.ContainsKey($ToolFileName)) {
        Write-Info "说明" $toolDescriptions[$ToolFileName]
    }
    Write-Host ""
    Write-Host ("=" * 44)
    Write-Host ""

    # 执行工具
    Push-Location $scriptDir
    try {
        & $toolPath
    }
    finally {
        Pop-Location
    }

    Write-Host ""
    Write-Host ("=" * 44)
    Write-Host "  工具执行完毕"
    Write-Host ("=" * 44)
    Write-Host ""

    Read-Host "按回车键继续"
}

# ============================================
#  覆盖 Show-ScriptList 以显示工具列表
# ============================================

function Show-ToolList {
    <#
    .SYNOPSIS
        显示可用工具列表
    #>
    $tools = Get-ScriptTools

    Write-Header "可用工具"
    Write-Host "  ($scriptDir)"
    Write-Host ""
    $index = 1
    $tools | ForEach-Object {
        $desc = ""
        if ($toolDescriptions.ContainsKey($_.Name)) {
            $desc = $toolDescriptions[$_.Name]
        }
        if ($desc) {
            Write-Host ("  [{0,2}] {1,-35} - {2}" -f $index, $_.Name, $desc)
        } else {
            Write-Host ("  [{0,2}] {1}" -f $index, $_.Name)
        }
        $index++
    }
    Write-Host ""
}

# ============================================
#  覆盖 Show-ConfigInfo 以显示工具集信息
# ============================================

function Show-ToolInfo {
    <#
    .SYNOPSIS
        显示工具集配置信息
    #>
    Write-Header "Script 工具集 配置信息"
    Write-Host ""
    Write-Info "脚本目录" $scriptDir
    Write-Info "模块目录" $modulesDir
    Write-Info "项目根目录" $config.ProjectRoot
    Write-Host ""
}

# ============================================
#  覆盖 Show-FileContent 以显示工具内容
# ============================================

function Show-ToolContent {
    <#
    .SYNOPSIS
        显示指定工具文件内容
    #>
    param([string]$FileName)

    $showPath = Join-Path $scriptDir $FileName
    if (-not (Test-Path $showPath)) {
        Write-Host "[错误] 文件不存在: $showPath"
        Write-Host ""
        Write-Host "可用的工具:"
        $tools = Get-ScriptTools
        $index = 1
        $tools | ForEach-Object {
            Write-Host ("  [{0,2}] {1}" -f $index, $_.Name)
            $index++
        }
        exit 1
    }

    Write-Host ""
    Write-Host ("=" * 44)
    Write-Host "  文件: $FileName"
    Write-Host "  路径: $showPath"
    Write-Host ("=" * 44)
    Write-Host ""
    Get-Content $showPath
    Write-Host ""
}

# ============================================
#  覆盖 Show-HelpText
# ============================================

function Show-ToolHelp {
    <#
    .SYNOPSIS
        显示帮助文本
    #>
    $scriptName = Split-Path -Leaf $MyInvocation.MyCommand.Path

    Write-Header "Script 工具集 - 帮助"
    Write-Host ""
    Write-Host "  用法:"
    Write-Host "    $scriptName <工具文件名>      运行指定工具"
    Write-Host "    $scriptName --list            列出可用工具"
    Write-Host "    $scriptName --info            显示配置信息"
    Write-Host "    $scriptName --show <文件名>   显示指定工具内容"
    Write-Host "    $scriptName --help            显示本帮助"
    Write-Host ""
    Write-Host "  可用工具:"
    $tools = Get-ScriptTools
    $tools | ForEach-Object {
        $desc = ""
        if ($toolDescriptions.ContainsKey($_.Name)) {
            $desc = " - $($toolDescriptions[$_.Name])"
        }
        Write-Host ("    {0,-35}{1}" -f $_.Name, $desc)
    }
    Write-Host ""
    Write-Host "  示例:"
    Write-Host "    $scriptName CleanupExtraBuildOutput.ps1"
    Write-Host "    $scriptName --list"
    Write-Host "    $scriptName --show CleanupExtraBuildOutput.ps1"
    Write-Host ""
}

# ============================================
#  交互选择界面
# ============================================

function Show-Interactive {
    <#
    .SYNOPSIS
        交互选择工具
    #>
    $tools = Get-ScriptTools
    $maxIdx = $tools.Count

    while ($true) {
        Show-ToolList

        Write-Host "  可用命令: list / info / show / help"
        Write-Host ""
        $choice = Read-Host "请输入序号（1-$maxIdx），或命令，直接回车退出"

        if ([string]::IsNullOrEmpty($choice)) {
            return
        }

        $lowerChoice = $choice.Trim().ToLower()
        switch ($lowerChoice) {
            "list" { Show-ToolList; continue }
            "info" { Show-ToolInfo; continue }
            "show" {
                $showFile = Read-Host "请输入要显示的工具文件名"
                if ($showFile) { Show-ToolContent -FileName $showFile }
                continue
            }
            "help" { Show-ToolHelp; continue }
        }

        if ($choice -match '^\d+$') {
            $num = [int]$choice
            if ($num -ge 1 -and $num -le $maxIdx) {
                $selectedTool = $tools[$num - 1].Name
                $runAction.Invoke($selectedTool)
                continue
            }
        }

        Write-Host "  [错误] 无效的选择，请输入 1-$maxIdx 之间的数字，或命令（list/info/show/help）"
    }
}

# ============================================
#  主入口
# ============================================

# 处理 --show 参数
if ($Show) {
    Show-ToolContent -FileName $Show
    exit 0
}

# 确定操作模式
if ($List) {
    Show-ToolList
    exit 0
}

if ($Info) {
    Show-ToolInfo
    exit 0
}

if ($Dirs) {
    Write-Host "  Script 目录: $scriptDir"
    Write-Host "  模块目录: $modulesDir"
    exit 0
}

if ($Help) {
    Show-ToolHelp
    exit 0
}

if ($ToolName) {
    $runAction.Invoke($ToolName)
    exit 0
}

# 不传参进入交互选择界面
Show-Interactive
