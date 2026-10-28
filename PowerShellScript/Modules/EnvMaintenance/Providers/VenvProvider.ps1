# @requires: Core/EnvProviders
# @version: 1.0.0
# @author: WorkFlowPrj
# @category: env-maintenance
# @alias: env-maintenance-provider-venv
# @exports: Get-VenvProvider

<#
.SYNOPSIS
    venv Provider —— 创建/探测 Python 虚拟环境。
#>

function Get-VenvProvider {
    return (New-EnvProvider -Type 'venv' -Name 'venv Provider' `
        -Test ${function:Test-VenvItem} -Install ${function:Install-VenvItem})
}

function Test-VenvItem {
    param($Item, $Manifest)

    $venv = [string]$Item.venv
    if (-not $venv) {
        return [pscustomobject]@{ Installed = $false; Outdated = $false; Version = $null; Reason = '未声明 venv 路径' }
    }

    $python = Resolve-VenvPython -VenvDir $venv
    if (-not (Test-Path -LiteralPath $python)) {
        return [pscustomobject]@{ Installed = $false; Outdated = $false; Version = $null; Reason = "venv 未创建: $venv" }
    }

    $version = Get-EnvPythonVersion -Python $python -Manifest $Manifest
    return [pscustomobject]@{ Installed = $true; Outdated = $false; Version = $version; Reason = '' }
}

function Install-VenvItem {
    param($Item, $Manifest)

    $venv = [string]$Item.venv
    if (-not $venv) { Write-EnvFail 'venv 项缺少 venv 路径'; return $false }

    $base = Get-EnvManifestPython -Manifest $Manifest
    Write-EnvStep "创建 venv: $venv（解释器 $base）"

    $logPath = Get-EnvItemLogPath -Item $Item -Manifest $Manifest -Tag 'venv'
    $result = Invoke-EnvNativeCommand -FilePath $base -ArgList @('-m', 'venv', $venv) `
        -TimeoutSeconds 300 -LogPath $logPath -Environment @{ 'PYTHONIOENCODING' = 'utf-8' } -Quiet
    if ($result.ExitCode -ne 0) {
        Write-EnvFail "venv 创建失败: $($result.Reason)"
        return $false
    }

    $created = Test-Path -LiteralPath (Resolve-VenvPython -VenvDir $venv)
    if ($created) { Write-EnvOk "venv 已创建: $venv" } else { Write-EnvFail "venv 创建后未找到 python.exe: $venv" }
    return $created
}
