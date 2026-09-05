<#
.SYNOPSIS
    GitHub Release 下载安装通用模块 — 提供从 GitHub Releases 下载、解压、安装的通用函数

.DESCRIPTION
    从 Install-McpServer.ps1 中提取的纯通用 GitHub Release 操作函数。
    不依赖任何项目特定常量（路径等由调用方传入）。
    支持在本地或远程（Docker 容器）环境中执行操作。

    包含函数:
        Get-GitHubLatestRelease     — 获取 GitHub 仓库的最新 release 信息
        Find-GitHubReleaseAsset     — 在 release 中查找匹配的 asset
        Install-GitHubRelease       — 下载并安装 GitHub Release asset（通用）
        Install-GitHubReleaseToDir  — 下载 GitHub Release asset 到指定目录

    用法:
        Import-Module .\BasicMethod-GitHubRelease.psm1
        $release = Get-GitHubLatestRelease -Repo "owner/repo"
        $asset = Find-GitHubReleaseAsset -Release $release -Pattern "*.tgz"
        Install-GitHubRelease -Repo "owner/repo" -InstallDir "/opt/mcp-servers/my-server"
#>

# ============================================
# GitHub API 函数
# ============================================

function Get-GitHubLatestRelease {
    <#
    .SYNOPSIS
        获取 GitHub 仓库的最新 release 信息
    .PARAMETER Repo
        仓库名称，格式 "owner/repo"
    .PARAMETER Token
        GitHub Token（可选，用于私有仓库或提高 API 限流）
    .OUTPUTS
        [object] release 信息对象，失败返回 $null
    .EXAMPLE
        $release = Get-GitHubLatestRelease -Repo "anthropics/anthropic-mcp"
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$Repo,

        [string]$Token = ""
    )

    $apiUrl = "https://api.github.com/repos/$Repo/releases/latest"
    $headers = @{ "Accept" = "application/json" }
    if ($Token) {
        $headers["Authorization"] = "Bearer $Token"
    }

    try {
        $releaseInfo = Invoke-RestMethod -Uri $apiUrl -Headers $headers -ErrorAction Stop
        return $releaseInfo
    } catch {
        Write-Host "  ❌ GitHub API 请求失败: $_" -ForegroundColor Red
        return $null
    }
}

function Find-GitHubReleaseAsset {
    <#
    .SYNOPSIS
        在 release 信息中查找匹配通配符模式的 asset
    .PARAMETER Release
        release 信息对象（来自 Get-GitHubLatestRelease）
    .PARAMETER Pattern
        通配符模式，如 "*.tgz", "*.zip", "*-linux-*.tar.gz"
    .OUTPUTS
        [object] 匹配的 asset 对象，未找到返回 $null
    .EXAMPLE
        $asset = Find-GitHubReleaseAsset -Release $release -Pattern "*.tgz"
    #>
    param(
        [Parameter(Mandatory = $true)]
        [object]$Release,

        [string]$Pattern = "*.tgz"
    )

    $asset = $Release.assets | Where-Object { $_.name -like $Pattern } | Select-Object -First 1
    return $asset
}

# ============================================
# 安装函数
# ============================================

function Install-GitHubReleaseToDir {
    <#
    .SYNOPSIS
        下载 GitHub Release asset 到指定目录并解压
    .PARAMETER Repo
        仓库名称，格式 "owner/repo"
    .PARAMETER InstallDir
        安装目标目录
    .PARAMETER AssetPattern
        asset 文件名通配符模式（默认 "*.tgz"）
    .PARAMETER Executor
        执行器脚本块，用于在远程环境执行命令。
        格式: { param($cmd) & docker exec $ContainerName sh -c $cmd }
        不传则在本地执行。
    .PARAMETER Token
        GitHub Token（可选）
    .OUTPUTS
        [bool] 成功返回 $true，失败返回 $false
    .EXAMPLE
        Install-GitHubReleaseToDir -Repo "owner/repo" -InstallDir "/opt/mcp-servers/my-server"
    .EXAMPLE
        Install-GitHubReleaseToDir -Repo "owner/repo" -InstallDir "/opt/mcp-servers/my-server" `
            -Executor { param($cmd) docker exec mycontainer sh -c $cmd }
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$Repo,

        [Parameter(Mandatory = $true)]
        [string]$InstallDir,

        [string]$AssetPattern = "*.tgz",

        [scriptblock]$Executor = $null,

        [string]$Token = ""
    )

    Write-Host "  ℹ️  从 GitHub Releases 下载: $Repo" -ForegroundColor Gray

    # 获取最新 release
    $releaseInfo = Get-GitHubLatestRelease -Repo $Repo -Token $Token
    if (-not $releaseInfo) { return $false }

    $tagName = $releaseInfo.tag_name
    Write-Host "  ℹ️  最新版本: $tagName" -ForegroundColor Gray

    # 查找匹配的 asset
    $asset = Find-GitHubReleaseAsset -Release $releaseInfo -Pattern $AssetPattern
    if (-not $asset) {
        Write-Host "  ❌ 未找到匹配的 asset: $AssetPattern" -ForegroundColor Red
        return $false
    }

    $downloadUrl = $asset.browser_download_url
    $assetName = $asset.name
    Write-Host "  ℹ️  下载: $assetName" -ForegroundColor Gray

    try {
        if ($Executor) {
            # 在远程环境执行
            $commands = @(
                "mkdir -p $InstallDir",
                "cd $InstallDir && curl -sL '$downloadUrl' -o '$assetName'",
                "cd $InstallDir && tar -xzf '$assetName' 2>/dev/null || unzip -o '$assetName' 2>/dev/null || echo 'extracted'",
                "cd $InstallDir && rm -f '$assetName'",
                "cd $InstallDir && (test -f package.json && npm install --production 2>/dev/null || true)"
            )

            foreach ($cmd in $commands) {
                $result = & $Executor $cmd
                if ($LASTEXITCODE -ne 0) {
                    Write-Host "  ⚠️  命令执行警告: $cmd" -ForegroundColor Yellow
                }
            }
        } else {
            # 在本地执行
            if (-not (Test-Path $InstallDir)) {
                New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
            }

            $assetPath = Join-Path $InstallDir $assetName
            Invoke-WebRequest -Uri $downloadUrl -OutFile $assetPath -ErrorAction Stop

            # 解压
            if ($assetName -like "*.tgz" -or $assetName -like "*.tar.gz") {
                tar -xzf $assetPath -C $InstallDir 2>&1 | Out-Null
            } elseif ($assetName -like "*.zip") {
                Expand-Archive -Path $assetPath -DestinationPath $InstallDir -Force
            }

            Remove-Item $assetPath -Force

            # 如果有 package.json，安装依赖
            $pkgJson = Join-Path $InstallDir "package.json"
            if (Test-Path $pkgJson) {
                Push-Location $InstallDir
                npm install --production 2>&1 | Out-Null
                Pop-Location
            }
        }

        Write-Host "  ✅ $Repo 安装成功到 $InstallDir" -ForegroundColor Green
        return $true
    } catch {
        Write-Host "  ❌ GitHub Release 安装失败: $_" -ForegroundColor Red
        return $false
    }
}

function Install-GitHubRelease {
    <#
    .SYNOPSIS
        从 GitHub Releases 下载并安装（简化接口，自动生成安装目录名）
    .PARAMETER Repo
        仓库名称，格式 "owner/repo"
    .PARAMETER Name
        安装名称（用于生成目录名）
    .PARAMETER BaseDir
        基础安装目录（默认 $env:USERPROFILE/.mcp-servers）
    .PARAMETER AssetPattern
        asset 文件名通配符模式（默认 "*.tgz"）
    .PARAMETER Executor
        执行器脚本块，用于在远程环境执行命令
    .PARAMETER Token
        GitHub Token（可选）
    .OUTPUTS
        [bool] 成功返回 $true，失败返回 $false
    .EXAMPLE
        Install-GitHubRelease -Repo "owner/repo" -Name "my-server"
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$Repo,

        [Parameter(Mandatory = $true)]
        [string]$Name,

        [string]$BaseDir = "",

        [string]$AssetPattern = "*.tgz",

        [scriptblock]$Executor = $null,

        [string]$Token = ""
    )

    $installDir = if ($BaseDir) {
        Join-Path $BaseDir $Name
    } elseif ($Executor) {
        "/opt/mcp-servers/$Name"
    } else {
        Join-Path $env:USERPROFILE ".mcp-servers" $Name
    }

    return Install-GitHubReleaseToDir -Repo $Repo -InstallDir $installDir -AssetPattern $AssetPattern -Executor $Executor -Token $Token
}

# ============================================
# 模块导出
# ============================================

Export-ModuleMember -Function @(
    'Get-GitHubLatestRelease',
    'Find-GitHubReleaseAsset',
    'Install-GitHubReleaseToDir',
    'Install-GitHubRelease'
)
