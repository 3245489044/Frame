<#
.SYNOPSIS
    日志查看器 - 从 JSON 文件读取日志（无需额外进程）

.DESCRIPTION
    提供分层交互的日志查看功能：
    - 第一层：日志概览（统计摘要 + 操作菜单）
    - 第二层：按条件筛选（级别/分类/模块）
    - 第三层：查看具体条目

    ★ 优化：不再通过 --pipe 启动额外 Launcher 进程，
    ★ 改为直接读取 Launcher 在 Shutdown 阶段导出的 JSON 日志文件。
    ★ 文件路径：{ProjectRoot}/logs/batch_{batchId}_latest.json

.PARAMETER ProjectRoot
    项目根目录

.PARAMETER LogsDir
    日志文件目录（可选，默认 {ProjectRoot}/logs）

.PARAMETER ExecCommand
    内部参数，用于递归调用时传递具体的 log 命令

.EXAMPLE
    . "$PSScriptRoot\LogViewer.ps1"
    Show-LogViewer -ProjectRoot "D:\...\WorkFlowPrj"

.EXAMPLE
    Show-LogViewer -ProjectRoot "D:\...\WorkFlowPrj" -ExecCommand "level ERROR"
#>

# ============================================
#  日志查看器入口
# ============================================

function Show-LogViewer {
    <#
    .SYNOPSIS
        日志查看器入口 - 分层交互查看日志
    #>
    param(
        [Parameter(Mandatory=$true)]
        [string]$ProjectRoot,

        [Parameter(Mandatory=$false)]
        [string]$LogsDir = "",

        [Parameter(Mandatory=$false)]
        [string]$ExecCommand = ""
    )

    # 确定日志目录
    if (-not $LogsDir) {
        $LogsDir = Join-Path $ProjectRoot "logs"
    }

    try {
        if ($ExecCommand) {
            # 有具体命令时，直接执行并显示结果
            Show-LogDetail -LogsDir $LogsDir -Command $ExecCommand
        } else {
            # 无命令时，显示日志概览（第一层）
            Show-LogOverview -LogsDir $LogsDir
        }
    }
    finally {
        # ★ 优化：不再需要清理管道进程
    }
}

# ============================================
#  日志数据加载 — 从 JSON 文件读取
# ============================================

$script:LogDataCache = $null   # 缓存日志数据
$script:LogDataTimestamp = $null  # 缓存时间戳

function Get-LogData {
    <#
    .SYNOPSIS
        从 JSON 文件加载日志数据
        ★ 所有日志都是分批的：只读取 batch_{batchId}_latest.json
        ★ 无指定批次时自动读取最新的 batch_*_latest.json
    #>
    param(
        [Parameter(Mandatory=$true)]
        [string]$LogsDir,

        [Parameter(Mandatory=$false)]
        [switch]$ForceReload,

        [Parameter(Mandatory=$false)]
        [string]$BatchId = ""
    )

    # 检查缓存是否有效（5 秒内不重复读取）
    if (-not $ForceReload -and $script:LogDataCache -and $script:LogDataTimestamp) {
        $elapsed = [DateTime]::Now - $script:LogDataTimestamp
        if ($elapsed.TotalSeconds -lt 5) {
            return $script:LogDataCache
        }
    }

    # 检查日志目录是否存在
    if (-not (Test-Path $LogsDir)) {
        Write-Debug "日志目录不存在: $LogsDir"
        return $null
    }

    # 检查 .empty 标记（无日志）
    $emptyMarker = Join-Path $LogsDir ".empty"
    if (Test-Path $emptyMarker) {
        Write-Debug "日志为空（.empty 标记存在）"
        return $null
    }

    # ★ 确定要读取的文件
    $targetFile = $null
    if ($BatchId) {
        # 指定批次：读取 batch_{BatchId}_latest.json
        $targetFile = Join-Path $LogsDir "batch_${BatchId}_latest.json"
    } else {
        # 未指定批次：自动检测最新的 batch_*_latest.json
        $batchLatestFiles = Get-ChildItem -Path $LogsDir -Filter "batch_*_latest.json" | Sort-Object LastWriteTime -Descending
        if ($batchLatestFiles.Count -gt 0) {
            $targetFile = $batchLatestFiles[0].FullName
        }
    }

    if ($targetFile -and (Test-Path $targetFile)) {
        try {
            $json = Get-Content $targetFile -Raw -Encoding UTF8
            $data = $json | ConvertFrom-Json
            $script:LogDataCache = $data
            $script:LogDataTimestamp = [DateTime]::Now
            return $data
        }
        catch {
            Write-Debug "读取 $targetFile 失败: $_"
        }
    }

    return $null
}

function Clear-LogDataCache {
    <#
    .SYNOPSIS
        清除日志数据缓存（强制下次重新读取）
    #>
    $script:LogDataCache = $null
    $script:LogDataTimestamp = $null
}

# ============================================
#  日志查询函数（替代 Invoke-LogCommand）
# ============================================

function Get-LogEntries {
    <#
    .SYNOPSIS
        从日志数据中查询符合条件的条目
    .PARAMETER Level
        按级别筛选（如 "ERROR", "INFO"）
    .PARAMETER Category
        按分类筛选（如 "Launcher_Lifecycle"）
    .PARAMETER Module
        按模块筛选（如 "Launcher"）
    .PARAMETER Query
        搜索关键词
    .PARAMETER Limit
        最大返回条数
    #>
    param(
        [Parameter(Mandatory=$true)]
        [string]$LogsDir,

        [Parameter(Mandatory=$false)]
        [string]$Level = "",

        [Parameter(Mandatory=$false)]
        [string]$Category = "",

        [Parameter(Mandatory=$false)]
        [string]$Module = "",

        [Parameter(Mandatory=$false)]
        [string]$Query = "",

        [Parameter(Mandatory=$false)]
        [int]$Limit = 0
    )

    $data = Get-LogData -LogsDir $LogsDir
    if (-not $data -or -not $data.entries) {
        return @()
    }

    $entries = $data.entries

    # 按级别筛选
    if ($Level) {
        $entries = $entries | Where-Object { $_.level -eq $Level }
    }

    # 按分类筛选
    if ($Category) {
        $entries = $entries | Where-Object { $_.category -eq $Category }
    }

    # 按模块筛选
    if ($Module) {
        $entries = $entries | Where-Object { $_.module -eq $Module }
    }

    # 搜索关键词
    if ($Query) {
        $entries = $entries | Where-Object {
            $_.message -match $Query -or
            $_.category -match $Query -or
            $_.module -match $Query
        }
    }

    # 限制条数
    if ($Limit -gt 0 -and $entries.Count -gt $Limit) {
        $entries = $entries | Select-Object -First $Limit
    }

    return $entries
}

# ============================================
#  第一层：日志概览
# ============================================

function Show-LogOverview {
    <#
    .SYNOPSIS
        显示日志概览（统计摘要 + 操作菜单）
    #>
    param(
        [Parameter(Mandatory=$true)]
        [string]$LogsDir
    )

    # 加载日志数据
    $data = Get-LogData -LogsDir $LogsDir -ForceReload
    $totalCount = if ($data -and $data.totalCount) { $data.totalCount } else { 0 }

    # 获取各级别统计
    $levelStats = @{}
    if ($data -and $data.summary -and $data.summary.byLevel) {
        foreach ($entry in $data.summary.byLevel.PSObject.Properties) {
            $levelStats[$entry.Name] = [int]$entry.Value
        }
    }

    # 获取各模块统计
    $moduleStats = @{}
    if ($data -and $data.summary -and $data.summary.byModule) {
        foreach ($entry in $data.summary.byModule.PSObject.Properties) {
            $moduleStats[$entry.Name] = [int]$entry.Value
        }
    }

    $errorCount = if ($levelStats.ContainsKey("ERROR")) { $levelStats["ERROR"] } else { 0 }
    $fatalCount = if ($levelStats.ContainsKey("FATAL")) { $levelStats["FATAL"] } else { 0 }

    # 显示日志概览
    Clear-Host
    Write-Host ""
    Write-Host ("=" * 60)
    Write-Host "  日志查看器"
    Write-Host ("=" * 60)
    Write-Host ""

    # ★ 显示当前查看的批次信息
    $batchInfo = ""
    if ($data -and $data.batchId) {
        $batchInfo = "批次: $($data.batchId)"
    }
    if ($batchInfo) {
        Write-Host "  $batchInfo" -ForegroundColor Cyan
        Write-Host ""
    }

    # ★ 检查是否有批次日志文件
    $batchFiles = Get-ChildItem -Path $LogsDir -Filter "batch_*_latest.json" -ErrorAction SilentlyContinue
    $hasLogFiles = ($batchFiles -and $batchFiles.Count -gt 0)
    if (-not $hasLogFiles) {
        Write-Host "  [提示] 未找到批次日志文件" -ForegroundColor Yellow
        Write-Host "  [提示] 请先运行测试，Launcher 会在退出时自动导出日志" -ForegroundColor Yellow
        Write-Host ""
    }

    Write-Host "  日志统计:"
    Write-Host ("  {0,-20} {1}" -f "总日志数:", $totalCount)
    Write-Host ""
    Write-Host "  按级别:"
    $levelOrder = @("INFO", "WARN", "ERROR", "DEBUG", "FATAL")
    $levelColors = @{
        "INFO" = "Green"
        "WARN" = "Yellow"
        "ERROR" = "Red"
        "DEBUG" = "Cyan"
        "FATAL" = "DarkRed"
    }
    foreach ($lv in $levelOrder) {
        $count = if ($levelStats.ContainsKey($lv)) { $levelStats[$lv] } else { 0 }
        $color = $levelColors[$lv]
        $bar = "█" * [Math]::Min($count / 5, 40)
        Write-Host ("    {0,-6} : {1,4}  {2}" -f $lv, $count, $bar) -ForegroundColor $color
    }
    Write-Host ""
    Write-Host ("  错误统计:  Errors={0}, Fatals={1}, Total={2}" -f $errorCount, $fatalCount, ($errorCount + $fatalCount))
    Write-Host ""
    Write-Host "  按模块:"
    foreach ($mod in $moduleStats.Keys) {
        $count = $moduleStats[$mod]
        if ($count -gt 0) {
            Write-Host ("    {0,-12} : {1,4}" -f $mod, $count)
        }
    }
    Write-Host ""
    Write-Host ("=" * 60)
    Write-Host "  可用操作:"
    Write-Host ""
    Write-Host "    [1] 按级别查看     → 查看指定级别的日志详情"
    Write-Host "    [2] 按分类查看     → 查看指定分类的日志详情"
    Write-Host "    [3] 按模块查看     → 查看指定模块的日志详情"
    Write-Host "    [4] 查看错误日志   → 查看所有 ERROR/FATAL 日志"
    Write-Host "    [5] 查看所有日志   → 查看全部日志条目"
    Write-Host "    [6] 刷新统计       → 重新从文件加载日志"
    Write-Host "    [0] 返回上级菜单"
    Write-Host ""
    Write-Host ("=" * 60)
    Write-Host ""

    # 交互选择
    $choice = Read-Host "  请输入操作编号"
    if ([string]::IsNullOrEmpty($choice)) { return }
    
    switch ($choice.Trim()) {
        "1" { Show-LevelSelector -LogsDir $LogsDir }
        "2" { Show-CategorySelector -LogsDir $LogsDir }
        "3" { Show-ModuleSelector -LogsDir $LogsDir }
        "4" { Show-LogDetail -LogsDir $LogsDir -Command "errors" }
        "5" { Show-LogDetail -LogsDir $LogsDir -Command "" }
        "6" { Show-LogOverview -LogsDir $LogsDir }
        "0" { return }
        default {
            Write-Host "  无效选择，请重新输入" -ForegroundColor Red
            Read-Host "  按回车键继续"
            Show-LogOverview -LogsDir $LogsDir
        }
    }
}

# ============================================
#  第二层：级别选择器
# ============================================

function Show-LevelSelector {
    <#
    .SYNOPSIS
        显示级别选择菜单
    #>
    param(
        [Parameter(Mandatory=$true)]
        [string]$LogsDir
    )

    Clear-Host
    Write-Host ""
    Write-Host ("=" * 60)
    Write-Host "  按级别查看日志"
    Write-Host ("=" * 60)
    Write-Host ""
    Write-Host "  可选级别:"
    Write-Host ""
    Write-Host "    [1] INFO    - 信息日志"
    Write-Host "    [2] WARN    - 警告日志"
    Write-Host "    [3] ERROR   - 错误日志"
    Write-Host "    [4] DEBUG   - 调试日志"
    Write-Host "    [5] FATAL   - 致命错误"
    Write-Host "    [0] 返回上级"
    Write-Host ""
    Write-Host ("=" * 60)
    Write-Host ""

    $choice = Read-Host "  请选择级别"
    
    $levelMap = @{
        "1" = "INFO"
        "2" = "WARN"
        "3" = "ERROR"
        "4" = "DEBUG"
        "5" = "FATAL"
    }

    if ($levelMap.ContainsKey($choice.Trim())) {
        Show-LogDetail -LogsDir $LogsDir -Command ("level " + $levelMap[$choice.Trim()])
    } elseif ($choice.Trim() -eq "0") {
        Show-LogOverview -LogsDir $LogsDir
    } else {
        Write-Host "  无效选择" -ForegroundColor Red
        Read-Host "  按回车键继续"
        Show-LevelSelector -LogsDir $LogsDir
    }
}

# ============================================
#  第二层：分类选择器
# ============================================

function Show-CategorySelector {
    <#
    .SYNOPSIS
        显示分类选择菜单（从日志数据中动态获取分类列表）
    #>
    param(
        [Parameter(Mandatory=$true)]
        [string]$LogsDir
    )

    # 从日志数据中获取所有分类
    $data = Get-LogData -LogsDir $LogsDir
    $allCategories = @()
    if ($data -and $data.entries) {
        $allCategories = $data.entries | ForEach-Object { $_.category } | Sort-Object -Unique
    }

    # 构建分类选项
    $categoryOptions = @()
    $index = 1
    foreach ($cat in $allCategories) {
        $categoryOptions += @{
            Index = $index
            Name = $cat
            Description = $cat
        }
        $index++
    }

    while ($true) {
        Clear-Host
        Write-Host ""
        Write-Host ("=" * 60)
        Write-Host "  按分类查看日志（可多选）"
        Write-Host ("=" * 60)
        Write-Host ""

        if ($categoryOptions.Count -eq 0) {
            Write-Host "  (无可用分类)" -ForegroundColor Yellow
            Write-Host ""
            Write-Host "  提示: 请先运行测试生成日志"
            Write-Host ""
            Read-Host "  按回车键返回"
            Show-LogOverview -LogsDir $LogsDir
            return
        }

        # 按模块分组显示
        $grouped = $categoryOptions | Group-Object { 
            $parts = $_.Name -split '/'
            if ($parts.Count -gt 1) { $parts[0] } else { "其他" }
        }

        foreach ($group in $grouped) {
            Write-Host "  [$($group.Name)]"
            foreach ($opt in $group.Group) {
                $pathDisplay = $opt.Name
                if ($opt.Name -match '/') {
                    $pathDisplay = $opt.Name.Substring($group.Name.Length + 1)
                }
                Write-Host ("    [{0,2}] {1,-20}" -f $opt.Index, $pathDisplay)
            }
            Write-Host ""
        }

        Write-Host "  [A] 全部日志"
        Write-Host "  [0] 返回上级"
        Write-Host ""
        Write-Host ("=" * 60)
        Write-Host ""

        $choice = Read-Host "  请输入序号（可输入多个，如 2,3,4），或 A=全部，0=返回"
        $choice = $choice.Trim()

        if ($choice -eq "0") {
            Show-LogOverview -LogsDir $LogsDir
            return
        }

        if ($choice -eq "A" -or $choice -eq "a") {
            Show-LogDetail -LogsDir $LogsDir -Command ""
            return
        }

        # 解析用户输入的序号列表
        $selectedIndices = @()
        $parts = $choice -split ',' | ForEach-Object { $_.Trim() }
        $valid = $true
        foreach ($part in $parts) {
            if ([int]::TryParse($part, [ref]$null)) {
                $idx = [int]$part
                $matched = $categoryOptions | Where-Object { $_.Index -eq $idx }
                if ($matched) {
                    $selectedIndices += $idx
                } else {
                    Write-Host "  无效序号: $idx" -ForegroundColor Red
                    $valid = $false
                }
            } else {
                Write-Host "  无效输入: $part" -ForegroundColor Red
                $valid = $false
            }
        }

        if (-not $valid -or $selectedIndices.Count -eq 0) {
            Write-Host "  请重新输入" -ForegroundColor Yellow
            Read-Host "  按回车键继续"
            continue
        }

        # 构建查询路径
        $selectedPaths = @()
        foreach ($idx in $selectedIndices) {
            $opt = $categoryOptions | Where-Object { $_.Index -eq $idx }
            if ($opt) {
                $selectedPaths += $opt.Name
            }
        }

        if ($selectedPaths.Count -eq 0) {
            Write-Host "  未选择有效分类" -ForegroundColor Yellow
            Read-Host "  按回车键继续"
            continue
        }

        # 使用 log query 命令查询
        $pathStr = $selectedPaths -join ","
        $queryCommand = "query path=`"$pathStr`" limit=200"
        Show-LogDetail -LogsDir $LogsDir -Command $queryCommand
        return
    }
}

# ============================================
#  第二层：模块选择器
# ============================================

function Show-ModuleSelector {
    <#
    .SYNOPSIS
        显示模块选择菜单
    #>
    param(
        [Parameter(Mandatory=$true)]
        [string]$LogsDir
    )

    # 从日志数据中获取所有模块
    $data = Get-LogData -LogsDir $LogsDir
    $allModules = @()
    if ($data -and $data.entries) {
        $allModules = $data.entries | ForEach-Object { $_.module } | Sort-Object -Unique
    }

    Clear-Host
    Write-Host ""
    Write-Host ("=" * 60)
    Write-Host "  按模块查看日志"
    Write-Host ("=" * 60)
    Write-Host ""
    Write-Host "  可选模块:"
    Write-Host ""

    $moduleOptions = @()
    $index = 1
    foreach ($mod in $allModules) {
        $moduleOptions += @{ Index = $index; Name = $mod }
        Write-Host ("    [{0}] {1}" -f $index, $mod)
        $index++
    }

    if ($allModules.Count -eq 0) {
        Write-Host "  (无可用模块)" -ForegroundColor Yellow
    }

    Write-Host "    [0] 返回上级"
    Write-Host ""
    Write-Host ("=" * 60)
    Write-Host ""

    $choice = Read-Host "  请选择模块"
    
    $selectedModule = $moduleOptions | Where-Object { $_.Index -eq [int]$choice.Trim() }
    if ($selectedModule) {
        Show-LogDetail -LogsDir $LogsDir -Command ("module " + $selectedModule.Name)
    } elseif ($choice.Trim() -eq "0") {
        Show-LogOverview -LogsDir $LogsDir
    } else {
        Write-Host "  无效选择" -ForegroundColor Red
        Read-Host "  按回车键继续"
        Show-ModuleSelector -LogsDir $LogsDir
    }
}

# ============================================
#  第三层：日志详情展示
# ============================================

function Show-LogDetail {
    <#
    .SYNOPSIS
        显示日志详情，支持分页查看
    #>
    param(
        [Parameter(Mandatory=$true)]
        [string]$LogsDir,

        [Parameter(Mandatory=$true)]
        [string]$Command
    )

    # 解析命令并获取日志条目
    $entries = @()
    $summary = "日志详情"

    if ($Command -eq "" -or $Command -eq "all") {
        # 全部日志
        $entries = Get-LogEntries -LogsDir $LogsDir
        $summary = "全部日志"
    } elseif ($Command -match "^level\s+(.+)$") {
        $level = $matches[1].Trim()
        $entries = Get-LogEntries -LogsDir $LogsDir -Level $level
        $summary = "级别: $level"
    } elseif ($Command -match "^module\s+(.+)$") {
        $module = $matches[1].Trim()
        $entries = Get-LogEntries -LogsDir $LogsDir -Module $module
        $summary = "模块: $module"
    } elseif ($Command -match "^query\s+path=`"(.+?)`"") {
        $paths = $matches[1] -split ','
        $allEntries = @()
        foreach ($path in $paths) {
            $path = $path.Trim()
            $matched = Get-LogEntries -LogsDir $LogsDir -Category $path
            $allEntries += $matched
        }
        $entries = $allEntries | Sort-Object { $_.timestamp }
        $summary = "分类: $($paths -join ', ')"
    } elseif ($Command -eq "errors") {
        $entries = Get-LogEntries -LogsDir $LogsDir -Level "ERROR"
        $entries += Get-LogEntries -LogsDir $LogsDir -Level "FATAL"
        $entries = $entries | Sort-Object { $_.timestamp }
        $summary = "错误日志 (ERROR/FATAL)"
    } else {
        # 尝试作为搜索关键词
        $entries = Get-LogEntries -LogsDir $LogsDir -Query $Command
        $summary = "搜索: $Command"
    }

    $totalEntries = $entries.Count

    # 分页参数
    $pageSize = 20
    $currentPage = 0
    $totalPages = [Math]::Max(1, [Math]::Ceiling($totalEntries / $pageSize))

    while ($true) {
        Clear-Host
        Write-Host ""
        Write-Host ("=" * 60)
        Write-Host "  $summary"
        Write-Host ("=" * 60)
        Write-Host ""

        if ($totalEntries -eq 0) {
            Write-Host "  (无匹配日志条目)" -ForegroundColor Yellow
        } else {
            # 计算当前页的条目
            $startIdx = $currentPage * $pageSize
            $endIdx = [Math]::Min($startIdx + $pageSize - 1, $totalEntries - 1)
            
            Write-Host "  第 $($currentPage + 1)/$totalPages 页，共 $totalEntries 条"
            Write-Host "  显示第 $($startIdx + 1)-$($endIdx + 1) 条"
            Write-Host ""

            for ($i = $startIdx; $i -le $endIdx; $i++) {
                $entry = $entries[$i]
                $line = "[$($entry.timestamp)] [$($entry.level)] [$($entry.category)] $($entry.message)"
                
                # 根据级别着色
                if ($entry.level -eq "ERROR") {
                    Write-Host "  $line" -ForegroundColor Red
                } elseif ($entry.level -eq "FATAL") {
                    Write-Host "  $line" -ForegroundColor DarkRed
                } elseif ($entry.level -eq "WARN") {
                    Write-Host "  $line" -ForegroundColor Yellow
                } elseif ($entry.level -eq "DEBUG") {
                    Write-Host "  $line" -ForegroundColor Cyan
                } else {
                    Write-Host "  $line"
                }
            }
        }

        Write-Host ""
        Write-Host ("=" * 60)
        Write-Host "  操作:"
        Write-Host "    [n] 下一页    [p] 上一页    [f] 第一页    [l] 最后一页"
        Write-Host "    [s] 搜索      [a] 显示全部    [0] 返回上级"
        Write-Host ""
        Write-Host ("=" * 60)
        Write-Host ""

        $action = Read-Host "  输入操作"

        switch ($action.Trim().ToLower()) {
            "n" {
                if ($currentPage -lt $totalPages - 1) { $currentPage++ }
                else { Write-Host "  已经是最后一页" -ForegroundColor Yellow; Read-Host "  按回车键继续" }
            }
            "p" {
                if ($currentPage -gt 0) { $currentPage-- }
                else { Write-Host "  已经是第一页" -ForegroundColor Yellow; Read-Host "  按回车键继续" }
            }
            "f" { $currentPage = 0 }
            "l" { $currentPage = $totalPages - 1 }
            "s" {
                $keyword = Read-Host "  输入搜索关键词"
                if ($keyword) {
                    $matchedEntries = $entries | Where-Object {
                        $_.message -match $keyword -or
                        $_.category -match $keyword -or
                        $_.module -match $keyword
                    }
                    if ($matchedEntries.Count -gt 0) {
                        Write-Host "  找到 $($matchedEntries.Count) 条匹配:" -ForegroundColor Green
                        foreach ($entry in $matchedEntries) {
                            Write-Host "    [$($entry.timestamp)] [$($entry.level)] $($entry.message)"
                        }
                    } else {
                        Write-Host "  未找到匹配" -ForegroundColor Yellow
                    }
                    Read-Host "  按回车键继续"
                }
            }
            "a" {
                # 显示全部（不分页）
                Clear-Host
                Write-Host ""
                Write-Host ("=" * 60)
                Write-Host "  $summary (全部 $totalEntries 条)"
                Write-Host ("=" * 60)
                Write-Host ""
                foreach ($entry in $entries) {
                    $line = "[$($entry.timestamp)] [$($entry.level)] [$($entry.category)] $($entry.message)"
                    if ($entry.level -eq "ERROR") {
                        Write-Host "  $line" -ForegroundColor Red
                    } elseif ($entry.level -eq "FATAL") {
                        Write-Host "  $line" -ForegroundColor DarkRed
                    } elseif ($entry.level -eq "WARN") {
                        Write-Host "  $line" -ForegroundColor Yellow
                    } elseif ($entry.level -eq "DEBUG") {
                        Write-Host "  $line" -ForegroundColor Cyan
                    } else {
                        Write-Host "  $line"
                    }
                }
                Write-Host ""
                Read-Host "  按回车键继续"
            }
            "0" {
                # 返回上级（回到概览）
                Show-LogOverview -LogsDir $LogsDir
                return
            }
            default {
                Write-Host "  无效操作" -ForegroundColor Red
                Read-Host "  按回车键继续"
            }
        }
    }
}

# ============================================
#  导出函数
# ============================================

# Export-ModuleMember -Function Show-LogViewer
