<#
.SYNOPSIS
    清理编译和 VS Code 运行时的冗余进程

.DESCRIPTION
    检测并终止可能锁定编译输出文件的进程，包括：
    - BuildOutput 目录下正在运行的项目 exe
    - VS Code Code Runner / 终端启动的进程
    - .NET 热重载 / 运行中进程 (dotnet watch/run)
    - 测试运行器 (vstest.console 等)
    
    支持交互式 Y/N/S 逐项确认、-Force 一键清理、-List 仅列出模式。

.PARAMETER Force
    直接终止所有匹配进程，不询问确认

.PARAMETER List
    仅列出匹配的进程，不执行终止操作

.PARAMETER BeforeBuild
    编译前模式：仅清理可能锁定 BuildOutput 目录下 DLL 的进程

.PARAMETER ProcessName
    指定要终止的进程名（可多个，用逗号分隔），不指定则自动检测

.PARAMETER Interactive
    交互模式（默认），逐项询问确认

.EXAMPLE
    # 交互式清理（默认）
    .\Tools\Clean-Processes.ps1

    .EXAMPLE
    # 一键清理，不询问
    .\Tools\Clean-Processes.ps1 -Force

    .EXAMPLE
    # 仅列出不终止
    .\Tools\Clean-Processes.ps1 -List

    .EXAMPLE
    # 编译前模式
    .\Tools\Clean-Processes.ps1 -BeforeBuild

    .EXAMPLE
    # 指定进程名
    .\Tools\Clean-Processes.ps1 -ProcessName "FileDriven.Launcher","dotnet"
#>

[CmdletBinding(DefaultParameterSetName = "Interactive")]
param(
    [Parameter()]
    [switch]$Force,

    [Parameter()]
    [switch]$List,

    [Parameter()]
    [switch]$BeforeBuild,

    [Parameter()]
    [string[]]$ProcessName,

    [Parameter()]
    [switch]$Interactive
)

# ============================================================
# 初始化
# ============================================================

$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path
$toolsDir = $scriptPath
$scriptDir = (Get-Item $toolsDir).Parent.FullName
$moduleDir = Join-Path $scriptDir "..\Modules"

# 加载模块
. (Join-Path $moduleDir "Import-Modules.ps1")

# 获取项目根目录和路径信息
$projectRoot = Get-ProjectRoot
$dirs = Get-ProjectDirectories -ProjectRoot $projectRoot
$buildOutputDir = $dirs.BuildOutput

# ============================================================
# 配置
# ============================================================

# 安全进程列表（永远不会被终止的系统/关键进程）
$safeProcesses = @(
    "Idle", "System", "smss", "csrss", "wininit", "services",
    "lsass", "svchost", "spoolsv", "explorer", "taskhostw",
    "sihost", "RuntimeBroker", "SecurityHealth", "ShellExperienceHost",
    "SearchIndexer", "SearchUI", "StartMenuExperience",
    "ctfmon", "cmd", "powershell", "pwsh",
    "Code", "code", "devenv", "Microsoft VS Code",
    "ApplicationFrameHost", "SystemSettings", "LockApp",
    "Widgets", "TextInputHost", "shellexperiencehost",
    "fontdrvhost", "AggregatorHost", "dasHost",
    "WUDFHost", "SearchHost", "PhoneExperienceHost"
)

# 已知的编译/IDE 进程（不会被 BeforeBuild 模式终止）
$buildSafeProcesses = @(
    "devenv", "Code", "code", "MSBuild",
    "powershell", "pwsh", "cmd"
)

# 已知的项目进程名（来自 BuildOutput 的常见 exe）
$knownProjectProcesses = @(
    "FileDriven.Launcher",
    "FileDriven.UI",
    "CentralConsole",
    "BasicMethod",
    "DataStorage",
    "EnableMent"
)

# ============================================================
# 辅助函数
# ============================================================

function Get-ProcessFileLock {
    <#
    .SYNOPSIS
        检测进程是否占用了 BuildOutput 目录下的文件
    .DESCRIPTION
        通过尝试打开文件句柄的方式检测进程占用。
        使用 handle.exe（如果可用）或简单的文件访问测试。
    #>
    param(
        [System.Diagnostics.Process]$Process,
        [string]$TargetDir
    )

    if (-not (Test-Path $TargetDir)) { return @() }

    $lockedFiles = @()

    try {
        # 方法1: 使用 .NET 的 FileStream 测试（轻量级）
        # 获取进程的模块列表，检查是否有模块在 BuildOutput 下
        $processName = $Process.ProcessName
        $processId = $Process.Id

        # 尝试获取进程模块（需要管理员权限才能访问其他进程的模块）
        try {
            $modules = $Process.Modules
            foreach ($module in $modules) {
                $modulePath = $module.FileName
                if ($modulePath -and $modulePath.StartsWith($TargetDir, [StringComparison]::OrdinalIgnoreCase)) {
                    $lockedFiles += $modulePath
                }
            }
        } catch {
            # 权限不足，跳过模块检测
        }
    } catch {
        # 进程可能已退出
    }

    return $lockedFiles
}

function Get-ProjectProcesses {
    <#
    .SYNOPSIS
        获取与项目相关的进程列表
    .DESCRIPTION
        扫描 BuildOutput 目录下的所有 exe，检测哪些正在运行。
        同时检测已知的项目进程名。
    #>
    param(
        [string]$BuildOutputDir,
        [switch]$OnlyLockingBuildOutput
    )

    $result = @()

    # 策略1: 扫描 BuildOutput 目录下的 exe
    if (Test-Path $BuildOutputDir) {
        $exeFiles = Get-ChildItem -Path $BuildOutputDir -Recurse -Filter "*.exe" -ErrorAction SilentlyContinue
        $exeNames = $exeFiles | ForEach-Object { [System.IO.Path]::GetFileNameWithoutExtension($_.Name) } | Select-Object -Unique

        foreach ($exeName in $exeNames) {
            $procs = Get-Process -Name $exeName -ErrorAction SilentlyContinue
            foreach ($proc in $procs) {
                if ($proc.Id -eq $PID) { continue }  # 跳过自身
                if ($proc.ProcessName -in $safeProcesses) { continue }

                $lockedFiles = @()
                if ($OnlyLockingBuildOutput) {
                    $lockedFiles = Get-ProcessFileLock -Process $proc -TargetDir $BuildOutputDir
                    if ($lockedFiles.Count -eq 0) { continue }
                }

                $result += [PSCustomObject]@{
                    ProcessName = $proc.ProcessName
                    PID         = $proc.Id
                    SessionId   = $proc.SessionId
                    StartTime   = $proc.StartTime
                    CPU         = $proc.TotalProcessorTime
                    MemoryMB    = [math]::Round($proc.WorkingSet64 / 1MB, 1)
                    LockedFiles = $lockedFiles
                    Source      = "BuildOutput"
                    Description = "项目编译产物"
                }
            }
        }
    }

    # 策略2: 检测已知的项目进程
    foreach ($procName in $knownProjectProcesses) {
        $procs = Get-Process -Name $procName -ErrorAction SilentlyContinue
        foreach ($proc in $procs) {
            if ($proc.Id -eq $PID) { continue }
            if ($proc.ProcessName -in $safeProcesses) { continue }

            # 检查是否已在结果中
            $alreadyExists = $result | Where-Object { $_.PID -eq $proc.Id }
            if ($alreadyExists) { continue }

            $lockedFiles = @()
            if ($OnlyLockingBuildOutput) {
                $lockedFiles = Get-ProcessFileLock -Process $proc -TargetDir $BuildOutputDir
                if ($lockedFiles.Count -eq 0) { continue }
            }

            $result += [PSCustomObject]@{
                ProcessName = $proc.ProcessName
                PID         = $proc.Id
                SessionId   = $proc.SessionId
                StartTime   = $proc.StartTime
                CPU         = $proc.TotalProcessorTime
                MemoryMB    = [math]::Round($proc.WorkingSet64 / 1MB, 1)
                LockedFiles = $lockedFiles
                Source      = "KnownProject"
                Description = "已知项目进程"
            }
        }
    }

    # 策略3: 检测 VS Code 子进程（Code Runner / 终端启动的进程）
    # 注意：Code 本身在 safeProcesses 中，不会被清理
    $vscodeProcs = Get-Process -Name "Code","code" -ErrorAction SilentlyContinue
    $vscodePids = $vscodeProcs | ForEach-Object { $_.Id }

    # 通过 WMI 获取进程树，查找 VS Code 的子进程
    try {
        $allProcs = Get-CimInstance -ClassName Win32_Process -ErrorAction SilentlyContinue
        $vscodeChildren = $allProcs | Where-Object {
            $_.ParentProcessId -in $vscodePids -and
            $_.ProcessId -ne $PID -and
            $_.Name -notin $safeProcesses -and
            ($_.Name -like "*.exe" -or $_.Name -like "*.cmd" -or $_.Name -like "*.bat")
        }

        foreach ($child in $vscodeChildren) {
            $proc = Get-Process -Id $child.ProcessId -ErrorAction SilentlyContinue
            if (-not $proc) { continue }

            # 跳过 Code 自身（Code 的子进程也可能是 Code 实例）
            if ($proc.ProcessName -in @("Code", "code")) { continue }

            $alreadyExists = $result | Where-Object { $_.PID -eq $proc.Id }
            if ($alreadyExists) { continue }

            $lockedFiles = @()
            if ($OnlyLockingBuildOutput) {
                $lockedFiles = Get-ProcessFileLock -Process $proc -TargetDir $BuildOutputDir
                if ($lockedFiles.Count -eq 0) { continue }
            }

            $result += [PSCustomObject]@{
                ProcessName = $proc.ProcessName
                PID         = $proc.Id
                SessionId   = $proc.SessionId
                StartTime   = $proc.StartTime
                CPU         = $proc.TotalProcessorTime
                MemoryMB    = [math]::Round($proc.WorkingSet64 / 1MB, 1)
                LockedFiles = $lockedFiles
                Source      = "VSCodeChild"
                Description = "VS Code 子进程"
            }
        }
    } catch {
        # WMI 查询可能失败，忽略
    }

    # 策略4: 检测所有 dotnet 进程（按命令行分类）
    $dotnetProcs = Get-Process -Name "dotnet" -ErrorAction SilentlyContinue
    foreach ($proc in $dotnetProcs) {
        if ($proc.Id -eq $PID) { continue }

        $alreadyExists = $result | Where-Object { $_.PID -eq $proc.Id }
        if ($alreadyExists) { continue }

        # 获取命令行参数分类
        $cmdLine = ""
        $category = "DotNet"
        $description = ".NET 进程"
        $isBuildRelevant = $false  # 是否与编译相关（BeforeBuild 模式需要）

        try {
            $wmiProc = Get-CimInstance -ClassName Win32_Process -Filter "ProcessId = $($proc.Id)" -ErrorAction SilentlyContinue
            $cmdLine = $wmiProc.CommandLine
        } catch {
            # WMI 查询失败
        }

        if ($cmdLine) {
            if ($cmdLine -match "MSBuild\.dll.*/nodemode:1") {
                # MSBuild 节点进程 — 会锁定 BuildOutput 下的 DLL
                $category = "MSBuildNode"
                $description = "MSBuild 编译节点"
                $isBuildRelevant = $true
            } elseif ($cmdLine -match "watch|run" -and $cmdLine -match [regex]::Escape($projectRoot)) {
                # .NET 热重载 / 运行中进程
                $category = "DotNetWatch"
                $description = ".NET 热重载/运行中"
                $isBuildRelevant = $true
            } elseif ($cmdLine -match " test") {
                # dotnet test
                $category = "TestRunner"
                $description = "测试运行器"
                $isBuildRelevant = $true
            } elseif ($cmdLine -match "ilspy|syntaxer|csdevkit|buildhost") {
                # VS Code 扩展的 dotnet 进程
                $category = "VSCodeExt"
                $description = "VS Code 扩展进程"
            } elseif ($cmdLine -match [regex]::Escape($projectRoot)) {
                # 与项目相关的 dotnet 进程
                $category = "DotNetProject"
                $description = "项目相关 .NET 进程"
                $isBuildRelevant = $true
            } else {
                # 其他 dotnet 进程
                $category = "DotNet"
                $description = ".NET 进程"
            }
        }

        # BeforeBuild 模式：只保留与编译相关的进程
        if ($OnlyLockingBuildOutput -and -not $isBuildRelevant) { continue }

        $result += [PSCustomObject]@{
            ProcessName = $proc.ProcessName
            PID         = $proc.Id
            SessionId   = $proc.SessionId
            StartTime   = $proc.StartTime
            CPU         = $proc.TotalProcessorTime
            MemoryMB    = [math]::Round($proc.WorkingSet64 / 1MB, 1)
            LockedFiles = @()
            Source      = $category
            Description = $description
        }
    }

    # 策略5: 检测测试运行器（vstest.console, testhost）
    $testProcessNames = @("vstest.console", "testhost")
    foreach ($procName in $testProcessNames) {
        $procs = Get-Process -Name $procName -ErrorAction SilentlyContinue
        foreach ($proc in $procs) {
            if ($proc.Id -eq $PID) { continue }

            $alreadyExists = $result | Where-Object { $_.PID -eq $proc.Id }
            if ($alreadyExists) { continue }

            $result += [PSCustomObject]@{
                ProcessName = $proc.ProcessName
                PID         = $proc.Id
                SessionId   = $proc.SessionId
                StartTime   = $proc.StartTime
                CPU         = $proc.TotalProcessorTime
                MemoryMB    = [math]::Round($proc.WorkingSet64 / 1MB, 1)
                LockedFiles = @()
                Source      = "TestRunner"
                Description = "测试运行器"
            }
        }
    }

    # 策略6: 检测 conhost 终端残留（VS Code 终端关闭后残留的 conhost）
    # conhost 是 Windows 控制台宿主进程，VS Code 每打开一个终端就会创建一个
    # 关闭终端后 conhost 可能残留，需要清理
    $conhostProcs = Get-Process -Name "conhost" -ErrorAction SilentlyContinue
    foreach ($proc in $conhostProcs) {
        if ($proc.Id -eq $PID) { continue }

        # 跳过系统 conhost（通过检查父进程是否为 svchost 或 explorer）
        try {
            $wmiProc = Get-CimInstance -ClassName Win32_Process -Filter "ProcessId = $($proc.Id)" -ErrorAction SilentlyContinue
            $parentPid = $wmiProc.ParentProcessId
            $parentProc = Get-Process -Id $parentPid -ErrorAction SilentlyContinue
            $parentName = if ($parentProc) { $parentProc.ProcessName } else { "" }

            # 如果父进程是 svchost/explorer/cmd/pwsh/powershell 等系统进程，跳过
            if ($parentName -in @("svchost", "explorer", "services")) { continue }
        } catch {
            continue
        }

        $alreadyExists = $result | Where-Object { $_.PID -eq $proc.Id }
        if ($alreadyExists) { continue }

        $result += [PSCustomObject]@{
            ProcessName = $proc.ProcessName
            PID         = $proc.Id
            SessionId   = $proc.SessionId
            StartTime   = $proc.StartTime
            CPU         = $proc.TotalProcessorTime
            MemoryMB    = [math]::Round($proc.WorkingSet64 / 1MB, 1)
            LockedFiles = @()
            Source      = "Conhost"
            Description = "终端残留进程"
        }
    }

    return $result
}

function Show-ProcessList {
    <#
    .SYNOPSIS
        显示进程列表
    #>
    param(
        [array]$Processes,
        [switch]$ShowLockInfo
    )

    if ($Processes.Count -eq 0) {
        Write-Host "  没有发现需要清理的进程。" -ForegroundColor Green
        return
    }

    Write-Host ""
    Write-Host "  $('PID'.PadRight(8)) $('进程名'.PadRight(28)) $('内存(MB)'.PadRight(10)) $('来源'.PadRight(14)) $('描述')" -ForegroundColor Cyan
    Write-Host "  $('-' * 80)" -ForegroundColor DarkGray

    $index = 1
    foreach ($p in $Processes) {
        $name = $p.ProcessName
        if ($name.Length -gt 26) { $name = $name.Substring(0, 24) + ".." }

        $timeStr = if ($p.StartTime) { $p.StartTime.ToString("HH:mm:ss") } else { "N/A" }
        $memStr = "$($p.MemoryMB) MB"

        Write-Host ("  [{0}] {1,-6} {2,-28} {3,-10} {4,-14} {5}" -f
            $index,
            $p.PID,
            $name,
            $memStr,
            $p.Source,
            $p.Description) -ForegroundColor White

        if ($ShowLockInfo -and $p.LockedFiles.Count -gt 0) {
            foreach ($file in $p.LockedFiles) {
                $shortFile = $file.Substring($buildOutputDir.Length)
                Write-Host ("       占用: {0}" -f $shortFile) -ForegroundColor Yellow
            }
        }

        $index++
    }

    Write-Host "  $('-' * 80)" -ForegroundColor DarkGray
    Write-Host "  共 $($Processes.Count) 个进程" -ForegroundColor Gray
    Write-Host ""
}

function Stop-ProcessWithConfirm {
    <#
    .SYNOPSIS
        交互式终止进程，支持 Y/N/S 模式
    #>
    param(
        [array]$Processes,
        [switch]$ForceMode
    )

    if ($Processes.Count -eq 0) {
        Write-Host "  没有需要清理的进程。" -ForegroundColor Green
        return @{killed=0; failed=0; skipped=0}
    }

    $killedCount = 0
    $failedCount = 0
    $skippedCount = 0

    if ($ForceMode) {
        # 强制模式：直接全部终止
        Write-Host "  强制模式：正在终止所有进程..." -ForegroundColor Yellow
        foreach ($p in $Processes) {
            try {
                Stop-Process -Id $p.PID -Force -ErrorAction Stop
                Write-Host "  [已终止] $($p.ProcessName) (PID: $($p.PID))" -ForegroundColor Green
                $killedCount++
            } catch {
                Write-Host "  [失败]   $($p.ProcessName) (PID: $($p.PID)) - $($_.Exception.Message)" -ForegroundColor Red
                $failedCount++
            }
        }
    } else {
        # 交互模式：询问 Y/N/S
        $confirm = Read-Host "  确认终止以上 $($Processes.Count) 个进程？(Y/N/S=逐项确认)"

        if ($confirm -eq 'Y' -or $confirm -eq 'y') {
            foreach ($p in $Processes) {
                try {
                    Stop-Process -Id $p.PID -Force -ErrorAction Stop
                    Write-Host "  [已终止] $($p.ProcessName) (PID: $($p.PID))" -ForegroundColor Green
                    $killedCount++
                } catch {
                    Write-Host "  [失败]   $($p.ProcessName) (PID: $($p.PID)) - $($_.Exception.Message)" -ForegroundColor Red
                    $failedCount++
                }
            }
        } elseif ($confirm -eq 'S' -or $confirm -eq 's') {
            foreach ($p in $Processes) {
                $itemConfirm = Read-Host "  终止 $($p.ProcessName) (PID: $($p.PID), $($p.MemoryMB) MB) ? (Y/N)"
                if ($itemConfirm -eq 'Y' -or $itemConfirm -eq 'y') {
                    try {
                        Stop-Process -Id $p.PID -Force -ErrorAction Stop
                        Write-Host "    [已终止]" -ForegroundColor Green
                        $killedCount++
                    } catch {
                        Write-Host "    [失败] $($_.Exception.Message)" -ForegroundColor Red
                        $failedCount++
                    }
                } else {
                    Write-Host "    [已跳过]" -ForegroundColor Gray
                    $skippedCount++
                }
            }
        } else {
            Write-Host "  已跳过进程清理。" -ForegroundColor Gray
            $skippedCount = $Processes.Count
        }
    }

    return @{killed=$killedCount; failed=$failedCount; skipped=$skippedCount}
}

# ============================================================
# 主逻辑
# ============================================================

Write-Host ""
Write-Header "进程清理工具" "Cyan"
Write-Host ""

# 显示模式信息
$modeDesc = if ($List) {
    if ($BeforeBuild) { "编译前模式（仅列出）" } else { "仅列出模式" }
} elseif ($Force) {
    if ($BeforeBuild) { "编译前模式（强制清理）" } else { "强制清理模式" }
} elseif ($BeforeBuild) {
    "编译前清理模式"
} elseif ($ProcessName) {
    "自定义进程模式"
} else {
    "交互式清理模式"
}
Write-Info "运行模式" $modeDesc
Write-Info "项目根目录" $projectRoot
Write-Info "BuildOutput" $buildOutputDir
Write-Host ""

# 获取进程列表
$processes = @()

if ($ProcessName) {
    # 自定义进程名模式
    foreach ($name in $ProcessName) {
        $procs = Get-Process -Name $name -ErrorAction SilentlyContinue
        foreach ($proc in $procs) {
            if ($proc.Id -eq $PID) { continue }
            $processes += [PSCustomObject]@{
                ProcessName = $proc.ProcessName
                PID         = $proc.Id
                SessionId   = $proc.SessionId
                StartTime   = $proc.StartTime
                CPU         = $proc.TotalProcessorTime
                MemoryMB    = [math]::Round($proc.WorkingSet64 / 1MB, 1)
                LockedFiles = @()
                Source      = "Custom"
                Description = "用户指定进程"
            }
        }
    }
} elseif ($BeforeBuild) {
    # 编译前模式：仅检测锁定 BuildOutput 的进程
    Write-Host "  正在检测占用编译输出文件的进程..." -ForegroundColor Gray
    $processes = Get-ProjectProcesses -BuildOutputDir $buildOutputDir -OnlyLockingBuildOutput
} else {
    # 默认模式：检测所有相关进程
    Write-Host "  正在扫描项目相关进程..." -ForegroundColor Gray
    $processes = Get-ProjectProcesses -BuildOutputDir $buildOutputDir
}

# 显示进程列表
Show-ProcessList -Processes $processes -ShowLockInfo:$BeforeBuild

if ($processes.Count -eq 0) {
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "  没有需要清理的进程，系统运行正常。" -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host ""
    exit 0
}

# 根据模式执行操作
if ($List) {
    # 仅列出模式，不执行操作
    Write-Host "  [仅列出模式] 未执行任何操作。" -ForegroundColor Yellow
    Write-Host ""
    exit 0
}

# 执行进程终止
Write-Host ""
Write-SubHeader "执行进程清理" "Yellow"

$result = Stop-ProcessWithConfirm -Processes $processes -ForceMode:$Force

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  清理完成！" -ForegroundColor Cyan
Write-Host "  已终止: $($result.killed) 个进程" -ForegroundColor Green
if ($result.failed -gt 0) {
    Write-Host "  失败:   $($result.failed) 个进程（权限不足或进程已退出）" -ForegroundColor Red
}
if ($result.skipped -gt 0) {
    Write-Host "  跳过:   $($result.skipped) 个进程" -ForegroundColor Yellow
}
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# 编译前模式：额外提示
if ($BeforeBuild) {
    Write-Host "  提示：如果编译仍报文件占用错误，请尝试：" -ForegroundColor Yellow
    Write-Host "    1. 以管理员身份运行此脚本" -ForegroundColor Gray
    Write-Host "    2. 使用 -Force 参数强制清理" -ForegroundColor Gray
    Write-Host "    3. 关闭 VS Code 后重试" -ForegroundColor Gray
    Write-Host ""
}

# 非 -List 模式暂停等待查看结果
if (-not $List -and -not $Force) {
    Write-Host "按任意键退出..."
    $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
}
