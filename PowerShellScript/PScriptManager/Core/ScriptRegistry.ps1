# .SYNOPSIS
# PScriptManager 脚本注册中心 — 函数名 ↔ 脚本路径 的查找表
# .DESCRIPTION
# 核心功能：
# 1. 构建注册表 — 扫描脚本，提取 @exports 和函数定义，建立函数名→路径映射
# 2. 持久化 — 注册表保存到 registry.json，下次启动直接加载
# 3. 调用代理 — Invoke-Function 通过函数名自动查找、加载、调用
# 4. 依赖解析 — 根据 @requires 自动加载依赖脚本
#
# 设计目标：所有脚本通过函数名互相调用，完全解耦物理路径。
# 脚本移动后只需重新扫描更新注册表，所有调用方无需修改。
#
# @version: 1.0.0
# @author: WorkFlowPrj
# @category: 框架核心

# ============================================================
# 注册表数据结构
# ============================================================

$script:Registry = @{
    # 版本信息
    Version     = "1.0.0"
    LastScan    = $null          # 最后扫描时间

    # 函数名 → 脚本信息
    # @{ "FunctionName" = @{ Path="..."; Alias="..."; Description="..."; Exports=@(...) } }
    Functions   = @{}

    # 别名 → 函数名
    # @{ "alias" = "FunctionName" }
    Aliases     = @{}

    # 脚本路径 → 该脚本导出的函数列表
    # @{ "relative/path.ps1" = @("Func1", "Func2") }
    ScriptExports = @{}

    # 脚本路径 → 该脚本的 @requires 依赖（函数名列表）
    # @{ "relative/path.ps1" = @("DepFunc1", "DepFunc2") }
    ScriptRequires = @{}
}

# ============================================================
# 注册表文件路径
# ============================================================

$script:RegistryFilePath = ""

function Set-RegistryFilePath {
    <#
    .SYNOPSIS
        设置注册表文件的存储路径
    .PARAMETER Path
        注册表 JSON 文件的路径
    .EXAMPLE
        Set-RegistryFilePath -Path "D:\Project\registry.json"
    #>
    param([string]$Path)
    $script:RegistryFilePath = $Path
}

function Get-RegistryFilePath {
    <#
    .SYNOPSIS
        获取注册表文件的存储路径
    #>
    return $script:RegistryFilePath
}

# ============================================================
# 注册表构建
# ============================================================

function Build-ScriptRegistry {
    <#
    .SYNOPSIS
        扫描脚本并构建注册表
    .DESCRIPTION
        扫描指定目录下的所有 .ps1/.psm1 文件，提取 @exports 标签
        和函数定义，建立函数名→脚本路径的映射表。
    .PARAMETER Scripts
        Invoke-ScriptScan 返回的脚本元数据列表
    .PARAMETER ProjectRoot
        项目根目录（用于计算相对路径）
    .PARAMETER Force
        强制重新扫描所有脚本的函数定义（默认只使用 @exports 标签）
    .EXAMPLE
        $scripts = Invoke-ScriptScan -Path ".\PowerShellScript"
        Build-ScriptRegistry -Scripts $scripts -ProjectRoot "D:\Project"
    #>
    param(
        [Parameter(Mandatory = $true)]
        [object[]]$Scripts,

        [Parameter(Mandatory = $true)]
        [string]$ProjectRoot,

        [switch]$Force
    )

    # 重置注册表（保留版本信息）
    $script:Registry.Functions = @{}
    $script:Registry.Aliases = @{}
    $script:Registry.ScriptExports = @{}
    $script:Registry.ScriptRequires = @{}

    $totalFunctions = 0
    $totalAliases = 0

    foreach ($s in $Scripts) {
        $relativePath = $s.RelativePath
        $fullPath = $s.FilePath
        $exports = @()
        $requires = @()

        # 1. 从 @exports 标签获取导出的函数名
        if ($s.CustomTags.ContainsKey('exports')) {
            $exports = $s.CustomTags['exports'] -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ }
        }

        # 2. 从 @requires 标签获取依赖
        if ($s.Requires -and $s.Requires.Count -gt 0) {
            $requires = $s.Requires
        }

        # 3. 如果 Force 或没有 @exports，尝试从文件内容解析函数定义
        #    注意：当有 @exports 标签时，即使 Force 也不追加解析到的函数，
        #    因为 @exports 是作者明确声明的导出列表，优先级最高。
        if ($exports.Count -eq 0) {
            $parsedFunctions = Parse-FunctionDefinitions -Path $fullPath
            $exports += $parsedFunctions | Where-Object { $_ -notin $exports }
        }
        elseif ($Force) {
            # Force 模式下，仅当 @exports 为空时才补充解析
            # 但 @exports 已有内容时，尊重作者声明
        }

        # 4. 如果仍然没有找到导出函数，使用 Synopsis 作为默认函数名
        if ($exports.Count -eq 0 -and $s.Synopsis) {
            # 检查 Synopsis 是否看起来像函数名
            $candidate = $s.Synopsis.Trim()
            if ($candidate -match '^[A-Za-z][A-Za-z0-9_-]+$') {
                $exports += $candidate
            }
        }

        # 注册到注册表
        if ($exports.Count -gt 0) {
            $script:Registry.ScriptExports[$relativePath] = $exports

            foreach ($funcName in $exports) {
                # 如果函数已注册，优先保留有 @exports 标签的版本
                $hasExisting = $script:Registry.Functions.ContainsKey($funcName)
                $hasExportsTag = $s.CustomTags.ContainsKey('exports') -and [string]::IsNullOrWhiteSpace($s.CustomTags['exports']) -eq $false

                if ($hasExisting -and -not $hasExportsTag) {
                    # 当前脚本没有 @exports 标签，但已有注册 → 跳过（保留已有注册）
                    continue
                }

                $script:Registry.Functions[$funcName] = @{
                    Path        = $relativePath
                    FullPath    = $fullPath
                    Alias       = if ($s.Alias) { $s.Alias } else { $null }
                    Description = $s.Synopsis
                    Category    = $s.Category
                    Version     = $s.Version
                    Author      = $s.Author
                }
                $totalFunctions++

                # 注册别名
                if ($s.Alias) {
                    $script:Registry.Aliases[$s.Alias] = $funcName
                    $totalAliases++
                }
            }
        }

        # 注册依赖
        if ($requires.Count -gt 0) {
            $script:Registry.ScriptRequires[$relativePath] = $requires
        }
    }

    $script:Registry.LastScan = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss")

    Write-Host "  📋 注册表构建完成:" -ForegroundColor Green
    Write-Host "     函数: $totalFunctions 个 | 别名: $totalAliases 个 | 脚本: $($Scripts.Count) 个" -ForegroundColor Gray

    return $script:Registry
}

# ============================================================
# 函数定义解析
# ============================================================

function Parse-FunctionDefinitions {
    <#
    .SYNOPSIS
        从脚本文件中解析函数定义（function xxx { ... }）
    .PARAMETER Path
        脚本文件的完整路径
    .EXAMPLE
        Parse-FunctionDefinitions -Path "D:\script.ps1"
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $functions = @()

    try {
        $content = Get-Content $Path -Encoding UTF8 -ErrorAction Stop
        $inFunction = $false
        $braceDepth = 0

        foreach ($line in $content) {
            $trimmed = $line.Trim()

            # 跳过注释行
            if ($trimmed -match '^#') { continue }

            # 检测函数定义
            if ($trimmed -match '^function\s+([A-Za-z_][A-Za-z0-9_-]+)') {
                $funcName = $matches[1]
                $functions += $funcName
            }
        }
    }
    catch {
        # 解析失败时静默忽略
    }

    return $functions | Select-Object -Unique
}

# ============================================================
# 注册表持久化
# ============================================================

function Save-Registry {
    <#
    .SYNOPSIS
        将注册表保存到 JSON 文件
    .PARAMETER Path
        保存路径（默认使用 Set-RegistryFilePath 设置的路径）
    .EXAMPLE
        Save-Registry
        Save-Registry -Path "D:\registry.json"
    #>
    param(
        [string]$Path = ""
    )

    if (-not $Path) {
        $Path = $script:RegistryFilePath
    }

    if (-not $Path) {
        Write-Error "[Save-Registry] 未指定注册表文件路径，请使用 Set-RegistryFilePath 或 -Path 参数"
        return $false
    }

    try {
        # 确保目录存在
        $dir = Split-Path $Path -Parent
        if (-not (Test-Path $dir)) {
            New-Item -Path $dir -ItemType Directory -Force | Out-Null
        }

        # 转换为可序列化的格式（移除 FullPath 等运行时字段）
        $serializable = @{
            Version     = $script:Registry.Version
            LastScan    = $script:Registry.LastScan
            Functions   = @{}
            Aliases     = $script:Registry.Aliases
            ScriptExports = $script:Registry.ScriptExports
            ScriptRequires = $script:Registry.ScriptRequires
        }

        foreach ($key in $script:Registry.Functions.Keys) {
            $info = $script:Registry.Functions[$key]
            $serializable.Functions[$key] = @{
                Path        = $info.Path
                Alias       = $info.Alias
                Description = $info.Description
                Category    = $info.Category
                Version     = $info.Version
                Author      = $info.Author
            }
        }

        $json = $serializable | ConvertTo-Json -Depth 5
        $json | Set-Content $Path -Encoding UTF8

        Write-Host "  ✅ 注册表已保存: $Path" -ForegroundColor Green
        return $true
    }
    catch {
        Write-Error "[Save-Registry] 保存失败: $($_.Exception.Message)"
        return $false
    }
}

function Load-Registry {
    <#
    .SYNOPSIS
        从 JSON 文件加载注册表
    .PARAMETER Path
        加载路径（默认使用 Set-RegistryFilePath 设置的路径）
    .EXAMPLE
        Load-Registry
        Load-Registry -Path "D:\registry.json"
    #>
    param(
        [string]$Path = ""
    )

    if (-not $Path) {
        $Path = $script:RegistryFilePath
    }

    if (-not $Path -or -not (Test-Path $Path)) {
        Write-Warning "[Load-Registry] 注册表文件不存在: $Path"
        return $false
    }

    try {
        $json = Get-Content $Path -Encoding UTF8 -Raw | ConvertFrom-Json

        $script:Registry.Version = $json.Version
        $script:Registry.LastScan = $json.LastScan

        # 加载 Functions
        $script:Registry.Functions = @{}
        if ($json.Functions) {
            $funcObj = $json.Functions
            foreach ($key in $funcObj.PSObject.Properties.Name) {
                $info = $funcObj.$key
                $script:Registry.Functions[$key] = @{
                    Path        = $info.Path
                    FullPath    = $null  # 运行时填充
                    Alias       = $info.Alias
                    Description = $info.Description
                    Category    = $info.Category
                    Version     = $info.Version
                    Author      = $info.Author
                }
            }
        }

        # 加载 Aliases
        $script:Registry.Aliases = @{}
        if ($json.Aliases) {
            $aliasObj = $json.Aliases
            foreach ($key in $aliasObj.PSObject.Properties.Name) {
                $script:Registry.Aliases[$key] = $aliasObj.$key
            }
        }

        # 加载 ScriptExports
        $script:Registry.ScriptExports = @{}
        if ($json.ScriptExports) {
            $exportObj = $json.ScriptExports
            foreach ($key in $exportObj.PSObject.Properties.Name) {
                $script:Registry.ScriptExports[$key] = @($exportObj.$key)
            }
        }

        # 加载 ScriptRequires
        $script:Registry.ScriptRequires = @{}
        if ($json.ScriptRequires) {
            $reqObj = $json.ScriptRequires
            foreach ($key in $reqObj.PSObject.Properties.Name) {
                $script:Registry.ScriptRequires[$key] = @($reqObj.$key)
            }
        }

        Write-Host "  ✅ 注册表已加载: $Path" -ForegroundColor Green
        Write-Host "     函数: $($script:Registry.Functions.Count) 个 | 别名: $($script:Registry.Aliases.Count) 个" -ForegroundColor Gray
        return $true
    }
    catch {
        Write-Error "[Load-Registry] 加载失败: $($_.Exception.Message)"
        return $false
    }
}

# ============================================================
# 注册表查询
# ============================================================

function Find-Function {
    <#
    .SYNOPSIS
        查找函数名对应的脚本信息
    .PARAMETER Name
        函数名或别名
    .PARAMETER ResolveAlias
        是否自动解析别名（默认：$true）
    .EXAMPLE
        Find-Function -Name "Build-Project"
        Find-Function -Name "build"  # 自动解析别名
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [switch]$ResolveAlias = $true
    )

    # 先按函数名查找
    if ($script:Registry.Functions.ContainsKey($Name)) {
        return $script:Registry.Functions[$Name]
    }

    # 按别名查找
    if ($ResolveAlias -and $script:Registry.Aliases.ContainsKey($Name)) {
        $funcName = $script:Registry.Aliases[$Name]
        if ($script:Registry.Functions.ContainsKey($funcName)) {
            return $script:Registry.Functions[$funcName]
        }
    }

    return $null
}

function Get-RegistryFunctions {
    <#
    .SYNOPSIS
        获取注册表中所有已注册的函数列表
    .PARAMETER Category
        按分类筛选
    .PARAMETER Pattern
        按名称模式匹配（支持通配符 *）
    .EXAMPLE
        Get-RegistryFunctions
        Get-RegistryFunctions -Category "构建"
        Get-RegistryFunctions -Pattern "Build-*"
    #>
    param(
        [string]$Category = "",
        [string]$Pattern = ""
    )

    $result = @()

    foreach ($key in $script:Registry.Functions.Keys) {
        $info = $script:Registry.Functions[$key]
        $add = $true

        if ($Category -and $info.Category -ne $Category) {
            $add = $false
        }

        if ($Pattern -and $key -notlike $Pattern) {
            $add = $false
        }

        if ($add) {
            $result += [PSCustomObject]@{
                FunctionName = $key
                Path         = $info.Path
                Alias        = $info.Alias
                Description  = $info.Description
                Category     = $info.Category
                Version      = $info.Version
                Author       = $info.Author
            }
        }
    }

    return $result | Sort-Object FunctionName
}

function Get-RegistryStats {
    <#
    .SYNOPSIS
        获取注册表统计信息
    .EXAMPLE
        Get-RegistryStats
    #>
    $categories = @{}
    foreach ($key in $script:Registry.Functions.Keys) {
        $cat = $script:Registry.Functions[$key].Category
        if (-not $cat) { $cat = "未分类" }
        if (-not $categories.ContainsKey($cat)) {
            $categories[$cat] = 0
        }
        $categories[$cat]++
    }

    return [PSCustomObject]@{
        Version        = $script:Registry.Version
        LastScan       = $script:Registry.LastScan
        FunctionCount  = $script:Registry.Functions.Count
        AliasCount     = $script:Registry.Aliases.Count
        ScriptCount    = $script:Registry.ScriptExports.Count
        Categories     = $categories
    }
}

# ============================================================
# 调用代理
# ============================================================

$script:LoadedScripts = @{}  # 已加载的脚本路径 → $true

function Invoke-Function {
    <#
    .SYNOPSIS
        通过函数名调用脚本中的函数（核心调用代理）
    .DESCRIPTION
        自动完成：查找注册表 → 加载脚本 → 解析依赖 → 调用函数。
        调用方无需知道脚本的物理路径，只需知道函数名。
    .PARAMETER Name
        函数名或别名
    .PARAMETER Arguments
        传递给函数的参数（哈希表）
    .PARAMETER ProjectRoot
        项目根目录（用于解析脚本路径）
    .PARAMETER NoDependencyCheck
        跳过依赖检查（默认自动解析 @requires）
    .EXAMPLE
        Invoke-Function -Name "Build-Project" -Arguments @{ Configuration = "Release" }
        Invoke-Function -Name "build" -Arguments @{ Configuration = "Release" }  # 使用别名
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [hashtable]$Arguments = @{},

        [string]$ProjectRoot = "",

        [switch]$NoDependencyCheck
    )

    # 1. 查找函数
    $funcInfo = Find-Function -Name $Name
    if (-not $funcInfo) {
        Write-Error "[Invoke-Function] 未找到函数: $Name"
        Write-Host "  可用函数列表:" -ForegroundColor Yellow
        Get-RegistryFunctions | ForEach-Object {
            Write-Host "    $($_.FunctionName) ($($_.Path))" -ForegroundColor Gray
        }
        return $null
    }

    # 2. 确定脚本完整路径
    $scriptPath = $funcInfo.FullPath
    if (-not $scriptPath -or -not (Test-Path $scriptPath)) {
        # 尝试用 ProjectRoot + Path 拼接
        $effectiveProjectRoot = if ($ProjectRoot) { $ProjectRoot } else { $global:PScriptManagerProjectRoot }
        if ($effectiveProjectRoot -and $funcInfo.Path) {
            $scriptPath = [System.IO.Path]::GetFullPath((Join-Path $effectiveProjectRoot $funcInfo.Path))
        }
    }

    if (-not $scriptPath -or -not (Test-Path $scriptPath)) {
        Write-Error "[Invoke-Function] 脚本文件不存在: $($funcInfo.Path)"
        return $null
    }

    # 3. 解析依赖（自动加载 @requires 中声明的依赖）
    if (-not $NoDependencyCheck) {
        $relativePath = $funcInfo.Path
        if ($script:Registry.ScriptRequires.ContainsKey($relativePath)) {
            foreach ($depName in $script:Registry.ScriptRequires[$relativePath]) {
                # 检查依赖是否已加载
                $depInfo = Find-Function -Name $depName
                if ($depInfo) {
                    $depPath = $depInfo.FullPath
                    if (-not $depPath -and $ProjectRoot) {
                        $depPath = [System.IO.Path]::GetFullPath((Join-Path $ProjectRoot $depInfo.Path))
                    }
                    if ($depPath -and (Test-Path $depPath) -and -not $script:LoadedScripts.ContainsKey($depPath)) {
                        Write-Host "  🔗 加载依赖: $depName → $($depInfo.Path)" -ForegroundColor DarkGray
                        . $depPath
                        $script:LoadedScripts[$depPath] = $true
                    }
                }
            }
        }
    }

    # 4. 加载脚本（如果尚未加载）
    if (-not $script:LoadedScripts.ContainsKey($scriptPath)) {
        Write-Host "  📂 加载脚本: $($funcInfo.Path)" -ForegroundColor DarkGray
        try {
            . $scriptPath
            $script:LoadedScripts[$scriptPath] = $true
        }
        catch {
            Write-Error "[Invoke-Function] 加载脚本失败: $($funcInfo.Path) — $($_.Exception.Message)"
            return $null
        }
    }

    # 5. 调用函数
    $funcName = $Name
    # 如果传入的是别名，解析为真实函数名
    if ($script:Registry.Aliases.ContainsKey($Name)) {
        $funcName = $script:Registry.Aliases[$Name]
    }

    # 检查函数是否可用
    $command = Get-Command $funcName -ErrorAction SilentlyContinue
    if (-not $command) {
        Write-Error "[Invoke-Function] 函数未定义: $funcName（脚本已加载但未导出该函数）"
        return $null
    }

    Write-Host "  🚀 调用: $funcName" -ForegroundColor Cyan
    try {
        # 使用 splatting 传递参数
        if ($Arguments.Count -gt 0) {
            return & $funcName @Arguments
        }
        else {
            return & $funcName
        }
    }
    catch {
        Write-Error "[Invoke-Function] 调用失败: $funcName — $($_.Exception.Message)"
        return $null
    }
}

function Reset-LoadedScripts {
    <#
    .SYNOPSIS
        重置已加载脚本缓存（强制下次调用时重新加载）
    .EXAMPLE
        Reset-LoadedScripts
    #>
    $script:LoadedScripts = @{}
    Write-Host "  🔄 已重置脚本加载缓存" -ForegroundColor Yellow
}

# ============================================================
# 注册表管理命令
# ============================================================

function Show-Registry {
    <#
    .SYNOPSIS
        显示注册表内容
    .PARAMETER Category
        按分类筛选
    .PARAMETER Pattern
        按名称模式匹配
    .EXAMPLE
        Show-Registry
        Show-Registry -Category "构建"
        Show-Registry -Pattern "Build-*"
    #>
    param(
        [string]$Category = "",
        [string]$Pattern = ""
    )

    $functions = Get-RegistryFunctions -Category $Category -Pattern $Pattern

    Write-Host ""
    Write-Host "╔══════════════════════════════════════════════════╗" -ForegroundColor Cyan
    Write-Host "║          PScriptManager 函数注册表              ║" -ForegroundColor Cyan
    Write-Host "╚══════════════════════════════════════════════════╝" -ForegroundColor Cyan
    Write-Host ""

    $stats = Get-RegistryStats
    Write-Host "  📊 注册表统计:" -ForegroundColor Yellow
    Write-Host "    版本:        $($stats.Version)" -ForegroundColor Gray
    Write-Host "    最后扫描:    $($stats.LastScan)" -ForegroundColor Gray
    Write-Host "    函数总数:    $($stats.FunctionCount)" -ForegroundColor White
    Write-Host "    别名总数:    $($stats.AliasCount)" -ForegroundColor Gray
    Write-Host "    脚本总数:    $($stats.ScriptCount)" -ForegroundColor Gray

    if ($stats.Categories.Keys.Count -gt 0) {
        Write-Host ""
        Write-Host "  📂 分类分布:" -ForegroundColor Yellow
        foreach ($cat in ($stats.Categories.GetEnumerator() | Sort-Object Value -Descending)) {
            Write-Host "    • $($cat.Key): $($cat.Value) 个函数" -ForegroundColor White
        }
    }

    if ($functions.Count -gt 0) {
        Write-Host ""
        Write-Host "  📋 函数列表:" -ForegroundColor Yellow

        # 按分类分组显示
        $grouped = $functions | Group-Object Category
        foreach ($group in $grouped) {
            $catName = if ($group.Name) { $group.Name } else { "未分类" }
            Write-Host "  [$catName]" -ForegroundColor Green
            foreach ($f in ($group.Group | Sort-Object FunctionName)) {
                $aliasInfo = if ($f.Alias) { " (别名: $($f.Alias))" } else { "" }
                Write-Host "    $($f.FunctionName)$aliasInfo" -ForegroundColor White
                Write-Host "      → $($f.Path)" -ForegroundColor Gray
                if ($f.Description) {
                    Write-Host "      $($f.Description)" -ForegroundColor DarkGray
                }
            }
        }
    }
    else {
        Write-Host ""
        Write-Host "  ⚠️  注册表为空，请先运行扫描命令" -ForegroundColor Yellow
    }

    Write-Host ""
    Write-Host "  📌 使用: Invoke-Function -Name '<函数名>' -Arguments @{...}" -ForegroundColor Cyan
    Write-Host ""
}

# 注意：此文件通过 dot-source 加载，Export-ModuleMember 仅在作为 .psm1 模块时生效。
# dot-source 模式下所有函数自动可用，无需导出。
# 如果作为模块导入，取消下面的注释：
# Export-ModuleMember -Function @(
#     'Set-RegistryFilePath',
#     'Get-RegistryFilePath',
#     'Build-ScriptRegistry',
#     'Save-Registry',
#     'Load-Registry',
#     'Find-Function',
#     'Get-RegistryFunctions',
#     'Get-RegistryStats',
#     'Invoke-Function',
#     'Reset-LoadedScripts',
#     'Show-Registry'
# )
