<#
.SYNOPSIS
    Docker 环境诊断脚本 — 系统性检查 Docker Desktop 运行状态
.DESCRIPTION
    按以下流程逐项检查 Docker 环境，定位问题根因：
    1. 系统环境检查（OS、磁盘、WSL）
    2. Docker 服务状态检查
    3. Docker CLI 连接检查
    4. Docker 配置检查
    5. Docker 日志分析
    6. 容器状态检查
    7. 综合诊断结论

.PARAMETER OutputPath
    诊断报告输出路径（默认输出到控制台）
.PARAMETER Quick
    快速模式，只检查关键项

.EXAMPLE
    .\Test-DockerDiagnostic.ps1
    运行完整诊断

.EXAMPLE
    .\Test-DockerDiagnostic.ps1 -Quick
    快速诊断模式
#>

param(
    [string]$OutputPath,
    [switch]$Quick
)

# ============================================================
# 导入共享模块
# ============================================================
$ModulePath = Join-Path $PSScriptRoot "..\..\Modules\Docker\DockerCommon.psm1"
Import-Module $ModulePath -Force -DisableNameChecking

# ============================================================
# 常量（使用 DockerCommon 的函数获取）
# ============================================================
$ScriptRoot = $PSScriptRoot
$ContainerName = Get-ContainerName

# ============================================================
# 检查函数
# ============================================================


# ─── 1. 系统环境检查 ───

function Check-SystemEnvironment {
    Write-Section "1. 系统环境检查"

    # 1.1 OS 版本
    Write-SubSection "1.1 操作系统"
    try {
        $os = Get-CimInstance Win32_OperatingSystem
        Write-Diagnostic "系统环境" "OS版本" "INFO" "$($os.Caption) $($os.Version) (Build $($os.BuildNumber))"
        Write-Diagnostic "系统环境" "系统目录" "INFO" "$env:SystemRoot"
    } catch {
        Write-Diagnostic "系统环境" "OS版本" "WARN" "无法获取: $_"
    }

    # 1.2 磁盘空间
    Write-SubSection "1.2 磁盘空间"
    $drivesToCheck = @("C:", "D:")
    foreach ($drive in $drivesToCheck) {
        if (Test-Path "${drive}\") {
            try {
                $disk = Get-CimInstance Win32_LogicalDisk -Filter "DeviceID='$drive'"
                $freeGB = [math]::Round($disk.FreeSpace / 1GB, 1)
                $totalGB = [math]::Round($disk.Size / 1GB, 1)
                $pctFree = [math]::Round(($disk.FreeSpace / $disk.Size) * 100, 1)
                $status = if ($pctFree -lt 10) { "FAIL" } elseif ($pctFree -lt 20) { "WARN" } else { "PASS" }
                Write-Diagnostic "系统环境" "磁盘 $drive" $status "剩余 $freeGB GB / $totalGB GB ($pctFree%)"
            } catch {
                Write-Diagnostic "系统环境" "磁盘 $drive" "WARN" "无法获取: $_"
            }
        }
    }

    # 1.3 WSL 状态
    Write-SubSection "1.3 WSL 状态"
    if (Test-CommandExists "wsl") {
        try {
            $wslStatus = wsl --status 2>&1
            $wslList = wsl --list --verbose 2>&1
            Write-Diagnostic "系统环境" "WSL命令" "PASS" "可用"

            # 检查 docker-desktop 发行版
            if ($wslList -match "docker-desktop") {
                if ($wslList -match "docker-desktop\s+Running") {
                    Write-Diagnostic "系统环境" "WSL docker-desktop" "PASS" "运行中"
                } else {
                    Write-Diagnostic "系统环境" "WSL docker-desktop" "FAIL" "未运行"
                }
            } else {
                Write-Diagnostic "系统环境" "WSL docker-desktop" "FAIL" "发行版不存在"
            }

            # 检查默认 WSL 版本
            if ($wslStatus -match "Default Version:\s*2") {
                Write-Diagnostic "系统环境" "WSL版本" "PASS" "WSL2"
            } elseif ($wslStatus -match "Default Version:\s*1") {
                Write-Diagnostic "系统环境" "WSL版本" "WARN" "WSL1（建议使用 WSL2）"
            }
        } catch {
            Write-Diagnostic "系统环境" "WSL" "FAIL" "查询失败: $_"
        }
    } else {
        Write-Diagnostic "系统环境" "WSL" "FAIL" "wsl 命令不可用"
    }

    # 1.4 Hyper-V / 虚拟化
    Write-SubSection "1.4 虚拟化支持"
    try {
        $hyperv = Get-CimInstance Win32_ComputerSystem -ErrorAction SilentlyContinue
        if ($hyperv.HypervisorPresent) {
            Write-Diagnostic "系统环境" "虚拟化" "PASS" "已启用"
        } else {
            Write-Diagnostic "系统环境" "虚拟化" "FAIL" "未启用（BIOS 中需开启 VT-x/AMD-V）"
        }
    } catch {
        Write-Diagnostic "系统环境" "虚拟化" "WARN" "无法检测: $_"
    }
}

# ─── 2. Docker 服务状态检查 ───

function Check-DockerService {
    Write-Section "2. Docker 服务状态检查"

    # 2.1 Docker Desktop 进程
    Write-SubSection "2.1 Docker Desktop 进程"
    try {
        $processes = Get-Process -Name "Docker Desktop", "com.docker*", "docker*" -ErrorAction SilentlyContinue
        $found = @{}
        foreach ($p in $processes) {
            $found[$p.ProcessName] = $true
        }

        if ($found["Docker Desktop"]) {
            Write-Diagnostic "Docker服务" "Docker Desktop.exe" "PASS" "运行中"
        } else {
            Write-Diagnostic "Docker服务" "Docker Desktop.exe" "FAIL" "未运行"
        }

        if ($found["com.docker.backend"]) {
            Write-Diagnostic "Docker服务" "com.docker.backend" "PASS" "运行中"
        } else {
            Write-Diagnostic "Docker服务" "com.docker.backend" "FAIL" "未运行"
        }

        if ($found["com.docker.service"]) {
            Write-Diagnostic "Docker服务" "com.docker.service" "PASS" "运行中"
        } else {
            Write-Diagnostic "Docker服务" "com.docker.service" "FAIL" "未运行"
        }
    } catch {
        Write-Diagnostic "Docker服务" "进程检查" "WARN" "无法获取: $_"
    }

    # 2.2 Windows 服务
    Write-SubSection "2.2 Windows 服务"
    try {
        $svc = Get-Service -Name (Get-DockerServiceName) -ErrorAction SilentlyContinue
        if ($svc) {
            Write-Diagnostic "Docker服务" (Get-DockerServiceName) $(
                if ($svc.Status -eq "Running") { "PASS" } else { "FAIL" }
            ) "状态: $($svc.Status), 启动类型: $($svc.StartType)"
        } else {
            Write-Diagnostic "Docker服务" (Get-DockerServiceName) "FAIL" "服务不存在"
        }
    } catch {
        Write-Diagnostic "Docker服务" "Windows服务" "WARN" "无法获取: $_"
    }

    # 2.3 Docker 命名管道
    Write-SubSection "2.3 Docker 命名管道"
    $pipesToCheck = @(
        "\\.\pipe\docker_engine",
        "\\.\pipe\dockerDesktopLinuxEngine"
    )
    foreach ($pipe in $pipesToCheck) {
        try {
            $exists = [System.IO.Directory]::Exists($pipe)
            if ($exists) {
                Write-Diagnostic "Docker服务" "命名管道" "PASS" "$pipe 存在"
            } else {
                Write-Diagnostic "Docker服务" "命名管道" "FAIL" "$pipe 不存在"
            }
        } catch {
            Write-Diagnostic "Docker服务" "命名管道" "WARN" "$pipe 无法访问: $_"
        }
    }
}

# ─── 3. Docker CLI 连接检查 ───

function Check-DockerCLI {
    Write-Section "3. Docker CLI 连接检查"

    # 3.1 Docker 命令
    Write-SubSection "3.1 Docker 命令"
    if (Test-CommandExists "docker") {
        $dockerPath = (Get-Command docker).Source
        Write-Diagnostic "DockerCLI" "docker命令" "PASS" $dockerPath
    } else {
        Write-Diagnostic "DockerCLI" "docker命令" "FAIL" "docker 命令不可用"
        return
    }

    # 3.2 Docker 上下文
    Write-SubSection "3.2 Docker 上下文"
    try {
        $contexts = docker context ls 2>&1
        $currentContext = ($contexts | Select-String "\*") -replace '^\s*\*\s*', '' -replace '\s+.*$', ''
        Write-Diagnostic "DockerCLI" "当前上下文" "INFO" $currentContext

        $desktopLinux = $contexts | Select-String "desktop-linux"
        if ($desktopLinux) {
            $endpoint = ($desktopLinux -replace '.*\s+(npipe://[^\s]+).*', '$1').Trim()
            Write-Diagnostic "DockerCLI" "desktop-linux端点" "INFO" $endpoint
        }
    } catch {
        Write-Diagnostic "DockerCLI" "上下文" "WARN" "查询失败: $_"
    }

    # 3.3 Docker 连接测试（带超时）
    Write-SubSection "3.3 Docker 连接测试"
    try {
        $timeoutSeconds = 10
        $job = Start-Job -ScriptBlock {
            param($ctx)
            docker --context $ctx info --format "{{.ServerVersion}}" 2>&1
        } -ArgumentList $currentContext

        if (Wait-Job $job -Timeout $timeoutSeconds) {
            $result = Receive-Job $job -ErrorAction SilentlyContinue
            if ($result -match '\d+\.\d+\.\d+') {
                Write-Diagnostic "DockerCLI" "docker info" "PASS" "服务端版本: $($result.Trim())"
            } else {
                Write-Diagnostic "DockerCLI" "docker info" "FAIL" "连接失败: $($result -join '; ')"
            }
        } else {
            Stop-Job $job -ErrorAction SilentlyContinue
            Write-Diagnostic "DockerCLI" "docker info" "FAIL" "连接超时（${timeoutSeconds}s）"
        }
        Remove-Job $job -ErrorAction SilentlyContinue
    } catch {
        Write-Diagnostic "DockerCLI" "docker info" "FAIL" "异常: $_"
    }

    # 3.4 Docker 环境变量
    Write-SubSection "3.4 Docker 环境变量"
    $dockerHost = [Environment]::GetEnvironmentVariable("DOCKER_HOST", "User")
    $dockerHostSys = [Environment]::GetEnvironmentVariable("DOCKER_HOST", "Machine")
    if ($dockerHost -or $dockerHostSys) {
        Write-Diagnostic "DockerCLI" "DOCKER_HOST" "WARN" "已设置: $($dockerHost + $dockerHostSys)（可能覆盖默认连接）"
    } else {
        Write-Diagnostic "DockerCLI" "DOCKER_HOST" "PASS" "未设置（使用默认连接）"
    }
}

# ─── 4. Docker 配置检查 ───

function Check-DockerConfig {
    Write-Section "4. Docker 配置检查"

    # 4.1 Docker 数据目录
    Write-SubSection "4.1 Docker 数据目录"
    $dockerDataDir = Get-DockerDataDir
    if (Test-Path $dockerDataDir) {
        Write-Diagnostic "Docker配置" "数据目录" "PASS" $dockerDataDir
    } else {
        Write-Diagnostic "Docker配置" "数据目录" "FAIL" "不存在: $dockerDataDir"
    }

    # 4.2 daemon.json
    Write-SubSection "4.2 daemon.json"
    $daemonJson = Join-Path (Get-DockerConfigDir) "daemon.json"
    if (Test-Path $daemonJson) {
        try {
            $content = Get-Content $daemonJson -Raw | ConvertFrom-Json
            Write-Diagnostic "Docker配置" "daemon.json" "PASS" "存在且格式正确"
            if ($content."registry-mirrors") {
                Write-Diagnostic "Docker配置" "镜像加速器" "INFO" "$($content."registry-mirrors" -join ', ')"
            }
        } catch {
            Write-Diagnostic "Docker配置" "daemon.json" "FAIL" "JSON 格式错误: $_"
        }
    } else {
        Write-Diagnostic "Docker配置" "daemon.json" "INFO" "不存在（使用默认配置）"
    }

    # 4.3 Docker Desktop 设置
    Write-SubSection "4.3 Docker Desktop 设置"
    $settingsStore = "$env:USERPROFILE\AppData\Roaming\Docker\settings-store.json"
    if (Test-Path $settingsStore) {
        try {
            $settings = Get-Content $settingsStore -Raw | ConvertFrom-Json
            $dataFolder = $settings.DataFolder
            if ($dataFolder) {
                Write-Diagnostic "Docker配置" "数据目录(设置)" "INFO" $dataFolder
                if (Test-Path $dataFolder) {
                    Write-Diagnostic "Docker配置" "数据目录可用" "PASS" "可访问"
                } else {
                    Write-Diagnostic "Docker配置" "数据目录可用" "FAIL" "目录不存在: $dataFolder"
                }
            }
        } catch {
            Write-Diagnostic "Docker配置" "settings-store" "WARN" "读取失败: $_"
        }
    } else {
        Write-Diagnostic "Docker配置" "settings-store" "INFO" "不存在"
    }

    # 4.4 锁文件检查
    Write-SubSection "4.4 锁文件检查"
    $lockFiles = @(
        "$(Get-DockerDataDir)\backend.lock",
        "$(Get-DockerDataDir)\frontend.lock",
        "$(Get-DockerDataDir)\launcher.lock"
    )
    foreach ($lock in $lockFiles) {
        if (Test-Path $lock) {
            $age = (Get-Date) - (Get-Item $lock).LastWriteTime
            if ($age.TotalMinutes -gt 30) {
                Write-Diagnostic "Docker配置" "锁文件" "WARN" "$(Split-Path $lock -Leaf) 存在且已过期（$($age.TotalMinutes.ToString('0')) 分钟前）"
            } else {
                Write-Diagnostic "Docker配置" "锁文件" "INFO" "$(Split-Path $lock -Leaf) 存在（正常）"
            }
        }
    }
}

# ─── 5. Docker 日志分析 ───

function Check-DockerLogs {
    Write-Section "5. Docker 日志分析"

    # 5.1 日志文件存在性
    Write-SubSection "5.1 日志文件"
    $dockerLogDir = Get-DockerLogDir
    $logFiles = @(
        "$dockerLogDir\host\com.docker.backend.exe.log",
        "$dockerLogDir\vm\init.log"
    )
    $latestLog = $null
    foreach ($log in $logFiles) {
        $resolved = $log
        # 找最新的 init.log 轮转文件
        if ($log -match "init\.log$") {
            $dir = Split-Path $log -Parent
            $files = Get-ChildItem "$dir\init.log*" | Sort-Object LastWriteTime -Descending
            if ($files.Count -gt 0) {
                $resolved = $files[0].FullName
            }
        }

        if (Test-Path $resolved) {
            $size = (Get-Item $resolved).Length
            $sizeStr = if ($size -gt 1MB) { "$([math]::Round($size/1MB,1)) MB" } else { "$([math]::Round($size/1KB,0)) KB" }
            Write-Diagnostic "Docker日志" "$(Split-Path $resolved -Leaf)" "INFO" "$sizeStr"
            $latestLog = $resolved
        } else {
            Write-Diagnostic "Docker日志" "$(Split-Path $log -Leaf)" "INFO" "不存在"
        }
    }

    # 5.2 日志错误分析
    Write-SubSection "5.2 错误分析"
    $errorPatterns = @("error", "fail", "fatal", "panic", "exception", "timeout", "refused", "denied")

    $backendLog = "$dockerLogDir\host\com.docker.backend.exe.log"
    if (Test-Path $backendLog) {
        $foundErrors = @()
        foreach ($pattern in $errorPatterns) {
            try {
                $matches = Select-String -Path $backendLog -Pattern $pattern -CaseSensitive:$false -SimpleMatch:$false
                if ($matches) {
                    # 去重取前 5 个
                    $matches | Select-Object -First 5 -Unique | ForEach-Object {
                        $foundErrors += "[$pattern] $($_.Line.Trim())"
                    }
                }
            } catch {}
        }

        if ($foundErrors.Count -gt 0) {
            Write-Diagnostic "Docker日志" "backend错误" "WARN" "发现 $($foundErrors.Count) 条错误/警告"
            $foundErrors | Select-Object -First 10 | ForEach-Object {
                Write-Host "      $_" -ForegroundColor Gray
            }
        } else {
            Write-Diagnostic "Docker日志" "backend错误" "PASS" "未发现明显错误"
        }
    }

    # 5.3 VM 初始化日志
    $vmLogDir = "$dockerLogDir\vm"
    if (Test-Path $vmLogDir) {
        $vmFiles = Get-ChildItem "$vmLogDir\init.log*" | Sort-Object LastWriteTime -Descending
        if ($vmFiles.Count -gt 0) {
            $latestVmLog = $vmFiles[0].FullName
            $vmErrors = @()
            foreach ($pattern in $errorPatterns) {
                try {
                    $matches = Select-String -Path $latestVmLog -Pattern $pattern -CaseSensitive:$false -SimpleMatch:$false
                    if ($matches) {
                        $matches | Select-Object -First 3 -Unique | ForEach-Object {
                            $vmErrors += "[$pattern] $($_.Line.Trim())"
                        }
                    }
                } catch {}
            }

            if ($vmErrors.Count -gt 0) {
                Write-Diagnostic "Docker日志" "VM初始化错误" "WARN" "发现 $($vmErrors.Count) 条错误/警告"
                $vmErrors | Select-Object -First 10 | ForEach-Object {
                    Write-Host "      $_" -ForegroundColor Gray
                }
            } else {
                Write-Diagnostic "Docker日志" "VM初始化错误" "PASS" "未发现明显错误"
            }
        }
    }
}

# ─── 6. 容器状态检查 ───

function Check-ContainerStatus {
    Write-Section "6. 容器状态检查"

    # 6.1 容器列表
    Write-SubSection "6.1 容器列表"
    try {
        $timeoutSeconds = 10
        $job = Start-Job -ScriptBlock {
            docker ps -a --filter "name=$using:ContainerName" --format "table {{.ID}}\t{{.Names}}\t{{.Status}}\t{{.Ports}}" 2>&1
        }

        if (Wait-Job $job -Timeout $timeoutSeconds) {
            $result = Receive-Job $job
            if ($result -match $ContainerName) {
                Write-Diagnostic "容器状态" "容器列表" "PASS" "找到容器 $ContainerName"
                $result -split "`n" | ForEach-Object {
                    if ($_ -match $ContainerName) {
                        Write-Host "      $_" -ForegroundColor Gray
                    }
                }
            } else {
                Write-Diagnostic "容器状态" "容器列表" "FAIL" "容器 $ContainerName 不存在"
            }
        } else {
            Stop-Job $job -ErrorAction SilentlyContinue
            Write-Diagnostic "容器状态" "容器列表" "FAIL" "查询超时（${timeoutSeconds}s）"
        }
        Remove-Job $job -ErrorAction SilentlyContinue
    } catch {
        Write-Diagnostic "容器状态" "容器列表" "FAIL" "异常: $_"
    }

    # 6.2 Docker Compose 配置（使用 DockerCommon 的 Get-ComposeFile）
    Write-SubSection "6.2 Docker Compose 配置"
    $composeFile = Get-ComposeFile
    $resolved = Resolve-ProjectPath $composeFile
    if (Test-Path $resolved) {
        Write-Diagnostic "容器状态" "docker-compose.yml" "PASS" $resolved
    } else {
        Write-Diagnostic "容器状态" "docker-compose.yml" "INFO" "未找到: $resolved"
    }
}

# ============================================================
# 主流程
# ============================================================

function Main {
    Write-Banner -Title "Docker 环境诊断工具 v1.0" -Subtitle "系统性检查 Docker Desktop 运行状态"
    Write-Host "  开始时间: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -ForegroundColor Gray
    if ($Quick) {
        Write-Host "  模式: 快速诊断" -ForegroundColor Yellow
    }
    Write-Host ""

    # 初始化诊断上下文
    New-DiagnosticContext

    # 执行检查
    Check-SystemEnvironment

    if (-not $Quick) {
        Check-DockerService
    }

    Check-DockerCLI
    Check-DockerConfig

    if (-not $Quick) {
        Check-DockerLogs
        Check-ContainerStatus
    }

    # 综合结论（使用通用诊断总结）
    Show-DiagnosticSummary -Title "7. 综合诊断结论"

    # 输出报告
    if ($OutputPath) {
        Save-DiagnosticReport -Path $OutputPath
    }

    Write-Host "`n  完成时间: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -ForegroundColor Gray
    Write-Host ""
}

# ============================================================
# 启动
# ============================================================

Main
