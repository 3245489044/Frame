# @requires: Core/EnvPath, Core/NativeRunner
# @version: 1.0.0
# @author: WorkFlowPrj
# @category: env-maintenance
# @alias: env-maintenance-pip
# @exports: Invoke-EnvPip, Invoke-EnvPipInstall, Invoke-EnvPipRequirements, Get-EnvPipVersion, Test-EnvPipPackage, Get-EnvPipLogPath

<#
.SYNOPSIS
    统一 pip 调用器 —— 把“pip 卡死”这类环境问题收敛成固定口径。
.DESCRIPTION
    固定参数：--disable-pip-version-check --no-input --timeout 60 --retries 2 --no-cache-dir
    PIP_CACHE_DIR 指向被占用/慢盘时（本机 D:\DevEnv\pip\cache 即如此）pip 会长期不返回；
    因此默认强制 --no-cache-dir，需缓存时由清单 `cache: true` 显式开启。
#>

function Get-EnvPipLogPath {
    param(
        [string]$EnvName,
        [string]$Tag,
        [object]$Manifest = $null,
        [string]$LogRoot = ''
    )

    if (-not $LogRoot) { $LogRoot = Get-EnvLogRoot -Manifest $Manifest }
    if (-not $LogRoot) { return '' }
    $safeEnv = $EnvName -replace '[^A-Za-z0-9_\.\-]', '_'
    $safeTag = $Tag -replace '[^A-Za-z0-9_\.\-]', '_'
    return (Join-Path $LogRoot ("{0}-{1}-{2}.log" -f $safeEnv, $safeTag, (Get-Date -Format 'yyyyMMdd-HHmmss')))
}

function Invoke-EnvPip {
    <#
    .SYNOPSIS
        执行一段 pip 参数；返回 Invoke-EnvNativeCommand 的统一结果对象。
    .PARAMETER PipArgs
        例如 @('install','torch==2.10.0') 或 @('show','torch')。
    .PARAMETER Index
        主索引（torch 用 cu130 索引）；非空时自动附加 pypi 作为 extra-index 供依赖解析。
    .PARAMETER UseCache
        显式允许 pip 缓存（默认禁止）。
    #>
    param(
        [Parameter(Mandatory = $true)][string]$Python,
        [string[]]$PipArgs = @(),
        [string]$Index = '',
        [int]$TimeoutSeconds = 900,
        [string]$LogPath = '',
        [switch]$UseCache,
        [switch]$Quiet
    )

    $arguments = @('-m', 'pip') + $PipArgs
    $arguments += @('--disable-pip-version-check', '--no-input', '--timeout', '60', '--retries', '2')
    if ($Index) {
        $arguments += @('--index-url', $Index)
        $arguments += @('--extra-index-url', 'https://pypi.org/simple')
    }
    if (-not $UseCache) { $arguments += '--no-cache-dir' }

    $environment = @{
        'PYTHONIOENCODING' = 'utf-8'
        'PYTHONUNBUFFERED' = '1'
        'PIP_DISABLE_PIP_VERSION_CHECK' = '1'
    }

    return (Invoke-EnvNativeCommand -FilePath $Python -ArgList $arguments -TimeoutSeconds $TimeoutSeconds `
            -LogPath $LogPath -Environment $environment -Quiet:$Quiet)
}

function Invoke-EnvPipInstall {
    param(
        [Parameter(Mandatory = $true)][string]$Python,
        [string[]]$Packages = @(),
        [string]$Index = '',
        [int]$TimeoutSeconds = 900,
        [string]$LogPath = '',
        [switch]$UseCache,
        [switch]$Quiet
    )

    $pipArgs = @('install') + $Packages
    return (Invoke-EnvPip -Python $Python -PipArgs $pipArgs -Index $Index -TimeoutSeconds $TimeoutSeconds `
            -LogPath $LogPath -UseCache:$UseCache -Quiet:$Quiet)
}

function Invoke-EnvPipRequirements {
    param(
        [Parameter(Mandatory = $true)][string]$Python,
        [Parameter(Mandatory = $true)][string]$Requirements,
        [string]$Index = '',
        [int]$TimeoutSeconds = 1800,
        [string]$LogPath = '',
        [switch]$UseCache,
        [switch]$Quiet
    )

    $pipArgs = @('install', '-r', $Requirements)
    return (Invoke-EnvPip -Python $Python -PipArgs $pipArgs -Index $Index -TimeoutSeconds $TimeoutSeconds `
            -LogPath $LogPath -UseCache:$UseCache -Quiet:$Quiet)
}

function Get-EnvPipVersion {
    <#
    .SYNOPSIS
        读取已安装包版本；未安装返回 $null。
    #>
    param(
        [Parameter(Mandatory = $true)][string]$Python,
        [Parameter(Mandatory = $true)][string]$Package,
        [object]$Manifest = $null
    )

    if (-not (Test-Path -LiteralPath $Python)) { return $null }
    $logPath = Get-EnvPipLogPath -EnvName 'probe' -Tag ("show-$Package") -Manifest $Manifest
    $result = Invoke-EnvPip -Python $Python -PipArgs @('show', $Package) -TimeoutSeconds 120 -LogPath $logPath -Quiet
    if ($result.ExitCode -ne 0) { return $null }

    foreach ($line in ($result.Output -split "`r?`n")) {
        if ($line -match '^\s*Version:\s*(.+)$') { return $matches[1].Trim() }
    }
    return $null
}

function Test-EnvPipPackage {
    param(
        [Parameter(Mandatory = $true)][string]$Python,
        [Parameter(Mandatory = $true)][string]$Package,
        [object]$Manifest = $null
    )
    return ((Get-EnvPipVersion -Python $Python -Package $Package -Manifest $Manifest) -ne $null)
}
