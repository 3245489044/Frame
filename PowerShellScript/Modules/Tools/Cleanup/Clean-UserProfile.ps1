<#
.SYNOPSIS
    安全清理当前用户目录下的应用缓存和临时文件
.DESCRIPTION
    清理常见应用的缓存数据（保留配置和用户数据）。
    清理规则由同目录下的 Clean-UserProfile.config.json 配置文件定义。

    安全策略：
    - 只清理明确标记为"缓存"、"临时"、"日志"的目录
    - 保留所有应用的 config/Config/users 等配置目录
    - 不删除任何 Windows Store 应用包
    - 不删除任何应用本体，只清理可安全重建的缓存数据
.PARAMETER WhatIf
    预览模式，只显示将清理的内容而不实际删除
.PARAMETER Force
    强制模式，跳过所有确认提示
.PARAMETER ConfigPath
    指定配置文件的路径，默认使用同目录下的 Clean-UserProfile.config.json
.PARAMETER CustomPaths
    指定额外的自定义清理路径（字符串数组）
.EXAMPLE
    .\Clean-UserProfile.ps1 -WhatIf
    预览将要清理的内容

    .\Clean-UserProfile.ps1
    交互模式，每步确认后执行

    .\Clean-UserProfile.ps1 -Force
    自动执行所有清理（跳过确认）

    .\Clean-UserProfile.ps1 -CustomPaths "C:\Users\Ljy\AppData\Local\Google\Chrome\User Data\Default\Cache"
    在默认清理基础上，额外清理指定路径
#>

param(
    [switch]$WhatIf,
    [switch]$Force,
    [string]$ConfigPath = "",
    [string[]]$CustomPaths
)

# ============================================================
# 初始化
# ============================================================

$script:totalFreed = 0
$script:cleanedItems = @()
$script:config = $null

# ============================================================
# 配置加载
# ============================================================

function Get-ConfigPath {
    if ($ConfigPath -and (Test-Path $ConfigPath)) {
        return $ConfigPath
    }
    # 默认：脚本同目录下的 .config.json
    $scriptDir = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $PSCommandPath }
    $defaultPath = Join-Path $scriptDir "Clean-UserProfile.config.json"
    if (Test-Path $defaultPath) {
        return $defaultPath
    }
    return $null
}

function Load-Config {
    $path = Get-ConfigPath
    if (-not $path) {
        Write-Warning "未找到配置文件，请确保 Clean-UserProfile.config.json 与脚本在同一目录"
        return $null
    }
    try {
        $json = Get-Content -Path $path -Raw -Encoding UTF8
        $config = $json | ConvertFrom-Json
        Write-Host "  配置文件: $path" -ForegroundColor Gray
        return $config
    } catch {
        Write-Warning "配置文件加载失败: $($_.Exception.Message)"
        return $null
    }
}

# ============================================================
# 路径解析
# ============================================================

function Resolve-PathTemplate {
    param([string]$PathTemplate)
    $result = $PathTemplate
    $result = $result -replace '{APPDATA}', $env:APPDATA
    $result = $result -replace '{LOCALAPPDATA}', $env:LOCALAPPDATA
    $result = $result -replace '{USERPROFILE}', $env:USERPROFILE
    $result = $result -replace '{TEMP}', $env:TEMP
    $result = $result -replace '{TMP}', $env:TMP
    $result = $result -replace '{WINDIR}', $env:WINDIR
    return $result
}

# ============================================================
# 辅助函数
# ============================================================

function Write-Banner {
    $userName = [Environment]::UserName
    $hostName = [Environment]::MachineName
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "   用户目录安全清理工具" -ForegroundColor Cyan
    Write-Host "   用户: $userName @ $hostName" -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host ""
}

function Get-DirSize {
    param([string]$Path)
    if (-not (Test-Path $Path)) { return 0 }
    try {
        $size = (Get-ChildItem -Path $Path -Recurse -Force -ErrorAction SilentlyContinue |
                 Measure-Object -Property Length -Sum -ErrorAction SilentlyContinue).Sum
        return [math]::Max(0, $size)
    } catch { return 0 }
}

function Format-Size {
    param([long]$Bytes)
    if ($Bytes -ge 1TB) { return "{0:N2} TB" -f ($Bytes / 1TB) }
    if ($Bytes -ge 1GB) { return "{0:N2} GB" -f ($Bytes / 1GB) }
    if ($Bytes -ge 1MB) { return "{0:N2} MB" -f ($Bytes / 1MB) }
    if ($Bytes -ge 1KB) { return "{0:N2} KB" -f ($Bytes / 1KB) }
    return "$Bytes B"
}

function Confirm-Step {
    param([string]$Message)
    if ($Force) { return $true }
    if ($WhatIf) { return $false }
    $choice = Read-Host "$Message (y/n, 默认 n)"
    return ($choice -eq 'y' -or $choice -eq 'Y')
}

function Test-IsCacheDir {
    param([string]$DirName)
    $lowerName = $DirName.ToLower()
    foreach ($keyword in $script:config.cacheKeywords) {
        if ($lowerName -eq $keyword.ToLower()) { return $true }
    }
    return $false
}

function Add-CleanedItem {
    param($Category, $Label, $Path, $Size)
    $script:cleanedItems += [PSCustomObject]@{
        Category = $Category
        Label = $Label
        Path = $Path
        Size = $Size
        SizeStr = Format-Size -Bytes $Size
    }
}

# ============================================================
# 清理模式：scan - 扫描子目录中的缓存目录
# ============================================================

function Invoke-ScanClean {
    param(
        [string]$Path,
        [string]$Label,
        [string]$Category,
        [string]$Description,
        [string[]]$Exclude
    )

    if (-not (Test-Path $Path)) {
        Write-Host "  [跳过] $Label - 目录不存在" -ForegroundColor DarkGray
        return
    }

    $allExclude = @($script:config.excludeDirs) + $Exclude | Select-Object -Unique

    $cacheDirs = @(Get-ChildItem -Path $Path -Force -ErrorAction SilentlyContinue |
        Where-Object { $_.PSIsContainer } |
        Where-Object { $_.Name -notin $allExclude } |
        Where-Object { Test-IsCacheDir -DirName $_.Name })

    if ($cacheDirs.Count -eq 0) {
        Write-Host "  [跳过] $Label - 没有找到缓存目录" -ForegroundColor DarkGray
        return
    }

    $totalSize = 0
    $cacheDirs | ForEach-Object { $totalSize += Get-DirSize -Path $_.FullName }

    Write-Host "  [待清理] [$Category] $Label" -ForegroundColor Yellow
    Write-Host "          路径: $Path" -ForegroundColor Gray
    Write-Host "          匹配: $($cacheDirs.Count) 个缓存目录, 大小: $(Format-Size -Bytes $totalSize)" -ForegroundColor Yellow
    if ($Description) {
        Write-Host "          说明: $Description" -ForegroundColor Gray
    }

    if ($WhatIf) {
        Write-Host "  [预览] 将释放 $(Format-Size -Bytes $totalSize)" -ForegroundColor DarkYellow
        $script:totalFreed += $totalSize
        Add-CleanedItem -Category $Category -Label $Label -Path $Path -Size $totalSize
        return
    }

    if (-not (Confirm-Step -Message "    确认清理?")) {
        Write-Host "  [跳过] 用户取消" -ForegroundColor DarkGray
        return
    }

    try {
        $cleanedSize = 0
        foreach ($dir in $cacheDirs) {
            $dirSize = Get-DirSize -Path $dir.FullName
            Remove-Item -Path $dir.FullName -Recurse -Force -ErrorAction SilentlyContinue
            $cleanedSize += $dirSize
        }
        Write-Host "  [完成] 已清理 $(Format-Size -Bytes $cleanedSize)" -ForegroundColor Green
        $script:totalFreed += $cleanedSize
        Add-CleanedItem -Category $Category -Label $Label -Path $Path -Size $cleanedSize
    } catch {
        Write-Host "  [错误] $($_.Exception.Message)" -ForegroundColor Red
    }
}

# ============================================================
# 清理模式：direct - 直接删除整个目录
# ============================================================

function Invoke-DirectClean {
    param(
        [string]$Path,
        [string]$Label,
        [string]$Category,
        [string]$Description
    )

    $size = Get-DirSize -Path $Path
    if ($size -eq 0) {
        Write-Host "  [跳过] $Label - 目录不存在或已为空" -ForegroundColor DarkGray
        return
    }

    Write-Host "  [待清理] [$Category] $Label" -ForegroundColor Yellow
    Write-Host "          路径: $Path" -ForegroundColor Gray
    Write-Host "          大小: $(Format-Size -Bytes $size)" -ForegroundColor Yellow
    if ($Description) {
        Write-Host "          说明: $Description" -ForegroundColor Gray
    }

    if ($WhatIf) {
        Write-Host "  [预览] 将释放 $(Format-Size -Bytes $size)" -ForegroundColor DarkYellow
        $script:totalFreed += $size
        Add-CleanedItem -Category $Category -Label $Label -Path $Path -Size $size
        return
    }

    if (-not (Confirm-Step -Message "    确认清理?")) {
        Write-Host "  [跳过] 用户取消" -ForegroundColor DarkGray
        return
    }

    try {
        Remove-Item -Path $Path -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host "  [完成] 已清理 $(Format-Size -Bytes $size)" -ForegroundColor Green
        $script:totalFreed += $size
        Add-CleanedItem -Category $Category -Label $Label -Path $Path -Size $size
    } catch {
        Write-Host "  [错误] $($_.Exception.Message)" -ForegroundColor Red
    }
}

# ============================================================
# 清理模式：temp - 清空临时目录（保留目录本身）
# ============================================================

function Invoke-TempClean {
    param(
        [string]$Path,
        [string]$Label,
        [string]$Category,
        [string]$Description
    )

    if (-not (Test-Path $Path)) {
        Write-Host "  [跳过] $Label - 目录不存在" -ForegroundColor DarkGray
        return
    }

    $totalSize = Get-DirSize -Path $Path
    if ($totalSize -eq 0) {
        Write-Host "  [跳过] $Label - 已为空" -ForegroundColor DarkGray
        return
    }

    Write-Host "  [待清理] [$Category] $Label" -ForegroundColor Yellow
    Write-Host "          路径: $Path" -ForegroundColor Gray
    Write-Host "          大小: $(Format-Size -Bytes $totalSize)" -ForegroundColor Yellow
    if ($Description) {
        Write-Host "          说明: $Description" -ForegroundColor Gray
    }

    if ($WhatIf) {
        Write-Host "  [预览] 将释放 $(Format-Size -Bytes $totalSize)" -ForegroundColor DarkYellow
        $script:totalFreed += $totalSize
        Add-CleanedItem -Category $Category -Label $Label -Path $Path -Size $totalSize
        return
    }

    if (-not (Confirm-Step -Message "    确认清理?")) {
        Write-Host "  [跳过] 用户取消" -ForegroundColor DarkGray
        return
    }

    try {
        Get-ChildItem -Path $Path -Force -ErrorAction SilentlyContinue | ForEach-Object {
            Remove-Item -Path $_.FullName -Recurse -Force -ErrorAction SilentlyContinue
        }
        Write-Host "  [完成] 已清理 $(Format-Size -Bytes $totalSize)" -ForegroundColor Green
        $script:totalFreed += $totalSize
        Add-CleanedItem -Category $Category -Label $Label -Path $Path -Size $totalSize
    } catch {
        Write-Host "  [错误] $($_.Exception.Message)" -ForegroundColor Red
    }
}

# ============================================================
# 清理调度器
# ============================================================

function Get-ItemProperty {
    param([PSObject]$Item, [string]$Name)
    if ($Item.PSObject.Properties.Name -contains $Name) {
        return $Item.$Name
    }
    return $null
}

function Invoke-CleanItem {
    param(
        [PSObject]$Item,
        [string]$Category
    )

    $resolvedPath = Resolve-PathTemplate -PathTemplate $Item.path
    $label = $Item.label
    $description = Get-ItemProperty -Item $Item -Name "description"
    if (-not $description) { $description = "" }
    $excludeRaw = Get-ItemProperty -Item $Item -Name "exclude"
    $exclude = if ($excludeRaw) { @($excludeRaw) } else { @() }
    $mode = Get-ItemProperty -Item $Item -Name "mode"
    if (-not $mode) { $mode = "scan" }

    switch ($mode) {
        "scan" {
            Invoke-ScanClean -Path $resolvedPath -Label $label -Category $Category -Description $description -Exclude $exclude
        }
        "direct" {
            Invoke-DirectClean -Path $resolvedPath -Label $label -Category $Category -Description $description
        }
        "temp" {
            Invoke-TempClean -Path $resolvedPath -Label $label -Category $Category -Description $description
        }
        default {
            Write-Host "  [跳过] $label - 未知的清理模式: $mode" -ForegroundColor DarkGray
        }
    }
}

# ============================================================
# 主流程
# ============================================================

Write-Banner

# 加载配置
$script:config = Load-Config
if (-not $script:config) {
    Write-Error "无法加载配置文件，请检查 Clean-UserProfile.config.json 是否存在"
    exit 1
}

if ($WhatIf) {
    Write-Host ">>> 预览模式 - 仅显示将清理的内容，不会实际删除 <<<" -ForegroundColor DarkYellow
    Write-Host ""
}

# 按分类执行清理
$categoryIndex = 1
foreach ($category in $script:config.categories) {
    Write-Host "─── $categoryIndex. $($category.label) ────────────────────────────" -ForegroundColor Magenta

    foreach ($item in $category.items) {
        Invoke-CleanItem -Item $item -Category $category.name
    }

    Write-Host ""
    $categoryIndex++
}

# 自定义路径
if ($CustomPaths -and $CustomPaths.Count -gt 0) {
    Write-Host "─── $categoryIndex. 自定义清理路径 ────────────────────────" -ForegroundColor Magenta
    foreach ($cp in $CustomPaths) {
        if (Test-Path $cp) {
            $label = Split-Path $cp -Leaf
            Invoke-DirectClean -Path $cp -Label "自定义: $label" -Category "自定义" -Description "用户指定的自定义清理路径"
        } else {
            Write-Host "  [跳过] 自定义路径不存在: $cp" -ForegroundColor DarkGray
        }
    }
    Write-Host ""
}

# ============================================================
# 汇总
# ============================================================
Write-Host "========================================" -ForegroundColor Cyan
if ($WhatIf) {
    Write-Host "  预览完成" -ForegroundColor DarkYellow
    if ($script:cleanedItems.Count -gt 0) {
        Write-Host "  预计可释放空间: $(Format-Size -Bytes $script:totalFreed)" -ForegroundColor DarkYellow
        Write-Host ""
        Write-Host "  清理明细:" -ForegroundColor DarkYellow
        $script:cleanedItems | Group-Object Category | ForEach-Object {
            $catTotal = ($_.Group | Measure-Object Size -Sum).Sum
            Write-Host "    $($_.Name): $(Format-Size -Bytes $catTotal) ($($_.Count) 项)" -ForegroundColor Gray
        }
    } else {
        Write-Host "  没有找到可清理的内容" -ForegroundColor DarkYellow
    }
    Write-Host ""
    Write-Host "  运行时不加 -WhatIf 参数即可实际执行清理" -ForegroundColor DarkYellow
} else {
    Write-Host "  清理完成" -ForegroundColor Green
    if ($script:totalFreed -gt 0) {
        Write-Host "  已释放空间: $(Format-Size -Bytes $script:totalFreed)" -ForegroundColor Green
        Write-Host ""
        Write-Host "  清理明细:" -ForegroundColor Green
        $script:cleanedItems | Group-Object Category | ForEach-Object {
            $catTotal = ($_.Group | Measure-Object Size -Sum).Sum
            Write-Host "    $($_.Name): $(Format-Size -Bytes $catTotal) ($($_.Count) 项)" -ForegroundColor Gray
        }
    } else {
        Write-Host "  没有清理任何内容" -ForegroundColor Green
    }
}
Write-Host "========================================" -ForegroundColor Cyan

if (-not $WhatIf -and $script:totalFreed -gt 0) {
    Write-Host ""
    Write-Host "💡 建议：重启计算机以确保所有锁定的临时文件被彻底清除。" -ForegroundColor Cyan
    Write-Host "💡 提示：应用会在下次启动时自动重建必要的缓存。" -ForegroundColor Cyan
}
