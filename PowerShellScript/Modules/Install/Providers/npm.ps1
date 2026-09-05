# @requires: BasicMethod/DevOps/PackageManager, BasicMethod/Core/Output, Core/ProviderBase
# @version: 1.0.0
# @author: WorkFlowPrj
# @category: install-provider
# @alias: npm-provider
# @exports: Get-NpmProvider, Test-NpmProviderInstalled, Get-NpmProviderVersion, Invoke-NpmProviderInstall

<#
.SYNOPSIS
    npm 包安装 Provider
.DESCRIPTION
    包装 BasicMethod/DevOps/PackageManager 的 npm 相关功能，
    实现标准 Provider 接口：Test-Installed, Get-Version, Invoke-Install
.NOTES
    依赖：BasicMethod/DevOps/PackageManager, BasicMethod/Core/Output, Core/ProviderBase
#>

. (Resolve-SiblingPath "..\Core\ProviderBase.ps1")

function Get-NpmProvider {
    return New-Provider -Type 'npm' -Name 'npm Provider' `
        -TestInstalled ${function:Test-NpmProviderInstalled} `
        -GetVersion ${function:Get-NpmProviderVersion} `
        -InvokeInstall ${function:Invoke-NpmProviderInstall}
}

function Test-NpmProviderInstalled {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Item,
        [string]$Target = "local",
        [string]$ContainerName = "",
        [scriptblock]$Executor = $null
    )

    $packageName = Test-ProviderSource -Item $Item -FieldName "source" -FieldDescription "包名"
    if (-not $packageName) {
        return $false
    }

    $Executor = Get-ProviderExecutor -Target $Target -ContainerName $ContainerName -Executor $Executor

    try {
        $globalInstall = if ($Item.global -eq $true) { $true } else { $false }
        $result = Test-NpmPackageInstalled -PackageName $packageName -Global:$globalInstall -Executor $Executor
        
        Write-ProviderDetection -Name $packageName -Installed $result
        
        return $result
    } catch {
        Write-Warn "检测失败: $_"
        return $false
    }
}

function Get-NpmProviderVersion {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Item,
        [string]$Target = "local",
        [string]$ContainerName = "",
        [scriptblock]$Executor = $null
    )

    $packageName = Test-ProviderSource -Item $Item -FieldName "source" -FieldDescription "包名"
    if (-not $packageName) {
        return $null
    }

    $Executor = Get-ProviderExecutor -Target $Target -ContainerName $ContainerName -Executor $Executor

    try {
        $globalInstall = if ($Item.global -eq $true) { $true } else { $false }
        $version = Get-NpmPackageVersion -PackageName $packageName -Global:$globalInstall -Executor $Executor
        return $version
    } catch {
        return $null
    }
}

function Invoke-NpmProviderInstall {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Item,
        [string]$Target = "local",
        [string]$ContainerName = "",
        [scriptblock]$Executor = $null
    )

    $packageName = Test-ProviderSource -Item $Item -FieldName "source" -FieldDescription "包名"
    if (-not $packageName) {
        Write-Error "缺少包名（source 字段）"
        return $false
    }

    $Executor = Get-ProviderExecutor -Target $Target -ContainerName $ContainerName -Executor $Executor

    try {
        $globalInstall = if ($Item.global -eq $true) { $true } else { $false }
        $version = if ($Item.version) { $Item.version } else { "latest" }

        Write-Info "npm 安装: $packageName@$version"
        
        if ($globalInstall) {
            Write-Host "  💡 全局安装模式" -ForegroundColor DarkGray
        }

        $fullPackage = if ($version -eq "latest") { $packageName } else { "$packageName@$version" }
        $result = Invoke-NpmInstall -Package $fullPackage -Global:$globalInstall -Executor $Executor

        if ($result) {
            Write-Success "npm 安装成功: $packageName"
        } else {
            Write-Error "npm 安装失败: $packageName"
        }

        return $result
    } catch {
        Write-Error "npm 安装异常: $_"
        return $false
    }
}

if ($MyInvocation.MyCommand.Source -eq $null) {
    Export-ModuleMember -Function @('Get-NpmProvider', 'Test-NpmProviderInstalled', 'Get-NpmProviderVersion', 'Invoke-NpmProviderInstall')
}