<#
.SYNOPSIS
    集中清理脚本 — 删除 BuildOutput + bin/obj + .tsbuildinfo

.DESCRIPTION
    整合了 clean.ps1（删除 bin/obj）和 Invoke-FullRebuild.ps1（删除 BuildOutput）的清理逻辑。
    作为统一的清理入口，供 ScriptLauncher 和 Invoke-FullRebuild 调用。

    清理内容:
      1. BuildOutput 目录（所有构建产物，递归扫描整个项目根下的所有 BuildOutput）
      2. 所有 bin/obj 目录（项目级构建缓存）
      3. .tsbuildinfo 文件（TypeScript 增量编译信息）

    用法:
        Invoke-CleanAll.ps1              实际执行清理
        Invoke-CleanAll.ps1 -WhatIf      模拟模式，仅预览不删除
        Invoke-CleanAll.ps1 -Force       跳过确认提示

.NOTES
    依赖: OutputHelpers, ProjectPaths, BuildHelpers 模块（通过 Import-Modules.ps1 加载）
    替代: clean.ps1（轻量级 bin/obj 清理）— 本脚本是其超集
# @requires Write-Header, Write-Success, Write-Info, Write-SubHeader
# @requires Confirm-Action, Invoke-Step, Get-ProjectRoot
# @requires Get-BinObjDirectories, Remove-DirectoryWithLog
#>

param(
    # 模拟模式，仅预览不删除
    [switch]$WhatIf = $false,

    # 跳过确认提示
    [switch]$Force = $false
)

# ============================================================
# 加载模块
# ============================================================

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$moduleDir = Resolve-Path (Join-Path $scriptDir "..\..\Modules")
. "$moduleDir\Import-Modules.ps1"


# ============================================================
# 路径配置
# ============================================================

$projectRoot = Get-ProjectRoot

# ============================================================
# 辅助函数
# ============================================================

function Remove-ItemWithCount {
    <#
    .SYNOPSIS
        删除文件/目录并统计数量，支持 -WhatIf
    #>
    param(
        [string]$Path,
        [string]$Label,
        [switch]$Recurse,
        [switch]$IsFile
    )

    if (-not (Test-Path $Path)) {
        Write-Host "  [跳过] $Label — 不存在" -ForegroundColor DarkGray
        return 0
    }

    if ($WhatIf) {
        if ($IsFile) {
            $count = 1
            Write-Host "  [模拟] 将删除文件: $Path" -ForegroundColor Yellow
        } else {
            $items = Get-ChildItem -Path $Path -Recurse -File -ErrorAction SilentlyContinue
            $count = $items.Count + 1  # +1 表示目录本身
            Write-Host "  [模拟] 将删除目录: $Path（含 $($items.Count) 个文件）" -ForegroundColor Yellow
        }
        return $count
    }

    try {
        if ($IsFile) {
            Remove-Item -Path $Path -Force -ErrorAction Stop
            Write-Host "  [已删除] $Label" -ForegroundColor Green
            return 1
        } else {
            $items = Get-ChildItem -Path $Path -Recurse -File -ErrorAction SilentlyContinue
            $count = $items.Count
            Remove-Item -Path $Path -Recurse -Force -ErrorAction Stop
            Write-Host "  [已删除] $Label（$count 个文件）" -ForegroundColor Green
            return $count + 1
        }
    }
    catch {
        Write-Host "  [失败] $Label — $($_.Exception.Message)" -ForegroundColor Red
        return 0
    }
}

# ============================================================
# 确认提示
# ============================================================

Write-Header "Invoke-CleanAll — 集中清理"
Write-Host "  项目根目录: $projectRoot" -ForegroundColor Gray
if ($WhatIf) {
    Write-Host "  [模拟模式] 仅预览，不会实际删除" -ForegroundColor Yellow
}
Write-Host ""

if (-not $Force -and -not $WhatIf) {
    $confirmed = Confirm-Action -ActionDescription @(
        "所有 BuildOutput 目录（递归扫描全项目）",
        "所有 bin/obj 目录（项目级构建缓存）",
        ".tsbuildinfo 文件（TypeScript 增量编译信息）"
    )
    if (-not $confirmed) { exit 0 }
}

# ============================================================
# 清理 1：所有 BuildOutput 目录（递归扫描整个项目根）
# ============================================================

Write-SubHeader "▶ 清理 1/3：BuildOutput 目录（全项目扫描）"
$totalDeleted = 0

$allBuildOutputs = Get-ChildItem -Path $projectRoot -Directory -Recurse -Force -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -eq 'BuildOutput' -and $_.FullName -notmatch '\\node_modules\\' -and $_.FullName -notmatch '\\.git\\' }

if ($allBuildOutputs.Count -eq 0) {
    Write-Host "  [跳过] 未找到任何 BuildOutput 目录" -ForegroundColor DarkGray
} else {
    foreach ($dir in $allBuildOutputs) {
        $label = "BuildOutput: $(($dir.FullName -replace [regex]::Escape($projectRoot), '').TrimStart('\'))"
        $totalDeleted += Remove-ItemWithCount -Path $dir.FullName -Label $label -Recurse
    }
}

# ============================================================
# 清理 2：所有 bin/obj 目录
# ============================================================

Write-SubHeader "▶ 清理 2/3：bin/obj 目录"

$targetFolders = @("bin", "obj")
$allDirs = Get-ChildItem -Path $projectRoot -Directory -Recurse -Force -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -in $targetFolders -and $_.FullName -notmatch '\\node_modules\\' -and $_.FullName -notmatch '\\.git\\' }

$dirCount = 0
foreach ($dir in $allDirs) {
    $label = "bin/obj: $(($dir.FullName -replace [regex]::Escape($projectRoot), '').TrimStart('\'))"
    $count = Remove-ItemWithCount -Path $dir.FullName -Label $label -Recurse
    if ($count -gt 0) { $dirCount++ }
    $totalDeleted += $count
}

if ($dirCount -eq 0) {
    Write-Host "  [跳过] 未找到 bin/obj 目录" -ForegroundColor DarkGray
}

# ============================================================
# 清理 3：.tsbuildinfo 文件
# ============================================================

Write-SubHeader "▶ 清理 3/3：.tsbuildinfo 文件"

$tsBuildInfoFiles = Get-ChildItem -Path $projectRoot -Recurse -Filter "*.tsbuildinfo" -File -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '\\node_modules\\' }

$infoCount = 0
foreach ($file in $tsBuildInfoFiles) {
    $label = ".tsbuildinfo: $($file.Name)"
    $count = Remove-ItemWithCount -Path $file.FullName -Label $label -IsFile
    if ($count -gt 0) { $infoCount++ }
    $totalDeleted += $count
}

if ($infoCount -eq 0) {
    Write-Host "  [跳过] 未找到 .tsbuildinfo 文件" -ForegroundColor DarkGray
}

# ============================================================
# 完成
# ============================================================

Write-Host ""
if ($WhatIf) {
    Write-Host "模拟完成！共匹配 $totalDeleted 个清理目标" -ForegroundColor Yellow
} else {
    Write-Success "清理完成！共处理 $totalDeleted 个文件/目录"
}
Write-Host ""
