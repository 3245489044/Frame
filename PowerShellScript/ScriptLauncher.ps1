<#
.SYNOPSIS
    WorkFlowPrj Script 统一启动器
.DESCRIPTION
    覆盖 Script/ 目录下所有工具的统一启动入口。
    支持分层菜单导航、快捷参数调用、自动发现工具。
    
    功能分类：
    [1] 构建与清理     — 构建输出清理、bin/obj 清理、项目重建
    [2] 解决方案管理   — .sln 配置精简、磁盘同步、跨项目分析修复
    [3] 代码分析       — csproj 引用分析
    [4] Git 工具       — 分支管理
    [5] 环境配置       — VS Code / MCP / Pester / Code Runner 配置
    [6] FileDriven 测试 — GUI 模式测试运行器、日志查看器

.PARAMETER Tool
    快捷参数，直接运行指定工具（不显示菜单）。
    可用值: clean, cleanup, sln-clean, sln-sync, fix-cross,
            analyze, git-clean, env-config, validate-mcp, setup-pester,
            fix-coderunner, register-entry, sync-entries,
            filedriven, log, clean-user, all

.PARAMETER List
    列出所有可用工具及其描述，不运行任何工具。

.PARAMETER ConfigPath
    工具配置文件路径（默认自动检测 Script/launcher.config.json）

.EXAMPLE
    # 显示交互菜单
    .\ScriptLauncher.ps1

    .EXAMPLE
    # 直接运行清理工具
    .\ScriptLauncher.ps1 -Tool clean

    .EXAMPLE
    # 列出所有工具
    .\ScriptLauncher.ps1 -List

    .EXAMPLE
    # 显示完整菜单
    .\ScriptLauncher.ps1 -Tool all
#>

# 注意：不使用 [CmdletBinding()]，以便 -Tool 之后的未知参数（如 -All、-CheckOnly）
# 能自然落入 $args 中，透传给目标工具脚本
param(
    [string]$Tool = "",

    [switch]$List,

    [string]$ConfigPath = ""
)

# ============================================================
# 初始化
# ============================================================

$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Resolve-Path (Join-Path $scriptPath "..")

# 自动检测配置文件路径
if ([string]::IsNullOrEmpty($ConfigPath)) {
    $ConfigPath = Join-Path $scriptPath "launcher.config.json"
}

# ============================================================
# 预加载 Script\Modules 模块
# ============================================================
# 所有通过启动器运行的脚本均可直接使用模块中的函数
# （如 StringTools 的 Invoke-SimpleReplace、Convert-NameCase 等）

$importModulesScript = Join-Path $scriptPath "Modules\Import-Modules.ps1"
if (Test-Path $importModulesScript) {
    # 使用点引入加载模块（静默模式，不显示加载日志）
    . $importModulesScript *>$null
}

# ============================================================
# 加载配置
# ============================================================

function Load-Config {
    <#
    .SYNOPSIS
        加载 launcher.config.json 配置文件
    #>
    param([string]$Path)

    if (-not (Test-Path $Path)) {
        Write-Warning "[配置] 配置文件不存在: $Path"
        return $null
    }

    try {
        $config = Get-Content $Path -Raw -Encoding UTF8 | ConvertFrom-Json
        return $config
    }
    catch {
        Write-Warning "[配置] 配置文件解析失败: $($_.Exception.Message)"
        return $null
    }
}

# ============================================================
# 内联工具（无需外部脚本）
# ============================================================

function Invoke-Rebuild {
    <#
    .SYNOPSIS
        编译前清理进程并重新编译（原 Rebuild_With_Kill.bat 的内联实现）
    #>
    Write-Host "============================================" -ForegroundColor Cyan
    Write-Host "  正在检查并关闭占用编译输出的进程..." -ForegroundColor White
    Write-Host "============================================" -ForegroundColor Cyan
    Write-Host ""

    # 使用 Clean-Processes.ps1 进行编译前进程清理
    $cleanProcScript = Join-Path $scriptPath "Tools\Clean-Processes.ps1"
    if (Test-Path $cleanProcScript) {
        & $cleanProcScript -BeforeBuild -Force
    } else {
        # 回退：硬编码的进程终止逻辑
        Write-Host "  正在关闭 FileDriven.Launcher.exe ..." -ForegroundColor Gray
        $procs = Get-Process -Name "FileDriven.Launcher" -ErrorAction SilentlyContinue
        if ($procs) {
            $procs | Stop-Process -Force
            Write-Host "  [成功] FileDriven.Launcher.exe 已终止" -ForegroundColor Green
        }
        else {
            Write-Host "  [信息] FileDriven.Launcher.exe 未运行，无需关闭" -ForegroundColor Yellow
        }
    }

    Write-Host ""
    Write-Host "  等待进程完全退出..." -ForegroundColor Gray
    Start-Sleep -Seconds 2

    Write-Host ""
    Write-Host "============================================" -ForegroundColor Cyan
    Write-Host "  开始编译项目..." -ForegroundColor White
    Write-Host "============================================" -ForegroundColor Cyan
    Write-Host ""

    Push-Location $projectRoot
    try {
        $result = dotnet build "WorkFlowPrj.sln" -c Debug 2>&1
        $result | ForEach-Object { Write-Host $_ }

        if ($LASTEXITCODE -eq 0) {
            Write-Host ""
            Write-Host "============================================" -ForegroundColor Cyan
            Write-Host "  [成功] 编译完成！" -ForegroundColor Green
            Write-Host "============================================" -ForegroundColor Cyan
        }
        else {
            Write-Host ""
            Write-Host "============================================" -ForegroundColor Cyan
            Write-Host "  [失败] 编译出错，请检查上面的错误信息" -ForegroundColor Red
            Write-Host "============================================" -ForegroundColor Cyan
        }
    }
    finally {
        Pop-Location
    }
}

# ============================================================
# 工具执行
# ============================================================

function Invoke-Tool {
    <#
    .SYNOPSIS
        执行指定的工具脚本
    .PARAMETER ToolConfig
        工具配置对象（来自 launcher.config.json 的 tools 条目）
    .PARAMETER ToolPath
        工具脚本的完整路径
    .PARAMETER ToolArgs
        传递给工具脚本的额外参数（数组）
    #>
    param(
        [Parameter(Mandatory = $true)]
        [object]$ToolConfig,

        [Parameter(Mandatory = $true)]
        [string]$ToolPath,

        [Parameter()]
        [string[]]$ToolArgs = @()
    )

    $toolName = Split-Path -Leaf $ToolPath
    $workDir = $ToolConfig.workDir
    if ($workDir -eq ".") {
        $workDir = $projectRoot
    }
    else {
        $workDir = Resolve-Path (Join-Path $scriptPath $workDir)
    }

    Write-Host ""
    Write-Host ("=" * 60) -ForegroundColor Cyan
    Write-Host "  运行: $toolName" -ForegroundColor White
    Write-Host "  描述: $($ToolConfig.description)" -ForegroundColor Gray
    if ($ToolArgs.Count -gt 0) {
        Write-Host "  参数: $($ToolArgs -join ' ')" -ForegroundColor Yellow
    }
    Write-Host ("=" * 60) -ForegroundColor Cyan
    Write-Host ""

    # 检查文件是否存在
    if (-not (Test-Path $ToolPath)) {
        Write-Host "  [错误] 文件不存在: $ToolPath" -ForegroundColor Red
        Write-Host ""
        Pause-Message
        return
    }

    $extension = [System.IO.Path]::GetExtension($ToolPath).ToLower()

    Push-Location $workDir
    try {
        if ($extension -eq ".ps1") {
            # 执行 PowerShell 脚本，透传额外参数
            if ($ToolArgs.Count -gt 0) {
                # 使用 Invoke-Expression 确保 switch 参数（如 -All、-CheckOnly）正确解析
                $argString = ($ToolArgs | ForEach-Object { $_ }) -join ' '
                $cmdLine = "& '$ToolPath' $argString"
                Invoke-Expression $cmdLine
            }
            else {
                & $ToolPath
            }
        }
        else {
            Write-Host "  [错误] 不支持的文件类型: $extension" -ForegroundColor Red
        }
    }
    catch {
        Write-Host "  [错误] 执行失败: $($_.Exception.Message)" -ForegroundColor Red
    }
    finally {
        Pop-Location
    }

    Write-Host ""
    Pause-Message
}

function Pause-Message {
    <#
    .SYNOPSIS
        暂停等待用户按键
    #>
    if ($host.Name -eq "ConsoleHost") {
        Write-Host "  按回车键继续..." -ForegroundColor Gray
        $null = Read-Host
    }
}

# ============================================================
# 菜单显示
# ============================================================

function Show-MainMenu {
    <#
    .SYNOPSIS
        显示主菜单
    #>
    param(
        [Parameter(Mandatory = $true)]
        [object]$Config
    )

    # 按 order 排序分类
    $sortedCategories = $Config.categories.PSObject.Properties |
        Sort-Object { $_.Value.order }

    while ($true) {
        Clear-Host
        Write-Host ""
        Write-Host "╔══════════════════════════════════════════════════╗" -ForegroundColor Cyan
        Write-Host "║        WorkFlowPrj Script 工具集启动器           ║" -ForegroundColor Cyan
        Write-Host "╠══════════════════════════════════════════════════╣" -ForegroundColor Cyan
        Write-Host "║  项目: WorkFlowPrj                               ║" -ForegroundColor Cyan
        Write-Host "║  配置: launcher.config.json                      ║" -ForegroundColor Cyan
        Write-Host "╚══════════════════════════════════════════════════╝" -ForegroundColor Cyan
        Write-Host ""

        # 显示分类菜单
        $menuIndex = 1
        $categoryMap = @{}
        foreach ($cat in $sortedCategories) {
            $catKey = $cat.Name
            $catValue = $cat.Value
            $icon = if ($catValue.icon) { $catValue.icon } else { "📁" }
            $desc = if ($catValue.description) { "— $($catValue.description)" } else { "" }

            Write-Host "  [$menuIndex] $icon $($catValue.name)" -ForegroundColor White
            Write-Host "       $desc" -ForegroundColor Gray

            $categoryMap[$menuIndex.ToString()] = $catKey
            $menuIndex++
        }

        Write-Host ""
        Write-Host "  [L] 列出所有工具" -ForegroundColor Yellow
        Write-Host "  [Q] 退出" -ForegroundColor Red
        Write-Host ""

        $choice = Read-Host "  请选择分类编号"

        if ($choice -eq "Q" -or $choice -eq "q") {
            Write-Host "  再见！" -ForegroundColor Green
            return
        }

        if ($choice -eq "L" -or $choice -eq "l") {
            Show-ToolList -Config $Config
            Pause-Message
            continue
        }

        if ([string]::IsNullOrWhiteSpace($choice)) {
            continue
        }

        if ($categoryMap.ContainsKey($choice)) {
            $catKey = $categoryMap[$choice]
            Show-CategoryMenu -Config $Config -CategoryKey $catKey
        }
        else {
            Write-Host "  无效选择，请重新输入" -ForegroundColor Red
            Start-Sleep -Milliseconds 800
        }
    }
}

function Show-CategoryMenu {
    <#
    .SYNOPSIS
        显示分类子菜单
    #>
    param(
        [Parameter(Mandatory = $true)]
        [object]$Config,

        [Parameter(Mandatory = $true)]
        [string]$CategoryKey
    )

    $catValue = $Config.categories.$CategoryKey
    $icon = if ($catValue.icon) { $catValue.icon } else { "📁" }

    # 获取该分类下的所有工具
    $tools = @{}
    $toolIndex = 1
    $toolOrder = @()

    foreach ($toolEntry in $Config.tools.PSObject.Properties) {
        if ($toolEntry.Value.category -eq $CategoryKey) {
            $tools[$toolIndex.ToString()] = @{
                Key  = $toolEntry.Name
                Config = $toolEntry.Value
            }
            $toolOrder += $toolIndex.ToString()
            $toolIndex++
        }
    }

    while ($true) {
        Clear-Host
        Write-Host ""
        Write-Host "╔══════════════════════════════════════════════════╗" -ForegroundColor Cyan
        Write-Host "║  $icon $($catValue.name)" -ForegroundColor Cyan
        Write-Host "╠══════════════════════════════════════════════════╣" -ForegroundColor Cyan
        Write-Host "║  $($catValue.description)" -ForegroundColor Cyan
        Write-Host "╚══════════════════════════════════════════════════╝" -ForegroundColor Cyan
        Write-Host ""

        if ($tools.Count -eq 0) {
            Write-Host "  (该分类下暂无工具)" -ForegroundColor Yellow
            Write-Host ""
            Write-Host "  [0] 返回上级菜单" -ForegroundColor Gray
            Write-Host ""
            $choice = Read-Host "  请选择"
            if ($choice -eq "0") { return }
            continue
        }

        foreach ($idx in $toolOrder) {
            $tool = $tools[$idx]
            $toolConfig = $tool.Config
            $desc = if ($toolConfig.description) { $toolConfig.description } else { "（无描述）" }
            $alias = if ($toolConfig.alias) { " [别名: $($toolConfig.alias)]" } else { "" }

            Write-Host ("  [{0}] {1}" -f $idx, $desc) -ForegroundColor White
            Write-Host ("       脚本: {0}{1}" -f $tool.Key, $alias) -ForegroundColor Gray
        }

        Write-Host ""
        Write-Host "  [0] 返回上级菜单" -ForegroundColor Gray
        Write-Host ""

        $choice = Read-Host "  请选择工具编号"

        if ($choice -eq "0") { return }

        if ($tools.ContainsKey($choice)) {
            $tool = $tools[$choice]
            $toolPath = Join-Path $scriptPath $tool.Key

            # 询问是否一键执行（针对支持 -All / -Force 等参数的工具）
            Write-Host ""
            $allChoice = Read-Host "  是否一键执行所有操作？[y/N]"
            if ($allChoice -eq "y" -or $allChoice -eq "Y") {
                $allParam = if ($tool.Config.allParam) { $tool.Config.allParam } else { "-All" }
                Invoke-Tool -ToolConfig $tool.Config -ToolPath $toolPath -ToolArgs @($allParam)
            }
            else {
                Invoke-Tool -ToolConfig $tool.Config -ToolPath $toolPath
            }
        }
        else {
            Write-Host "  无效选择，请重新输入" -ForegroundColor Red
            Start-Sleep -Milliseconds 500
        }
    }
}

function Show-ToolList {
    <#
    .SYNOPSIS
        列出所有可用工具
    #>
    param(
        [Parameter(Mandatory = $true)]
        [object]$Config
    )

    Clear-Host
    Write-Host ""
    Write-Host "╔══════════════════════════════════════════════════╗" -ForegroundColor Cyan
    Write-Host "║              所有可用工具列表                    ║" -ForegroundColor Cyan
    Write-Host "╚══════════════════════════════════════════════════╝" -ForegroundColor Cyan
    Write-Host ""

    # 按分类分组显示
    $sortedCategories = $Config.categories.PSObject.Properties |
        Sort-Object { $_.Value.order }

    foreach ($cat in $sortedCategories) {
        $catKey = $cat.Name
        $catValue = $cat.Value
        $icon = if ($catValue.icon) { $catValue.icon } else { "📁" }

        Write-Host "  $icon $($catValue.name)" -ForegroundColor Yellow
        Write-Host "  $('-' * 50)" -ForegroundColor DarkGray

        $hasTool = $false
        foreach ($toolEntry in $Config.tools.PSObject.Properties) {
            if ($toolEntry.Value.category -eq $catKey) {
                $hasTool = $true
                $desc = if ($toolEntry.Value.description) { $toolEntry.Value.description } else { "（无描述）" }
                $alias = if ($toolEntry.Value.alias) { " [别名: $($toolEntry.Value.alias)]" } else { "" }

                Write-Host ("    • {0,-35} {1}" -f $toolEntry.Key, $desc) -ForegroundColor White
                Write-Host ("      快捷: ScriptLauncher.ps1 -Tool {0}{1}" -f $toolEntry.Value.alias, $alias) -ForegroundColor Gray
            }
        }

        if (-not $hasTool) {
            Write-Host "    （该分类下暂无工具）" -ForegroundColor DarkGray
        }

        Write-Host ""
    }

    Write-Host "  $('=' * 50)" -ForegroundColor Cyan
    Write-Host "  使用方式:" -ForegroundColor White
    Write-Host "    交互菜单: .\ScriptLauncher.ps1" -ForegroundColor Gray
    Write-Host "    快捷调用: .\ScriptLauncher.ps1 -Tool <别名>" -ForegroundColor Gray
    Write-Host "    列出工具: .\ScriptLauncher.ps1 -List" -ForegroundColor Gray
    Write-Host ""
}

# ============================================================
# 快捷工具执行
# ============================================================

function Invoke-ToolByAlias {
    <#
    .SYNOPSIS
        通过别名或工具名直接执行工具
    .PARAMETER Config
        配置对象
    .PARAMETER Alias
        别名或工具名
    .PARAMETER ToolArgs
        传递给工具脚本的额外参数（数组）
    #>
    param(
        [Parameter(Mandatory = $true)]
        [object]$Config,

        [Parameter(Mandatory = $true)]
        [string]$Alias,

        [Parameter()]
        [string[]]$ToolArgs = @()
    )

    $aliasLower = $Alias.ToLower()

    # 特殊处理：内联工具（无外部脚本）
    $builtinAliases = @{
        "rebuild" = "Invoke-Rebuild"
    }

    if ($builtinAliases.ContainsKey($aliasLower)) {
        & $builtinAliases[$aliasLower]
        Write-Host ""
        Pause-Message
        return
    }

    # 查找匹配的工具
    $matchedTool = $null
    $matchedKey = $null

    foreach ($toolEntry in $Config.tools.PSObject.Properties) {
        $toolConfig = $toolEntry.Value
        $toolAlias = if ($toolConfig.alias) { $toolConfig.alias.ToLower() } else { "" }
        $toolName = [System.IO.Path]::GetFileNameWithoutExtension($toolEntry.Name).ToLower()

        if ($toolAlias -eq $aliasLower -or $toolName -eq $aliasLower) {
            $matchedTool = $toolConfig
            $matchedKey = $toolEntry.Name
            break
        }
    }

    if ($null -eq $matchedTool) {
        Write-Host "  [错误] 未找到匹配的工具: $Alias" -ForegroundColor Red
        Write-Host "  使用 .\ScriptLauncher.ps1 -List 查看所有可用工具" -ForegroundColor Yellow
        return
    }

    $toolPath = Join-Path $scriptPath $matchedKey
    Invoke-Tool -ToolConfig $matchedTool -ToolPath $toolPath -ToolArgs $ToolArgs
}

# ============================================================
# 主入口
# ============================================================

# 加载配置
$config = Load-Config -Path $ConfigPath

if ($null -eq $config) {
    Write-Host ""
    Write-Host "  [错误] 无法加载配置文件: $ConfigPath" -ForegroundColor Red
    Write-Host "  请确保 launcher.config.json 存在于 Script/ 目录下" -ForegroundColor Yellow
    Write-Host ""
    Pause-Message
    exit 1
}

# 收集额外参数（-Tool 之后的参数，透传给工具脚本）
$extraArgs = @()
if ($args.Count -gt 0) {
    $extraArgs = @($args)
}

# 根据参数决定行为
if ($List) {
    # 列出所有工具
    Show-ToolList -Config $config
    Pause-Message
}
elseif ($Tool) {
    if ($Tool.ToLower() -eq "all") {
        # 显示完整菜单
        Show-MainMenu -Config $config
    }
    else {
        # 通过别名直接执行，透传额外参数
        Invoke-ToolByAlias -Config $config -Alias $Tool -ToolArgs $extraArgs
    }
}
else {
    # 显示交互菜单
    Show-MainMenu -Config $config
}
