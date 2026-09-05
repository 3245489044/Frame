# @requires: BasicMethod/Core/Output, BasicMethod/Core/Path, Core/ProviderBase
# @version: 1.0.0
# @author: WorkFlowPrj
# @category: install-provider
# @alias: local-provider
# @exports: Get-LocalProvider, Test-LocalInstalled, Get-LocalVersion, Invoke-LocalInstall

<#
.SYNOPSIS
    本地项目验证 Provider
.DESCRIPTION
    验证本地项目目录是否存在，适用于已存在的本地项目，
    实现标准 Provider 接口：Test-Installed, Get-Version, Invoke-Install
.NOTES
    依赖：BasicMethod/Core/Output, BasicMethod/Core/Path, Core/ProviderBase
#>

. (Resolve-SiblingPath "..\Core\ProviderBase.ps1")

function Get-LocalProvider {
    return New-Provider -Type 'local' -Name 'Local Provider' `
        -TestInstalled ${function:Test-LocalInstalled} `
        -GetVersion ${function:Get-LocalVersion} `
        -InvokeInstall ${function:Invoke-LocalInstall}
}

function Test-LocalInstalled {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Item,
        [string]$Target = "local",
        [string]$ContainerName = "",
        [scriptblock]$Executor = $null
    )

    $localPath = Test-ProviderSource -Item $Item -FieldName "source" -FieldDescription "本地路径"
    if (-not $localPath) {
        return $false
    }

    $Executor = Get-ProviderExecutor -Target $Target -ContainerName $ContainerName -Executor $Executor

    try {
        $exists = & $Executor "Test-Path '$localPath'"
        
        Write-ProviderDetection -Name $Item.name -Installed $exists
        
        return $exists
    } catch {
        Write-Warning "检测失败: $_"
        return $false
    }
}

function Get-LocalVersion {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Item,
        [string]$Target = "local",
        [string]$ContainerName = "",
        [scriptblock]$Executor = $null
    )

    $localPath = Test-ProviderSource -Item $Item -FieldName "source" -FieldDescription "本地路径"
    if (-not $localPath) {
        return $null
    }

    $Executor = Get-ProviderExecutor -Target $Target -ContainerName $ContainerName -Executor $Executor

    try {
        if ($Item.version) {
            return $Item.version
        }

        $versionFile = Join-Path $localPath "VERSION"
        $versionFileExists = & $Executor "Test-Path '$versionFile'"
        if ($versionFileExists) {
            $version = & $Executor "Get-Content '$versionFile' -Raw"
            if ($version) {
                return $version.Trim()
            }
        }

        $packageJsonPath = Join-Path $localPath "package.json"
        $packageJsonExists = & $Executor "Test-Path '$packageJsonPath'"
        if ($packageJsonExists) {
            $content = & $Executor "Get-Content '$packageJsonPath' -Raw"
            if ($content) {
                try {
                    $json = $content | ConvertFrom-Json
                    if ($json.version) {
                        return $json.version
                    }
                } catch {}
            }
        }

        return $null
    } catch {
        return $null
    }
}

function Invoke-LocalInstall {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Item,
        [string]$Target = "local",
        [string]$ContainerName = "",
        [scriptblock]$Executor = $null
    )

    $localPath = Test-ProviderSource -Item $Item -FieldName "source" -FieldDescription "本地路径"
    if (-not $localPath) {
        Write-Error "缺少本地路径（source 字段）"
        return $false
    }

    $Executor = Get-ProviderExecutor -Target $Target -ContainerName $ContainerName -Executor $Executor

    try {
        $exists = & $Executor "Test-Path '$localPath'"
        if ($exists) {
            Write-Success "本地项目目录存在: $localPath"
        } else {
            Write-Error "本地项目目录不存在: $localPath"
            Write-Hint "请确保项目已挂载或创建到指定位置"
            return $false
        }

        if ($Item.buildCommand) {
            Write-Hint "执行构建命令: $($Item.buildCommand)"
            $result = & $Executor "cd '$localPath' && $($Item.buildCommand) 2>&1"
            if ($LASTEXITCODE -ne 0) {
                Write-Error "构建失败: $result"
                return $false
            }
            Write-Hint "构建完成"
        }

        Set-PathEntries -PathEntries $Item.pathEntries -BaseDir $localPath -Target $Target -Executor $Executor

        return $true
    } catch {
        Write-Error "本地项目验证异常: $_"
        return $false
    }
}