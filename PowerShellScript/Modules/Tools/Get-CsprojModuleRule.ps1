<#
.SYNOPSIS
    查询 .csproj 文件的 ModuleRule 属性（ModuleRule 项目规则）
.DESCRIPTION
    从 .csproj 文件中读取 ModuleRule 自定义属性，支持多种查询模式。
    用于 ModuleRule 在对话开始时获取项目架构概览，或按需查询特定模块详情。
.PARAMETER Overview
    获取所有项目的 ModuleRule 属性概览（按层分组）
.PARAMETER Path
    查询单个 .csproj 文件的 ModuleRule 属性详情
.PARAMETER Layer
    按层编号筛选（0-7）
.PARAMETER Keyword
    按关键词搜索
.PARAMETER Detail
    显示详细信息（与 -Overview 或 -Path 配合使用）
.PARAMETER ProjectRoot
    项目根目录（默认：当前目录）
.EXAMPLE
    .\Get-CsprojModuleRule.ps1 -Overview
.EXAMPLE
    .\Get-CsprojModuleRule.ps1 -Overview -Detail
.EXAMPLE
    .\Get-CsprojModuleRule.ps1 -Path "BasicMethod\BasicMethod.csproj"
.EXAMPLE
    .\Get-CsprojModuleRule.ps1 -Layer 1
.EXAMPLE
    .\Get-CsprojModuleRule.ps1 -Keyword "pipeline"
#>

param(
    [switch]$Overview,
    [string]$Path,
    [int]$Layer = -1,
    [string]$Keyword,
    [switch]$Detail,
    [string]$ProjectRoot = (Get-Location)
)

# ============================================================
# 层名称映射
# ============================================================
$LayerNames = @{
    0 = "辅助/测试"
    1 = "基础层 (BasicMethod)"
    2 = "模块层 (Modules)"
    3 = "共享层 (Share)"
    4 = "操作层 (Operation)"
    5 = "应用层 (FileDriven)"
    6 = "平台层 (Plate)"
    7 = "工具层 (Storage/Utilitys)"
}

# ============================================================
# 从 .csproj 文件解析 ModuleRule 属性
# ============================================================
function Get-ModuleRuleProperty {
    param([string]$filePath)

    if (-not (Test-Path $filePath)) { return $null }

    $content = Get-Content $filePath -Raw -ErrorAction SilentlyContinue
    if (-not $content) { return $null }

    # 检查是否包含 ModuleRule 属性
    if ($content -notmatch '<PropertyGroup Label="ModuleRule">') { return $null }

    $result = [PSCustomObject]@{
        Path        = $filePath
        Title       = ""
        Layer       = -1
        Description = ""
        Keywords    = ""
        Rules       = @()
        Prompts     = @()
        HasModuleRule    = $true
    }

    # 提取 PropertyGroup 内容
    if ($content -match '(?s)<PropertyGroup Label="ModuleRule">(.*?)</PropertyGroup>') {
        $groupContent = $matches[1]

        if ($groupContent -match '<ModuleRule_Title>(.*?)</ModuleRule_Title>') {
            $result.Title = $matches[1]
        }
        if ($groupContent -match '<ModuleRule_Layer>(.*?)</ModuleRule_Layer>') {
            $result.Layer = [int]::Parse($matches[1])
        }
        if ($groupContent -match '<ModuleRule_Description>(.*?)</ModuleRule_Description>') {
            $result.Description = $matches[1]
        }
        if ($groupContent -match '<ModuleRule_Keywords>(.*?)</ModuleRule_Keywords>') {
            $result.Keywords = $matches[1]
        }
    }

    # 提取 Rules
    if ($content -match '(?s)<ItemGroup Label="ModuleRule_Rules">(.*?)</ItemGroup>') {
        $rulesContent = $matches[1]
        $matches = [regex]::Matches($rulesContent, '<ModuleRule_Rule Include="(.*?)"')
        $result.Rules = $matches | ForEach-Object { $_.Groups[1].Value }
    }

    # 提取 Prompts
    if ($content -match '(?s)<ItemGroup Label="ModuleRule_Prompts">(.*?)</ItemGroup>') {
        $promptsContent = $matches[1]
        $matches = [regex]::Matches($promptsContent, '<ModuleRule_Prompt Include="(.*?)"')
        $result.Prompts = $matches | ForEach-Object { $_.Groups[1].Value }
    }

    return $result
}

# ============================================================
# 获取所有 .csproj 文件的 ModuleRule 属性
# ============================================================
function Get-AllModuleRuleProperties {
    $csprojFiles = Get-ChildItem -Path $ProjectRoot -Recurse -Filter "*.csproj" |
        Where-Object { $_.FullName -notmatch '\\obj\\' -and $_.FullName -notmatch '\\bin\\' -and $_.FullName -notmatch '\\backups\\' }

    $results = @()
    foreach ($file in $csprojFiles) {
        $prop = Get-ModuleRuleProperty -filePath $file.FullName
        if ($prop) {
            $relativePath = [System.IO.Path]::GetRelativePath($ProjectRoot, $file.FullName)
            $prop.Path = $relativePath
            $results += $prop
        }
    }
    return $results
}

# ============================================================
# 显示概览（按层分组）
# ============================================================
function Show-Overview {
    param([bool]$showDetail)

    $props = Get-AllModuleRuleProperties

    if ($props.Count -eq 0) {
        Write-Host "`n⚠️  未找到任何包含 ModuleRule 属性的 .csproj 文件" -ForegroundColor Yellow
        Write-Host "   请先运行 Set-CsprojModuleRule.ps1 注入 ModuleRule 属性" -ForegroundColor Yellow
        return
    }

    # 按层分组
    $grouped = $props | Group-Object Layer

    Write-Host "`n╔══════════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
    Write-Host "║          项目架构概览（按层分组）                          ║" -ForegroundColor Cyan
    Write-Host "╚══════════════════════════════════════════════════════════════╝" -ForegroundColor Cyan

    foreach ($group in ($grouped | Sort-Object Name)) {
        $layerName = $LayerNames[[int]$group.Name]
        if (-not $layerName) { $layerName = "未知层" }

        Write-Host "`n📂 [层 $($group.Name)] $layerName ($($group.Count) 个项目)" -ForegroundColor Cyan

        foreach ($prop in ($group.Group | Sort-Object Path)) {
            if ($showDetail) {
                Write-Host "  ├─ $($prop.Path)" -ForegroundColor White
                Write-Host "  │  ├─ 标题: $($prop.Title)" -ForegroundColor Gray
                Write-Host "  │  ├─ 描述: $($prop.Description)" -ForegroundColor Gray
                if ($prop.Keywords) {
                    Write-Host "  │  ├─ 关键词: $($prop.Keywords)" -ForegroundColor Gray
                }
                if ($prop.Rules.Count -gt 0) {
                    Write-Host "  │  ├─ 规则: $($prop.Rules -join '; ')" -ForegroundColor Gray
                }
                if ($prop.Prompts.Count -gt 0) {
                    Write-Host "  │  └─ 提示: $($prop.Prompts -join '; ')" -ForegroundColor Gray
                }
            } else {
                Write-Host "  ├─ $($prop.Path) → $($prop.Title)" -ForegroundColor White
            }
        }
    }

    Write-Host "`n📊 总计: $($props.Count) 个项目已配置 ModuleRule 属性" -ForegroundColor Green
}

# ============================================================
# 显示单个项目详情
# ============================================================
function Show-Detail {
    param([string]$filePath)

    $fullPath = if ([System.IO.Path]::IsPathRooted($filePath)) {
        $filePath
    } else {
        Join-Path $ProjectRoot $filePath
    }

    $prop = Get-ModuleRuleProperty -filePath $fullPath

    if (-not $prop) {
        Write-Host "`n⚠️  未找到 ModuleRule 属性: $filePath" -ForegroundColor Yellow
        return
    }

    $relativePath = [System.IO.Path]::GetRelativePath($ProjectRoot, $fullPath)
    $layerName = $LayerNames[$prop.Layer]
    if (-not $layerName) { $layerName = "未知层" }

    Write-Host "`n╔══════════════════════════════════════════╗" -ForegroundColor Cyan
    Write-Host "║  模块详情: $($prop.Title)" -ForegroundColor Cyan
    Write-Host "╚══════════════════════════════════════════╝" -ForegroundColor Cyan
    Write-Host "  文件路径: $relativePath"
    Write-Host "  层: [$($prop.Layer)] $layerName"
    Write-Host "  描述: $($prop.Description)"
    if ($prop.Keywords) {
        Write-Host "  关键词: $($prop.Keywords)"
    }
    if ($prop.Rules.Count -gt 0) {
        Write-Host "`n  规则:"
        $prop.Rules | ForEach-Object { Write-Host "    - $_" }
    }
    if ($prop.Prompts.Count -gt 0) {
        Write-Host "`n  提示词:"
        $prop.Prompts | ForEach-Object { Write-Host "    - $_" }
    }
}

# ============================================================
# 按层筛选
# ============================================================
function Show-ByLayer {
    param([int]$layer)

    $props = Get-AllModuleRuleProperties | Where-Object { $_.Layer -eq $layer }
    $layerName = $LayerNames[$layer]

    if ($props.Count -eq 0) {
        Write-Host "`n⚠️  层 [$layer] $layerName 下没有已配置 ModuleRule 属性的项目" -ForegroundColor Yellow
        return
    }

    Write-Host "`n📂 [层 $layer] $layerName ($($props.Count) 个项目)" -ForegroundColor Cyan
    foreach ($prop in ($props | Sort-Object Path)) {
        Write-Host "  ├─ $($prop.Path) → $($prop.Title)" -ForegroundColor White
        Write-Host "  │  └─ $($prop.Description)" -ForegroundColor Gray
    }
}

# ============================================================
# 按关键词搜索
# ============================================================
function Search-ByKeyword {
    param([string]$keyword)

    $props = Get-AllModuleRuleProperties | Where-Object {
        $_.Title -match $keyword -or
        $_.Description -match $keyword -or
        $_.Keywords -match $keyword -or
        $_.Path -match $keyword
    }

    if ($props.Count -eq 0) {
        Write-Host "`n⚠️  未找到匹配 ""$keyword"" 的项目" -ForegroundColor Yellow
        return
    }

    Write-Host "`n🔍 搜索 ""$keyword"" 找到 $($props.Count) 个匹配项目:" -ForegroundColor Cyan
    foreach ($prop in ($props | Sort-Object Path)) {
        $layerName = $LayerNames[$prop.Layer]
        Write-Host "  ├─ [$($prop.Layer)] $($prop.Path) → $($prop.Title)" -ForegroundColor White
        Write-Host "  │  └─ $($prop.Description)" -ForegroundColor Gray
    }
}

# ============================================================
# 主入口
# ============================================================
Write-Host "╔══════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║  Get-CsprojModuleRule.ps1 - ModuleRule 属性查询工具   ║" -ForegroundColor Cyan
Write-Host "╚══════════════════════════════════════════╝" -ForegroundColor Cyan

if ($Overview) {
    Show-Overview -showDetail $Detail
}
elseif ($Path) {
    Show-Detail -filePath $Path
}
elseif ($Layer -ge 0) {
    Show-ByLayer -layer $Layer
}
elseif ($Keyword) {
    Search-ByKeyword -keyword $Keyword
}
else {
    # 默认行为：显示简洁概览
    Show-Overview -showDetail $false
}

