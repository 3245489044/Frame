# @requires: BasicMethod/Core/Output, BasicMethod/Core/Path, Core/ProviderBase
# @version: 1.0.0
# @author: WorkFlowPrj
# @category: install-provider
# @alias: msi-provider
# @exports: Get-MsiProvider, Test-MsiInstalled, Get-MsiVersion, Invoke-MsiInstall

<#
.SYNOPSIS
    MSI 安装包安装 Provider
.DESCRIPTION
    支持下载并安装 MSI 安装包，
    实现标准 Provider 接口：Test-Installed, Get-Version, Invoke-Install
.NOTES
    依赖：BasicMethod/Core/Output, BasicMethod/Core/Path, Core/ProviderBase
#>

. (Resolve-SiblingPath "..\Core\ProviderBase.ps1")

function Get-MsiProvider {
    return New-Provider -Type 'msi' -Name 'MSI Provider' `
        -TestInstalled ${function:Test-MsiInstalled} `
        -GetVersion ${function:Get-MsiVersion} `
        -InvokeInstall ${function:Invoke-MsiInstall}
}

function Test-MsiInstalled {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Item,
        [string]$Target = "local",
        [string]$ContainerName = "",
        [scriptblock]$Executor = $null
    )

    $Executor = Get-ProviderExecutor -Target $Target -ContainerName $ContainerName -Executor $Executor

    try {
        if ($Item.requiredFiles -and $Item.requiredFiles.Count -gt 0) {
            foreach ($file in $Item.requiredFiles) {
                $exists = & $Executor "Test-Path '$file'"
                if (-not $exists) {
                    Write-Hint "检测: $file 不存在"
                    return $false
                }
            }
            Write-ProviderDetection -Name $Item.name -Installed $true
            return $true
        }

        if ($Item.productCode) {
            if ($Target -eq "docker") {
                Write-Warning "MSI 安装包不支持 Docker 环境"
                Write-ProviderDetection -Name $Item.name -Installed $false
                return $false
            } else {
                $result = & $Executor "Get-WmiObject -Class Win32_Product -Filter 'Name LIKE ''%$($Item.productCode)%''' 2>&1"
                $installed = ($LASTEXITCODE -eq 0 -and $result)
            }
            Write-ProviderDetection -Name $Item.name -Installed $installed
            return $installed
        }

        Write-ProviderDetection -Name $Item.name -Installed $false
        return $false
    } catch {
        Write-Warning "检测失败: $_"
        return $false
    }
}

function Get-MsiVersion {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Item,
        [string]$Target = "local",
        [string]$ContainerName = "",
        [scriptblock]$Executor = $null
    )

    if ($Item.version) {
        return $Item.version
    }

    $Executor = Get-ProviderExecutor -Target $Target -ContainerName $ContainerName -Executor $Executor

    try {
        if ($Item.binaryName) {
            $result = & $Executor "$($Item.binaryName) --version 2>&1"
            if ($LASTEXITCODE -eq 0 -and $result) {
                $versionMatch = [regex]::Match($result, '(\d+\.\d+\.\d+)')
                if ($versionMatch.Success) {
                    return $versionMatch.Groups[1].Value
                }
            }
        }

        return $null
    } catch {
        return $null
    }
}

function Invoke-MsiInstall {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Item,
        [string]$Target = "local",
        [string]$ContainerName = "",
        [scriptblock]$Executor = $null
    )

    $url = Test-ProviderSource -Item $Item -FieldName "source" -FieldDescription "下载 URL"
    if (-not $url) {
        Write-Error "缺少下载 URL（source 字段）"
        return $false
    }

    $Executor = Get-ProviderExecutor -Target $Target -ContainerName $ContainerName -Executor $Executor

    try {
        if ($Target -eq "docker") {
            Write-Error "MSI 安装包不支持 Docker 环境"
            return $false
        }

        Write-Info "MSI 安装: $url"

        $tempPath = Join-Path $env:TEMP "$($Item.name).msi"

        Write-Hint "下载 MSI..."
        Invoke-WebRequest -Uri $url -OutFile $tempPath -UseBasicParsing -ErrorAction Stop
        Write-Hint "下载完成: $([math]::Round((Get-Item $tempPath).Length/1MB,1)) MB"

        Write-Hint "安装 MSI..."
        $arguments = "/i `"$tempPath`" /quiet /norestart"
        $process = Start-Process -FilePath "msiexec.exe" -ArgumentList $arguments -Wait -PassThru -NoNewWindow
        if ($process.ExitCode -eq 0 -or $process.ExitCode -eq 3010) {
            Write-Hint "安装成功"
        } else {
            Write-Error "安装失败，退出码: $($process.ExitCode)"
            return $false
        }

        Remove-Item $tempPath -Force -ErrorAction SilentlyContinue
        Write-Hint "清理安装包"

        Write-Success "MSI 安装成功: $($Item.name)"

        Set-PathEntries -PathEntries $Item.pathEntries -BaseDir "" -Target $Target -Executor $Executor

        return $true
    } catch {
        Write-Error "MSI 安装异常: $_"
        return $false
    }
}