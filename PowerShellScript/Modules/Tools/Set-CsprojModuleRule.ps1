<#
.SYNOPSIS
    批量注入/更新/验证 .csproj 文件的 ModuleRule 属性（ModuleRule 项目规则）
.DESCRIPTION
    为 .csproj 文件添加 ModuleRule 自定义属性，使 ModuleRule 能够动态读取项目规则。
    支持三种模式：
      1. 单项目注入：-Path -Title -Layer -Description -Keywords
      2. 批量注入：-Config (CSV 配置文件路径)
      3. 验证模式：-Validate (检查所有项目是否已注入)
.PARAMETER Path
    目标 .csproj 文件路径（相对于项目根目录）
.PARAMETER Title
    模块显示名称
.PARAMETER Layer
    层编号（0=辅助/测试, 1=基础层, 2=模块层, 3=共享层, 4=操作层, 5=应用层, 6=平台层, 7=工具层）
.PARAMETER Description
    模块功能描述
.PARAMETER Keywords
    关键词列表（逗号分隔）
.PARAMETER Rules
    模块级规则列表（分号分隔）
.PARAMETER Prompts
    模块级提示词列表（分号分隔）
.PARAMETER Config
    CSV 配置文件路径，格式：Path,Title,Layer,Description,Keywords,Rules,Prompts
.PARAMETER Validate
    验证所有 .csproj 是否已注入 ModuleRule 属性
.PARAMETER ProjectRoot
    项目根目录（默认：当前目录）
.EXAMPLE
    .\Set-CsprojModuleRule.ps1 -Path "BasicMethod\BasicMethod.csproj" -Title "基础方法库" -Layer 1 -Description "核心抽象层，提供基础工具函数和框架" -Keywords "基础,工具,框架"
.EXAMPLE
    .\Set-CsprojModuleRule.ps1 -Config "ModuleRule-config.csv"
.EXAMPLE
    .\Set-CsprojModuleRule.ps1 -Validate
#>

param(
    [string]$Path,
    [string]$Title,
    [int]$Layer = -1,
    [string]$Description,
    [string]$Keywords,
    [string]$Rules,
    [string]$Prompts,
    [string]$Config,
    [switch]$Validate,
    [string]$ProjectRoot = (Get-Location)
)

# ============================================================
# ModuleRule 属性 XML 模板
# ============================================================
function Get-ModuleRulePropertyGroupXml {
    param(
        [string]$title,
        [int]$layer,
        [string]$description,
        [string]$keywords,
        [string]$rules,
        [string]$prompts
    )

    $sb = [System.Text.StringBuilder]::new()

    # PropertyGroup
    [void]$sb.AppendLine("  <PropertyGroup Label=""ModuleRule"">")
    if ($title)    { [void]$sb.AppendLine("    <ModuleRule_Title>$title</ModuleRule_Title>") }
    if ($layer -ge 0) { [void]$sb.AppendLine("    <ModuleRule_Layer>$layer</ModuleRule_Layer>") }
    if ($description) { [void]$sb.AppendLine("    <ModuleRule_Description>$description</ModuleRule_Description>") }
    if ($keywords) { [void]$sb.AppendLine("    <ModuleRule_Keywords>$keywords</ModuleRule_Keywords>") }
    [void]$sb.AppendLine("  </PropertyGroup>")

    # Rules
    if ($rules) {
        [void]$sb.AppendLine("")
        [void]$sb.AppendLine("  <ItemGroup Label=""ModuleRule_Rules"">")
        foreach ($rule in ($rules -split ';' | ForEach-Object { $_.Trim() })) {
            if ($rule) {
                [void]$sb.AppendLine("    <ModuleRule_Rule Include=""$rule"" />")
            }
        }
        [void]$sb.AppendLine("  </ItemGroup>")
    }

    # Prompts
    if ($prompts) {
        [void]$sb.AppendLine("")
        [void]$sb.AppendLine("  <ItemGroup Label=""ModuleRule_Prompts"">")
        foreach ($prompt in ($prompts -split ';' | ForEach-Object { $_.Trim() })) {
            if ($prompt) {
                [void]$sb.AppendLine("    <ModuleRule_Prompt Include=""$prompt"" />")
            }
        }
        [void]$sb.AppendLine("  </ItemGroup>")
    }

    return $sb.ToString()
}

# ============================================================
# 检查是否已包含 ModuleRule 属性
# ============================================================
function Test-HasModuleRuleProperty {
    param([string]$filePath)
    if (-not (Test-Path $filePath)) { return $false }
    $content = Get-Content $filePath -Raw -ErrorAction SilentlyContinue
    return $content -match '<PropertyGroup Label="ModuleRule">'
}

# ============================================================
# 注入 ModuleRule 属性到单个 .csproj
# ============================================================
function Set-ModuleRuleProperty {
    param(
        [string]$filePath,
        [string]$title,
        [int]$layer,
        [string]$description,
        [string]$keywords,
        [string]$rules,
        [string]$prompts
    )

    if (-not (Test-Path $filePath)) {
        Write-Warning "文件不存在: $filePath"
        return $false
    }

    $content = Get-Content $filePath -Raw

    # 如果已存在 ModuleRule 属性，先移除旧的
    if ($content -match '(?s)<PropertyGroup Label="ModuleRule">.*?</PropertyGroup>') {
        $content = $content -replace '(?s)\s*<PropertyGroup Label="ModuleRule">.*?</PropertyGroup>', ''
    }
    if ($content -match '(?s)<ItemGroup Label="ModuleRule_Rules">.*?</ItemGroup>') {
        $content = $content -replace '(?s)\s*<ItemGroup Label="ModuleRule_Rules">.*?</ItemGroup>', ''
    }
    if ($content -match '(?s)<ItemGroup Label="ModuleRule_Prompts">.*?</ItemGroup>') {
        $content = $content -replace '(?s)\s*<ItemGroup Label="ModuleRule_Prompts">.*?</ItemGroup>', ''
    }

    # 生成新的 ModuleRule 属性 XML
    $ModuleRuleXml = Get-ModuleRulePropertyGroupXml -title $title -layer $layer -description $description -keywords $keywords -rules $rules -prompts $prompts

    # 在第一个 PropertyGroup 之后插入
    if ($content -match '(?s)(<Project[^>]*>)\s*(\r?\n)\s*(<PropertyGroup[^>]*>)') {
        $content = $content -replace '(?s)(<Project[^>]*>\s*\r?\n\s*)(<PropertyGroup[^>]*>)', "`$1$ModuleRuleXml`r`n  `$2"
        Set-Content $filePath -Value $content -Encoding UTF8
        Write-Host "✅ 已注入 ModuleRule 属性: $filePath" -ForegroundColor Green
        return $true
    } else {
        Write-Warning "无法解析 Project 结构: $filePath"
        return $false
    }
}

# ============================================================
# 验证模式
# ============================================================
function Invoke-ValidateModuleRule {
    Write-Host "`n🔍 验证所有 .csproj 文件的 ModuleRule 属性状态..." -ForegroundColor Cyan

    $csprojFiles = Get-ChildItem -Path $ProjectRoot -Recurse -Filter "*.csproj" |
        Where-Object { $_.FullName -notmatch '\\obj\\' -and $_.FullName -notmatch '\\bin\\' -and $_.FullName -notmatch '\\backups\\' }

    $total = 0
    $hasModuleRule = 0
    $noModuleRule = @()

    foreach ($file in $csprojFiles) {
        $total++
        $relativePath = [System.IO.Path]::GetRelativePath($ProjectRoot, $file.FullName)
        if (Test-HasModuleRuleProperty -filePath $file.FullName) {
            $hasModuleRule++
        } else {
            $noModuleRule += $relativePath
        }
    }

    Write-Host "`n📊 统计结果:" -ForegroundColor Cyan
    Write-Host "  总项目数: $total"
    Write-Host "  已注入: $hasModuleRule" -ForegroundColor Green
    Write-Host "  未注入: $($noModuleRule.Count)" -ForegroundColor $(if ($noModuleRule.Count -gt 0) { "Yellow" } else { "Green" })

    if ($noModuleRule.Count -gt 0) {
        Write-Host "`n📋 未注入的项目列表:" -ForegroundColor Yellow
        $noModuleRule | ForEach-Object { Write-Host "  - $_" }
    }

    return $noModuleRule.Count -eq 0
}

# ============================================================
# 批量模式（从 CSV 配置读取）
# ============================================================
function Invoke-BatchFromConfig {
    param([string]$configPath)

    if (-not (Test-Path $configPath)) {
        Write-Error "配置文件不存在: $configPath"
        return
    }

    $entries = Import-Csv $configPath
    $success = 0
    $failed = 0

    foreach ($entry in $entries) {
        $filePath = Join-Path $ProjectRoot $entry.Path
        if (Set-ModuleRuleProperty -filePath $filePath -title $entry.Title -layer ([int]::Parse($entry.Layer)) -description $entry.Description -keywords $entry.Keywords -rules $entry.Rules -prompts $entry.Prompts) {
            $success++
        } else {
            $failed++
        }
    }

    Write-Host "`n📊 批量注入完成: 成功 $success, 失败 $failed" -ForegroundColor Cyan
}

# ============================================================
# 主入口
# ============================================================
Write-Host "╔══════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║     Set-CsprojModuleRule.ps1 - ModuleRule 属性注入工具   ║" -ForegroundColor Cyan
Write-Host "╚══════════════════════════════════════════╝" -ForegroundColor Cyan

if ($Validate) {
    Invoke-ValidateModuleRule
}
elseif ($Config) {
    Invoke-BatchFromConfig -configPath $Config
}
elseif ($Path) {
    $fullPath = Join-Path $ProjectRoot $Path
    Set-ModuleRuleProperty -filePath $fullPath -title $Title -layer $Layer -description $Description -keywords $Keywords -rules $Rules -prompts $Prompts
}
else {
    Write-Host "`n用法: 请指定 -Path, -Config 或 -Validate 参数" -ForegroundColor Yellow
    Write-Host "示例:" -ForegroundColor Yellow
    Write-Host "  单项目注入: .\Set-CsprojModuleRule.ps1 -Path ""BasicMethod\BasicMethod.csproj"" -Title ""基础方法库"" -Layer 1 -Description ""核心抽象层""" -ForegroundColor Yellow
    Write-Host "  批量注入:   .\Set-CsprojModuleRule.ps1 -Config ""ModuleRule-config.csv""" -ForegroundColor Yellow
    Write-Host "  验证模式:   .\Set-CsprojModuleRule.ps1 -Validate" -ForegroundColor Yellow
}

