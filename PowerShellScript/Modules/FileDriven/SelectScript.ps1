<#
.SYNOPSIS
    通用脚本选择器 - 交互选择脚本文件

.DESCRIPTION
    列出指定目录中的脚本文件，让用户通过序号选择。
    支持输入 mode 切换自动/手动模式，模式通过环境变量传递。
    支持输入命令关键字执行对应操作。
    输出格式供 bat 的 for /f 解析使用。

.PARAMETER ScriptsDir
    脚本目录路径

.PARAMETER RunMode
    当前运行模式（auto/manual）

.PARAMETER Title
    显示标题（可选，默认为"脚本选择"）

.PARAMETER FilePattern
    文件扩展名过滤（可选，默认为 ".json|.txt"）

.PARAMETER ShowDirInfo
    是否显示目录结构信息（可选，默认为 $false）

.PARAMETER DirInfoPath
    目录结构信息的根路径（可选，默认为 ScriptsDir 的父目录）

.PARAMETER ValidCommands
    可识别的命令关键字哈希表（可选），格式: @{ "list" = $true; "info" = $true }
    默认为 list/info/dirs/show/help

.OUTPUTS
    选中的脚本文件名，或命令关键字（如 list/info/dirs/show/help/mode），或空（退出）

.EXAMPLE
    powershell -File SelectScript.ps1 -ScriptsDir "D:\Scripts" -RunMode "auto"

.EXAMPLE
    powershell -File SelectScript.ps1 -ScriptsDir "D:\Scripts" -RunMode "manual" -Title "测试脚本选择" -FilePattern ".json"
#>

param(
    [Parameter(Mandatory=$true)]
    [string]$ScriptsDir,

    [Parameter(Mandatory=$true)]
    [string]$RunMode,

    [Parameter(Mandatory=$false)]
    [string]$Title = "脚本选择",

    [Parameter(Mandatory=$false)]
    [string]$FilePattern = ".json|.txt",

    [Parameter(Mandatory=$false)]
    [switch]$ShowDirInfo,

    [Parameter(Mandatory=$false)]
    [string]$DirInfoPath = "",

    [Parameter(Mandatory=$false)]
    [hashtable]$ValidCommands = $null,

    [Parameter(Mandatory=$false)]
    [hashtable]$ExtraValidCommands = $null
)

# ★ 修复：param() 块中不能使用哈希表字面量作为默认值
#   在脚本主体中初始化默认值
if ($null -eq $ValidCommands) {
    $ValidCommands = @{
        "list" = $true
        "info" = $true
        "dirs" = $true
        "show" = $true
        "help" = $true
        "mode" = $true
        "log"  = $true
    }
}
if ($null -eq $ExtraValidCommands) {
    $ExtraValidCommands = @{}
}


# ============================================
#  辅助函数（使用唯一名称避免与 FileDrivenTestUtils.ps1 冲突）
# ============================================

function Write-SelectHeader {
    param([string]$Text)
    $line = "=" * 75
    Write-Output ""
    Write-Output $line
    Write-Output "  $Text"
    Write-Output $line
    Write-Output ""
}

function Write-SelectSubHeader {
    param([string]$Text)
    $line = "=" * 44
    Write-Output ""
    Write-Output "  $line"
    Write-Output "   $Text"
    Write-Output "  $line"
}

# ============================================
#  清理路径（去除引号、统一反斜杠、去除尾部反斜杠）
# ============================================
$cleanDir = $ScriptsDir -replace '["/]', '\' -replace '\\+$', ''

# ============================================
#  显示目录结构信息（可选）
# ============================================
if ($ShowDirInfo) {
    $infoPath = $DirInfoPath
    if ([string]::IsNullOrEmpty($infoPath)) {
        $infoPath = Split-Path $cleanDir -Parent
    }
    
    Write-SelectHeader "FileDriven\DrivenConfig\ 目录结构"
    
    if (Test-Path $infoPath) {
        Get-ChildItem "$infoPath" -Directory | ForEach-Object {
            $count = @(Get-ChildItem $_.FullName -File).Count
            Write-Output ("    {0,-20} ({1} 个文件)" -f ($_.Name + "\"), $count)
        }
    }
    Write-Output ""
}

# ============================================
#  获取脚本列表（递归扫描所有子目录）
# ============================================
$extensions = $FilePattern -split '\|'
$scripts = Get-ChildItem -Path "$cleanDir" -File -Recurse | 
    Where-Object { $_.Extension -in $extensions } | 
    Sort-Object FullName

if ($scripts.Count -eq 0) {
    Write-Error "未找到任何脚本文件（格式: $FilePattern）！"
    exit 1
}

# ============================================
#  显示脚本列表（带目录结构）
# ============================================
Write-SelectSubHeader $Title

Write-Output "  序号 | 脚本文件（相对路径）               | 说明"
Write-Output "  ----|------------------------------------|------------------------------------------"
$idx = 0
foreach ($script in $scripts) {
    $idx++
    # 计算相对路径（如 "Math/math_functions_test.json"）
    $relativePath = $script.FullName.Substring($cleanDir.Length + 1)
    $desc = ''
    if ($script.Extension -eq '.json') {
        try {
            $desc = (Get-Content $script.FullName -Raw | ConvertFrom-Json).description
        } catch {
            # JSON 解析失败，忽略描述（可能是空文件或格式错误）
            $desc = $null
        }
    }
    if ($desc) {
        Write-Output ("  {0,-4} | {1,-35} | {2}" -f $idx, $relativePath, $desc)
    } else {
        Write-Output ("  {0,-4} | {1}" -f $idx, $relativePath)
    }
}

# ============================================
#  显示当前模式
# ============================================
if ($RunMode -eq "auto") {
    Write-Output "`n  [当前模式: 自动] 运行完自动关闭窗口"
} else {
    Write-Output "`n  [当前模式: 手动] 运行完后暂停等待按键"
}

# ============================================
#  可识别的命令关键字（合并内置命令和额外命令）
# ============================================
$validCommands = $ValidCommands.Clone()
foreach ($key in $ExtraValidCommands.Keys) {
    $validCommands[$key] = $true
}

# 构建命令提示字符串
$builtinCommands = @("list", "info", "dirs", "show", "help", "mode")
$extraCmdNames = @()
foreach ($key in $ExtraValidCommands.Keys) {
    $extraCmdNames += $key
}
$allCmdNames = $builtinCommands + $extraCmdNames
$cmdHint = $allCmdNames -join "/"


# ============================================
#  用户交互输入
# ============================================
$maxIdx = $scripts.Count
while ($true) {
    $choice = Read-Host "`n请输入序号（1-$maxIdx），或命令（$cmdHint），直接回车退出"
    
    if ([string]::IsNullOrEmpty($choice)) {
        # 退出：输出退出标记，供 ScriptRunner.ps1 识别
        Write-Output "__EXIT__"
        exit 0
    }
    
    # 检查是否为命令关键字
    $lowerChoice = $choice.ToLower().Trim()
    if ($validCommands.ContainsKey($lowerChoice)) {
        Write-Output $lowerChoice
        exit 0
    }
    
    # 验证输入是否为有效数字
    if ($choice -match '^\d+$') {
        $num = [int]$choice
        if ($num -ge 1 -and $num -le $maxIdx) {
            # 输出选中的脚本文件相对路径（含子目录，如 "Math/test.json"）
            $relativePath = $scripts[$num - 1].FullName.Substring($cleanDir.Length + 1)
            Write-Output $relativePath
            exit 0
        }
    }
    
    Write-Output "  [错误] 无效的选择，请输入 1-$maxIdx 之间的数字，或命令（$cmdHint）"
}
