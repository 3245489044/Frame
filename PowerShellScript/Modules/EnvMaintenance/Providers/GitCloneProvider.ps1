# @requires: Core/EnvProviders
# @version: 1.0.0
# @author: WorkFlowPrj
# @category: env-maintenance
# @alias: env-maintenance-provider-git
# @exports: Get-GitCloneProvider

<#
.SYNOPSIS
    git-clone Provider —— 仓库缺失时克隆；已存在时只在 -Force 语义下不动作（避免误更新代码）。
#>

function Get-GitCloneProvider {
    return (New-EnvProvider -Type 'git-clone' -Name 'git-clone Provider' `
        -Test ${function:Test-GitCloneItem} -Install ${function:Install-GitCloneItem} -Verify ${function:Verify-GitCloneItem})
}

function Test-GitCloneItem {
    param($Item, $Manifest)

    $directory = [string]$Item.directory
    if (-not $directory) { $directory = [string]$Item.installDir }
    if (-not $directory) {
        return [pscustomobject]@{ Installed = $false; Outdated = $false; Version = $null; Reason = '未声明 directory' }
    }
    if (-not (Test-Path -LiteralPath $directory)) {
        return [pscustomobject]@{ Installed = $false; Outdated = $false; Version = $null; Reason = "目录不存在: $directory" }
    }

    foreach ($file in @($Item.requiredFiles)) {
        if (-not $file) { continue }
        if (-not (Test-Path -LiteralPath (Join-Path $directory $file))) {
            return [pscustomobject]@{ Installed = $false; Outdated = $false; Version = $null; Reason = "缺少文件: $file" }
        }
    }

    return [pscustomobject]@{ Installed = $true; Outdated = $false; Version = $null; Reason = '' }
}

function Install-GitCloneItem {
    param($Item, $Manifest)

    $directory = [string]$Item.directory
    if (-not $directory) { $directory = [string]$Item.installDir }
    $source = [string]$Item.source
    if (-not $source) { $source = [string]$Item.url }
    if (-not $directory -or -not $source) {
        Write-EnvFail 'git-clone 需要 directory 与 source'
        return $false
    }

    $logPath = Get-EnvItemLogPath -Item $Item -Manifest $Manifest -Tag 'git'
    $timeout = 900
    if ($Item.timeoutSeconds) { $timeout = [int]$Item.timeoutSeconds }

    if (Test-Path -LiteralPath (Join-Path $directory '.git')) {
        Write-EnvStep "git pull --ff-only: $directory（已存在，仅补快进）"
        $result = Invoke-EnvNativeCommand -FilePath 'git' -ArgList @('-C', $directory, 'pull', '--ff-only') `
            -TimeoutSeconds $timeout -LogPath $logPath -Quiet
        if ($result.ExitCode -ne 0) {
            Write-EnvWarn "git pull 失败（保留现状）: $($result.Reason)"
            return (Test-GitCloneItem -Item $Item -Manifest $Manifest).Installed
        }
        return $true
    }

    $parent = Split-Path -Parent $directory
    if ($parent -and -not (Test-Path -LiteralPath $parent)) { New-Item -ItemType Directory -Force -Path $parent | Out-Null }

    $args = @('clone', '--depth', '1')
    if ($Item.branch) { $args += @('--branch', [string]$Item.branch) }
    $args += @($source, $directory)

    Write-EnvStep "git clone $source → $directory"
    $result = Invoke-EnvNativeCommand -FilePath 'git' -ArgList $args -TimeoutSeconds $timeout -LogPath $logPath -Quiet
    if ($result.ExitCode -ne 0) {
        Write-EnvFail "git clone 失败: $($result.Reason)"
        return $false
    }
    Write-EnvOk "仓库已克隆: $directory"
    return $true
}

function Verify-GitCloneItem {
    param($Item, $Manifest)
    $test = Test-GitCloneItem -Item $Item -Manifest $Manifest
    return [pscustomobject]@{ Ok = $test.Installed; Reason = $test.Reason; Output = '' }
}
