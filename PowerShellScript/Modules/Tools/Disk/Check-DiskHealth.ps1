<#
.SYNOPSIS
    检查磁盘健康状况（只读，不修改任何数据）
.DESCRIPTION
    通过 S.M.A.R.T.、磁盘运行状态、分区信息等多维度检查磁盘健康情况。
    支持指定盘符或检查所有磁盘，可选执行 chkdsk 只读扫描。
    全部操作为只读，不修改磁盘任何数据。
.PARAMETER DriveLetter
    目标盘符，如 "F:"（默认自动检测 USB 移动硬盘）
.PARAMETER AllDisks
    显示所有物理磁盘的健康状态
.PARAMETER ChkDskScan
    是否执行 chkdsk 只读扫描（不含 /f /r，仅检查）
.PARAMETER Simple
    简洁模式，仅显示健康状态摘要
.EXAMPLE
    .\Check-DiskHealth.ps1
    检查默认移动硬盘（自动检测 USB 外置盘）
.EXAMPLE
    .\Check-DiskHealth.ps1 -DriveLetter "D:"
    检查指定盘符
.EXAMPLE
    .\Check-DiskHealth.ps1 -AllDisks
    检查所有磁盘
.EXAMPLE
    .\Check-DiskHealth.ps1 -DriveLetter "F:" -ChkDskScan
    检查 F: 盘并执行 chkdsk 只读扫描
.EXAMPLE
    .\Check-DiskHealth.ps1 -AllDisks -Simple
    简洁模式查看所有磁盘健康摘要
#>

[CmdletBinding()]
param(
    [string]$DriveLetter,
    [switch]$AllDisks,
    [switch]$ChkDskScan,
    [switch]$Simple
)

# ===== 颜色输出辅助函数 =====
function Write-Color {
    param(
        [string]$Text,
        [string]$Color = "White"
    )
    Write-Host $Text -ForegroundColor $Color
}

function Write-Status {
    param(
        [string]$Label,
        [string]$Value,
        [string]$Status = "Info"
    )
    $colorMap = @{
        "OK"      = "Green"
        "Warning" = "Yellow"
        "Error"   = "Red"
        "Info"    = "Cyan"
    }
    $c = $colorMap[$Status]
    if (-not $c) { $c = "White" }
    Write-Host ("  {0,-20} : " -f $Label) -NoNewline
    Write-Host $Value -ForegroundColor $c
}

function Write-Section {
    param([string]$Title)
    Write-Host ""
    Write-Host ("=" * 60) -ForegroundColor "DarkGray"
    Write-Host ("  $Title") -ForegroundColor "Magenta"
    Write-Host ("=" * 60) -ForegroundColor "DarkGray"
}

# ===== 检测目标磁盘 =====
function Get-TargetDisks {
    $allPhysicalDisks = Get-PhysicalDisk -ErrorAction SilentlyContinue
    if (-not $allPhysicalDisks) {
        Write-Color "  [错误] 无法获取物理磁盘信息，请以管理员身份运行。" "Red"
        return $null
    }

    if ($AllDisks) {
        return $allPhysicalDisks
    }

    if ($DriveLetter) {
        # 通过盘符查找对应的物理磁盘
        $vol = Get-Volume -DriveLetter $DriveLetter.TrimEnd(':') -ErrorAction SilentlyContinue
        if (-not $vol) {
            Write-Color "  [错误] 未找到盘符 $DriveLetter，请检查输入。" "Red"
            return $null
        }
        $part = Get-Partition -DriveLetter $DriveLetter.TrimEnd(':') -ErrorAction SilentlyContinue
        if ($part -and $part.DiskNumber -ge 0) {
            $disk = $allPhysicalDisks | Where-Object { $_.DeviceId -eq $part.DiskNumber -or $_.FriendlyName -eq $part.DiskNumber }
            # 用 DiskNumber 匹配
            $disk = $allPhysicalDisks | Where-Object { $_.DeviceId -eq $part.DiskNumber }
            if ($disk) { return @($disk) }
        }
        # 回退：通过大小匹配
        $diskSize = $vol.Size
        $disk = $allPhysicalDisks | Where-Object { $_.Size -eq $diskSize }
        if ($disk) { return @($disk) }
        
        Write-Color "  [错误] 无法找到盘符 $DriveLetter 对应的物理磁盘。" "Red"
        return $null
    }

    # 默认：自动检测 USB 外置移动硬盘
    $usbDisks = $allPhysicalDisks | Where-Object { $_.BusType -eq "USB" -or $_.MediaType -eq "External" }
    if ($usbDisks) {
        return $usbDisks
    }

    # 没有 USB 盘，返回所有非系统盘
    $systemDisk = (Get-Partition -DriveLetter C -ErrorAction SilentlyContinue).DiskNumber
    return $allPhysicalDisks | Where-Object { $_.DeviceId -ne $systemDisk }
}

# ===== 获取磁盘详细信息 =====
function Get-DiskDetail {
    param([Microsoft.Management.Infrastructure.CimInstance]$Disk)

    $diskNum = $Disk.DeviceId
    $friendlyName = $Disk.FriendlyName
    $sizeGB = [math]::Round($Disk.Size / 1GB, 2)
    $health = $Disk.HealthStatus
    $status = $Disk.OperationalStatus
    $busType = $Disk.BusType
    $mediaType = $Disk.MediaType

    # 获取对应分区和卷信息
    $partitions = Get-Partition -DiskNumber $diskNum -ErrorAction SilentlyContinue
    $volumes = @()
    foreach ($part in $partitions) {
        if ($part.DriveLetter) {
            $vol = Get-Volume -DriveLetter $part.DriveLetter -ErrorAction SilentlyContinue
            if ($vol) {
                $volumes += [PSCustomObject]@{
                    DriveLetter = $part.DriveLetter
                    SizeGB      = [math]::Round($vol.Size / 1GB, 2)
                    FreeGB      = [math]::Round($vol.SizeRemaining / 1GB, 2)
                    UsedGB      = [math]::Round(($vol.Size - $vol.SizeRemaining) / 1GB, 2)
                    UsedPct     = if ($vol.Size -gt 0) { [math]::Round(($vol.Size - $vol.SizeRemaining) / $vol.Size * 100, 1) } else { 0 }
                }
            }
        }
    }

    # 获取 S.M.A.R.T. 属性（需要管理员权限）
    $smartStatus = "N/A"
    try {
        $smart = Get-WmiObject -Namespace "root\wmi" -Class MSStorageDriver_FailurePredictStatus -Filter "InstanceName LIKE '%$($friendlyName.Substring(0, [Math]::Min(8, $friendlyName.Length)))%'" -ErrorAction SilentlyContinue
        if ($smart) {
            $smartStatus = if ($smart.PredictFailure) { "Predicted Failure" } else { "OK" }
        }
    } catch { $smartStatus = "N/A" }

    # 温度（如果有）
    $temperature = "N/A"
    try {
        $temp = Get-WmiObject -Namespace "root\wmi" -Class MSStorageDriver_ATAPISmartData -Filter "InstanceName LIKE '%$($friendlyName.Substring(0, [Math]::Min(8, $friendlyName.Length)))%'" -ErrorAction SilentlyContinue
        if ($temp) { $temperature = "Available" }
    } catch { $temperature = "N/A" }

    return [PSCustomObject]@{
        Disk          = $Disk
        DiskNum       = $diskNum
        FriendlyName  = $friendlyName
        SizeGB        = $sizeGB
        HealthStatus  = $health
        Status        = $status
        BusType       = $busType
        MediaType     = $mediaType
        SmartStatus   = $smartStatus
        Temperature   = $temperature
        Volumes       = $volumes
    }
}

# ===== 显示磁盘健康详情 =====
function Show-DiskHealth {
    param([PSCustomObject]$Detail)

    $disk = $Detail.Disk
    $healthColor = @{
        "Healthy"   = "Green"
        "Warning"   = "Yellow"
        "Unhealthy" = "Red"
    }
    $hc = $healthColor[$Detail.HealthStatus]
    if (-not $hc) { $hc = "Gray" }

    $statusColor = @{
        "OK"        = "Green"
        "Online"    = "Green"
        "Degraded"  = "Yellow"
        "Failed"    = "Red"
    }
    $sc = $statusColor[$Detail.Status]
    if (-not $sc) { $sc = "Gray" }

    Write-Host ""
    Write-Host ("  ┌─ " + $Detail.FriendlyName) -ForegroundColor "White" -NoNewline
    Write-Host ("  (" + $Detail.SizeGB + " GB)") -ForegroundColor "DarkGray"
    Write-Host ("  ├─ Bus Type    : " + $Detail.BusType) -ForegroundColor "Gray"
    Write-Host ("  ├─ Media Type  : " + $Detail.MediaType) -ForegroundColor "Gray"
    Write-Host ("  ├─ Health      : ") -NoNewline
    Write-Host $Detail.HealthStatus -ForegroundColor $hc
    Write-Host ("  ├─ Status      : ") -NoNewline
    Write-Host $Detail.Status -ForegroundColor $sc
    Write-Host ("  ├─ S.M.A.R.T.  : ") -NoNewline
    if ($Detail.SmartStatus -eq "OK") {
        Write-Host "OK" -ForegroundColor "Green"
    } elseif ($Detail.SmartStatus -eq "Predicted Failure") {
        Write-Host "Predicted Failure!" -ForegroundColor "Red"
    } else {
        Write-Host $Detail.SmartStatus -ForegroundColor "Gray"
    }

    # 卷信息
    if ($Detail.Volumes.Count -gt 0) {
        Write-Host ("  ├─ Volumes     :") -ForegroundColor "Gray"
        foreach ($v in $Detail.Volumes) {
            $barLen = 20
            $filled = [math]::Max(1, [math]::Round($v.UsedPct / 100 * $barLen))
            $bar = ("█" * $filled) + ("░" * ($barLen - $filled))
            $barColor = if ($v.UsedPct -gt 90) { "Red" } elseif ($v.UsedPct -gt 75) { "Yellow" } else { "Green" }
            Write-Host ("  │   $($v.DriveLetter):\  ") -NoNewline
            Write-Host ("$($v.UsedGB) / $($v.SizeGB) GB") -NoNewline -ForegroundColor "DarkGray"
            Write-Host ("  [$bar]") -NoNewline -ForegroundColor $barColor
            Write-Host ("  $($v.UsedPct)%") -ForegroundColor "DarkGray"
        }
    } else {
        Write-Host ("  ├─ Volumes     : (none)") -ForegroundColor "DarkGray"
    }
    Write-Host ("  └─" + ("─" * 40)) -ForegroundColor "DarkGray"
}

# ===== 简洁模式显示 =====
function Show-DiskHealthSimple {
    param([PSCustomObject]$Detail)

    $healthColor = @{
        "Healthy"   = "Green"
        "Warning"   = "Yellow"
        "Unhealthy" = "Red"
    }
    $hc = $healthColor[$Detail.HealthStatus]
    if (-not $hc) { $hc = "Gray" }

    $volStr = if ($Detail.Volumes.Count -gt 0) {
        ($Detail.Volumes | ForEach-Object { "$($_.DriveLetter):\$($_.UsedPct)%" }) -join ", "
    } else { "no vol" }

    Write-Host ("  {0,-30} {1,8} GB  " -f $Detail.FriendlyName, $Detail.SizeGB) -NoNewline
    Write-Host ("{0,-12}" -f $Detail.HealthStatus) -NoNewline -ForegroundColor $hc
    Write-Host ("{0,-12}" -f $Detail.BusType) -NoNewline -ForegroundColor "DarkGray"
    Write-Host $volStr -ForegroundColor "Gray"
}

# ===== 执行 chkdsk 只读扫描 =====
function Invoke-ChkDskReadOnly {
    param([string]$Drive)

    $driveLetter = $Drive.TrimEnd(':')
    Write-Section "chkdsk 只读扫描"
    Write-Color "  [信息] 正在执行 chkdsk $Drive （只读模式，不修复）..." "Cyan"
    Write-Color "  [注意] 2.79TB 机械盘可能需要几分钟到几十分钟，请耐心等待..." "Yellow"
    Write-Host ""

    try {
        $result = chkdsk "$Drive" 2>&1
        $result | ForEach-Object { Write-Host ("  " + $_) }
    }
    catch {
        Write-Color "  [错误] chkdsk 执行失败: $_" "Red"
    }
}

# ===== 主流程 =====
function Main {
    Write-Host ""
    Write-Host ("  ╔" + ("═" * 50) + "╗") -ForegroundColor "DarkGray"
    Write-Host ("  ║" + " " * 50 + "║") -ForegroundColor "DarkGray"
    Write-Host ("  ║" + "     磁盘健康检查工具  (只读模式)     " + "║") -ForegroundColor "Cyan"
    Write-Host ("  ║" + " " * 50 + "║") -ForegroundColor "DarkGray"
    Write-Host ("  ╚" + ("═" * 50) + "╝") -ForegroundColor "DarkGray"
    Write-Host ""

    # 检查管理员权限
    $isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    if (-not $isAdmin) {
        Write-Color "  [提示] 未以管理员身份运行，部分信息（如 S.M.A.R.T.）可能不可用。" "Yellow"
        Write-Color "         建议以管理员身份运行以获取完整信息。" "DarkGray"
        Write-Host ""
    }

    # 获取目标磁盘
    $targetDisks = Get-TargetDisks
    if (-not $targetDisks) {
        Write-Color "  [错误] 未找到目标磁盘。" "Red"
        return
    }

    # 显示磁盘信息
    if ($Simple) {
        Write-Section "磁盘健康摘要"
        Write-Host ("  {0,-30} {1,8}  {2,-12} {3,-12} {4}" -f "名称", "容量", "健康状态", "接口", "卷(使用率)")
        Write-Host ("  " + ("-" * 80)) -ForegroundColor "DarkGray"
    }

    $allHealthy = $true
    foreach ($disk in $targetDisks) {
        $detail = Get-DiskDetail -Disk $disk
        if ($Simple) {
            Show-DiskHealthSimple -Detail $detail
        } else {
            Show-DiskHealth -Detail $detail
        }
        if ($detail.HealthStatus -ne "Healthy") { $allHealthy = $false }
    }

    # 总体评价
    Write-Section "总体评价"
    if ($allHealthy) {
        Write-Color "  ✅ 所有磁盘健康状况正常。" "Green"
    } else {
        Write-Color "  ⚠️  部分磁盘存在健康问题，请关注上方的警告/错误信息。" "Yellow"
    }

    # 建议
    $hasWarning = $targetDisks | Where-Object { $_.HealthStatus -ne "Healthy" }
    if ($hasWarning) {
        Write-Color "  [建议] 请立即备份重要数据，并考虑更换故障磁盘。" "Red"
    } else {
        Write-Color "  [建议] 定期检查磁盘健康，保持良好使用习惯。" "DarkGray"
    }

    # 可选 chkdsk 只读扫描
    if ($ChkDskScan) {
        $targetDrive = if ($DriveLetter) { $DriveLetter } else {
            # 自动选择第一个有分区的磁盘的第一个卷
            $firstDetail = Get-DiskDetail -Disk $targetDisks[0]
            if ($firstDetail.Volumes.Count -gt 0) {
                "$($firstDetail.Volumes[0].DriveLetter):"
            } else { "F:" }
        }
        Invoke-ChkDskReadOnly -Drive $targetDrive
    }

    Write-Host ""
    Write-Color "  [完成] 检查结束。" "Green"
}

# 执行
Main
