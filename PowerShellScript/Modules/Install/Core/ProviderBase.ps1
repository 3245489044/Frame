# @requires: Path Framework, BasicMethod/Core/Output
# @version: 1.1.0
# @author: WorkFlowPrj
# @category: install
# @alias: provider-base
# @exports: Get-ProviderExecutor, Test-ProviderSource, Write-ProviderDetection, Set-PathEntries, Set-EnvironmentVariables

function Get-ProviderExecutor {
    param(
        [string]$Target = "local",
        [string]$ContainerName = "",
        [scriptblock]$Executor = $null
    )

    if (-not $Executor) {
        $Executor = Get-Executor -Target $Target -ContainerName $ContainerName
    }

    return $Executor
}

function Test-ProviderSource {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Item,
        [string]$FieldName = "source",
        [string]$FieldDescription = "源"
    )

    $value = $Item.$FieldName
    if (-not $value) {
        Write-Warn "缺少$FieldDescription（$FieldName 字段）"
        return $null
    }

    return $value
}

function Write-ProviderDetection {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [Parameter(Mandatory = $true)]
        [bool]$Installed
    )

    if ($Installed) {
        Write-Host "  💡 检测: $Name 已安装" -ForegroundColor DarkGray
    } else {
        Write-Host "  💡 检测: $Name 未安装" -ForegroundColor DarkGray
    }
}

function New-Provider {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Type,
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [Parameter(Mandatory = $true)]
        [scriptblock]$TestInstalled,
        [Parameter(Mandatory = $true)]
        [scriptblock]$GetVersion,
        [Parameter(Mandatory = $true)]
        [scriptblock]$InvokeInstall
    )

    return @{
        'Test-Installed' = $TestInstalled
        'Get-Version'    = $GetVersion
        'Invoke-Install' = $InvokeInstall
        'Type'           = $Type
        'Name'           = $Name
    }
}

function Set-PathEntries {
    param(
        [array]$PathEntries,
        [string]$BaseDir,
        [string]$Target,
        [scriptblock]$Executor
    )
    if (-not $PathEntries -or $PathEntries.Count -eq 0) { return }
    Write-Info "配置 PATH 环境变量..."
    foreach ($entry in $PathEntries) {
        $fullPath = if ([System.IO.Path]::IsPathRooted($entry)) { $entry } else { Join-Path $BaseDir $entry }
        if ($Target -eq "docker") {
            & $Executor "echo 'export PATH=`$PATH:$fullPath' >> /root/.bashrc"
        } else {
            $currentPath = [Environment]::GetEnvironmentVariable("Path", "User")
            if ($currentPath -notlike "*$fullPath*") {
                [Environment]::SetEnvironmentVariable("Path", "$currentPath;$fullPath", "User")
                $env:Path = "$env:Path;$fullPath"
            }
        }
        Write-Hint "已添加: $fullPath"
    }
}

function Set-EnvironmentVariables {
    param(
        [object]$EnvVars,
        [string]$InstallDir,
        [string]$Target,
        [scriptblock]$Executor
    )
    if (-not $EnvVars) { return }
    Write-Info "配置环境变量..."
    foreach ($var in $EnvVars.PSObject.Properties) {
        $value = $var.Value
        if ($value -match '\$installDir|\$\{installDir\}') {
            $value = $value -replace '\$installDir|\$\{installDir\}', $InstallDir
        }
        if ($Target -eq "docker") {
            & $Executor "echo 'export $($var.Name)=$value' >> /root/.bashrc"
        } else {
            [Environment]::SetEnvironmentVariable($var.Name, $value, "User")
            Set-Item "Env:$($var.Name)" $value
        }
        Write-Hint "已设置: $($var.Name) = $value"
    }
}