<#
.SYNOPSIS
    FileDriven 测试通用工具函数

.DESCRIPTION
    提供 FileDriven 测试脚本所需的通用功能：
    - 获取项目配置（路径、别名等）
    - 运行模式管理
    - 辅助输出函数

    用法:
        . "$PSScriptRoot\FileDrivenTestUtils.ps1"   # 点引入加载所有函数
        $config = Get-FileDrivenConfig               # 获取配置
        $runMode = Get-RunMode                       # 获取运行模式
        Write-Header "标题"                          # 输出标题
        Write-Info "标签" "值"                       # 输出信息

.NOTES
    此脚本设计为通过点引入（dot-source）方式加载，不直接执行。
#>

# ============================================
#  配置获取
# ============================================

function Get-FileDrivenConfig {
    <#
    .SYNOPSIS
        获取 FileDriven 项目配置信息

    .DESCRIPTION
        计算项目根目录、FileDriven 目录、脚本目录等路径，
        并加载路径别名。
        ★ 重构：委托给 ProjectPaths 模块统一处理路径检测

    .PARAMETER ScriptPath
        调用方脚本的完整路径（通常使用 $MyInvocation.MyCommand.Path）
        ★ 不再需要，保留仅为兼容旧调用方

    .PARAMETER ProjectRootUpLevels
        ★ 不再需要，ProjectPaths 模块自动检测项目根目录

    .OUTPUTS
        Hashtable，包含以下键：
        - ProjectRoot      : 项目根目录
        - FileDrivenDir    : FileDriven 目录
        - ScriptsDir       : 测试脚本目录
        - Aliases          : 路径别名哈希表
        - BatToolConfigPath: BatToolConfig.bat 路径
    #>
    param(
        [Parameter(Mandatory=$false)]
        [string]$ScriptPath = "",

        [Parameter(Mandatory=$false)]
        [int]$ProjectRootUpLevels = 3
    )

    # ★ 使用 ProjectPaths 模块自动检测项目根目录
    $projectRoot = Get-ProjectRoot

    # ★ 使用统一路径函数获取 FileDriven 目录（自动拼接 CsharpProjectRoot）
    $fileDrivenDir = Get-FileDrivenDir -ProjectRoot $projectRoot

    # 测试脚本目录
    $scriptsDir = Join-Path $fileDrivenDir "DrivenConfig\Scripts"

    # BatToolConfig 路径
    $batToolConfigPath = Join-Path $projectRoot "PowerShellScript\Modules\Core\BatToolConfig.psm1"

    # ★ 使用 ProjectPaths 模块获取路径别名
    $aliases = Get-PathAliases -ProjectRoot $projectRoot

    return @{
        ProjectRoot       = $projectRoot
        FileDrivenDir     = $fileDrivenDir
        ScriptsDir        = $scriptsDir
        Aliases           = $aliases
        BatToolConfigPath = $batToolConfigPath
    }
}

# ============================================
#  运行模式管理
# ============================================

function Test-PipeMode {
    <#
    .SYNOPSIS
        检测是否处于管道输入模式（即时指令经管道投喂）

    .DESCRIPTION
        管道模式有两个判定信号：
        1. 环境变量 FILEDRIVEN_INSTANT_BODY 非空（交互启动.bat 经管道捕获 JSON 请求体时设置）
        2. 标准输入不是控制台（stdin 被管道重定向，无 TTY 可读）
        管道模式下禁止任何 Read-Host 等待按键（无 TTY 会无限阻塞），
        所有手动暂停/交互循环必须跳过。

    .OUTPUTS
        bool: true=管道模式（禁止 Read-Host）
    #>
    if (-not [string]::IsNullOrWhiteSpace($env:FILEDRIVEN_INSTANT_BODY)) {
        return $true
    }
    try {
        # IsInputRedirected=true 表示 stdin 被重定向（管道输入），判定为管道模式
        return [Console]::IsInputRedirected
    } catch {
        # [Console]::IsInputRedirected 在某些宿主（如 ISE）不可用
        return $false
    }
}

function Get-RunMode {
    <#
    .SYNOPSIS
        获取当前运行模式

    .DESCRIPTION
        从环境变量 FILEDRIVEN_RUN_MODE 获取运行模式，
        未设置时默认为 auto。
        ★ 管道模式（Test-PipeMode=true）强制返回 auto——
          管道输入无 TTY 可读，manual 模式的 Read-Host 将无限阻塞。

    .PARAMETER Default
        默认模式，默认为 "auto"

    .OUTPUTS
        string: "auto" 或 "manual"
    #>
    param(
        [Parameter(Mandatory=$false)]
        [ValidateSet("auto", "manual")]
        [string]$Default = "auto"
    )

    if (Test-PipeMode) {
        return "auto"
    }

    $mode = $env:FILEDRIVEN_RUN_MODE
    if ([string]::IsNullOrWhiteSpace($mode)) {
        $mode = $Default
    }
    return $mode
}

function Set-RunMode {
    <#
    .SYNOPSIS
        设置运行模式到环境变量

    .PARAMETER Mode
        运行模式: "auto" 或 "manual"
    #>
    param(
        [Parameter(Mandatory=$true)]
        [ValidateSet("auto", "manual")]
        [string]$Mode
    )

    $env:FILEDRIVEN_RUN_MODE = $Mode
}

function Switch-RunMode {
    <#
    .SYNOPSIS
        切换运行模式（auto ↔ manual）

    .OUTPUTS
        string: 切换后的模式
    #>
    $current = Get-RunMode
    if ($current -eq "auto") {
        Set-RunMode "manual"
        Write-Host "`n  [信息] 已切换到手动模式（运行结束后将暂停等待按键）"
    } else {
        Set-RunMode "auto"
        Write-Host "`n  [信息] 已切换到自动模式（运行结束后自动关闭）"
    }
    return (Get-RunMode)
}

# ============================================
#  辅助输出函数
# ============================================

function Write-Header {
    <#
    .SYNOPSIS
        输出带等号线框的标题

    .PARAMETER Text
        标题文本
    #>
    param([string]$Text)
    $line = "=" * 44
    Write-Host ""
    Write-Host $line
    Write-Host "  $Text"
    Write-Host $line
}

function Write-Info {
    <#
    .SYNOPSIS
        输出标签-值格式的信息

    .PARAMETER Label
        标签名

    .PARAMETER Value
        值
    #>
    param([string]$Label, [string]$Value)
    Write-Host ("  {0,-18}: {1}" -f $Label, $Value)
}

# ============================================
#  路径别名注入
# ============================================

function Set-AliasEnvironment {
    <#
    .SYNOPSIS
        将路径别名注入到环境变量

    .PARAMETER Aliases
        别名哈希表（来自 Get-FileDrivenConfig 的 Aliases）

    .PARAMETER Overrides
        要覆盖的别名键值对哈希表（可选）
        例如: @{ "Output" = "D:\some\path" }
    #>
    param(
        [Parameter(Mandatory=$true)]
        [hashtable]$Aliases,

        [Parameter(Mandatory=$false)]
        [hashtable]$Overrides = @{}
    )

    foreach ($key in $Aliases.Keys) {
        $envKey = "FILEDRIVEN_ALIAS_$key"
        $value = $Aliases[$key]
        if ($Overrides.ContainsKey($key)) {
            $value = $Overrides[$key]
        }
        Set-Item -Path "env:$envKey" -Value $value
    }
}

# ============================================
#  脚本列表获取
# ============================================

function Get-ScriptFiles {
    <#
    .SYNOPSIS
        获取指定目录中的脚本文件列表（支持递归扫描子目录）

    .PARAMETER ScriptsDir
        脚本目录路径

    .PARAMETER FilePattern
        文件扩展名过滤，用 | 分隔，默认为 ".json|.txt"

    .PARAMETER Recurse
        是否递归扫描子目录（默认为 $false）

    .OUTPUTS
        System.IO.FileInfo[] 文件对象数组
    #>
    param(
        [Parameter(Mandatory=$true)]
        [string]$ScriptsDir,

        [Parameter(Mandatory=$false)]
        [string]$FilePattern = ".json|.txt",

        [Parameter(Mandatory=$false)]
        [switch]$Recurse
    )

    $extensions = $FilePattern -split '\|'
    
    if ($Recurse) {
        $scripts = Get-ChildItem -Path $ScriptsDir -File -Recurse |
            Where-Object { $_.Extension -in $extensions } |
            Sort-Object FullName
    } else {
        $scripts = Get-ChildItem -Path $ScriptsDir -File |
            Where-Object { $_.Extension -in $extensions } |
            Sort-Object Name
    }

    return $scripts
}

# ============================================
#  启动器查找
# ============================================

function Find-LauncherDir {
    <#
    .SYNOPSIS
        查找 FileDriven.Launcher.exe 的输出目录

    .DESCRIPTION
        委托给 FindLauncherDir.ps1 脚本查找启动器目录。
        优先 Debug，其次 Release，未找到时自动构建。

    .PARAMETER FindScriptPath
        FindLauncherDir.ps1 的完整路径

    .PARAMETER FileDrivenDir
        FileDriven 项目根目录

    .OUTPUTS
        string: 启动器目录路径，未找到时返回 $null
    #>
    param(
        [Parameter(Mandatory=$true)]
        [string]$FindScriptPath,

        [Parameter(Mandatory=$true)]
        [string]$FileDrivenDir
    )

    # FindLauncherDir.ps1 使用 Write-Output 输出路径（stdout），
    # 使用 Write-Warning/Write-Error 输出日志（stderr），
    # 因此分别捕获 stdout 和 stderr
    $stdout = & powershell -NoProfile -File $FindScriptPath -FileDrivenDir $FileDrivenDir 2>&1 | Where-Object { $_ -is [string] }
    $pathLine = $stdout | Select-Object -Last 1

    return $pathLine
}
