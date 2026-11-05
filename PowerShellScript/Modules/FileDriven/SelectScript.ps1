<#
.SYNOPSIS
    通用脚本选择器 - 分层（文件夹）交互选择脚本文件

.DESCRIPTION
    按文件夹分层列出脚本：
      顶层列出各文件夹（附递归用例数），输入文件夹编号进入该文件夹；
      文件夹内列出该文件夹下的用例，输入编号运行；
      也可用 1-2-3 路径式输入从根目录直接定位（末级为文件则运行，为文件夹则进入）。
    支持批量运行整个文件夹（run 2 或 2-*）、返回上级（0/..）、回到根（root）、退出（exit/空行）。

    交互菜单经 Write-Host 直接显示在控制台；
    选择结果经 Write-Output 输出给调用方（ScriptRunner.ps1）。

    编号规则：同一层级内先列子文件夹、再列脚本文件，
    两组均按名称 OrdinalIgnoreCase 排序，编号稳定可预测。

.PARAMETER ScriptsDir
    脚本目录路径（根）

.PARAMETER RunMode
    当前运行模式（auto/manual）

.PARAMETER Title
    显示标题（可选，默认为"脚本选择"）

.PARAMETER FilePattern
    文件扩展名过滤（可选，默认为 ".json"；.txt 属历史文本命令脚本，须显式指定才纳入）

.PARAMETER ShowDirInfo
    是否显示 DrivenConfig 目录结构信息（可选，默认为 $false）

.PARAMETER DirInfoPath
    目录结构信息的根路径（可选，默认为 ScriptsDir 的父目录）

.PARAMETER ValidCommands
    可识别的命令关键字哈希表（可选），格式: @{ "list" = $true; "info" = $true }

.PARAMETER ExtraValidCommands
    额外命令关键字哈希表（可选，如 log/analyze/docker）

.OUTPUTS
    选中的脚本相对路径，或命令关键字（list/info/dirs/show/help/mode/log），
    或批量标记 __RUN_DIR__:<相对文件夹路径>，或 __EXIT__（退出）

.EXAMPLE
    powershell -File SelectScript.ps1 -ScriptsDir "D:\Scripts" -RunMode "auto"
#>

param(
    [Parameter(Mandatory=$true)]
    [string]$ScriptsDir,

    [Parameter(Mandatory=$true)]
    [string]$RunMode,

    [Parameter(Mandatory=$false)]
    [string]$Title = "脚本选择",

    [Parameter(Mandatory=$false)]
    [string]$FilePattern = ".json",

    [Parameter(Mandatory=$false)]
    [switch]$ShowDirInfo,

    [Parameter(Mandatory=$false)]
    [string]$DirInfoPath = "",

    [Parameter(Mandatory=$false)]
    [hashtable]$ValidCommands = $null,

    [Parameter(Mandatory=$false)]
    [hashtable]$ExtraValidCommands = $null
)

# 复用脚本目录分层浏览函数（Get-ScriptCatalogChildren / Resolve-ScriptCatalogSpec / Show-ScriptTree 等）
. (Join-Path $PSScriptRoot "FileDrivenTestUtils.ps1")

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
#  清理路径（去除引号、统一反斜杠、去除尾部反斜杠）
# ============================================
$cleanDir = $ScriptsDir -replace '["/]', '\' -replace '\\+$', ''

if (-not (Test-Path -LiteralPath $cleanDir)) {
    Write-Error "脚本目录不存在: $cleanDir"
    exit 1
}

# ============================================
#  显示目录结构信息（可选，仅在进入时显示一次）
# ============================================
if ($ShowDirInfo) {
    $infoPath = $DirInfoPath
    if ([string]::IsNullOrEmpty($infoPath)) {
        $infoPath = Split-Path $cleanDir -Parent
    }

    Write-Host ""
    Write-Host ("=" * 75)
    Write-Host "  FileDriven\DrivenConfig\ 目录结构"
    Write-Host ("=" * 75)
    Write-Host ""

    if (Test-Path -LiteralPath $infoPath) {
        Get-ChildItem -LiteralPath $infoPath -Directory | ForEach-Object {
            $count = @(Get-ChildItem -LiteralPath $_.FullName -File).Count
            Write-Host ("    {0,-20} ({1} 个文件)" -f ($_.Name + '\'), $count)
        }
    }
    Write-Host ""
}

# ============================================
#  分层菜单显示
# ============================================

function Show-CatalogMenu {
    param([string]$RelativeDir)

    $location = if ([string]::IsNullOrWhiteSpace($RelativeDir)) {
        "Scripts\（根目录）"
    } else {
        "Scripts\$RelativeDir"
    }

    Write-Host ""
    Write-Host ("=" * 44)
    Write-Host "  $Title"
    Write-Host ("=" * 44)
    Write-Host "  当前位置: $location"
    Write-Host ""
    Show-ScriptTree -ScriptsDir $cleanDir -FilePattern $FilePattern -RelativeDir $RelativeDir
    Write-Host ""
    Write-Host "  输入: 文件夹编号进入 / 用例编号运行 / 1-2-3 路径直达"
    Write-Host "        run 2 或 2-* 批量运行文件夹；0/.. 上级；root 根；exit 退出"
    Write-Host "        list 刷新；help 语法；show/info/dirs/mode/log 为全局命令"
    Write-Host ""
}

function Show-CatalogHelp {
    Write-Host ""
    Write-Host "  分层选择用法（路径编号始终从根目录起算）:"
    Write-Host "    2            进入文件夹 2；在文件夹内输入编号即运行对应用例"
    Write-Host "    1-2-3        路径式直达：末级为文件则运行，为文件夹则进入"
    Write-Host "    run 2        批量运行文件夹 2 下全部用例（含子文件夹）"
    Write-Host "    2-*          批量运行的等价写法（2-3-* 表示嵌套文件夹）"
    Write-Host "    0 / .. / back 返回上一级；root 回到根目录"
    Write-Host "    list         刷新当前层；help/? 显示本帮助"
    Write-Host "    exit / 空行  退出"
    Write-Host "    全局命令: show/info/dirs/mode/log"
    Write-Host "    清单只收测试脚本（JSON 顶层须同时含 script 与 commands）"
    Write-Host ""
}

# 合并内置命令与额外命令（供全局命令识别）
$allValidCommands = $ValidCommands.Clone()
foreach ($key in $ExtraValidCommands.Keys) {
    $allValidCommands[$key] = $true
}

$currentRel = ""

while ($true) {
    Show-CatalogMenu -RelativeDir $currentRel

    $choice = Read-Host "  请输入"
    if ($null -eq $choice) { $choice = "" }
    $text = $choice.Trim()
    $low = $text.ToLower()

    # ---- 退出 ----
    if ($text -eq "" -or $low -in @("exit", "quit", "q")) {
        Write-Output "__EXIT__"
        exit 0
    }

    # ---- 返回上级 / 回到根 ----
    if ($low -in @("0", "..", "back", "up", "返回", "上级")) {
        if ([string]::IsNullOrWhiteSpace($currentRel)) {
            Write-Host "  [提示] 已在根目录" -ForegroundColor Yellow
        } else {
            $parent = Split-Path $currentRel -Parent
            $currentRel = if ($null -eq $parent) { "" } else { [string]$parent }
        }
        continue
    }
    if ($low -in @("root", "/", "根目录")) {
        if ([string]::IsNullOrWhiteSpace($currentRel)) {
            Write-Host "  [提示] 已在根目录" -ForegroundColor Yellow
        } else {
            $currentRel = ""
        }
        continue
    }

    # ---- 当前层刷新 / 本地帮助 ----
    if ($low -eq "list") { continue }
    if ($low -eq "help" -or $text -eq "?") {
        Show-CatalogHelp
        continue
    }

    # ---- 全局命令（交给 ScriptRunner 处理）----
    if ($allValidCommands.ContainsKey($low)) {
        Write-Output $low
        exit 0
    }

    # ---- 当前层编号：文件夹则进入，文件则运行 ----
    if ($text -match '^\d+$') {
        $children = @(Get-ScriptCatalogChildren -ScriptsDir $cleanDir -RelativeDir $currentRel -FilePattern $FilePattern)
        $num = [int]$text
        if ($num -lt 1 -or $num -gt $children.Count) {
            $range = if ($children.Count -eq 0) { '该目录为空' } else { "1-$($children.Count)" }
            Write-Host "  [错误] 编号 $num 超出有效范围（$range）" -ForegroundColor Red
            continue
        }
        $entry = $children[$num - 1]
        if ($entry.Kind -eq 'Folder') {
            $currentRel = $entry.RelativePath
            continue
        }
        Write-Output $entry.RelativePath
        exit 0
    }

    # ---- 路径式 / 批量 / run 指令 ----
    if ($text -match '(?i)^run' -or $text.Contains('-') -or $text.EndsWith('*')) {
        $resolved = Resolve-ScriptCatalogSpec -ScriptsDir $cleanDir -Spec $text -FilePattern $FilePattern
        if ($resolved.Kind -eq 'Invalid') {
            Write-Host "  [错误] $($resolved.Message)" -ForegroundColor Red
            continue
        }
        if ($resolved.Batch -or $text -match '(?i)^run') {
            if ($resolved.Kind -ne 'Folder') {
                Write-Host "  [错误] '$text' 指向单个文件，不能按文件夹批量运行" -ForegroundColor Red
                continue
            }
            # 空文件夹不进入批量、不抛错：提示后留在当前层
            $batchCount = @(Get-ScriptBatchFiles -ScriptsDir $cleanDir -RelativeDir $resolved.RelativePath -FilePattern $FilePattern).Count
            if ($batchCount -eq 0) {
                Write-Host "  [提示] 该文件夹无可用用例，留在当前层" -ForegroundColor Yellow
                continue
            }
            Write-Output ("__RUN_DIR__:" + $resolved.RelativePath)
            exit 0
        }
        if ($resolved.Kind -eq 'Folder') {
            $currentRel = $resolved.RelativePath
            continue
        }
        Write-Output $resolved.RelativePath
        exit 0
    }

    # ---- 非法输入 ----
    Write-Host "  [错误] 无法识别的输入: $text（输入 help 查看用法）" -ForegroundColor Red
}
