<#
.SYNOPSIS
    构建配置解析模块 - 从项目文件中动态读取构建配置

.DESCRIPTION
    从 Directory.Build.props 等项目文件中读取构建配置信息，
    替换 MSBuild 变量为实际路径，消除 PowerShell 脚本中的硬编码路径。
    
    设计原则：
    - 零依赖：不依赖任何外部模块，可被任何 .ps1 脚本点引入使用
    - 单一职责：只负责从项目文件读取配置
    - 自动适配：构建配置变更时自动同步

    依赖:
        RootDetection.ps1 - Get-ProjectRoot

    用法:
        . "$PSScriptRoot\BuildConfig.ps1"
        $outputPath = Get-BuildOutputPath
        $targetFramework = Get-TargetFramework

.NOTES
    此脚本设计为通过点引入（dot-source）方式加载，不直接执行。
#>

$script:ModuleDir = Split-Path -Parent $PSCommandPath
. "$script:ModuleDir\..\Core\RootDetection.ps1"

$script:BC_LauncherCsprojRelPath = "FileDriven\FileDriven.Launcher\FileDriven.Launcher.csproj"
$script:BC_FileDrivenDirName = "FileDriven"
$script:BC_FileDrivenBuildOutputRelPath = "BuildOutput"

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

    if (-not $TargetFramework) {
        $TargetFramework = Get-TargetFramework -ProjectRoot $ProjectRoot
    }

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
        Write-Warning "Directory.Build.props 不存在，使用回退路径"
        return Join-Path $ProjectRoot "BuildOutput\$Configuration\$TargetFramework"
    }

    try {
        $propsXml = [xml](Get-Content $buildProps)

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

        $outputPath = $outputPathTemplate
        $buildPropsDir = Split-Path $buildProps -Parent
        $outputPath = $outputPath -replace '\$\(MSBuildThisFileDirectory\)', "$buildPropsDir\"
        $outputPath = $outputPath -replace '\$\(Configuration\)', $Configuration
        $outputPath = $outputPath -replace '\$\(TargetFramework\)', $TargetFramework

        $fullPath = [System.IO.Path]::GetFullPath($outputPath)
        return $fullPath
    }
    catch {
        Write-Warning "解析 Directory.Build.props 失败: $($_.Exception.Message)，使用回退路径"
        return Join-Path $ProjectRoot "BuildOutput\$Configuration\$TargetFramework"
    }
}

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
        静默模式，不输出警告信息
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
            Write-Warning "Directory.Build.props 不存在，使用默认值 CsharpPrj"
        }
        return "CsharpPrj"
    }

    try {
        $propsXml = [xml](Get-Content $buildProps)

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

    $launcherCsproj = Join-Path $ProjectRoot $script:BC_LauncherCsprojRelPath
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

    $debugDir = Get-BuildOutputPath -ProjectRoot $ProjectRoot -Configuration "Debug"
    $debugExe = Join-Path $debugDir "FileDriven.Launcher.exe"
    if (Test-Path $debugExe) {
        return $debugExe
    }

    $fallbackDebugDir = Join-Path $ProjectRoot "BuildOutput\Debug"
    $fallbackDebugExe = Join-Path $fallbackDebugDir "FileDriven.Launcher.exe"
    if (Test-Path $fallbackDebugExe) {
        return $fallbackDebugExe
    }

    $fileDrivenDebugDir = Join-Path $ProjectRoot "$($script:BC_FileDrivenDirName)\$($script:BC_FileDrivenBuildOutputRelPath)\Debug"
    $fileDrivenDebugExe = Join-Path $fileDrivenDebugDir "FileDriven.Launcher.exe"
    if (Test-Path $fileDrivenDebugExe) {
        return $fileDrivenDebugExe
    }

    $buildOutputRoot = Join-Path $ProjectRoot "BuildOutput"
    $foundExe = Find-FileDrivenLauncherExe -BuildOutputRoot $buildOutputRoot
    if ($foundExe) {
        return $foundExe
    }

    $fileDrivenBuildOutputRoot = Join-Path $ProjectRoot "$($script:BC_FileDrivenDirName)\$($script:BC_FileDrivenBuildOutputRelPath)"
    $foundExe = Find-FileDrivenLauncherExe -BuildOutputRoot $fileDrivenBuildOutputRoot
    if ($foundExe) {
        return $foundExe
    }

    $releaseDir = Get-BuildOutputPath -ProjectRoot $ProjectRoot -Configuration "Release"
    $releaseExe = Join-Path $releaseDir "FileDriven.Launcher.exe"
    if (Test-Path $releaseExe) {
        return $releaseExe
    }

    $fallbackReleaseDir = Join-Path $ProjectRoot "BuildOutput\Release"
    $fallbackReleaseExe = Join-Path $fallbackReleaseDir "FileDriven.Launcher.exe"
    if (Test-Path $fallbackReleaseExe) {
        return $fallbackReleaseExe
    }

    if ($AutoBuild) {
        Write-Host "[INFO] 未找到编译产物，正在自动构建 Debug 版本..." -ForegroundColor Yellow
        Write-Host ""

        $csproj = Join-Path $ProjectRoot $script:BC_LauncherCsprojRelPath
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

        if (Test-Path $debugExe) {
            return $debugExe
        }
        if (Test-Path $fallbackDebugExe) {
            return $fallbackDebugExe
        }

        $foundExe = Find-FileDrivenLauncherExe -BuildOutputRoot $buildOutputRoot
        if ($foundExe) {
            return $foundExe
        }

        Write-Host "[WARN] 构建完成但未在预期路径找到启动器" -ForegroundColor Yellow
    }

    return $null
}

function Get-LauncherOutputDir {
    <#
    .SYNOPSIS
        获取 FileDriven.Launcher.exe 所在目录
    .DESCRIPTION
        调用 Find-LauncherExe 获取 exe 路径，返回其所在目录。
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