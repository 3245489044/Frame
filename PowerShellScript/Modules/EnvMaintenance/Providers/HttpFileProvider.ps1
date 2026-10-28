# @requires: Core/EnvProviders
# @version: 1.0.0
# @author: WorkFlowPrj
# @category: env-maintenance
# @alias: env-maintenance-provider-http
# @exports: Get-HttpFileProvider

<#
.SYNOPSIS
    http-file Provider —— 下载模型/权重到目标目录（curl 优先，带超时与 .part 原子落盘）。
#>

function Get-HttpFileProvider {
    return (New-EnvProvider -Type 'http-file' -Name 'http-file Provider' `
        -Test ${function:Test-HttpFileItem} -Install ${function:Install-HttpFileItem} -Verify ${function:Verify-HttpFileItem})
}

function Get-HttpFileTarget {
    param($Item)
    $directory = [string]$Item.directory
    if (-not $directory) { $directory = [string]$Item.installDir }
    $fileName = [string]$Item.fileName
    if (-not $fileName -and $Item.source) { $fileName = [System.IO.Path]::GetFileName(([string]$Item.source).Split('?')[0]) }
    if (-not $directory -or -not $fileName) { return '' }
    return (Join-Path $directory $fileName)
}

function Test-HttpFileItem {
    param($Item, $Manifest)

    $target = Get-HttpFileTarget -Item $Item
    if (-not $target) {
        return [pscustomobject]@{ Installed = $false; Outdated = $false; Version = $null; Reason = '未声明 directory/fileName' }
    }
    if (-not (Test-Path -LiteralPath $target)) {
        return [pscustomobject]@{ Installed = $false; Outdated = $false; Version = $null; Reason = "文件不存在: $target" }
    }

    $minBytes = 1
    if ($Item.minBytes) { $minBytes = [long]$Item.minBytes }
    $size = (Get-Item -LiteralPath $target).Length
    if ($size -lt $minBytes) {
        return [pscustomobject]@{ Installed = $false; Outdated = $false; Version = ("{0}B" -f $size); Reason = "文件过小（$size < $minBytes）" }
    }

    return [pscustomobject]@{ Installed = $true; Outdated = $false; Version = ("{0:N1}MB" -f ($size / 1MB)); Reason = '' }
}

function Install-HttpFileItem {
    param($Item, $Manifest)

    $target = Get-HttpFileTarget -Item $Item
    $source = [string]$Item.source
    if (-not $target -or -not $source) {
        Write-EnvFail 'http-file 需要 source 与 directory/fileName'
        return $false
    }

    $directory = Split-Path -Parent $target
    if ($directory -and -not (Test-Path -LiteralPath $directory)) { New-Item -ItemType Directory -Force -Path $directory | Out-Null }

    $partial = $target + '.part'
    $logPath = Get-EnvItemLogPath -Item $Item -Manifest $Manifest -Tag 'http'
    $timeout = 1800
    if ($Item.timeoutSeconds) { $timeout = [int]$Item.timeoutSeconds }

    Write-EnvStep "下载 $source → $target"
    $curl = Get-Command 'curl.exe' -ErrorAction SilentlyContinue
    if ($curl) {
        $result = Invoke-EnvNativeCommand -FilePath $curl.Source `
            -ArgList @('-L', '--fail', '--retry', '2', '-o', $partial, $source) `
            -TimeoutSeconds $timeout -LogPath $logPath -Quiet
    } else {
        try {
            Invoke-WebRequest -Uri $source -OutFile $partial -UseBasicParsing -TimeoutSec ([Math]::Max(60, $timeout))
            $result = [pscustomobject]@{ ExitCode = 0; Reason = '' }
        } catch {
            $result = [pscustomobject]@{ ExitCode = 1; Reason = $_.Exception.Message }
        }
    }

    if ($result.ExitCode -ne 0) {
        Write-EnvFail "下载失败: $($result.Reason)"
        if (Test-Path -LiteralPath $partial) { Remove-Item -LiteralPath $partial -Force -ErrorAction SilentlyContinue }
        return $false
    }

    Move-Item -LiteralPath $partial -Destination $target -Force
    Write-EnvOk "已下载: $target"
    return $true
}

function Verify-HttpFileItem {
    param($Item, $Manifest)
    $test = Test-HttpFileItem -Item $Item -Manifest $Manifest
    return [pscustomobject]@{ Ok = $test.Installed; Reason = $test.Reason; Output = '' }
}
