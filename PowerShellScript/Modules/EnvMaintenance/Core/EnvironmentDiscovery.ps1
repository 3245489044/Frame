# @requires: Core/EnvPath
# @version: 1.0.0
# @author: WorkFlowPrj
# @category: env-maintenance
# @alias: env-maintenance-discovery
# @exports: Get-ProduceEnvironments, Get-ProduceEnvironment

<#
.SYNOPSIS
    环境发现 —— 从 Produce\Config\envs.json 读取运行期环境声明（不重复维护路径）。
.DESCRIPTION
    C# 侧 EnvHost 管启停/探活，本模块管依赖体检/修复/升级；两边共用同一份 envs.json。
#>

function Get-ProduceEnvironments {
    param([string]$ConfigRoot = '')

    if (-not $ConfigRoot) { $ConfigRoot = Get-ProduceConfigRoot }
    $path = Join-Path $ConfigRoot 'envs.json'
    $config = Read-JsonConfig -Path $path

    $result = @()
    foreach ($env in @($config.environments)) {
        if (-not $env -or -not $env.id) { continue }

        $launchFile = ''
        $launchArgs = @()
        $workDir = ''
        if ($env.launch) {
            $launchFile = Expand-EnvPath ([string]$env.launch.file)
            if ($env.launch.arguments) {
                $launchArgs = @($env.launch.arguments | ForEach-Object { [string]$_ })
            }
            $workDir = Expand-EnvPath ([string]$env.launch.workingDirectory)
        }

        $venvDir = ''
        if ($launchFile) { $venvDir = Get-VenvDirFromPython -PythonPath $launchFile }

        $python = ''
        if ($venvDir) { $python = Resolve-VenvPython -VenvDir $venvDir }

        $result += [pscustomobject]@{
            Id                = [string]$env.id
            Name              = $(if ($env.name) { [string]$env.name } else { [string]$env.id })
            Capability        = [string]$env.capability
            ProbeUrl          = [string]$env.probeUrl
            LaunchFile        = $launchFile
            LaunchArguments   = $launchArgs
            WorkingDirectory  = $workDir
            VenvDir           = $venvDir
            Python            = $python
            AutoStart         = [bool]$env.autoStart
        }
    }

    return $result
}

function Get-ProduceEnvironment {
    param([Parameter(Mandatory = $true)][string]$Id, [string]$ConfigRoot = '')

    $all = Get-ProduceEnvironments -ConfigRoot $ConfigRoot
    return ($all | Where-Object { $_.Id -ieq $Id } | Select-Object -First 1)
}
