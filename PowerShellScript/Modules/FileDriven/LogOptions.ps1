<#
.SYNOPSIS
    日志选项管理模块 — 提供 UI 排布打印选项的选择和切换

.DESCRIPTION
    参考 FileDrivenTestUtils.ps1 的 RunMode 管理风格（Get-RunMode/Set-RunMode/Switch-RunMode），
    提供日志选项的统一管理：
    - 通过环境变量 FILEDRIVEN_LOG_OPTION 持久化选项
    - 支持交互式切换（类似 mode 命令切换运行模式）
    - 将选项转换为 C# 端 --log 参数值

    日志选项说明：
      none    — 不打印 UI 排布（默认）
      summary — 仅打印控件位置摘要（名称 + 位置）
      report  — 打印完整布局报告（文本格式）
      json    — 打印 JSON 布局报告
      overlap — 打印布局报告 + 重叠检测
      all     — 全部打印（最详细）

    用法:
        . "$PSScriptRoot\LogOptions.ps1"   # 点引入加载所有函数
        $option = Get-LogOption              # 获取当前选项
        Set-LogOption "report"               # 设置选项
        Switch-LogOption                     # 交互式切换
        $logArg = Get-LogCategoriesFromOption  # 转换为 --log 参数

.NOTES
    此脚本设计为通过点引入（dot-source）方式加载，不直接执行。
    与 FileDrivenTestUtils.ps1 的 RunMode 管理保持一致的风格。
#>

# ============================================
#  日志选项定义
# ============================================

$script:LogOptionNames = @{
    "none"    = "不打印 UI 排布"
    "summary" = "仅打印控件位置摘要"
    "report"  = "打印完整布局报告（文本）"
    "json"    = "打印 JSON 布局报告"
    "overlap" = "打印布局报告 + 重叠检测"
    "all"     = "全部打印（最详细）"
}

# ============================================
#  日志选项管理
# ============================================

function Get-LogOption {
    <#
    .SYNOPSIS
        获取当前日志选项

    .DESCRIPTION
        从环境变量 FILEDRIVEN_LOG_OPTION 获取日志选项，
        未设置时默认为 none。

    .PARAMETER Default
        默认选项，默认为 "none"

    .OUTPUTS
        string: 日志选项名称（none/summary/report/json/overlap/all）
    #>
    param(
        [Parameter(Mandatory=$false)]
        [string]$Default = "none"
    )

    $option = $env:FILEDRIVEN_LOG_OPTION
    if ([string]::IsNullOrWhiteSpace($option)) {
        $option = $Default
    }
    return $option
}

function Set-LogOption {
    <#
    .SYNOPSIS
        设置日志选项到环境变量

    .PARAMETER Option
        日志选项名称（none/summary/report/json/overlap/all）
    #>
    param(
        [Parameter(Mandatory=$true)]
        [ValidateSet("none", "summary", "report", "json", "overlap", "all")]
        [string]$Option
    )

    $env:FILEDRIVEN_LOG_OPTION = $Option
}

function Switch-LogOption {
    <#
    .SYNOPSIS
        交互式切换日志选项

    .DESCRIPTION
        显示所有可用的日志选项，让用户通过序号选择。
        当前选项会标记 [当前]。
        直接回车取消切换。

    .OUTPUTS
        string: 切换后的选项名称
    #>
    $current = Get-LogOption
    $keys = @($script:LogOptionNames.Keys)
    $displayNames = @($script:LogOptionNames.Values)

    Write-Host ""
    Write-Host "  ============================================"
    Write-Host "  UI 排布打印设置"
    Write-Host "  ============================================"
    Write-Host ""
    
    $idx = 0
    foreach ($key in $keys) {
        $idx++
        $mark = if ($key -eq $current) { "  [当前]" } else { "" }
        Write-Host ("    [{0}] {1}{2}" -f $idx, $script:LogOptionNames[$key], $mark)
    }
    
    Write-Host ""
    $choice = Read-Host "  请选择 (1-$idx)，直接回车取消"
    
    if ([string]::IsNullOrEmpty($choice)) {
        return $current
    }
    
    if ($choice -match '^\d+$') {
        $num = [int]$choice
        if ($num -ge 1 -and $num -le $keys.Count) {
            $selected = $keys[$num - 1]
            Set-LogOption $selected
            Write-Host ""
            Write-Host "  [信息] 已切换到: $($script:LogOptionNames[$selected])"
            Write-Host ""
            return $selected
        }
    }
    
    Write-Host "  [错误] 无效的选择" -ForegroundColor Red
    return $current
}

# ============================================
#  选项转换
# ============================================

function Get-LogCategoriesFromOption {
    <#
    .SYNOPSIS
        将日志选项转换为 C# 端 --log 参数值

    .DESCRIPTION
        根据当前日志选项，生成对应的 --log 参数值字符串。
        空字符串表示不传递 --log 参数。

    .PARAMETER Option
        日志选项名称（可选，默认为当前选项）

    .OUTPUTS
        string: --log 参数值，空字符串表示不启用
    #>
    param(
        [Parameter(Mandatory=$false)]
        [string]$Option = ""
    )

    if ([string]::IsNullOrEmpty($Option)) {
        $Option = Get-LogOption
    }

    # ★ 合并冗余 case：summary/report/json/overlap 都返回相同的值
    switch ($Option) {
        "none"    { return "" }
        { $_ -in @("summary", "report", "json", "overlap") } { return "FileDriven.Inspector" }
        "all"     { return "FileDriven.Inspector,WdUI.Layout,WdUI.Render" }
        default   { return "" }
    }
}

function Get-LogOptionDescription {
    <#
    .SYNOPSIS
        获取日志选项的描述文本

    .PARAMETER Option
        日志选项名称（可选，默认为当前选项）

    .OUTPUTS
        string: 选项的描述文本
    #>
    param(
        [Parameter(Mandatory=$false)]
        [string]$Option = ""
    )

    if ([string]::IsNullOrEmpty($Option)) {
        $Option = Get-LogOption
    }

    if ($script:LogOptionNames.ContainsKey($Option)) {
        return $script:LogOptionNames[$Option]
    }
    return "未知选项"
}
