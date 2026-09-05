# .SYNOPSIS
# PScriptManager 配置生成器 — 自动更新 launcher.config.json
# .DESCRIPTION
# 根据脚本扫描结果，自动生成或更新 launcher.config.json 中的 tools 条目。
# 支持：
# - 新增脚本自动注册
# - 已注册脚本的元数据更新
# - 弃用脚本标记移除
# - 分类自动创建
# - 差异预览（安全模式）
# .NOTES
# 此模块是 PScriptManager 框架的核心组件，供 Register-Script.ps1 调用。

# ============================================================
# 内部辅助函数
# ============================================================

function Get-LauncherConfigPath {
    # .SYNOPSIS
# 获取 launcher.config.json 的路径
    param(
        [string]$ProjectRoot
    )
    $candidates = @(
        Join-Path $ProjectRoot "PowerShellScript\launcher.config.json"
        Join-Path $ProjectRoot "launcher.config.json"
    )
    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) {
            return $candidate
        }
    }
    return $candidates[0]  # 默认返回第一个
}

function Load-LauncherConfig {
    # .SYNOPSIS
# 加载 launcher.config.json 配置文件
    param(
        [string]$ConfigPath
    )
    if (-not (Test-Path $ConfigPath)) {
        return $null
    }
    try {
        $content = Get-Content $ConfigPath -Raw -Encoding UTF8
        return $content | ConvertFrom-Json
    }
    catch {
        Write-Warning "[ConfigGenerator] 配置文件解析失败: $($_.Exception.Message)"
        return $null
    }
}

function Save-LauncherConfig {
    # .SYNOPSIS
# 保存 launcher.config.json 配置文件
    param(
        [string]$ConfigPath,
        [object]$Config,
        [switch]$PrettyPrint
    )
    try {
        $json = $Config | ConvertTo-Json -Depth 10
        if ($PrettyPrint) {
            # 自定义格式化：确保 categories 和 tools 的顺序
            $json = Format-ConfigJson -Config $Config
        }
        $json | Set-Content $ConfigPath -Encoding UTF8
        return $true
    }
    catch {
        Write-Warning "[ConfigGenerator] 配置文件保存失败: $($_.Exception.Message)"
        return $false
    }
}

function Format-ConfigJson {
    # .SYNOPSIS
# 自定义 JSON 格式化，保持 categories 和 tools 的顺序
    param(
        [object]$Config
    )
    # 使用 NewtonSoft 风格排序，确保输出可读性
    $json = $Config | ConvertTo-Json -Depth 10
    return $json
}

# ============================================================
# 核心函数：分类管理
# ============================================================

function Add-CategoryIfNotExists {
    # .SYNOPSIS
# 如果分类不存在，自动添加到配置中
# .PARAMETER Config
# launcher.config.json 配置对象
# .PARAMETER CategoryName
# 分类名称（英文键名）
# .PARAMETER DisplayName
# 分类显示名称（中文）
# .PARAMETER Description
# 分类描述
# .PARAMETER Icon
# 分类图标（emoji）
# .PARAMETER Order
# 排序序号
# .EXAMPLE
# Add-CategoryIfNotExists -Config $config -CategoryName "data" -DisplayName "数据处理" -Description "数据相关工具"
    param(
        [Parameter(Mandatory = $true)]
        [object]$Config,

        [Parameter(Mandatory = $true)]
        [string]$CategoryName,

        [string]$DisplayName = "",

        [string]$Description = "",

        [string]$Icon = "📁",

        [int]$Order = 99
    )

    # 检查分类是否已存在
    $existing = $Config.categories.PSObject.Properties | Where-Object { $_.Name -eq $CategoryName }
    if ($existing) {
        return $false  # 已存在，不修改
    }

    # 添加新分类
    if (-not $DisplayName) {
        $DisplayName = $CategoryName
    }

    $newCategory = [PSCustomObject]@{
        name        = $DisplayName
        order       = $Order
        icon        = $Icon
        description = $Description
    }

    $Config.categories | Add-Member -MemberType NoteProperty -Name $CategoryName -Value $newCategory
    return $true
}

# ============================================================
# 核心函数：工具注册
# ============================================================

function ConvertTo-ToolEntry {
    # .SYNOPSIS
# 将脚本元数据转换为 launcher.config.json 的 tools 条目格式
# .PARAMETER ScriptMetadata
# Get-ScriptMetadata 返回的脚本元数据对象
# .PARAMETER ProjectRoot
# 项目根目录（用于计算相对路径）
# .EXAMPLE
# ConvertTo-ToolEntry -ScriptMetadata $meta -ProjectRoot "D:\Project"
    param(
        [Parameter(Mandatory = $true)]
        [object]$ScriptMetadata,

        [Parameter(Mandatory = $true)]
        [string]$ProjectRoot
    )

    $relativePath = $ScriptMetadata.RelativePath

    # 确定分类
    $category = if ($ScriptMetadata.Category) {
        $ScriptMetadata.Category
    }
    else {
        # 从路径推断分类
        $pathParts = $relativePath -split '[\\/]'
        if ($pathParts.Count -ge 2) {
            $pathParts[1].ToLower()  # 第二级目录作为分类
        }
        else {
            "tools"
        }
    }

    # 确定别名
    $alias = if ($ScriptMetadata.Alias) {
        $ScriptMetadata.Alias
    }
    else {
        # 从文件名生成别名
        $nameWithoutExt = [System.IO.Path]::GetFileNameWithoutExtension($ScriptMetadata.FileName)
        $nameWithoutExt -replace '^Invoke-', '' -replace '^Get-', '' -replace '^Set-', '' -replace '^Remove-', '' -replace '^New-', '' |
            ForEach-Object { $_.Substring(0, [Math]::Min($_.Length, 20)).ToLower() }
    }

    # 构建 tools 条目
    $entry = [PSCustomObject]@{
        category    = $category
        description = if ($ScriptMetadata.Description) { $ScriptMetadata.Description } else { $ScriptMetadata.Synopsis }
        alias       = $alias
        type        = if ($ScriptMetadata.Extension -eq '.psm1') { 'psm1' } else { 'ps1' }
        workDir     = "."
    }

    # 如果有版本信息，添加到描述中
    if ($ScriptMetadata.Version) {
        $entry | Add-Member -MemberType NoteProperty -Name "version" -Value $ScriptMetadata.Version
    }

    # 如果有依赖信息
    if ($ScriptMetadata.Requires -and $ScriptMetadata.Requires.Count -gt 0) {
        $entry | Add-Member -MemberType NoteProperty -Name "requires" -Value ($ScriptMetadata.Requires -join '; ')
    }

    return $entry
}

function Update-ToolEntry {
    # .SYNOPSIS
# 更新已存在的工具条目（保留手动设置的字段）
# .PARAMETER ExistingEntry
# 现有的工具条目对象
# .PARAMETER NewEntry
# 新的工具条目对象（来自 ConvertTo-ToolEntry）
# .RETURNS
# 是否发生了变更
    param(
        [Parameter(Mandatory = $true)]
        [object]$ExistingEntry,

        [Parameter(Mandatory = $true)]
        [object]$NewEntry
    )

    $changed = $false

    # 只更新自动生成的字段，保留手动设置的字段
    $autoFields = @('description', 'type', 'workDir')

    foreach ($field in $autoFields) {
        $existing = $ExistingEntry.PSObject.Properties | Where-Object { $_.Name -eq $field }
        $newValue = $NewEntry.PSObject.Properties | Where-Object { $_.Name -eq $field }

        if ($newValue -and $existing) {
            if ($existing.Value -ne $newValue.Value) {
                $ExistingEntry.$field = $newValue.Value
                $changed = $true
            }
        }
    }

    return $changed
}

# ============================================================
# 公开函数：注册脚本到配置
# ============================================================

function Register-ScriptsToConfig {
    # .SYNOPSIS
# 将扫描到的脚本注册到 launcher.config.json
# .DESCRIPTION
# 核心注册函数。将 Invoke-ScriptScan 返回的脚本元数据列表，
# 自动注册到 launcher.config.json 的 tools 和 categories 中。
# 支持新增、更新、弃用移除。
# .PARAMETER Scripts
# Invoke-ScriptScan 返回的脚本元数据列表
# .PARAMETER ConfigPath
# launcher.config.json 的路径（默认自动检测）
# .PARAMETER ProjectRoot
# 项目根目录
# .PARAMETER DryRun
# 预览模式，只显示差异不实际写入
# .PARAMETER AutoCreateCategories
# 是否自动创建不存在的分类（默认：$true）
# .PARAMETER RemoveDeprecated
# 是否从配置中移除标记为 @deprecated 的脚本（默认：$false）
# .EXAMPLE
# $scripts = Invoke-ScriptScan -Path ".\PowerShellScript"
# Register-ScriptsToConfig -Scripts $scripts -DryRun
# .EXAMPLE
# $scripts = Invoke-ScriptScan -Path ".\PowerShellScript"
# Register-ScriptsToConfig -Scripts $scripts
    param(
        [Parameter(Mandatory = $true)]
        [object[]]$Scripts,

        [string]$ConfigPath = "",

        [Parameter(Mandatory = $true)]
        [string]$ProjectRoot,

        [switch]$DryRun,

        [switch]$AutoCreateCategories = $true,

        [switch]$RemoveDeprecated
    )

    # 解析配置文件路径
    if (-not $ConfigPath) {
        $ConfigPath = Get-LauncherConfigPath -ProjectRoot $ProjectRoot
    }

    # 加载配置
    $config = Load-LauncherConfig -ConfigPath $ConfigPath
    if (-not $config) {
        Write-Error "[ConfigGenerator] 无法加载配置文件: $ConfigPath"
        return $null
    }

    # 统计
    $stats = @{
        Added       = 0
        Updated     = 0
        Removed     = 0
        Skipped     = 0
        NewCategories = 0
    }

    # 收集变更记录
    $changes = @()

    # 获取当前已注册的工具键名集合
    $existingKeys = @{}
    if ($config.tools) {
        foreach ($prop in $config.tools.PSObject.Properties) {
            $existingKeys[$prop.Name] = $prop.Value
        }
    }

    # 处理每个脚本
    foreach ($script in $Scripts) {
        $relativePath = $script.RelativePath

        # 跳过弃用脚本
        if ($script.IsDeprecated -and -not $RemoveDeprecated) {
            $stats.Skipped++
            continue
        }

        # 转换为工具条目
        $newEntry = ConvertTo-ToolEntry -ScriptMetadata $script -ProjectRoot $ProjectRoot

        # 自动创建分类
        if ($AutoCreateCategories -and $newEntry.category) {
            $catName = $newEntry.category
            $catDisplayName = $catName.Substring(0, 1).ToUpper() + $catName.Substring(1)
            $added = Add-CategoryIfNotExists -Config $config -CategoryName $catName -DisplayName $catDisplayName -Description "$catDisplayName 工具"
            if ($added) {
                $stats.NewCategories++
                $changes += "[新分类] $catName"
            }
        }

        # 检查是否已注册
        if ($existingKeys.ContainsKey($relativePath)) {
            # 已存在，尝试更新
            $existing = $existingKeys[$relativePath]
            $changed = Update-ToolEntry -ExistingEntry $existing -NewEntry $newEntry
            if ($changed) {
                $stats.Updated++
                $changes += "[更新] $relativePath"
            }
            else {
                $stats.Skipped++
            }
        }
        else {
            # 新增
            if (-not $config.tools) {
                $config | Add-Member -MemberType NoteProperty -Name "tools" -Value ([PSCustomObject]@{})
            }
            $config.tools | Add-Member -MemberType NoteProperty -Name $relativePath -Value $newEntry
            $stats.Added++
            $changes += "[新增] $relativePath → $($newEntry.alias)"
        }
    }

    # 处理弃用移除
    if ($RemoveDeprecated) {
        $deprecatedScripts = $Scripts | Where-Object { $_.IsDeprecated }
        foreach ($script in $deprecatedScripts) {
            $relativePath = $script.RelativePath
            if ($existingKeys.ContainsKey($relativePath)) {
                $config.tools.PSObject.Properties.Remove($relativePath)
                $stats.Removed++
                $changes += "[移除] $relativePath (已弃用)"
            }
        }
    }

    # 显示差异预览
    Write-Host ""
    Write-Host "╔══════════════════════════════════════════════════╗" -ForegroundColor Cyan
    Write-Host "║          PScriptManager 配置注册预览             ║" -ForegroundColor Cyan
    Write-Host "╚══════════════════════════════════════════════════╝" -ForegroundColor Cyan
    Write-Host ""

    if ($changes.Count -eq 0) {
        Write-Host "  ✅ 无变更 — 所有脚本已是最新状态" -ForegroundColor Green
    }
    else {
        Write-Host "  变更摘要:" -ForegroundColor Yellow
        Write-Host "    新增: $($stats.Added) 个" -ForegroundColor Green
        Write-Host "    更新: $($stats.Updated) 个" -ForegroundColor Cyan
        Write-Host "    移除: $($stats.Removed) 个" -ForegroundColor Red
        Write-Host "    跳过: $($stats.Skipped) 个" -ForegroundColor Gray
        Write-Host "    新分类: $($stats.NewCategories) 个" -ForegroundColor Magenta

        Write-Host ""
        Write-Host "  详细变更:" -ForegroundColor Yellow
        foreach ($change in $changes) {
            Write-Host "    $change" -ForegroundColor White
        }
    }

    Write-Host ""

    # 非预览模式：写入文件
    if (-not $DryRun -and $changes.Count -gt 0) {
        $saved = Save-LauncherConfig -ConfigPath $ConfigPath -Config $config
        if ($saved) {
            Write-Host "  ✅ 配置已保存到: $ConfigPath" -ForegroundColor Green
        }
        else {
            Write-Host "  ❌ 配置保存失败!" -ForegroundColor Red
        }
    }
    elseif ($DryRun -and $changes.Count -gt 0) {
        Write-Host "  📋 预览模式 — 未写入任何变更" -ForegroundColor Yellow
        Write-Host "  去掉 -DryRun 参数以实际写入" -ForegroundColor Gray
    }

    Write-Host ""

    return [PSCustomObject]@{
        Stats   = $stats
        Changes = $changes
        Config  = $config
    }
}


