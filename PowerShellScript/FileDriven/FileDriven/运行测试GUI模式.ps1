<#
.SYNOPSIS
    FileDriven GUI 模式测试运行器

.DESCRIPTION
    使用 --show-gui 强制 GUI 模式执行测试脚本，显示真实 WinForms 窗口，操作真实控件。
    不传参进入交互选择界面，输入 mode 可切换自动/手动模式。
    详细文档见: FileDriven\Docs\ 目录

.PARAMETER ScriptName
    要运行的测试脚本文件名（可选）。不传参进入交互选择界面。

.PARAMETER List
    列出可用脚本。

.PARAMETER Info
    显示配置信息。

.PARAMETER Show
    显示指定脚本内容，后跟文件名。

.PARAMETER Dirs
    显示目录结构。

.PARAMETER Help
    显示帮助。

.EXAMPLE
    .\运行测试GUI模式.ps1 test_math_workflow.json
    .\运行测试GUI模式.ps1 --list
    .\运行测试GUI模式.ps1 --show test_math_operations.json
    .\运行测试GUI模式.ps1 --info
#>

param(
    [Parameter(Position=0)]
    [string]$ScriptName = "",

    [switch]$List,
    [switch]$Info,
    [string]$Show = "",
    [switch]$Dirs,
    [switch]$Help,

    # 日志查看模式
    # 不带参数：进入分层交互式日志查看器
    # 带参数：直接执行 log 命令，如 --log "level ERROR"
    [string]$Log = "",

    # 无头模式：不传 --show-gui，Launcher 以 CLI 模式（虚拟控件）执行，不弹出真实窗口。
    # 适用脚本/即时指令执行（结果经命名管道回读）；GUI 渲染/布局检查等功能不适用。
    [switch]$Headless
)

# ============================================
#  行级诊断日志（管道模式卡死可观测性）
#  日志文件: %TEMP%\fileDriven_launcher.log
#  所有关键步骤（Raw-Body 识别 / 合成脚本 / Launcher 启动 / 管道连接 / 超时）均落盘，
#  即使进程卡死也有最后轨迹可定位。
# ============================================

$script:LauncherLogPath = Join-Path $env:TEMP "fileDriven_launcher.log"

function Write-LauncherLog {
    <#
    .SYNOPSIS
        追加一行诊断日志到 %TEMP%\fileDriven_launcher.log（带时间戳）
    #>
    param([string]$Message)
    $stamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss.fff"
    try {
        ("[$stamp] $Message") | Add-Content -Path $script:LauncherLogPath -Encoding UTF8
    } catch {
        # 日志写入失败不阻断主流程
    }
}

function Write-HangDiagnostics {
    <#
    .SYNOPSIS
        卡死诊断：在管道空闲超时（判定测试卡死）强制终止前，向终端打印详细现场。
        现场 = 停滞位置（最后回读的脚本标题/结果行）+ Launcher 轨迹日志尾部 + 取证建议。
        说明：驱动侧只能拿到"停在哪一步"；锁归属/托管栈需进程侧看门狗或离线 dump（见设计稿）。
    #>
    param(
        [string]$ScriptPath,
        [int]$LauncherPid,
        [double]$StallSeconds,
        [string[]]$RecentOutput,
        [string]$LauncherLogPath,
        [int]$LauncherLogTail = 30
    )
    $stamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss.fff"
    Write-Host ""
    Write-Host ("=" * 70) -ForegroundColor Red
    Write-Host "  [卡死诊断] 测试无输出 $([int]$StallSeconds) 秒，判定卡死（PID=$LauncherPid 仍存活）" -ForegroundColor Red
    Write-Host "  时间: $stamp" -ForegroundColor Gray
    Write-Host "  脚本: $ScriptPath" -ForegroundColor Gray
    Write-Host ("-" * 70)
    Write-Host "  停滞位置（最后回读的 $($RecentOutput.Count) 行，倒序 = 最近最先）:" -ForegroundColor Cyan
    if ($RecentOutput.Count -gt 0) {
        $idx = $RecentOutput.Count - 1
        $shown = 0
        while ($idx -ge 0 -and $shown -lt 10) {
            $t = ($RecentOutput[$idx] | Out-String).TrimEnd("`r","`n")
            if ($t) { Write-Host "    $t" -ForegroundColor Gray; $shown++ }
            $idx--
        }
    } else {
        Write-Host "    （无任何回读输出——可能卡在启动/连接阶段，见下方 Launcher 轨迹）" -ForegroundColor Yellow
    }
    Write-Host ("-" * 70)
    Write-Host "  Launcher 轨迹日志尾部（$LauncherLogPath）:" -ForegroundColor Cyan
    try {
        $tailLines = @(Get-Content $LauncherLogPath -ErrorAction Stop | Select-Object -Last $LauncherLogTail)
        if ($tailLines.Count -gt 0) { foreach ($l in $tailLines) { Write-Host "    $l" -ForegroundColor Gray } }
        else { Write-Host "    （空）" -ForegroundColor Gray }
    } catch {
        Write-Host "    （读取失败: $($_.Exception.Message)）" -ForegroundColor Gray
    }
    Write-Host ("-" * 70)
    Write-Host "  取证建议:" -ForegroundColor Cyan
    Write-Host "    1) 若需锁归属/托管栈定性，请在卡死时尽快于另一终端抓全量 dump（本脚本打印后会自动终止进程）:" -ForegroundColor Gray
    $dumpPath = Join-Path $env:TEMP ("hang_" + $LauncherPid + ".dmp")
    $dumpCmd  = "dotnet-dump collect -p $LauncherPid -o `"$dumpPath`""
    Write-Host "         $dumpCmd" -ForegroundColor Gray
    Write-Host "    2) 命令级运行期遥测已接线(卡死 15s/30s 即时报 + 进程快照)；HangProbe 资源/持锁方级取证仍待 S3 接线。" -ForegroundColor Gray
    Write-Host ("=" * 70)
}

Write-LauncherLog "===== 运行测试GUI模式.ps1 启动 ====="
Write-LauncherLog "PSScriptRoot=$PSScriptRoot"

# ============================================
#  加载通用工具（从 PowerShellScript\Modules\ 加载）
# ============================================

$scriptModulesDir = Resolve-Path "$PSScriptRoot\..\..\..\PowerShellScript\Modules"

Write-LauncherLog "scriptModulesDir=$scriptModulesDir"

# ★ 使用 ProjectPaths 模块统一管理路径
Import-Module (Join-Path $scriptModulesDir "Core\ProjectPaths.psm1") -Force -DisableNameChecking

# ★ 同时点引入 ReadProjectConfig.ps1 到全局作用域
#   ProjectPaths.psm1 内部点引入了 ReadProjectConfig.ps1，但函数在模块作用域内，
#   后续 FileDrivenTestUtils.ps1 等脚本在全局作用域调用 Get-FileDrivenDir 等函数时找不到。
#   因此需要在全局作用域也点引入一次。
. (Join-Path $scriptModulesDir "Core\ReadProjectConfig.ps1")

. "$scriptModulesDir\FileDriven\FileDrivenTestUtils.ps1"
. "$scriptModulesDir\FileDriven\ScriptRunner.ps1"
. "$scriptModulesDir\FileDriven\LogOptions.ps1"

# ============================================
#  获取配置
# ============================================

$config = Get-FileDrivenConfig -ScriptPath $MyInvocation.MyCommand.Path
$projectRoot    = $config.ProjectRoot
$fileDrivenDir  = $config.FileDrivenDir
$scriptsDir     = $config.ScriptsDir
$aliases        = $config.Aliases

Write-LauncherLog "配置加载完成: ProjectRoot=$projectRoot"
Write-LauncherLog "ScriptsDir=$scriptsDir"
Write-LauncherLog "管道模式(Test-PipeMode)=$(Test-PipeMode) | FILEDRIVEN_INSTANT_BODY=$(if ($env:FILEDRIVEN_INSTANT_BODY) { '<非空>' } else { '(空)' })"

# ============================================
#  定义运行逻辑
#  框架约定：$runAction 签名统一为 param([string]$Action, [string]$Arg = "")
#  - $Action = "run": 运行测试脚本，$Arg 为脚本文件名
#  - $Action = "log": 日志查看，$Arg 为日志命令参数（如 "level ERROR"）
#  - $Action = "analyze": 代码路径分析，$Arg 为分析路径（可选）
# ============================================

$runAction = {
    param([string]$Action, [string]$Arg = "")

    if ($Action -eq "log") {
        # ★ 日志查看器改为从 JSON 文件读取，不再需要 LauncherDir
        # ★ 不再启动 --pipe 额外进程，也不需要 Find-LauncherDir
        # 加载日志查看器
        . "$PSScriptRoot\LogViewer.ps1"

        if ($Arg) {
            # --log "level ERROR" 带参数：直接执行单条 log 命令
            Write-Host ""
            Write-Host ("=" * 60)
            Write-Host "  执行日志命令: log $Arg"
            Write-Host ("=" * 60)
            Write-Host ""

            # ★ 优化：从 JSON 文件读取日志，不再通过管道进程
            Show-LogViewer -ProjectRoot $projectRoot -ExecCommand $Arg
        } else {
            # --log 不带参数：进入分层交互式日志查看器
            Show-LogViewer -ProjectRoot $projectRoot
        }
        return
    }

    # ★ Docker 容器管理分支 — 调用 Invoke-DockerReset.ps1
    if ($Action -eq "docker") {
        Write-Host ""
        Write-Host ("=" * 60)
        Write-Host "  Docker 容器管理"
        Write-Host ("=" * 60)
        Write-Host ""

        $dockerResetScript = Join-Path $projectRoot "PowerShellScript\Build\Invoke-DockerReset.ps1"
        if (-not (Test-Path $dockerResetScript)) {
            Write-Host "  [错误] 找不到 Docker 重置脚本: $dockerResetScript" -ForegroundColor Red
            Read-Host "  按回车键继续"
            return
        }

        # 显示 Docker 容器状态
        Write-Host "  正在检查 Docker 容器状态..."
        Write-Host ""

        # 检查 docker 是否可用
        $dockerOk = Get-Command "docker" -ErrorAction SilentlyContinue
        if (-not $dockerOk) {
            Write-Host "  [错误] Docker 不可用，请先安装 Docker Desktop" -ForegroundColor Red
            Read-Host "  按回车键继续"
            return
        }

        # 显示容器状态
        $containerName = "workflow-mcp-env"
        $state = docker inspect --format "{{.State.Status}}" $containerName 2>&1
        if ($LASTEXITCODE -eq 0) {
            $uptime = docker inspect --format "{{.State.StartedAt}}" $containerName 2>&1
            $mem = docker stats $containerName --no-stream --format "{{.MemUsage}}" 2>&1
            Write-Host "  容器: $containerName" -ForegroundColor White
            Write-Host "  状态: $(switch ($state) { 'running' { '✅ 运行中' } 'exited' { '⏹️  已停止' } default { "❓ $state" } })"
            if ($state -eq "running") {
                Write-Host "  内存: $mem"
            }
        } else {
            Write-Host "  容器: $containerName" -ForegroundColor White
            Write-Host "  状态: ❌ 不存在"
        }

        Write-Host ""
        Write-Host "  请选择操作:"
        Write-Host "    [1] L1 软重启（docker restart，释放内存）"
        Write-Host "    [2] L2 标准重置（docker compose down + up，保留数据）"
        Write-Host "    [3] L3 深度重置（docker compose down -v + up，清除所有数据）"
        Write-Host "    [0] 返回"
        Write-Host ""

        $choice = Read-Host "  请输入选择 (0-3)"
        switch ($choice) {
            "1" {
                Write-Host ""
                Write-Host "  执行 L1 软重启..."
                & $dockerResetScript -Level L1 -Force
            }
            "2" {
                Write-Host ""
                Write-Host "  执行 L2 标准重置..."
                & $dockerResetScript -Level L2 -Force
            }
            "3" {
                Write-Host ""
                Write-Host "  执行 L3 深度重置..."
                $backupChoice = Read-Host "  是否备份命名卷？(y/n, 默认 n)"
                if ($backupChoice -eq "y") {
                    $backupPath = Join-Path $projectRoot "Storage\DockerBackup"
                    & $dockerResetScript -Level L3 -Force -BackupPath $backupPath
                } else {
                    & $dockerResetScript -Level L3 -Force
                }
            }
            default {
                Write-Host "  已取消"
            }
        }

        Write-Host ""
        Read-Host "  按回车键继续"
        return
    }

    # ★ 新增：analyze 分支 — 运行 CodePathExplorer 代码路径分析
    if ($Action -eq "analyze") {

        Write-Host ""
        Write-Host ("=" * 60)
        Write-Host "  CodePathExplorer 代码路径分析"
        Write-Host ("=" * 60)
        Write-Host ""

        # 确定分析路径
        $analyzePath = if ($Arg) { $Arg } else { $projectRoot }

        # 生成批次 ID
        $timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
        $batchId = "CodePathAnalysis_$timestamp"

        # 日志输出目录
        $logDir = Join-Path $projectRoot "logs"

        Write-Host "  分析路径: $analyzePath"
        Write-Host "  日志目录: $logDir"
        Write-Host "  批次 ID : $batchId"
        Write-Host ""

        # ★ 批次注册由 C# 端 BatchLoggerBridge.RegisterBatch() 在程序启动时完成
        # ★ 不再通过环境变量传递批次信息（新日志模块使用 C# 内部 API）
        # 批次 ID 通过 --batch-id 命令行参数传递给 CodePathExplorer

        # 构建 CodePathExplorer 项目路径
        $cpeProject = Join-Path $projectRoot "Utilitys\CodePathExplorer\CodePathExplorer.csproj"

        if (-not (Test-Path $cpeProject)) {
            Write-Host "  [错误] 找不到 CodePathExplorer 项目: $cpeProject" -ForegroundColor Red
            Read-Host "  按回车键继续"
            return
        }

        Write-Host "  正在启动 CodePathExplorer 分析..."
        Write-Host ""

        # 调用 dotnet run 执行分析
        # --no-interactive 非交互模式（不等待按键）
        # --log-output 指定日志输出目录
        # --batch-id 指定批次 ID
        $process = Start-Process -FilePath "dotnet" -ArgumentList "run --project `"$cpeProject`" -- `"$analyzePath`" --no-interactive --log-output `"$logDir`" --batch-id `"$batchId`"" -Wait -PassThru -NoNewWindow

        if ($process.ExitCode -ne 0) {
            Write-Host ""
            Write-Host "  [错误] CodePathExplorer 分析失败，错误码: $($process.ExitCode)" -ForegroundColor Red
        } else {
            Write-Host ""
            Write-Host "  ============================================"
            Write-Host "   分析完成！"
            Write-Host "   分析结果已记录到日志系统"
            Write-Host "   可通过 'log' 命令查看分析详情"
            Write-Host "  ============================================"
        }

        Write-Host ""
        Read-Host "  按回车键继续"
        return
    }


    # ★ 以下代码仅 run 分支需要，log 分支不经过此处

    # 查找启动器目录
    $findScript = Join-Path $scriptModulesDir "FileDriven\FindLauncherDir.ps1"
    $launcherDir = Find-LauncherDir -FindScriptPath $findScript -FileDrivenDir $fileDrivenDir
    if (-not $launcherDir) {
        Write-Host "[ERROR] 无法获取启动器目录" -ForegroundColor Red
        Read-Host "按回车键退出"
        return
    }

    # 设置路径别名环境变量
    Set-AliasEnvironment -Aliases $aliases -Overrides @{ "Output" = $launcherDir }

    # ★ 无头模式判定：-Headless 开关 → CLI 模式（虚拟控件，不弹窗）。
    #   仅脚本/即时指令执行适用；依赖 GUI 渲染/布局检查的功能不适用。
    $headlessRun = $Headless
    Write-LauncherLog "无头模式(headlessRun)=$headlessRun"

    # ★ 即时指令（Raw-Body）支持：
    #   当 $Arg 不是脚本文件路径，而是 JSON 请求体（含 "action" 且无 "commands"）时，
    #   视为即时指令——合成单命令脚本（exec AgentUi_RunJson requestJson=<body>）执行，
    #   复用既有 Launcher/管道/结果读回管线。使 LLM/脚本可经管道或参数直接投喂请求体，
    #   无需编写脚本文件。
    $synthesizedTemp = $null
    $isRawBody = $false
    $bodyParseError = $null
    $bodyLooksLikeJson = $false
    if (-not [string]::IsNullOrWhiteSpace($Arg)) {
        $trimmedArg = $Arg.TrimStart()
        $bodyLooksLikeJson = $trimmedArg.StartsWith("{") -or $trimmedArg.StartsWith("[")
        $parsedBody = $null
        try { $parsedBody = $Arg | ConvertFrom-Json } catch { $parsedBody = $null; $bodyParseError = $_.Exception.Message }
        if ($null -ne $parsedBody) {
            $propNames = @($parsedBody.PSObject.Properties.Name)
            if (($propNames -contains "action") -and ($propNames -notcontains "commands")) {
                $isRawBody = $true
            }
        }
    }

    Write-LauncherLog "Raw-Body 判别: isRawBody=$isRawBody bodyLooksLikeJson=$bodyLooksLikeJson bodyParseError=$bodyParseError"

    if ($isRawBody) {
        $bodyJson = $parsedBody | ConvertTo-Json -Depth 20 -Compress
        Write-Host "  [即时指令] 检测到 JSON 请求体，作为即时指令执行（不经脚本文件）" -ForegroundColor Cyan
        Write-Host "            请求体: $bodyJson" -ForegroundColor Gray
        $synthesizedTemp = Join-Path $env:TEMP "fileDriven_instant_$([System.Guid]::NewGuid().ToString('N')).json"
        Write-LauncherLog "[即时指令] 合成临时脚本: $synthesizedTemp"
        $scriptObj = [ordered]@{
            description = "即时指令（Raw-Body 合成脚本）"
            script      = "instant_command"
            commands    = @(
                [ordered]@{
                    action = "exec"
                    target = "AgentUi_RunJson"
                    value  = "requestJson=$bodyJson"
                    output = "resultTextBox"
                }
            )
        }
        $scriptObj | ConvertTo-Json -Depth 20 | Set-Content -Path $synthesizedTemp -Encoding UTF8
        $Arg = $synthesizedTemp
    } elseif ($bodyLooksLikeJson) {
        # 形似 JSON 请求体（以 {/[ 开头）但解析失败 → 给出解析诊断，而非"脚本不存在"
        Write-Host "[错误] JSON 请求体解析失败（可能是转义或引号问题）:" -ForegroundColor Red
        Write-Host "  $bodyParseError" -ForegroundColor Red
        Write-Host "  请确认为合法 JSON（路径反斜杠需写为 \\）后重试。" -ForegroundColor Yellow
        Read-Host "按回车键退出"
        return
    }

    # 运行测试脚本（即时指令时直接使用合成临时脚本绝对路径）
    $testPath = if ($isRawBody) { $synthesizedTemp } else { Join-Path $scriptsDir $Arg }

    if (-not (Test-Path $testPath)) {
        Write-Host "[错误] 测试脚本不存在: $testPath"
        Write-Host ""
        Write-Host "可用的测试脚本:"
        $scripts = Get-ScriptFiles -ScriptsDir $scriptsDir
        $scripts | ForEach-Object { Write-Host "  $($_.Name)" }
        Read-Host "按回车键退出"
        return
    }

    $launcherExe = "$launcherDir\FileDriven.Launcher.exe"

    # S4·部署单轨校验：launcher 目录内关键 DLL 须不早于 CsharpPrj\BuildOutput 根布局（MainProj.sln 输出）最新构建。
    # 若 launcher 侧 DLL 早于 MainProj 输出 → 运行将加载旧 DLL（部署双轨风险），黄色告警提示重建 FileDriven.sln。
    # 注：固定比较根路径（不递归）——CsharpPrj\BuildOutput\Debug\ 等次级布局含测试构建产物，递归会命中错误基准。
    $deploymentCsharpRoot = Join-Path $projectRoot "CsharpPrj\BuildOutput"
    foreach ($deploymentKey in @("FrontEndDriven.dll", "Share.LayoutAnalysis.dll")) {
        $deployedPath = Join-Path $launcherDir $deploymentKey
        if (-not (Test-Path $deployedPath)) { continue }
        $canonicalPath = Join-Path $deploymentCsharpRoot $deploymentKey
        if (-not (Test-Path $canonicalPath)) { continue }
        $deployedTime = (Get-Item $deployedPath).LastWriteTime
        $canonicalTime = (Get-Item $canonicalPath).LastWriteTime
        if ($deployedTime -lt $canonicalTime) {
            Write-Host "[警告] 部署双轨风险: $deploymentKey 时间戳早于 CsharpPrj\BuildOutput 最新构建" -ForegroundColor Yellow
            Write-Host "        launcher=$deployedTime  CsharpPrj=$canonicalTime" -ForegroundColor Yellow
            Write-Host "        请重新构建 FileDriven.sln（MainProj.sln 先于 FileDriven.sln）以确保部署单轨" -ForegroundColor Yellow
        }
    }

    Write-Header "FileDriven GUI 模式测试运行器"
    Write-Host ""
    Write-Info "启动器" $launcherExe
    Write-Info "测试脚本" $testPath
    Write-Info "运行模式" $(if ($headlessRun) { "CLI (--headless，无窗口)" } else { "GUI (--show-gui)" })
    Write-Host ""
    if ($headlessRun) {
        Write-Host "  说明: 无头模式（CLI 虚拟控件），不弹出窗口"
        Write-Host "        结果经日志/命名管道回显"
    } else {
        Write-Host "  说明: 将显示真实 WinForms 窗口"
        Write-Host "        脚本中的 set/get 操作真实控件"
        Write-Host "        请勿在纯命令行环境中运行"
    }
    Write-Host ""
    Write-Header ""

    # 切换到项目根目录执行
    Push-Location $projectRoot

    try {
        # ★ 生成唯一管道名称，用于 PowerShell 与 Launcher 进程间通信
        # ★ Launcher 连接后，所有 Console.WriteLine 输出通过管道传回 PowerShell
        $pipeName = "FileDrivenPipe_$([System.Guid]::NewGuid().ToString("N"))"

        # ★ 创建命名管道服务器（在 PowerShell 端）
        # ★ Launcher 作为客户端连接，把 Console.Out 重定向到管道
        # ★ 使用 WaitForConnectionAsync 替代轮询 IsConnected，避免 .NET 版本兼容性问题
        $pipeServer = New-Object System.IO.Pipes.NamedPipeServerStream($pipeName, [System.IO.Pipes.PipeDirection]::In)
        $pipeReader = $null

        try {
            # 执行启动器（GUI 应用程序会自动打开新窗口；无头模式为 CLI 虚拟控件）
            if ($headlessRun) {
                Write-Host "  正在无头模式执行（CLI 虚拟控件，不弹窗）..."
            } else {
                Write-Host "  正在启动测试窗口..."
            }

            # ★ 读取日志输出模式环境变量（由 bat 脚本设置）
            # ★ Console 模式：日志直接打印到控制台，不写入数据库
            # ★ Database 模式（默认）：日志写入内存仓库，Shutdown 时刷入 SQLite
            $logOutputMode = [System.Environment]::GetEnvironmentVariable("LOG_OUTPUT_MODE")
            if ([string]::IsNullOrEmpty($logOutputMode)) {
                $logOutputMode = "database"
            }

            # ★ 读取 UI 排布打印选项（由 bat 脚本通过 FILEDRIVEN_LOG_OPTION 环境变量设置）
            # ★ 通过 LogOptions.ps1 模块转换为 --log 参数值
            $logCategories = Get-LogCategoriesFromOption

            # ★ 构建带管道名称的参数
            if ($headlessRun) {
                $baseArgs = "--pipe-name `"$pipeName`" --file `"$testPath`" --log-mode $logOutputMode"
            } else {
                $baseArgs = "--show-gui --pipe-name `"$pipeName`" --file `"$testPath`" --log-mode $logOutputMode"
            }
            if ($logCategories) {
                $baseArgs += " --log `"$logCategories`""
            }
            $runMode = Get-RunMode
            if ($runMode -eq "manual") {
                # 手动模式：不自动关闭
                $process = Start-Process -FilePath $launcherExe -ArgumentList $baseArgs -PassThru `
                    -WorkingDirectory $projectRoot
            } else {
                # 自动模式：使用 --auto-close 让启动器自动关闭
                if ($headlessRun) {
                    Write-Host "  [自动模式] 无头执行（--auto-close），等待测试完成..."
                } else {
                    Write-Host "  [自动模式] 启动测试窗口（--auto-close），等待测试完成..."
                }
                $process = Start-Process -FilePath $launcherExe -ArgumentList "$baseArgs --auto-close" -PassThru `
                    -WorkingDirectory $projectRoot
            }

            # ★ 等待 Launcher 连接管道（带超时保护）
            # ★ 使用 WaitForConnectionAsync + Task.WaitAny 实现可超时的异步等待
            # ★ 替代旧的轮询 IsConnected 方式，避免 .NET 版本兼容性问题
            Write-Host "  等待 Launcher 连接..."
            $pipeConnected = $false
            $connectMaxWaitMs = 15000  # 15 秒连接超时

            # ★ 启动异步等待连接
            $connectTask = $pipeServer.WaitForConnectionAsync()
            $timeoutTask = [System.Threading.Tasks.Task]::Delay($connectMaxWaitMs)
            $completedIndex = [System.Threading.Tasks.Task]::WaitAny($connectTask, $timeoutTask)

            if ($completedIndex -eq 0) {
                # 连接成功（connectTask 完成）
                $pipeConnected = $true
                $pipeReader = New-Object System.IO.StreamReader($pipeServer)
                Write-Host "  Launcher 已连接，开始接收输出..."
                Write-Host ""
            } else {
                # 超时（timeoutTask 完成）
                Write-Host ""
                Write-Host "[超时] Launcher 在 $connectMaxWaitMs ms 内未连接管道" -ForegroundColor Yellow

                # ★ 检查 Launcher 进程是否已退出
                if ($process.HasExited) {
                    Write-Host "[错误] Launcher 进程已退出，未能连接管道" -ForegroundColor Red
                    Write-Host "[错误] 请检查 Launcher 初始化是否成功" -ForegroundColor Red
                } else {
                    Write-Host "[超时] 正在强制终止 Launcher..." -ForegroundColor Yellow
                    $process.Kill()
                }

                # ★ 检查 Fallback 日志文件（Launcher 端管道连接失败时写入）
                # ★ 用于诊断管道连接失败的原因
                $fallbackLogs = Get-ChildItem "$env:TEMP\FileDriven_PipeFallback_*.log" `
                    | Sort-Object LastWriteTime -Descending `
                    | Select-Object -First 3
                if ($fallbackLogs) {
                    Write-Host ""
                    Write-Host "[诊断] 发现 Launcher Fallback 日志文件:" -ForegroundColor Cyan
                    foreach ($log in $fallbackLogs) {
                        Write-Host "  $($log.FullName)" -ForegroundColor Cyan
                        $logContent = Get-Content $log.FullName
                        foreach ($line in $logContent) {
                            Write-Host "    $line" -ForegroundColor Gray
                        }
                        Write-Host ""
                    }
                }
            }

            # ★ 实时读取管道数据，原样显示
            # ★ Launcher 的所有 Console.WriteLine 输出都会通过管道传过来
            # ★ 采用"空闲超时"而非"总墙钟超时"：只要测试持续产出输出就绝不强制中断，
            #   仅当连续 N 秒无任何输出时才判定为卡死（落实"正常运行不中断"原则）。
            # ★ 使用异步读取（ReadLineAsync），使空闲超时检查能够真正触发
            #   （阻塞式 ReadLine() 卡在等待时到不了超时检查点，会导致迟到的假超时误杀）。
            # ★ 强制终止前守卫：进程已正常退出时不再打印"[超时]"。
            # ★ 若管道未连接（$pipeConnected = $false），跳过读取循环
            if ($pipeConnected) {
                # ★ 管道模式（Test-PipeMode）空闲超时收紧为 30s——管道输入无 TTY，
                #   用户无法手动干预，长时间无输出按卡死处理比长时间等待更可诊断
                $idleTimeoutMs = if (Test-PipeMode) { 30000 } else { 120000 }
                $idleCheckMs   = 500     # 空闲检查间隔（毫秒）
                $idle = [System.Diagnostics.Stopwatch]::StartNew()
                # ★ 卡死诊断缓冲：保留最近 N 条回读输出，供空闲超时（判定卡死）时定位停滞步骤
                $recentOutput = [System.Collections.Generic.List[string]]::new()
                $recentOutputCap = 20
                $readTask = $null
                Write-LauncherLog "开始管道读取循环 | idleTimeoutMs=$idleTimeoutMs | 管道模式=$(Test-PipeMode)"

                while ($true) {
                    # ★ 复用/启动异步读取任务（同一 StreamReader 同时仅允许一个挂起读）
                    if ($null -eq $readTask -or $readTask.IsCompleted) {
                        if ($null -ne $readTask -and $readTask.IsCompleted) { $readTask.Dispose() }
                        $readTask = $pipeReader.ReadLineAsync()
                    }

                    # ★ 等待读完成或到达空闲检查点（不阻塞，可被超时中断）
                    $delayTask = [System.Threading.Tasks.Task]::Delay($idleCheckMs)
                    $idx = [System.Threading.Tasks.Task]::WaitAny($readTask, $delayTask)

                    # 读任务完成
                    if ($idx -eq 0 -and $readTask.IsCompleted) {
                        $line = $null
                        try {
                            $line = $readTask.GetAwaiter().GetResult()
                        } catch {
                            break  # 管道读取异常，视为已关闭
                        }
                        if ($null -ne $line) {
                            Write-Host $line
                            # ★ 维护卡死诊断缓冲（保序、只留最近 N 条，天然去掉空行噪声可选——保留全部便于定位）
                            $recentOutput.Add($line)
                            if ($recentOutput.Count -gt $recentOutputCap) {
                                $recentOutput.RemoveAt(0)
                            }
                            $idle.Restart()  # 有输出 → 重置空闲计时
                            continue
                        }
                        # ReadLineAsync 返回 null → 管道已关闭（EOF），测试结束
                        break
                    }

                    # 空闲超时检查：仅当进程仍在运行（确实卡死）才打印详细诊断并强制终止
                    if ($idle.ElapsedMilliseconds -gt $idleTimeoutMs) {
                        if ($process.HasExited) {
                            break  # 进程已退出但管道未关闭 → 正常结束，避免误报
                        }
                        $stallSeconds = [math]::Round($idle.ElapsedMilliseconds / 1000.0, 1)
                        # ★ 打印详细卡死诊断（停滞步骤/脚本/PID/轨迹日志尾部/取证建议），再强制终止
                        Write-HangDiagnostics `
                            -ScriptPath $testPath `
                            -LauncherPid $process.Id `
                            -StallSeconds $stallSeconds `
                            -RecentOutput $recentOutput `
                            -LauncherLogPath $script:LauncherLogPath
                        Write-Host ""
                        Write-Host "[超时] 连续 $([int]($idleTimeoutMs / 1000)) 秒无输出，判定测试卡死，正在强制终止..." -ForegroundColor Yellow
                        Write-LauncherLog "[卡死] 连续 $([int]($idleTimeoutMs / 1000)) 秒无输出，强制终止 Launcher PID=$($process.Id)"
                        if (-not $process.HasExited) { $process.Kill() }
                        break
                    }

                    # 进程已退出且管道已关闭 → 正常结束
                    if ($process.HasExited -and (-not $pipeServer.IsConnected -or $pipeServer.CanRead -eq $false)) {
                        break
                    }
                }
                if ($null -ne $readTask -and $readTask.IsCompleted) { $readTask.Dispose() }
            } else {
                # ★ 管道未连接，等待 Launcher 进程退出
                Write-Host "[警告] 管道未连接，等待 Launcher 进程退出..." -ForegroundColor Yellow
                $process.WaitForExit()
            }

            # 显示退出状态
            Write-Host ""
            Write-LauncherLog "Launcher 退出: ExitCode=$($process.ExitCode)"
            if ($process.ExitCode -ne 0) {
                Write-Host "[完成] 测试执行结束（错误码: $($process.ExitCode)）"
            } else {
                Write-Host "[完成] 测试执行结束"
            }

            if ($runMode -eq "manual" -and -not (Test-PipeMode)) {
                Write-Host ""
                Read-Host "按回车键继续"
            }
        }
        finally {
            if ($pipeReader) { $pipeReader.Dispose() }
            if ($pipeServer) { $pipeServer.Dispose() }
        }
    }
    finally {
        # 即时指令合成的临时脚本清理
        if ($synthesizedTemp -and (Test-Path $synthesizedTemp)) {
            Remove-Item $synthesizedTemp -Force
        }
        Pop-Location
    }
}

# ============================================
#  即时指令支持：bat 经环境变量 FILEDRIVEN_INSTANT_BODY 传入 JSON 请求体
#  （管道输入，规避命令行引号/空格拆分问题）。此处将其视为脚本名交给运行器，
#  run 分支会自动识别为即时指令（Raw-Body），不经文件解析。
#  优先级：显式命令行脚本参数优先；无命令行参数时采用管道请求体。
# ============================================
$envInstantBody = [System.Environment]::GetEnvironmentVariable("FILEDRIVEN_INSTANT_BODY")
if (-not [string]::IsNullOrWhiteSpace($envInstantBody) -and [string]::IsNullOrWhiteSpace($ScriptName)) {
    $ScriptName = $envInstantBody
}

# ============================================
#  预处理：解析数字参数为脚本路径
#  ★ 2026-07-26 新增：支持 echo 5 | 交互启动.bat 直接传序号
#    修复管道流被菜单层级消费的问题。数字序号直接对应脚本清单的序号，
#    无需经过 SelectScript.ps1 的 Read-Host 交互选择。
# ============================================

if ($ScriptName -match '^\d+$') {
    $seqNumber = [int]$ScriptName
    $extensions = @(".json", ".txt")
    $allScripts = Get-ChildItem -Path $scriptsDir -File -Recurse | 
        Where-Object { $_.Extension -in $extensions } | 
        Sort-Object FullName

    if ($seqNumber -ge 1 -and $seqNumber -le $allScripts.Count) {
        $resolvedPath = $allScripts[$seqNumber - 1].FullName.Substring($scriptsDir.Length + 1)
        Write-Host "  [自动解析] 序号 $seqNumber → $resolvedPath" -ForegroundColor Cyan
        $ScriptName = $resolvedPath
    } else {
        Write-Host "  [错误] 序号 $seqNumber 超出范围（1-$( $allScripts.Count)）" -ForegroundColor Red
        Write-Host ""
        pause
        exit 1
    }
}

# ============================================
#  创建运行器并执行
#  所有操作（包括 --log）统一由 ScriptRunner.Invoke 分发，
#  框架统一管理调用约定（$runAction($Action, $Arg)），
#  不再在顶层拦截，确保框架暂停逻辑对所有操作生效。
# ============================================

$runner = New-ScriptRunner `
    -Config $config `
    -RunAction $runAction `
    -Title "FileDriven GUI 模式测试运行器" `
    -ScriptName (Split-Path -Leaf $MyInvocation.MyCommand.Path) `
    -ExtraCommands @{
        "log" = "查看日志（分层交互式日志查看器）"
        "analyze" = "代码路径分析（CodePathExplorer）"
        "docker" = "Docker 容器管理（重启/重置）"
    }

$runner.Invoke($ScriptName, $List, $Info, $Show, $Dirs, $Help, $Log)


