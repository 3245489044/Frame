# @requires: Core/NativeRunner
# @version: 1.0.0
# @author: WorkFlowPrj
# @category: env-maintenance
# @alias: env-maintenance-python-verify
# @exports: Test-EnvPythonExpression, Get-EnvPythonVersion

<#
.SYNOPSIS
    Python 断言器 —— 用 `python -c` 把“装好了”升级成“真的能用”。
#>

function Test-EnvPythonExpression {
    <#
    .SYNOPSIS
        执行一段 Python 断言代码；ExitCode=0 即通过。
    .PARAMETER Code
        单行或分号分隔的 Python 代码（避免多行引号问题）。
    #>
    param(
        [Parameter(Mandatory = $true)][string]$Python,
        [Parameter(Mandatory = $true)][string]$Code,
        [int]$TimeoutSeconds = 120,
        [string]$LogPath = '',
        [switch]$Quiet
    )

    if (-not (Test-Path -LiteralPath $Python)) {
        return [pscustomobject]@{ Ok = $false; Reason = "python 不存在: $Python"; Output = '' }
    }

    $result = Invoke-EnvNativeCommand -FilePath $Python -ArgList @('-c', $Code) -TimeoutSeconds $TimeoutSeconds `
        -LogPath $LogPath -Environment @{ 'PYTHONIOENCODING' = 'utf-8' } -Quiet:$Quiet

    $ok = ($result.ExitCode -eq 0 -and -not $result.TimedOut)
    return [pscustomobject]@{
        Ok     = $ok
        Reason = $(if ($ok) { '' } else { $result.Reason })
        Output = $result.Output
    }
}

function Get-EnvPythonVersion {
    param(
        [Parameter(Mandatory = $true)][string]$Python,
        [object]$Manifest = $null
    )

    if (-not (Test-Path -LiteralPath $Python)) { return $null }
    $logPath = Get-EnvPipLogPath -EnvName 'probe' -Tag 'python-version' -Manifest $Manifest
    $result = Invoke-EnvNativeCommand -FilePath $Python -ArgList @('-c', 'import sys;print(sys.version.split()[0])') `
        -TimeoutSeconds 60 -LogPath $logPath -Quiet
    if ($result.ExitCode -ne 0) { return $null }
    return ($result.Output -split "`r?`n" | Where-Object { $_.Trim() } | Select-Object -First 1)
}
