<#
.SYNOPSIS
    通用脚本运行器框架 - 提供 CLI 脚本运行器的标准模式

.DESCRIPTION
    封装了脚本运行器的通用模式：
    - 参数解析与操作分发
    - 脚本列表展示
    - 配置信息展示
    - 目录结构展示
    - 帮助文本展示
    - 文件内容展示
    - 可操作列表展示
    - 交互选择循环

    用法:
        . "$PSScriptRoot\ScriptRunner.ps1"   # 点引入加载所有函数
        $runner = New-ScriptRunner -Config $config -RunAction $runAction
        $runner.Invoke($args)

.NOTES
    此脚本设计为通过点引入（dot-source）方式加载，不直接执行。

    $runAction 签名约定（框架统一管理）:
        param([string]$Action, [string]$Arg = "")
        - $Action: "run" 表示运行脚本，$Arg 为脚本文件名
        - $Action: ExtraCommand 名称（如 "log"），$Arg 为额外参数
        - 框架保证：运行脚本后不会退出交互循环，ExtraCommand 执行后也不会退出
#>

# ============================================
#  创建脚本运行器实例
# ============================================

function New-ScriptRunner {
    <#
    .SYNOPSIS
        创建脚本运行器实例

    .DESCRIPTION
        创建一个脚本运行器对象，封装了参数解析、操作分发、交互选择等通用逻辑。
        调用方只需提供配置和自定义的运行逻辑。

    .PARAMETER Config
        配置哈希表，必须包含以下键：
        - ProjectRoot   : 项目根目录
        - ScriptsDir    : 脚本目录
        - FileDrivenDir : FileDriven 目录（可选，用于目录结构展示）
        - Aliases       : 路径别名哈希表（可选）

    .PARAMETER RunAction
        脚本块，接收两个参数：
        - $Action: "run" 表示运行脚本，$Arg 为脚本文件名
        - $Action: ExtraCommand 名称（如 "log"），$Arg 为额外参数
        例如: { param($Action, $Arg) if ($Action -eq "run") { Run-Test $Arg } }

    .PARAMETER Title
        运行器标题（可选，默认为"脚本运行器"）

    .PARAMETER ScriptName
        当前脚本文件名（可选，默认为自动检测）

    .PARAMETER SelectScriptPath
        SelectScript.ps1 的路径（可选，默认为同目录下的 SelectScript.ps1）

    .PARAMETER FilePattern
        脚本文件扩展名过滤（可选，默认为 ".json|.txt"）

    .PARAMETER ExtraCommands
        额外命令哈希表（可选），格式: @{ "build" = "构建脚本"; "deploy" = "部署" }
        这些命令会显示在可操作列表和帮助中，框架统一管理调用。

    .OUTPUTS
        PSObject，包含 Invoke 方法的脚本运行器对象
    #>
    param(
        [Parameter(Mandatory=$true)]
        [hashtable]$Config,

        [Parameter(Mandatory=$true)]
        [scriptblock]$RunAction,

        [Parameter(Mandatory=$false)]
        [string]$Title = "脚本运行器",

        [Parameter(Mandatory=$false)]
        [string]$ScriptName = "",

        [Parameter(Mandatory=$false)]
        [string]$SelectScriptPath = "",

        [Parameter(Mandatory=$false)]
        [string]$FilePattern = ".json|.txt",

        [Parameter(Mandatory=$false)]
        [hashtable]$ExtraCommands = @{}
    )

    # 自动检测脚本名
    if (-not $ScriptName) {
        $ScriptName = Split-Path -Leaf $MyInvocation.ScriptName
    }

    # 默认 SelectScript 路径
    if (-not $SelectScriptPath) {
        $SelectScriptPath = Join-Path $PSScriptRoot "SelectScript.ps1"
    }

    # 构建运行器对象
    $runner = [PSCustomObject]@{
        Config            = $Config
        RunAction         = $RunAction
        Title             = $Title
        ScriptName        = $ScriptName
        SelectScriptPath  = $SelectScriptPath
        FilePattern       = $FilePattern
        ExtraCommands     = $ExtraCommands
    }

    # 添加 Invoke 方法
    $runner | Add-Member -MemberType ScriptMethod -Name "Invoke" -Value {
        param(
            [string]$ScriptName = "",
            [switch]$List,
            [switch]$Info,
            [string]$Show = "",
            [switch]$Dirs,
            [switch]$Help,
            [string]$Log = ""
        )

        # 处理 --show 参数（后跟文件名）
        if ($Show) {
            $ScriptName = "--show"
        }

        # 确定操作模式
        $action = ""
        if ($List)    { $action = "list" }
        elseif ($Info)  { $action = "info" }
        elseif ($Dirs)  { $action = "dirs" }
        elseif ($Help)  { $action = "help" }
        elseif ($ScriptName -eq "--show") { $action = "show" }
        elseif ($Log -ne "") { $action = "log" }

        elseif ($ScriptName) { $action = "run" }
        else            { $action = "interactive" }

        # ★ 上下文精简：仅交互模式（无参数）打印可操作列表。
        #   直接 run/list/info/dirs/help/show/log 等操作不再输出 ~50 行菜单样板噪声，
        #   减少每次调用（含即时指令/脚本）向管道/上下文输出与结果无关的内容。
        if ($action -eq "interactive") {
            $this.ShowActionableList()
        }

        # 操作分发
        switch ($action) {
            "list"        { $this.ShowList() }
            "info"        { $this.ShowInfo() }
            "dirs"        { $this.ShowDirs() }
            "help"        { $this.ShowHelp() }
            "show"        { $this.ShowFile($Show) }
            # --log 命令行参数：框架统一调用 $runAction("log", $Log)
            "log"         { $this.InvokeRunActionSafe("log", $Log) }

            # --run 命令行参数：框架统一调用 $runAction("run", $ScriptName)
            "run"         { $this.InvokeRunActionSafe("run", $ScriptName) }
            "interactive" { $this.ShowInteractive() }
        }

        # 框架统一暂停：非交互模式下，手动模式暂停
        # 让 list/info/dirs/help/show/run/log 等操作都受运行模式控制
        # ★ 管道模式（Test-PipeMode）强制跳过 Read-Host——管道无 TTY 可读，阻塞即卡死
        if ($action -ne "interactive" -and (Get-RunMode) -eq "manual" -and -not (Test-PipeMode)) {
            Write-Host ""
            Write-Host "  [手动模式] 运行结束，按回车键退出"
            Read-Host "按回车键退出"
        }
    }


    # 添加 InvokeRunActionSafe 方法（框架统一管理 $runAction 调用，捕获异常防止闪退）
    $runner | Add-Member -MemberType ScriptMethod -Name "InvokeRunActionSafe" -Value {
        param([string]$Action, [string]$Arg = "")

        try {
            # 框架统一调用约定：$runAction($Action, $Arg)
            # $Action: "run" 或 ExtraCommand 名称（如 "log"）
            # $Arg: 额外参数（如脚本文件名或日志命令参数）
            $this.RunAction.Invoke($Action, $Arg)
        }
        catch {
            Write-Host ""
            Write-Host "[警告] 操作 '$Action' 执行异常: $_" -ForegroundColor Yellow
            Write-Host ""
        }
    }

    # 添加 ShowList 方法
    $runner | Add-Member -MemberType ScriptMethod -Name "ShowList" -Value {
        Show-ScriptList -ScriptsDir $this.Config.ScriptsDir -FilePattern $this.FilePattern
    }

    # 添加 ShowInfo 方法
    $runner | Add-Member -MemberType ScriptMethod -Name "ShowInfo" -Value {
        Show-ConfigInfo -Config $this.Config -RunMode (Get-RunMode) -Title $this.Title
    }

    # 添加 ShowDirs 方法
    $runner | Add-Member -MemberType ScriptMethod -Name "ShowDirs" -Value {
        $dirPath = $this.Config.FileDrivenDir
        if (-not $dirPath) { $dirPath = $this.Config.ProjectRoot }
        Show-DirectoryStructure -RootPath (Join-Path $dirPath "DrivenConfig")
    }

    # 添加 ShowHelp 方法
    $runner | Add-Member -MemberType ScriptMethod -Name "ShowHelp" -Value {
        Show-HelpText -ScriptName $this.ScriptName -Title $this.Title -ExtraCommands $this.ExtraCommands
    }

    # 添加 ShowFile 方法
    $runner | Add-Member -MemberType ScriptMethod -Name "ShowFile" -Value {
        param([string]$FileName)
        Show-FileContent -FileName $FileName -ScriptsDir $this.Config.ScriptsDir -FilePattern $this.FilePattern
    }

    # 添加 ShowActionableList 方法
    $runner | Add-Member -MemberType ScriptMethod -Name "ShowActionableList" -Value {
        Show-ActionList -Title $this.Title -ScriptName $this.ScriptName -RunMode (Get-RunMode) `
            -ScriptsDir $this.Config.ScriptsDir -ExtraCommands $this.ExtraCommands
    }

    # 添加 ShowInteractive 方法
    $runner | Add-Member -MemberType ScriptMethod -Name "ShowInteractive" -Value {
        # ★ 管道模式（Test-PipeMode）禁止进入交互选择循环——交互依赖 Read-Host 等待用户按键，
        #   管道输入无 TTY 可读会直接无限阻塞。管道输入的 JSON 请求体/序号已在 run 分支处理。
        if (Test-PipeMode) {
            Write-Host ""
            Write-Host "  [管道模式] 跳过交互选择界面（管道输入无 TTY，交互菜单不可用）" -ForegroundColor Yellow
            Write-Host "            请通过管道/参数直接投喂脚本名或 JSON 请求体。" -ForegroundColor Yellow
            Write-Host ""
            return
        }

        $scriptsDir = $this.Config.ScriptsDir
        $runMode = Get-RunMode
        $selectScript = $this.SelectScriptPath
        $fileDrivenDir = $this.Config.FileDrivenDir

        # 防止无限循环的安全计数器（设为较大值，避免用户操作受限）
        $loopCount = 0
        $maxLoops = 100

        :loop while ($true) {
            # 安全计数器：防止意外无限循环
            $loopCount++
            if ($loopCount -gt $maxLoops) {
                Write-Host "[错误] 交互循环异常终止（超过 $maxLoops 次迭代）" -ForegroundColor Red
                return
            }

            # 将 ExtraCommands 的键也加入有效命令列表
            $extraValidCommands = @{}
            foreach ($extraCmd in $this.ExtraCommands.Keys) {
                $extraValidCommands[$extraCmd.ToLower()] = $true
            }

            $output = & $selectScript `
                -ScriptsDir $scriptsDir `
                -RunMode $runMode `
                -Title "$($this.Title) - 脚本选择" `
                -ShowDirInfo `
                -DirInfoPath "$fileDrivenDir\DrivenConfig" `
                -ExtraValidCommands $extraValidCommands

            # 检查 SelectScript 是否执行成功
            if ($LASTEXITCODE -ne 0) {
                Write-Host "[错误] 脚本选择器异常退出（错误码: $LASTEXITCODE）" -ForegroundColor Red
                return
            }

            # 检查输出是否为空
            if (-not $output) {
                Write-Host "[错误] 脚本选择器无输出" -ForegroundColor Red
                return
            }

            # 过滤空行和模式提示行，取最后一行作为结果
            $lines = $output | Where-Object { 
                $_ -and $_ -notmatch '^\s*$' -and $_ -notmatch '^\s*\[当前模式:'
            }
            $lastLine = $lines[-1]

            if ([string]::IsNullOrEmpty($lastLine)) {
                # 退出
                return
            }

            # 检查是否为退出标记
            if ($lastLine -eq '__EXIT__') {
                return
            }

            # 检查是否为命令关键字（list/info/dirs/show/help/mode）
            $cmdName = $lastLine.Trim().ToLower()
            if ($cmdName -eq "list")  { $this.ShowList();  continue loop }
            if ($cmdName -eq "info")  { $this.ShowInfo();  continue loop }
            if ($cmdName -eq "dirs")  { $this.ShowDirs();  continue loop }
            if ($cmdName -eq "show")  {
                $showFile = Read-Host "请输入要显示的文件名"
                if ($showFile) { $this.ShowFile($showFile) }
                continue loop
            }
            if ($cmdName -eq "help")  { $this.ShowHelp();  continue loop }
            if ($cmdName -eq "mode")  { Switch-RunMode; $runMode = Get-RunMode; continue loop }
            if ($cmdName -eq "log")   { Switch-LogOption; continue loop }

            # 检查是否为 ExtraCommands 中的命令
            $extraCmdMatch = $false
            foreach ($extraCmd in $this.ExtraCommands.Keys) {
                if ($cmdName -eq $extraCmd.ToLower()) {
                    $extraCmdMatch = $true
                    break
                }
            }
            if ($extraCmdMatch) {
                # 框架统一管理 ExtraCommands 调用：
                # 传 $cmdName（如 "log"）和空字符串（无额外参数）
                # 调用方 $runAction 中根据 $Action 判断是 "run" 还是 ExtraCommand
                $this.InvokeRunActionSafe($cmdName, "")
                continue loop
            }


            # 验证最后一行是否为有效的文件名（包含扩展名）
            if ($lastLine -notmatch '\.\w+$') {
                Write-Host "[错误] 选择结果无效: $lastLine" -ForegroundColor Red
                continue loop
            }

            # 选中的脚本文件名（可能是相对路径如 "Math/math_functions_test.json"）
            # 框架统一调用 $runAction("run", $lastLine)
            $this.InvokeRunActionSafe("run", $lastLine)

            # ★ 2026-07-26 修复：检测是否来自管道输入（如 echo 5 | 交互启动.bat）
            #   管道模式下一次运行后自动退出交互循环，避免再次 SelectScript 阻塞在 Read-Host
            try {
                if ($Host.UI.RawUI.KeyAvailable) {
                    # 有按键等待 -> 非管道模式，继续循环
                    continue loop
                }
            } catch {
                # KeyAvailable 在非交互环境中可能抛出异常 -> 管道模式，退出循环
                return
            }
            # 无按键等待 -> 管道模式，退出循环
            return

        }
    }

    return $runner
}

# ============================================
#  通用功能函数
# ============================================

function Show-ScriptList {
    <#
    .SYNOPSIS
        列出可用脚本（带序号，递归扫描子目录）
    #>
    param(
        [Parameter(Mandatory=$true)]
        [string]$ScriptsDir,

        [Parameter(Mandatory=$false)]
        [string]$FilePattern = ".json|.txt"
    )

    Write-Header "可用脚本"
    Write-Host "  ($ScriptsDir)"
    Write-Host ""
    $scripts = Get-ScriptFiles -ScriptsDir $ScriptsDir -FilePattern $FilePattern -Recurse
    $index = 1
    $scripts | ForEach-Object { 
        # 计算相对路径
        $relativePath = $_.FullName.Substring($ScriptsDir.Length + 1)
        Write-Host ("  [{0,2}] {1}" -f $index, $relativePath)
        $index++
    }
    Write-Host ""
}

function Show-ConfigInfo {
    <#
    .SYNOPSIS
        显示配置信息
    #>
    param(
        [Parameter(Mandatory=$true)]
        [hashtable]$Config,

        [Parameter(Mandatory=$false)]
        [string]$RunMode = "auto",

        [Parameter(Mandatory=$false)]
        [string]$Title = "配置信息"
    )

    Write-Header "$Title 配置信息"
    Write-Host ""
    Write-Info "项目根目录" $Config.ProjectRoot
    if ($Config.FileDrivenDir) {
        Write-Info "FileDriven 目录" $Config.FileDrivenDir
    }
    Write-Info "脚本目录" $Config.ScriptsDir
    $envMode = if ($env:FILEDRIVEN_RUN_MODE) { $env:FILEDRIVEN_RUN_MODE } else { "未设置" }
    Write-Info "运行模式" "$RunMode ($envMode)"
    Write-Host ""

    if ($Config.Aliases -and $Config.Aliases.Keys.Count -gt 0) {
        Write-Host "  ============================================"
        Write-Host "  路径别名（FILEDRIVEN_ALIAS_*）"
        Write-Host "  ============================================"
        foreach ($key in $Config.Aliases.Keys) {
            Write-Host ("  FILEDRIVEN_ALIAS_{0}={1}" -f $key, $Config.Aliases[$key])
        }
        Write-Host ""
    }
}

function Show-DirectoryStructure {
    <#
    .SYNOPSIS
        显示目录结构
    #>
    param(
        [Parameter(Mandatory=$true)]
        [string]$RootPath
    )

    Write-Header "目录结构"
    Write-Host ""
    Write-Host "  $RootPath"
    Write-Host ""
    if (Test-Path $RootPath) {
        Get-ChildItem "$RootPath" -Directory | ForEach-Object {
            $count = @(Get-ChildItem $_.FullName -File).Count
            Write-Host ("    {0,-20} ({1} 个文件)" -f ($_.Name + "\"), $count)
        }
    }
    Write-Host ""
}

function Show-HelpText {
    <#
    .SYNOPSIS
        显示帮助文本
    #>
    param(
        [Parameter(Mandatory=$true)]
        [string]$ScriptName,

        [Parameter(Mandatory=$false)]
        [string]$Title = "脚本运行器",

        [Parameter(Mandatory=$false)]
        [hashtable]$ExtraCommands = @{}
    )

    Write-Header "$Title - 帮助"
    Write-Host ""
    Write-Host "  用法:"
    Write-Host "    $ScriptName <脚本文件名>      运行指定脚本（默认自动模式）"
    Write-Host "    $ScriptName --list            列出可用脚本"
    Write-Host "    $ScriptName --info            显示配置信息"
    Write-Host "    $ScriptName --show <文件名>   显示指定脚本内容"
    Write-Host "    $ScriptName --dirs            显示目录结构"
    Write-Host "    $ScriptName --help            显示本帮助"

    # 显示额外命令
    if ($ExtraCommands.Keys.Count -gt 0) {
        foreach ($cmd in $ExtraCommands.Keys) {
            Write-Host ("    $ScriptName --{0,-20}{1}" -f $cmd, $ExtraCommands[$cmd])
        }
    }

    Write-Host ""
    Write-Host "  模式说明:"
    Write-Host "     自动模式（默认）— 运行完自动关闭窗口"
    Write-Host "     手动模式         — 运行完后暂停等待按键"
    Write-Host "     不传参进入交互选择界面，输入 mode 可切换自动/手动模式"
    Write-Host ""
    Write-Host "  示例:"
    Write-Host "    $ScriptName <脚本文件名>"
    Write-Host "    $ScriptName --list"
    Write-Host "    $ScriptName --show <文件名>"
    Write-Host "    $ScriptName --info"
    Write-Host ""
}

function Show-FileContent {
    <#
    .SYNOPSIS
        显示指定文件内容
    #>
    param(
        [Parameter(Mandatory=$true)]
        [string]$FileName,

        [Parameter(Mandatory=$true)]
        [string]$ScriptsDir,

        [Parameter(Mandatory=$false)]
        [string]$FilePattern = ".json|.txt"
    )

    $showPath = Join-Path $ScriptsDir $FileName
    if (-not (Test-Path $showPath)) {
        Write-Host "[错误] 文件不存在: $showPath"
        Write-Host ""
        Write-Host "可用的脚本:"
        $scripts = Get-ScriptFiles -ScriptsDir $ScriptsDir -FilePattern $FilePattern -Recurse
        $index = 1
        $scripts | ForEach-Object { 
            $relativePath = $_.FullName.Substring($ScriptsDir.Length + 1)
            Write-Host ("  [{0,2}] {1}" -f $index, $relativePath)
            $index++
        }
        exit 1
    }

    Write-Host ""
    Write-Host ("=" * 44)
    Write-Host "  文件: $FileName"
    Write-Host "  路径: $showPath"
    Write-Host ("=" * 44)
    Write-Host ""
    Get-Content $showPath
    Write-Host ""
}

function Show-ActionList {
    <#
    .SYNOPSIS
        显示可操作列表
    #>
    param(
        [Parameter(Mandatory=$false)]
        [string]$Title = "脚本运行器",

        [Parameter(Mandatory=$false)]
        [string]$ScriptName = "",

        [Parameter(Mandatory=$false)]
        [string]$RunMode = "auto",

        [Parameter(Mandatory=$false)]
        [string]$ScriptsDir = "",

        [Parameter(Mandatory=$false)]
        [hashtable]$ExtraCommands = @{}
    )

    Write-Host ""
    Write-Host ("=" * 44)
    Write-Host "  $Title"
    Write-Host ("=" * 44)
    Write-Host ""
    # ★ 修复：使用动态序号，避免硬编码
    $actionItems = @(
        @{ Name = "运行脚本"; Desc = "$ScriptName <脚本文件名>"; Extra = "不传参进入交互选择界面" }
        @{ Name = "列出可用脚本"; Desc = "$ScriptName --list" }
        @{ Name = "显示配置信息"; Desc = "$ScriptName --info" }
        @{ Name = "显示脚本内容"; Desc = "$ScriptName --show <文件名>" }
        @{ Name = "显示目录结构"; Desc = "$ScriptName --dirs" }
        @{ Name = "显示帮助"; Desc = "$ScriptName --help" }
    )

    Write-Host "  可用操作:"
    Write-Host ""
    $idx = 1
    foreach ($item in $actionItems) {
        Write-Host ("    [{0}] {1}" -f $idx, $item.Name)
        Write-Host ("        {0}" -f $item.Desc)
        if ($item.Extra) {
            Write-Host ("        {0}" -f $item.Extra)
        }
        Write-Host ""
        $idx++
    }

    # 显示额外命令（动态序号）
    if ($ExtraCommands.Keys.Count -gt 0) {
        foreach ($cmd in $ExtraCommands.Keys) {
            Write-Host ("    [{0}] {1}" -f $idx, $ExtraCommands[$cmd])
            Write-Host ("        $ScriptName --{0}" -f $cmd)
            Write-Host ""
            $idx++
        }
    }

    Write-Host ""
    Write-Host "  当前模式: $RunMode"
    if ($ScriptsDir) {
        Write-Host "  脚本目录: $ScriptsDir"
    }
    Write-Host ""
    Write-Host ("=" * 44)
    Write-Host ""
}
