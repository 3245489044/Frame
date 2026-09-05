# @requires: BasicMethod/Core/Output, BasicMethod/Core/Path, Core/ProviderBase
# @version: 1.0.0
# @author: WorkFlowPrj
# @category: install-provider
# @alias: git-clone-provider
# @exports: Get-GitCloneProvider, Test-GitCloneInstalled, Get-GitCloneVersion, Invoke-GitCloneInstall

<#
.SYNOPSIS
    Git Clone 安装 Provider
.DESCRIPTION
    支持从 Git 仓库克隆代码并执行构建命令，
    实现标准 Provider 接口：Test-Installed, Get-Version, Invoke-Install
.NOTES
    依赖：BasicMethod/Core/Output, BasicMethod/Core/Path, Core/ProviderBase
#>

. (Resolve-SiblingPath "..\Core\ProviderBase.ps1")

function Get-GitCloneProvider {
    return New-Provider -Type 'git-clone' -Name 'Git Clone Provider' `
        -TestInstalled ${function:Test-GitCloneInstalled} `
        -GetVersion ${function:Get-GitCloneVersion} `
        -InvokeInstall ${function:Invoke-GitCloneInstall}
}

function Test-GitCloneInstalled {
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
            Write-ProviderDetection -Name $Item.name -Installed $false
            return $false
        }

        $gitDirExists = & $Executor "Test-Path '$installDir/.git'"
        if ($gitDirExists) {
            Write-ProviderDetection -Name $Item.name -Installed $true
            return $true
        }

        $requiredFiles = $Item.requiredFiles
        if ($requiredFiles -and $requiredFiles.Count -gt 0) {
            $allExists = $true
            foreach ($file in $requiredFiles) {
                $fullPath = if ([System.IO.Path]::IsPathRooted($file)) { $file } else { Join-Path $installDir $file }
                $exists = & $Executor "Test-Path '$fullPath'"
                if (-not $exists) {
                    $allExists = $false
                    break
                }
            }
            if ($allExists) {
                Write-ProviderDetection -Name $Item.name -Installed $true
                return $true
            }
        }

        Write-ProviderDetection -Name $Item.name -Installed $false
        return $false
    } catch {
        Write-Warning "检测失败: $_"
        return $false
    }
}

function Get-GitCloneVersion {
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
        $result = & $Executor "cd '$installDir' && git describe --tags --always 2>&1"
        if ($LASTEXITCODE -eq 0 -and $result) {
            return $result.Trim()
        }

        $result = & $Executor "cd '$installDir' && git rev-parse --short HEAD 2>&1"
        if ($LASTEXITCODE -eq 0 -and $result) {
            return "commit $($result.Trim())"
        }

        return $null
    } catch {
        return $null
    }
}

function Invoke-GitCloneInstall {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Item,
        [string]$Target = "local",
        [string]$ContainerName = "",
        [scriptblock]$Executor = $null
    )

    $repository = Test-ProviderSource -Item $Item -FieldName "source" -FieldDescription "仓库地址"
    $installDir = Test-ProviderSource -Item $Item -FieldName "installDir" -FieldDescription "安装目录"

    if (-not $repository) {
        Write-Error "缺少仓库地址（source 字段）"
        return $false
    }
    if (-not $installDir) {
        Write-Error "缺少安装目录（installDir 字段）"
        return $false
    }

    $Executor = Get-ProviderExecutor -Target $Target -ContainerName $ContainerName -Executor $Executor

    try {
        $branch = if ($Item.branch) { $Item.branch } else { "main" }
        $buildCommand = if ($Item.buildCommand) { $Item.buildCommand } else { "" }

        Write-Info "Git Clone 安装: $repository"
        Write-Info "分支: $branch"
        Write-Info "目标目录: $installDir"

        Write-Hint "清理旧目录..."
        if ($Target -eq "docker") {
            & $Executor "rm -rf '$installDir'"
        } else {
            if (Test-Path $installDir) {
                Remove-Item $installDir -Recurse -Force
            }
        }

        Write-Hint "克隆仓库..."
        $result = & $Executor "git clone --branch $branch '$repository' '$installDir' 2>&1"
        if ($LASTEXITCODE -ne 0) {
            Write-Error "克隆失败: $result"
            return $false
        }
        Write-Hint "克隆完成"

        if ($buildCommand) {
            Write-Hint "执行构建命令: $buildCommand"
            $result = & $Executor "cd '$installDir' && $buildCommand 2>&1"
            if ($LASTEXITCODE -ne 0) {
                Write-Error "构建失败: $result"
                return $false
            }
            Write-Hint "构建完成"
        }

        Write-Success "Git Clone 安装成功: $($Item.name)"

        Set-PathEntries -PathEntries $Item.pathEntries -BaseDir $installDir -Target $Target -Executor $Executor

        return $true
    } catch {
        Write-Error "Git Clone 安装异常: $_"
        return $false
    }
}