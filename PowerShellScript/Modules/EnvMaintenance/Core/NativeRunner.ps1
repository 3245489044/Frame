# @requires: -
# @version: 1.0.0
# @author: WorkFlowPrj
# @category: env-maintenance
# @alias: env-maintenance-native
# @exports: Invoke-EnvNativeCommand, Format-EnvNativeArgument

<#
.SYNOPSIS
    原生进程执行器 —— 统一超时、日志捕获与失败原因。
.DESCRIPTION
    直接使用 System.Diagnostics.Process（PS 5.1 不支持 ArgumentList），
    stdout/stderr 异步读尽避免管道死锁；超时用 taskkill /T /F 杀进程树。
    任何异常都折叠为结果对象，不向调用方抛。
#>

function Format-EnvNativeArgument {
    param([string]$Value)
    if ($null -eq $Value) { return '""' }
    if ($Value -match '^[A-Za-z0-9_\./\\:=+\-]+$') { return $Value }
    return '"' + ($Value -replace '"', '\"') + '"'
}

function Invoke-EnvNativeCommand {
    <#
    .SYNOPSIS
        执行原生命令并返回 { ExitCode; TimedOut; Output; Reason; LogPath }。
    .PARAMETER TimeoutSeconds
        单次执行上限（默认 600s）；超时返回 TimedOut=$true 并终止进程树。
    #>
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [string[]]$ArgList = @(),
        [string]$WorkingDirectory = '',
        [int]$TimeoutSeconds = 600,
        [string]$LogPath = '',
        [hashtable]$Environment = @{},
        [switch]$Quiet
    )

    $result = [pscustomobject]@{
        ExitCode = -1
        TimedOut = $false
        Output   = ''
        Reason   = ''
        LogPath  = $LogPath
    }

    if (-not (Test-Path -LiteralPath $FilePath)) {
        $result.Reason = "可执行文件不存在: $FilePath"
        return $result
    }

    $arguments = ($ArgList | ForEach-Object { Format-EnvNativeArgument $_ }) -join ' '
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $FilePath
    $psi.Arguments = $arguments
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.StandardOutputEncoding = [System.Text.Encoding]::UTF8
    $psi.StandardErrorEncoding = [System.Text.Encoding]::UTF8
    if ($WorkingDirectory) { $psi.WorkingDirectory = $WorkingDirectory }
    foreach ($key in $Environment.Keys) {
        $psi.EnvironmentVariables[$key] = [string]$Environment[$key]
    }

    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $psi

    $stdout = ''
    $stderr = ''
    try {
        [void]$process.Start()
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()

        $timeoutMs = [Math]::Max(1000, $TimeoutSeconds * 1000)
        $exited = $process.WaitForExit($timeoutMs)
        if (-not $exited) {
            $result.TimedOut = $true
            try { & taskkill.exe /T /F /PID $process.Id | Out-Null } catch { }
            try { $process.Kill() } catch { }
        } else {
            $process.WaitForExit()
        }

        $stdout = $stdoutTask.Result
        $stderr = $stderrTask.Result
        $result.ExitCode = $(if ($process.HasExited) { $process.ExitCode } else { -1 })
    } catch {
        $result.Reason = "执行异常: $($_.Exception.Message)"
    } finally {
        try { $process.Dispose() } catch { }
    }

    $combined = @()
    if ($stdout) { $combined += $stdout.TrimEnd() }
    if ($stderr) { $combined += $stderr.TrimEnd() }
    $result.Output = ($combined -join "`r`n")

    if ($LogPath) {
        try {
            $logDir = Split-Path -Parent $LogPath
            if ($logDir -and -not (Test-Path -LiteralPath $logDir)) {
                New-Item -ItemType Directory -Force -Path $logDir | Out-Null
            }
            $header = "## $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') $FilePath $arguments"
            Set-Content -LiteralPath $LogPath -Value (@($header, $result.Output) -join "`r`n") -Encoding UTF8
        } catch {
            # 日志写失败不影响执行结论
        }
    }

    if ($result.TimedOut) {
        $result.Reason = "执行超时（${TimeoutSeconds}s）"
    } elseif ($result.ExitCode -ne 0 -and -not $result.Reason) {
        $lastLine = ''
        foreach ($line in ($result.Output -split "`r?`n")) {
            if ($line.Trim()) { $lastLine = $line.Trim() }
        }
        if ($lastLine) { $result.Reason = "退出码 $($result.ExitCode): $lastLine" }
        else { $result.Reason = "退出码 $($result.ExitCode)" }
    }

    if (-not $Quiet -and $result.Output) { Write-Host $result.Output -ForegroundColor DarkGray }
    return $result
}
