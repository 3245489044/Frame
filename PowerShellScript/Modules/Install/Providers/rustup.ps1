# @requires: BasicMethod/Core/Output, BasicMethod/Core/Path, Core/ProviderBase
# @version: 1.0.0
# @author: WorkFlowPrj
# @category: install-provider
# @alias: rustup-provider
# @exports: Get-RustupProvider, Test-RustupInstalled, Get-RustupVersion, Invoke-RustupInstall

<#
.SYNOPSIS
    Rustup (Rust 工具链) 安装 Provider
.DESCRIPTION
    支持设置 RUSTUP_HOME/CARGO_HOME 环境变量并安装 Rust 工具链，
    实现标准 Provider 接口：Test-Installed, Get-Version, Invoke-Install
.NOTES
    依赖：BasicMethod/Core/Output, BasicMethod/Core/Path, Core/ProviderBase
#>

. (Resolve-SiblingPath "..\Core\ProviderBase.ps1")

function Get-RustupProvider {
    return New-Provider -Type 'rustup' -Name 'Rustup Provider' `
        -TestInstalled ${function:Test-RustupInstalled} `
        -GetVersion ${function:Get-RustupVersion} `
        -InvokeInstall ${function:Invoke-RustupInstall}
}

function Test-RustupInstalled {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Item,
        [string]$Target = "local",
        [string]$ContainerName = "",
        [scriptblock]$Executor = $null
    )

    $Executor = Get-ProviderExecutor -Target $Target -ContainerName $ContainerName -Executor $Executor

    try {
        $result = & $Executor "rustc --version 2>&1"
        $installed = ($LASTEXITCODE -eq 0)

        Write-ProviderDetection -Name $Item.name -Installed $installed

        return $installed
    } catch {
        Write-Warning "检测失败: $_"
        return $false
    }
}

function Get-RustupVersion {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Item,
        [string]$Target = "local",
        [string]$ContainerName = "",
        [scriptblock]$Executor = $null
    )

    $Executor = Get-ProviderExecutor -Target $Target -ContainerName $ContainerName -Executor $Executor

    try {
        $result = & $Executor "rustc --version 2>&1"
        if ($LASTEXITCODE -eq 0 -and $result) {
            $versionMatch = [regex]::Match($result, 'rustc (\d+\.\d+\.\d+)')
            if ($versionMatch.Success) {
                return $versionMatch.Groups[1].Value
            }
        }
        return $null
    } catch {
        return $null
    }
}

function Invoke-RustupInstall {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Item,
        [string]$Target = "local",
        [string]$ContainerName = "",
        [scriptblock]$Executor = $null
    )

    $installDir = Test-ProviderSource -Item $Item -FieldName "installDir" -FieldDescription "安装目录"
    if (-not $installDir) {
        $installDir = Join-Path $env:USERPROFILE ".rustup"
    }

    $Executor = Get-ProviderExecutor -Target $Target -ContainerName $ContainerName -Executor $Executor

    try {
        $rustupHome = Join-Path $installDir ".rustup"
        $cargoHome = Join-Path $installDir ".cargo"

        Write-Info "Rustup 安装"
        Write-Info "安装目录: $installDir"

        Write-Hint "创建目标目录..."
        if (!(Test-Path $rustupHome)) {
            New-Item $rustupHome -ItemType Directory -Force | Out-Null
        }
        if (!(Test-Path $cargoHome)) {
            New-Item $cargoHome -ItemType Directory -Force | Out-Null
        }
        Write-Hint "目录已创建"

        $envVars = @{
            "RUSTUP_HOME" = $rustupHome
            "CARGO_HOME"  = $cargoHome
        }
        Set-EnvironmentVariables -EnvVars $envVars -InstallDir $installDir -Target $Target -Executor $Executor

        $pathEntries = @(
            (Join-Path $cargoHome "bin"),
            (Join-Path $rustupHome "bin")
        )
        Set-PathEntries -PathEntries $pathEntries -BaseDir $installDir -Target $Target -Executor $Executor

        if ($Target -eq "docker") {
            & $Executor "source /root/.bashrc"
            Write-Hint "环境变量已加载"

            Write-Hint "下载并运行 rustup-init..."
            $result = & $Executor "curl --proto '=https' --tlsv1.2 -sSf https://sh.rustup.rs | sh -s -- -y 2>&1"
            if ($LASTEXITCODE -ne 0) {
                Write-Error "rustup 安装失败"
                return $false
            }
            Write-Hint "rustup 安装完成"
        } else {
            Write-Hint "下载安装器..."
            $rustupUrl = "https://sh.rustup.rs"
            $installerPath = Join-Path $env:TEMP "rustup-init.exe"
            Invoke-WebRequest -Uri $rustupUrl -OutFile $installerPath -UseBasicParsing -ErrorAction Stop
            Write-Hint "下载完成"

            Write-Hint "运行安装器..."
            $process = Start-Process -FilePath $installerPath -ArgumentList "-y" -Wait -PassThru -NoNewWindow
            if ($process.ExitCode -eq 0) {
                Write-Hint "安装成功"
            } else {
                Write-Error "安装失败，退出码: $($process.ExitCode)"
                return $false
            }

            Remove-Item $installerPath -Force -ErrorAction SilentlyContinue
            Write-Hint "清理安装器"
        }

        Write-Hint "验证安装..."
        $versionResult = & $Executor "rustc --version 2>&1"
        if ($LASTEXITCODE -eq 0) {
            Write-Success "Rust 安装成功: $versionResult"
        } else {
            Write-Warning "验证失败，可能需要重启终端"
        }

        return $true
    } catch {
        Write-Error "Rustup 安装异常: $_"
        return $false
    }
}