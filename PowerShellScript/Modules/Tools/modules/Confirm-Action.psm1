<#
.SYNOPSIS
    交互确认模块 — 提供统一的用户交互确认函数
.DESCRIPTION
    包括确认提示、暂停等待等。支持 Force 和 WhatIf 模式自动跳过确认。

    用法:
        Import-Module .\Confirm-Action.psm1 -Force
        if (Confirm-Action "确认执行？") { ... }
        if (Confirm-Prompt "是否继续？" "n") { ... }
        Pause-Message
#>

function Confirm-Action {
    <#
    .SYNOPSIS
        确认操作（支持 Force 和 WhatIf 模式）
    .PARAMETER Message
        提示消息
    .PARAMETER Force
        是否强制模式（自动返回 true）
    .PARAMETER WhatIf
        是否预览模式（自动返回 true）
    .EXAMPLE
        if (Confirm-Action "确认清理？" -Force:$Force -WhatIf:$WhatIf) { ... }
    #>
    param(
        [string]$Message,
        [switch]$Force,
        [switch]$WhatIf
    )

    if ($Force) { return $true }
    if ($WhatIf) { return $true }
    $response = Read-Host "  $Message (y/N)"
    return $response -eq 'y' -or $response -eq 'Y'
}

function Confirm-Prompt {
    <#
    .SYNOPSIS
        通用确认提示（支持默认值）
    .PARAMETER Prompt
        提示文本
    .PARAMETER Default
        默认值（"y" 或 "n"）
    .PARAMETER Force
        是否强制模式（自动返回 true）
    .EXAMPLE
        if (Confirm-Prompt "是否继续？" "n") { ... }
        if (Confirm-Prompt "是否继续？" "y" -Force:$Force) { ... }
    #>
    param(
        [string]$Prompt,
        [string]$Default = "n",
        [switch]$Force
    )

    if ($Force) { return $true }
    $optStr = if ($Default -eq "y") { "Y/n" } else { "y/N" }
    $r = Read-Host "  $Prompt [$optStr]"
    if ([string]::IsNullOrWhiteSpace($r)) { $r = $Default }
    return $r.ToLower() -eq "y"
}

function Pause-Message {
    <#
    .SYNOPSIS
        暂停等待用户按回车
    .PARAMETER Message
        提示消息（默认 "按回车键继续..."）
    #>
    param([string]$Message = "按回车键继续...")

    if ($host.Name -eq "ConsoleHost") {
        Write-Host $Message -ForegroundColor Gray
        $null = Read-Host
    }
}

function Read-MenuChoice {
    <#
    .SYNOPSIS
        读取菜单选择
    .PARAMETER Prompt
        提示文本
    .PARAMETER ValidChoices
        有效选项数组
    .PARAMETER Default
        默认值
    .RETURNS
        用户选择的选项
    #>
    param(
        [string]$Prompt = "请输入数字",
        [string[]]$ValidChoices = @("1", "2", "3", "4"),
        [string]$Default = ""
    )

    while ($true) {
        $choice = Read-Host $Prompt
        if ([string]::IsNullOrWhiteSpace($choice) -and $Default) {
            return $Default
        }
        if ($choice -in $ValidChoices) {
            return $choice
        }
        Write-Host "  无效输入，请重新选择..." -ForegroundColor Red
    }
}

# 导出所有函数
Export-ModuleMember -Function Confirm-Action, Confirm-Prompt, Pause-Message, Read-MenuChoice
