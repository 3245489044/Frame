<#
.SYNOPSIS
    项目配置文件读取器 - 从项目文件中动态读取构建配置

.DESCRIPTION
    从 Directory.Build.props 等项目文件中读取构建配置信息，
    替换 MSBuild 变量为实际路径，消除 PowerShell 脚本中的硬编码路径。
    
    设计原则：
    - 零依赖：不依赖任何模块，可被任何 .ps1 脚本点引入使用
    - 单一职责：只负责从项目文件读取配置
    - 自动适配：构建配置变更时自动同步

    用法:
        . "$PSScriptRoot\ReadProjectConfig.ps1"   # 点引入加载所有函数
        $outputPath = Get-BuildOutputPath          # 获取构建输出目录
        $targetFramework = Get-TargetFramework     # 获取目标框架

.NOTES
    此脚本设计为通过点引入（dot-source）方式加载，不直接执行。
#>

# ============================================
#  内部常量
# ============================================

# 项目标记文件列表（已弃用）
# ★ MainProj.sln 和 Directory.Build.props 位于 CsharpPrj 子目录下，
#   用作根目录标记会导致路径重复。改用 .git 目录作为唯一标记。
$script:RPC_MarkerFiles = @()

$script:RPC_MarkerDirs = @(
    ".git"
)

# ============================================
#  集中管理的硬编码路径
#  所有脚本统一引用此处，路径变更时只需修改此处
# ============================================

# FileDriven.Launcher.csproj 相对于项目根目录的路径
$script:RPC_LauncherCsprojRelPath = "FileDriven\FileDriven.Launcher\FileDriven.Launcher.csproj"

# FileDriven 项目目录名（相对于项目根目录）
$script:RPC_FileDrivenDirName = "FileDriven"

# FileDriven 构建输出目录（相对于 FileDriven 项目目录）
$script:RPC_FileDrivenBuildOutputRelPath = "BuildOutput"

# ============================================
#  项目根目录检测
# ============================================

# 缓存变量（供 Get-ProjectRoot 使用）
$script:RPC_CachedProjectRoot = $null

function Get-ProjectRoot {
    <#
    .SYNOPSIS
        智能检测项目根目录

    .DESCRIPTION
        从调用方脚本目录（或指定起始路径）逐层向上查找，
        通过标记文件（.sln / .git 等）定位项目根目录。
        此函数为独立实现，不依赖任何外部模块。
        支持缓存，多次调用时性能更优。

    .PARAMETER StartPath
        起始路径，默认使用调用方脚本目录

    .PARAMETER ForceRescan
        强制重新扫描，忽略缓存

    .OUTPUTS
        string: 项目根目录的完整路径
    #>
    param(
        [Parameter(Mandatory=$false)]
        [string]$StartPath = "",

        [Parameter(Mandatory=$false)]
        [switch]$ForceRescan
    )

    # 缓存
    if (-not $ForceRescan -and $script:RPC_CachedProjectRoot) {
        return $script:RPC_CachedProjectRoot
    }

    if (-not $StartPath) {
        $callStack = Get-PSCallStack
        if ($callStack.Count -ge 2) {
            $callerScript = $callStack[1].ScriptName
            if ($callerScript) {
                $StartPath = Split-Path -Parent $callerScript
            }
        }
    }

    if (-not $StartPath) {
        $StartPath = $PSScriptRoot
    }

    $current = $StartPath
    while ($current) {
        foreach ($marker in $script:RPC_MarkerFiles) {
            if (Test-Path (Join-Path $current $marker)) {
                $script:RPC_CachedProjectRoot = $current
                return $current
            }
        }

        foreach ($marker in $script:RPC_MarkerDirs) {
            if (Test-Path (Join-Path $current $marker)) {
                $script:RPC_CachedProjectRoot = $current
                return $current
            }
        }

        $parent = Split-Path $current -Parent
        if ($parent -eq $current) { break }
        $current = $parent
    }

    # ★ 回退：从脚本位置逐级上移，检测标记文件
    # 脚本路径: Script\Modules\Core\ReadProjectConfig.ps1
    # 预期项目根: 上移 3 级（Core -> Modules -> Script -> 项目根）
    $fallback = $PSScriptRoot
    for ($i = 0; $i -lt 10; $i++) {
        $fallback = Split-Path $fallback -Parent
        foreach ($marker in $script:RPC_MarkerFiles) {
            if (Test-Path (Join-Path $fallback $marker)) {
                $script:RPC_CachedProjectRoot = $fallback
                return $fallback
            }
        }
        foreach ($marker in $script:RPC_MarkerDirs) {
            if (Test-Path (Join-Path $fallback $marker)) {
                $script:RPC_CachedProjectRoot = $fallback
                return $fallback
            }
        }
        if ($fallback -eq (Split-Path $fallback -Parent)) { break }
    }
    $script:RPC_CachedProjectRoot = $fallback
    return $fallback
}

function Reset-ProjectRootCache {
    <#
    .SYNOPSIS
        清除项目根目录缓存（用于测试或配置变更后重新检测）
    #>
    $script:RPC_CachedProjectRoot = $null
}


# ============================================
#  Directory.Build.props 解析
# ============================================

function Get-BuildOutputPath {
    <#
    .SYNOPSIS
        从 Directory.Build.props 中读取 OutputPath 配置，解析为实际路径

    .DESCRIPTION
        解析 Directory.Build.props 中的 <OutputPath> 模板，
        替换 MSBuild 变量（$(MSBuildThisFileDirectory)、$(Configuration)、$(TargetFramework)）为实际值。
        这样构建配置变更时，脚本自动适配，无需手动维护硬编码路径。

    .PARAMETER ProjectRoot
        项目根目录，默认自动检测

    .PARAMETER Configuration
        构建配置，默认为 "Debug"

    .PARAMETER TargetFramework
        目标框架，默认自动从 FileDriven.Launcher.csproj 读取

    .OUTPUTS
        string: 构建输出目录的完整路径
    #>
    param(
        [Parameter(Mandatory=$false)]
        [string]$ProjectRoot = "",

        [Parameter(Mandatory=$false)]
        [ValidateSet("Debug", "Release")]
        [string]$Configuration = "Debug",

        [Parameter(Mandatory=$false)]
        [string]$TargetFramework = ""
    )

    if (-not $ProjectRoot) {
        $ProjectRoot = Get-ProjectRoot
    }

    # 如果未指定 TargetFramework，自动从启动器项目文件读取
    if (-not $TargetFramework) {
        $TargetFramework = Get-TargetFramework -ProjectRoot $ProjectRoot
    }

    # ★ 查找 Directory.Build.props（可能在项目根目录或 CsharpPrj 子目录下）
    $buildProps = $null
    $csharpRoot = Get-CsharpProjectRoot -ProjectRoot $ProjectRoot -Quiet
    $candidatePaths = @(
        Join-Path $ProjectRoot "Directory.Build.props"
        Join-Path $ProjectRoot "$csharpRoot\Directory.Build.props"
    )
    foreach ($candidate in $candidatePaths) {
        if (Test-Path $candidate) {
            $buildProps = $candidate
            break
        }
    }

    if (-not $buildProps) {
        Write-Warning "Directory.Build.props 不存在（已检查根目录和 CsharpPrj 子目录），使用回退路径"
        return Join-Path $ProjectRoot "BuildOutput\$Configuration\$TargetFramework"
    }

    try {
        # 解析 XML
        $propsXml = [xml](Get-Content $buildProps)

        # 查找 OutputPath 节点（可能在多个 PropertyGroup 中，取第一个非空的）
        $outputPathTemplate = ""
        foreach ($pg in $propsXml.Project.PropertyGroup) {
            if ($pg.OutputPath) {
                $outputPathTemplate = $pg.OutputPath
                break
            }
        }

        if (-not $outputPathTemplate) {
            Write-Warning "Directory.Build.props 中未找到 OutputPath 配置，使用回退路径"
            return Join-Path $ProjectRoot "BuildOutput\$Configuration\$TargetFramework"
        }

        # 替换 MSBuild 变量
        $outputPath = $outputPathTemplate
        # MSBuildThisFileDirectory 指向 Directory.Build.props 所在目录
        $buildPropsDir = Split-Path $buildProps -Parent
        $outputPath = $outputPath -replace '\$\(MSBuildThisFileDirectory\)', "$buildPropsDir\"
        $outputPath = $outputPath -replace '\$\(Configuration\)', $Configuration
        $outputPath = $outputPath -replace '\$\(TargetFramework\)', $TargetFramework

        # 规范化路径（处理 ..\ 等相对路径）
        $fullPath = [System.IO.Path]::GetFullPath($outputPath)
        return $fullPath
    }
    catch {
        Write-Warning "解析 Directory.Build.props 失败: $($_.Exception.Message)，使用回退路径"
        return Join-Path $ProjectRoot "BuildOutput\$Configuration\$TargetFramework"
    }
}

# ============================================
#  C# 项目子目录根路径读取
#  从 Directory.Build.props 的 CsharpProjectRoot 属性读取
#  当 .sln 文件移动时，只需修改 Directory.Build.props 中的路径即可
# ============================================

function Get-CsharpProjectRoot {
    <#
    .SYNOPSIS
        获取 C# 项目子目录根路径（MainProj.sln 所在目录）

    .DESCRIPTION
        从 Directory.Build.props 的 CsharpProjectRoot 属性读取，
        返回相对于项目根目录的 C# 项目子目录路径。
        当 .sln 文件移动时，只需修改 Directory.Build.props 中的路径即可同步所有脚本。

    .PARAMETER ProjectRoot
        项目根目录，默认自动检测

    .PARAMETER Quiet
        静默模式，不输出警告信息（用于内部调用，避免重复警告）

    .OUTPUTS
        string: C# 项目子目录路径（如 CsharpPrj），相对于项目根目录
    #>
    param(
        [Parameter(Mandatory=$false)]
        [string]$ProjectRoot = "",

        [Parameter(Mandatory=$false)]
        [switch]$Quiet
    )

    if (-not $ProjectRoot) {
        $ProjectRoot = Get-ProjectRoot
    }

    # ★ 查找 Directory.Build.props（可能在项目根目录或 CsharpPrj 子目录下）
    $buildProps = $null
    $candidatePaths = @(
        Join-Path $ProjectRoot "Directory.Build.props"
        Join-Path $ProjectRoot "CsharpPrj\Directory.Build.props"
    )
    foreach ($candidate in $candidatePaths) {
        if (Test-Path $candidate) {
            $buildProps = $candidate
            break
        }
    }

    if (-not $buildProps) {
        if (-not $Quiet) {
            Write-Warning "Directory.Build.props 不存在（已检查根目录和 CsharpPrj 子目录），使用默认值 CsharpPrj"
        }
        return "CsharpPrj"
    }

    try {
        $propsXml = [xml](Get-Content $buildProps)

        # 查找 CsharpProjectRoot 属性（可能在任意 PropertyGroup 中）
        $csharpRoot = ""
        foreach ($pg in $propsXml.Project.PropertyGroup) {
            if ($pg.CsharpProjectRoot) {
                $csharpRoot = $pg.CsharpProjectRoot
                break
            }
        }

        if (-not $csharpRoot) {
            if (-not $Quiet) {
                Write-Debug "Directory.Build.props 中未找到 CsharpProjectRoot，使用默认值 CsharpPrj"
            }
            return "CsharpPrj"
        }

        return $csharpRoot
    }
    catch {
        if (-not $Quiet) {
            Write-Debug "解析 Directory.Build.props 失败: $($_.Exception.Message)，使用默认值 CsharpPrj"
        }
        return "CsharpPrj"
    }
}

# ============================================
#  TargetFramework 读取
# ============================================

function Get-TargetFramework {
    <#
    .SYNOPSIS
        从 FileDriven.Launcher.csproj 中读取 TargetFramework

    .DESCRIPTION
        解析启动器项目文件中的 <TargetFramework> 配置，
        获取当前使用的目标框架（如 net10.0-windows）。

    .PARAMETER ProjectRoot
        项目根目录，默认自动检测

    .OUTPUTS
        string: 目标框架名称
    #>
    param(
        [Parameter(Mandatory=$false)]
        [string]$ProjectRoot = ""
    )

    if (-not $ProjectRoot) {
        $ProjectRoot = Get-ProjectRoot
    }

    $launcherCsproj = Join-Path $ProjectRoot $script:RPC_LauncherCsprojRelPath
    if (-not (Test-Path $launcherCsproj)) {
        Write-Warning "FileDriven.Launcher.csproj 不存在: $launcherCsproj，使用默认 net10.0-windows"
        return "net10.0-windows"
    }

    try {
        $csprojXml = [xml](Get-Content $launcherCsproj)
        $targetFramework = $csprojXml.Project.PropertyGroup.TargetFramework

        if (-not $targetFramework) {
            Write-Warning "FileDriven.Launcher.csproj 中未找到 TargetFramework，使用默认 net10.0-windows"
            return "net10.0-windows"
        }

        return $targetFramework
    }
    catch {
        Write-Warning "解析 FileDriven.Launcher.csproj 失败: $($_.Exception.Message)，使用默认 net10.0-windows"
        return "net10.0-windows"
    }
}

# ============================================
#  文件名遍历算法：在 BuildOutput 下递归搜索
# ============================================

function Find-FileDrivenLauncherExe {
    <#
    .SYNOPSIS
        文件名遍历算法：在 BuildOutput 目录下递归搜索 FileDriven.Launcher.exe

    .DESCRIPTION
        使用 Get-ChildItem -Recurse 在 BuildOutput 下搜索所有 FileDriven.Launcher.exe，
        按目录深度排序（浅层优先），优先 Debug 路径。
        这样无论构建输出到 BuildOutput 下的任何子目录，都能自动找到，
        不再依赖硬编码的路径模板。

    .PARAMETER BuildOutputRoot
        BuildOutput 目录的完整路径

    .OUTPUTS
        string: 启动器 exe 的完整路径，未找到时返回 $null
    #>
    param(
        [Parameter(Mandatory=$true)]
        [string]$BuildOutputRoot
    )

    if (-not (Test-Path $BuildOutputRoot)) {
        return $null
    }

    try {
        $foundExes = Get-ChildItem -Path $BuildOutputRoot -Recurse -Filter "FileDriven.Launcher.exe" -ErrorAction SilentlyContinue
        if (-not $foundExes) {
            return $null
        }

        # 按目录深度排序（浅层优先），优先 Debug 路径
        # 排序规则：目录深度越小越优先，同深度下 Debug 优先于 Release
        $sorted = $foundExes | Sort-Object @{
            Expression = {
                $relative = $_.DirectoryName.Substring($BuildOutputRoot.Length).TrimStart('\')
                $depth = ($relative -split '\\').Count
                $isDebug = if ($_.DirectoryName -match '\\Debug\\' -or $_.DirectoryName -match '\\Debug$') { 0 } else { 1 }
                return $depth * 10 + $isDebug
            }
            Ascending = $true
        }

        return $sorted[0].FullName
    }
    catch {
        Write-Debug "文件名遍历算法异常: $($_.Exception.Message)"
        return $null
    }
}

# ============================================
#  Launcher 可执行文件路径查找
# ============================================

function Find-LauncherExe {
    <#
    .SYNOPSIS
        查找 FileDriven.Launcher.exe 的完整路径

    .DESCRIPTION
        使用 Get-BuildOutputPath 动态获取构建输出目录，
        然后拼接 exe 文件名。优先 Debug，其次 Release。
        如果都未找到，自动执行 dotnet build 构建 Debug 版本。

    .PARAMETER ProjectRoot
        项目根目录，默认自动检测

    .PARAMETER AutoBuild
        是否在未找到时自动构建，默认为 $true

    .OUTPUTS
        string: 启动器 exe 的完整路径，未找到时返回 $null
    #>
    param(
        [Parameter(Mandatory=$false)]
        [string]$ProjectRoot = "",

        [Parameter(Mandatory=$false)]
        [bool]$AutoBuild = $true
    )

    if (-not $ProjectRoot) {
        $ProjectRoot = Get-ProjectRoot
    }

    # ★ 查找策略（按可靠性从高到低）：
    #   0. S4·部署单轨契约：CsharpPrj\BuildOutput（MainProj 输出）为 DLL 唯一部署根；FileDriven\BuildOutput 仅承载
    #      FileDriven.Launcher.exe 及其复制依赖（由 FileDriven.sln 构建在 MainProj.sln 之后生成，时间戳最晚 = 最新）。
    #      运行入口（运行测试GUI模式.ps1）已加时间戳断言：launcher 目录 DLL 早于 CsharpPrj 最新构建 → 告警部署双轨。
    #   1. 从 Directory.Build.props 的 OutputPath 模板解析的路径查找
    #   2. 尝试 BuildOutput\Debug\ 根目录（部分项目可能覆盖了 OutputPath）
    #   3. 尝试 CsharpPrj\BuildOutput\Debug\ 根目录（不带 TargetFramework 子目录）
    #   4. ★ 文件名遍历算法：在 BuildOutput 下递归搜索 FileDriven.Launcher.exe
    #      按目录深度排序（浅层优先），优先 Debug 路径
    #   5. 查找 Release 版本（模板路径 + 根目录）
    #   6. 都找不到则自动构建，构建后再次遍历


    # 策略1：从 OutputPath 模板解析的路径查找
    $debugDir = Get-BuildOutputPath -ProjectRoot $ProjectRoot -Configuration "Debug"
    $debugExe = Join-Path $debugDir "FileDriven.Launcher.exe"
    if (Test-Path $debugExe) {
        return $debugExe
    }

    # 策略2：尝试 BuildOutput\Debug\ 根目录（部分项目可能覆盖了 OutputPath 设置）
    $fallbackDebugDir = Join-Path $ProjectRoot "BuildOutput\Debug"
    $fallbackDebugExe = Join-Path $fallbackDebugDir "FileDriven.Launcher.exe"
    if (Test-Path $fallbackDebugExe) {
        return $fallbackDebugExe
    }

    # ★ 策略3：尝试 FileDriven\BuildOutput\Debug\ 根目录
    #   实际构建输出可能位于 FileDriven 项目下的 BuildOutput\Debug\（不带 TargetFramework 子目录）
    $fileDrivenDebugDir = Join-Path $ProjectRoot "$($script:RPC_FileDrivenDirName)\$($script:RPC_FileDrivenBuildOutputRelPath)\Debug"
    $fileDrivenDebugExe = Join-Path $fileDrivenDebugDir "FileDriven.Launcher.exe"
    if (Test-Path $fileDrivenDebugExe) {
        return $fileDrivenDebugExe
    }

    # ★ 策略4：文件名遍历算法（项目根目录下 BuildOutput）
    #   在 BuildOutput 下递归搜索 FileDriven.Launcher.exe
    #   按目录深度排序（浅层优先），优先 Debug 路径
    $buildOutputRoot = Join-Path $ProjectRoot "BuildOutput"
    $foundExe = Find-FileDrivenLauncherExe -BuildOutputRoot $buildOutputRoot
    if ($foundExe) {
        return $foundExe
    }

    # ★ 策略5：文件名遍历算法（FileDriven\BuildOutput 下）
    #   实际构建输出可能位于 FileDriven 项目下的 BuildOutput
    $fileDrivenBuildOutputRoot = Join-Path $ProjectRoot "$($script:RPC_FileDrivenDirName)\$($script:RPC_FileDrivenBuildOutputRelPath)"
    $foundExe = Find-FileDrivenLauncherExe -BuildOutputRoot $fileDrivenBuildOutputRoot
    if ($foundExe) {
        return $foundExe
    }

    # 策略6：查找 Release 版本（模板路径）

    $releaseDir = Get-BuildOutputPath -ProjectRoot $ProjectRoot -Configuration "Release"
    $releaseExe = Join-Path $releaseDir "FileDriven.Launcher.exe"
    if (Test-Path $releaseExe) {
        return $releaseExe
    }

    # 策略7：尝试 BuildOutput\Release\ 根目录

    $fallbackReleaseDir = Join-Path $ProjectRoot "BuildOutput\Release"
    $fallbackReleaseExe = Join-Path $fallbackReleaseDir "FileDriven.Launcher.exe"
    if (Test-Path $fallbackReleaseExe) {
        return $fallbackReleaseExe
    }


    # 未找到编译产物，自动构建
    if ($AutoBuild) {
        Write-Host "[INFO] 未找到编译产物，正在自动构建 Debug 版本..." -ForegroundColor Yellow
        Write-Host ""

        $csproj = Join-Path $ProjectRoot $script:RPC_LauncherCsprojRelPath
        Write-Host "[INFO] 开始构建: dotnet build $csproj -c Debug" -ForegroundColor Cyan
        $buildOutput = dotnet build $csproj -c Debug 2>&1
        $exitCode = $LASTEXITCODE

        $buildOutput | ForEach-Object { Write-Host $_ }

        if ($exitCode -ne 0) {
            Write-Host ""
            Write-Host "[ERROR] 构建失败（错误码: $exitCode），请检查代码错误后重试" -ForegroundColor Red
            return $null
        }

        Write-Host ""
        Write-Host "[INFO] 构建成功" -ForegroundColor Green
        Write-Host ""

        # 构建后再次检查（先查策略1路径，再查策略2路径，最后遍历）
        if (Test-Path $debugExe) {
            return $debugExe
        }
        if (Test-Path $fallbackDebugExe) {
            return $fallbackDebugExe
        }

        # ★ 构建后再次使用文件名遍历算法
        $foundExe = Find-FileDrivenLauncherExe -BuildOutputRoot $buildOutputRoot
        if ($foundExe) {
            return $foundExe
        }

        Write-Host "[WARN] 构建完成但未在预期路径找到启动器" -ForegroundColor Yellow
        Write-Host "[WARN]   策略1: $debugExe" -ForegroundColor Yellow
        Write-Host "[WARN]   策略2: $fallbackDebugExe" -ForegroundColor Yellow
        Write-Host "[WARN]   遍历: BuildOutput 下未找到 FileDriven.Launcher.exe" -ForegroundColor Yellow

    }

    return $null
}

# ============================================
#  快捷函数：获取启动器目录（兼容旧接口）
# ============================================

function Get-LauncherOutputDir {
    <#
    .SYNOPSIS
        获取 FileDriven.Launcher.exe 所在目录（兼容旧接口）

    .DESCRIPTION
        调用 Find-LauncherExe 获取 exe 路径，返回其所在目录。
        与 ProjectPaths.psm1 中的 Get-LauncherOutputDir 接口一致。

    .PARAMETER ProjectRoot
        项目根目录，默认自动检测

    .PARAMETER AutoBuild
        是否在未找到时自动构建，默认为 $true

    .OUTPUTS
        string: 启动器目录路径，未找到时返回 $null
    #>
    param(
        [Parameter(Mandatory=$false)]
        [string]$ProjectRoot = "",

        [Parameter(Mandatory=$false)]
        [bool]$AutoBuild = $true
    )

    $exePath = Find-LauncherExe -ProjectRoot $ProjectRoot -AutoBuild $AutoBuild
    if ($exePath) {
        return Split-Path $exePath -Parent
    }
    return $null
}

# ============================================
#  统一路径函数
#  所有脚本通过这两个函数获取路径，避免硬编码分散
#  路径变更时只需修改此处
# ============================================

function Get-FileDrivenDir {
    <#
    .SYNOPSIS
        统一获取 FileDriven 项目目录

    .DESCRIPTION
        返回 FileDriven 目录的完整路径（位于项目根目录下）。
        所有脚本统一调用此函数，避免路径硬编码分散。

    .PARAMETER ProjectRoot
        项目根目录，默认自动检测

    .OUTPUTS
        string: FileDriven 目录的完整路径
    #>
    param(
        [Parameter(Mandatory=$false)]
        [string]$ProjectRoot = ""
    )

    if (-not $ProjectRoot) {
        $ProjectRoot = Get-ProjectRoot
    }

    return Join-Path $ProjectRoot "FileDriven"
}

function Get-ScriptModulesDir {
    <#
    .SYNOPSIS
        统一获取 Script 模块目录（PowerShellScript\Modules）

    .DESCRIPTION
        返回 PowerShellScript\Modules 目录的完整路径。
        所有脚本统一调用此函数，避免路径硬编码分散。

    .PARAMETER ProjectRoot
        项目根目录，默认自动检测

    .OUTPUTS
        string: Script 模块目录的完整路径
    #>
    param(
        [Parameter(Mandatory=$false)]
        [string]$ProjectRoot = ""
    )

    if (-not $ProjectRoot) {
        $ProjectRoot = Get-ProjectRoot
    }

    return Join-Path $ProjectRoot "PowerShellScript\Modules"
}
