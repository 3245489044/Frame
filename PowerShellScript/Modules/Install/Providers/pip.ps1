# @requires: BasicMethod/DevOps/PackageManager, BasicMethod/Core/Output, Core/ProviderBase
# @version: 1.0.0
# @author: WorkFlowPrj
# @category: install-provider
# @alias: pip-provider
# @exports: Get-PipProvider, Test-PipProviderInstalled, Get-PipProviderVersion, Invoke-PipProviderInstall

<#
.SYNOPSIS
    pip 包安装 Provider
.DESCRIPTION
    包装 BasicMethod/DevOps/PackageManager 的 pip 相关功能，
    实现标准 Provider 接口：Test-Installed, Get-Version, Invoke-Install
.NOTES
    依赖：BasicMethod/DevOps/PackageManager, BasicMethod/Core/Output, Core/ProviderBase
#>

. (Resolve-SiblingPath "..\Core\ProviderBase.ps1")

function Get-PipProvider {
    return New-Provider -Type 'pip' -Name 'pip Provider' `
        -TestInstalled ${function:Test-PipProviderInstalled} `
        -GetVersion ${function:Get-PipProviderVersion} `
        -InvokeInstall ${function:Invoke-PipProviderInstall}
}

function Test-PipProviderInstalled {
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
        $result = Test-PipPackageInstalled -PackageName $packageName -Executor $Executor
        
        Write-ProviderDetection -Name $packageName -Installed $result
        
        return $result
    } catch {
        Write-Warn "检测失败: $_"
        return $false
    }
}

function Get-PipProviderVersion {
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
        $version = Get-PipPackageVersion -PackageName $packageName -Executor $Executor
        return $version
    } catch {
        return $null
    }
}

function Invoke-PipProviderInstall {
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
        $version = if ($Item.version) { $Item.version } else { "latest" }

        Write-Info "pip 安装: $packageName@$version"

        $fullPackage = if ($version -eq "latest") { $packageName } else { "$packageName==$version" }
        $result = Invoke-PipInstall -Package $fullPackage -Executor $Executor

        if ($result) {
            Write-Success "pip 安装成功: $packageName"
        } else {
            Write-Error "pip 安装失败: $packageName"
        }

        return $result
    } catch {
        Write-Error "pip 安装异常: $_"
        return $false
    }
}

if ($MyInvocation.MyCommand.Source -eq $null) {
    Export-ModuleMember -Function @('Get-PipProvider', 'Test-PipProviderInstalled', 'Get-PipProviderVersion', 'Invoke-PipProviderInstall')
}