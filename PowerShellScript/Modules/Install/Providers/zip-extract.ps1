# @requires: BasicMethod/Core/Output, BasicMethod/Core/Path, Core/ProviderBase
# @version: 1.0.0
# @author: WorkFlowPrj
# @category: install-provider
# @alias: zip-extract-provider
# @exports: Get-ZipExtractProvider, Test-ZipExtractInstalled, Get-ZipExtractVersion, Invoke-ZipExtractInstall

<#
.SYNOPSIS
    ZIP 下载解压安装 Provider
.DESCRIPTION
    支持从 URL 下载 ZIP 包并解压到指定目录，
    实现标准 Provider 接口：Test-Installed, Get-Version, Invoke-Install
.NOTES
    依赖：BasicMethod/Core/Output, BasicMethod/Core/Path, Core/ProviderBase
#>

. (Resolve-SiblingPath "..\Core\ProviderBase.ps1")

function Get-ZipExtractProvider {
    return New-Provider -Type 'zip-extract' -Name 'ZIP Extract Provider' `
        -TestInstalled ${function:Test-ZipExtractInstalled} `
        -GetVersion ${function:Get-ZipExtractVersion} `
        -InvokeInstall ${function:Invoke-ZipExtractInstall}
}

function Test-ZipExtractInstalled {
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
        $requiredFiles = $Item.requiredFiles
        if ($requiredFiles -and $requiredFiles.Count -gt 0) {
            foreach ($file in $requiredFiles) {
                $fullPath = if ([System.IO.Path]::IsPathRooted($file)) { $file } else { Join-Path $installDir $file }
                $exists = & $Executor "Test-Path '$fullPath'"
                if (-not $exists) {
                    Write-Hint "检测: $fullPath 不存在"
                    return $false
                }
            }
            Write-ProviderDetection -Name $Item.name -Installed $true
            return $true
        }

        $dirExists = & $Executor "Test-Path '$installDir'"
        if ($dirExists) {
            $hasFiles = & $Executor "(Get-ChildItem '$installDir' -Recurse -File -ErrorAction SilentlyContinue).Count -gt 0"
            if ($hasFiles) {
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

function Get-ZipExtractVersion {
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
        if ($Item.version) {
            return $Item.version
        }

        $versionFile = Join-Path $installDir "VERSION"
        $versionFileExists = & $Executor "Test-Path '$versionFile'"
        if ($versionFileExists) {
            $version = & $Executor "Get-Content '$versionFile' -Raw"
            if ($version) {
                return $version.Trim()
            }
        }

        $binaryName = $Item.binaryName
        if ($binaryName) {
            $binaryPath = Join-Path $installDir $binaryName
            $result = & $Executor "$binaryPath --version 2>&1"
            if ($result) {
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

function Invoke-ZipExtractInstall {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Item,
        [string]$Target = "local",
        [string]$ContainerName = "",
        [scriptblock]$Executor = $null
    )

    $url = Test-ProviderSource -Item $Item -FieldName "source" -FieldDescription "下载 URL"
    $installDir = Test-ProviderSource -Item $Item -FieldName "installDir" -FieldDescription "安装目录"

    if (-not $url) {
        Write-Error "缺少下载 URL（source 字段）"
        return $false
    }
    if (-not $installDir) {
        Write-Error "缺少安装目录（installDir 字段）"
        return $false
    }

    $Executor = Get-ProviderExecutor -Target $Target -ContainerName $ContainerName -Executor $Executor

    try {
        $version = if ($Item.version) { $Item.version } else { "" }
        $extractRoot = if ($Item.extractRoot) { $Item.extractRoot } else { $null }

        Write-Info "ZIP 下载安装: $url"
        Write-Info "目标目录: $installDir"

        if ($Target -eq "docker") {
            $tempZipPath = "/tmp/$($Item.name).zip"
            $tempExtractDir = "/tmp/$($Item.name)-extract"
        } else {
            $tempZipPath = Join-Path $env:TEMP "$($Item.name).zip"
            $tempExtractDir = Join-Path $env:TEMP "$($Item.name)-extract"
        }

        if ($Target -eq "docker") {
            $result = & $Executor "curl -sSL -o '$tempZipPath' '$url'"
            if ($LASTEXITCODE -ne 0) {
                Write-Error "下载失败: $url"
                return $false
            }
            Write-Hint "下载完成"

            & $Executor "rm -rf '$tempExtractDir' && mkdir -p '$tempExtractDir'"
            $result = & $Executor "unzip -o '$tempZipPath' -d '$tempExtractDir'"
            if ($LASTEXITCODE -ne 0) {
                Write-Error "解压失败"
                return $false
            }
            Write-Hint "解压完成"

            & $Executor "rm -rf '$installDir' && mkdir -p '$installDir'"
            if ($extractRoot) {
                $result = & $Executor "mv '$tempExtractDir/$extractRoot/*' '$installDir/'"
            } else {
                $result = & $Executor "mv '$tempExtractDir/*' '$installDir/'"
            }
            if ($LASTEXITCODE -ne 0) {
                Write-Error "移动文件失败"
                return $false
            }

            & $Executor "rm -rf '$tempExtractDir' '$tempZipPath'"
        } else {
            Write-Hint "下载 $url ..."
            Invoke-WebRequest -Uri $url -OutFile $tempZipPath -UseBasicParsing -ErrorAction Stop
            Write-Hint "下载完成: $([math]::Round((Get-Item $tempZipPath).Length/1MB,1)) MB"

            if (Test-Path $tempExtractDir) {
                Remove-Item $tempExtractDir -Recurse -Force
            }
            Expand-Archive -Path $tempZipPath -DestinationPath $tempExtractDir -Force
            Write-Hint "解压完成"

            if (Test-Path $installDir) {
                Remove-Item $installDir -Recurse -Force
            }

            if ($extractRoot) {
                $sourceDir = Join-Path $tempExtractDir $extractRoot
                if (Test-Path $sourceDir) {
                    Move-Item "$sourceDir\*" $installDir -Force
                } else {
                    Move-Item "$tempExtractDir\*" $installDir -Force
                }
            } else {
                $firstDir = Get-ChildItem $tempExtractDir -Directory | Select-Object -First 1
                if ($firstDir) {
                    Move-Item "$($firstDir.FullName)\*" $installDir -Force
                } else {
                    Move-Item "$tempExtractDir\*" $installDir -Force
                }
            }

            Remove-Item $tempExtractDir -Recurse -Force
            Remove-Item $tempZipPath -Force
            Write-Hint "清理临时文件完成"
        }

        Write-Success "ZIP 安装成功: $($Item.name)"

        Set-PathEntries -PathEntries $Item.pathEntries -BaseDir $installDir -Target $Target -Executor $Executor
        Set-EnvironmentVariables -EnvVars $Item.envVars -InstallDir $installDir -Target $Target -Executor $Executor

        return $true
    } catch {
        Write-Error "ZIP 安装异常: $_"
        return $false
    }
}