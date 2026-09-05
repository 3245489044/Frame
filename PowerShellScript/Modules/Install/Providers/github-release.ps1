# @requires: BasicMethod/DevOps/GitHubRelease, BasicMethod/Core/Output, BasicMethod/Core/Path, Core/ProviderBase
# @version: 1.0.0
# @author: WorkFlowPrj
# @category: install-provider
# @alias: github-release-provider
# @exports: Get-GitHubReleaseProvider, Test-GitHubReleaseInstalled, Get-GitHubReleaseVersion, Invoke-GitHubReleaseInstall

<#
.SYNOPSIS
    GitHub Release 安装 Provider
.DESCRIPTION
    包装 BasicMethod/DevOps/GitHubRelease 的下载安装功能，
    实现标准 Provider 接口：Test-Installed, Get-Version, Invoke-Install
.NOTES
    依赖：BasicMethod/DevOps/GitHubRelease, BasicMethod/Core/Output, BasicMethod/Core/Path, Core/ProviderBase
#>

. (Resolve-SiblingPath "..\Core\ProviderBase.ps1")

function Get-GitHubReleaseProvider {
    return New-Provider -Type 'github-release' -Name 'GitHub Release Provider' `
        -TestInstalled ${function:Test-GitHubReleaseInstalled} `
        -GetVersion ${function:Get-GitHubReleaseVersion} `
        -InvokeInstall ${function:Invoke-GitHubReleaseInstall}
}

function Test-GitHubReleaseInstalled {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Item,
        [string]$Target = "local",
        [string]$ContainerName = "",
        [scriptblock]$Executor = $null
    )

    $installDir = Test-ProviderSource -Item $Item -FieldName "installDir" -FieldDescription "安装目录"
    if (-not $installDir) {
        return $false
    }

    $Executor = Get-ProviderExecutor -Target $Target -ContainerName $ContainerName -Executor $Executor

    try {
        $dirExists = & $Executor "Test-Path '$installDir'"
        
        if (-not $dirExists) {
            Write-Hint "检测: $installDir 不存在"
            return $false
        }

        $versionFile = Join-Path $installDir "VERSION"
        $versionExists = & $Executor "Test-Path '$versionFile'"
        
        if ($versionExists) {
            Write-Hint "检测: $installDir 已安装（存在 VERSION 文件）"
            return $true
        }

        $binaryName = if ($Item.binaryName) { $Item.binaryName } else { $Item.source.Split('/')[-1] }
        $binaryPath = Join-Path $installDir $binaryName
        $binaryExists = & $Executor "Test-Path '$binaryPath'"
        
        if ($binaryExists) {
            Write-Hint "检测: $installDir 已安装（存在二进制文件）"
            return $true
        }

        Write-Hint "检测: $installDir 目录存在但无有效安装"
        return $false
    } catch {
        Write-Warning "检测失败: $_"
        return $false
    }
}

function Get-GitHubReleaseVersion {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Item,
        [string]$Target = "local",
        [string]$ContainerName = "",
        [scriptblock]$Executor = $null
    )

    $installDir = Test-ProviderSource -Item $Item -FieldName "installDir" -FieldDescription "安装目录"
    if (-not $installDir) {
        return $null
    }

    $Executor = Get-ProviderExecutor -Target $Target -ContainerName $ContainerName -Executor $Executor

    try {
        $versionFile = Join-Path $installDir "VERSION"
        $versionFileExists = & $Executor "Test-Path '$versionFile'"
        
        if ($versionFileExists) {
            $version = & $Executor "Get-Content '$versionFile' -Raw"
            if ($version) {
                return $version.Trim()
            }
        }

        $binaryName = if ($Item.binaryName) { $Item.binaryName } else { $Item.source.Split('/')[-1] }
        $binaryPath = Join-Path $installDir $binaryName
        
        $result = & $Executor "$binaryPath --version 2>&1"
        if ($result) {
            $versionMatch = [regex]::Match($result, '(\d+\.\d+\.\d+)')
            if ($versionMatch.Success) {
                return $versionMatch.Groups[1].Value
            }
        }

        return $null
    } catch {
        return $null
    }
}

function Invoke-GitHubReleaseInstall {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Item,
        [string]$Target = "local",
        [string]$ContainerName = "",
        [scriptblock]$Executor = $null
    )

    $repo = Test-ProviderSource -Item $Item -FieldName "source" -FieldDescription "仓库地址"
    $installDir = Test-ProviderSource -Item $Item -FieldName "installDir" -FieldDescription "安装目录"
    
    if (-not $repo) {
        Write-Error "缺少仓库地址（source 字段）"
        return $false
    }
    if (-not $installDir) {
        Write-Error "缺少安装目录（installDir 字段）"
        return $false
    }

    $Executor = Get-ProviderExecutor -Target $Target -ContainerName $ContainerName -Executor $Executor

    try {
        $version = if ($Item.version) { $Item.version } else { "latest" }
        $assetPattern = if ($Item.assetPattern) { $Item.assetPattern } else { $null }
        $extractDir = if ($Item.extractDir) { $Item.extractDir } else { $null }

        Write-Info "GitHub Release 安装: $repo@$version"
        Write-Info "目标目录: $installDir"

        $result = Invoke-GitHubReleaseDownload -Repo $repo -Version $version -InstallDir $installDir -AssetPattern $assetPattern -ExtractDir $extractDir

        if ($result) {
            Write-Success "GitHub Release 安装成功: $repo"
            
            if ($Item.pathEntries -and $Item.pathEntries.Count -gt 0) {
                Set-PathEntries -PathEntries $Item.pathEntries -BaseDir $installDir -Target $Target -Executor $Executor
            }

            return $true
        } else {
            Write-Error "GitHub Release 安装失败: $repo"
            return $false
        }
    } catch {
        Write-Error "GitHub Release 安装异常: $_"
        return $false
    }
}

if ($MyInvocation.MyCommand.Source -eq $null) {
    Export-ModuleMember -Function @('Get-GitHubReleaseProvider', 'Test-GitHubReleaseInstalled', 'Get-GitHubReleaseVersion', 'Invoke-GitHubReleaseInstall')
}