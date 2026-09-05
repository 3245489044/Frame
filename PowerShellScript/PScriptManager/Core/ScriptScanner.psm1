<#
.SYNOPSIS
    PScriptManager 脚本扫描器 — 扫描 .ps1/.psm1 文件并解析注释元数据
.DESCRIPTION
    递归扫描指定目录下的 PowerShell 脚本文件，解析文件头注释中的元数据标签：
      @requires:   依赖的模块/脚本路径（逗号分隔）
      @version:    版本号
      @author:     作者
      @description: 描述
      @category:   分类（对应 launcher.config.json 的 categories）
      @alias:      快捷别名
      @deprecated: 标记为已弃用

    同时兼容标准 <# .SYNOPSIS #> 和 <# .DESCRIPTION #> 注释块。
.NOTES
    此模块是 PScriptManager 框架的核心组件，供 Scan-Scripts.ps1 和 Register-Script.ps1 调用。
#>

# ============================================================
# 内部：解析脚本文件头注释
# ============================================================

function Resolve-ScriptPath {
    <#
    .SYNOPSIS
        将相对路径解析为相对于项目根目录的路径
    #>
    param(
        [string]$ScriptPath,
        [string]$ProjectRoot
    )
    if ([System.IO.Path]::IsPathRooted($ScriptPath)) {
        return $ScriptPath
    }
    return [System.IO.Path]::GetFullPath((Join-Path $ProjectRoot $ScriptPath))
}

function Get-ScriptMetadata {
    <#
    .SYNOPSIS
        解析单个 .ps1/.psm1 文件的注释元数据
    .DESCRIPTION
        读取文件前 50 行，解析以下内容：
        1. @requires / @version / @author / @description / @category / @alias / @deprecated 标签
        2. 标准 <# .SYNOPSIS #> 和 <# .DESCRIPTION #> 注释块
        3. 文件基本信息（路径、大小、行数、最后修改时间）
    .PARAMETER Path
        脚本文件的完整路径
    .PARAMETER ProjectRoot
        项目根目录（用于计算相对路径）
    .EXAMPLE
        Get-ScriptMetadata -Path "D:\Project\Script\test.ps1" -ProjectRoot "D:\Project"
    #>
    param(
        [Parameter(Mandatory = $true)]
        [ValidateScript({ Test-Path $_ })]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$ProjectRoot
    )

    $result = [PSCustomObject]@{
        # 文件信息
        FilePath        = $Path
        RelativePath    = ""
        FileName        = ""
        Extension       = ""
        SizeKB          = 0
        LineCount       = 0
        LastModified    = $null

        # 解析的元数据
        Synopsis        = ""
        Description     = ""
        Requires        = @()       # @requires 依赖列表
        Version         = ""        # @version
        Author          = ""        # @author
        Category        = ""        # @category
        Alias           = ""        # @alias
        IsDeprecated    = $false    # @deprecated
        CustomTags      = @{}       # 其他自定义标签

        # 解析状态
        HasMetadata     = $false
        ParseErrors     = @()
    }

    $fileInfo = Get-Item $Path
    $result.FileName = $fileInfo.Name
    $result.Extension = $fileInfo.Extension
    $result.SizeKB = [math]::Round($fileInfo.Length / 1KB, 2)
    $result.LastModified = $fileInfo.LastWriteTime

    # 计算相对路径
    $result.RelativePath = [System.IO.Path]::GetRelativePath($ProjectRoot, $Path)

    try {
        # 只读取前 50 行（注释元数据通常在文件头部）
        $lines = Get-Content $Path -TotalCount 50 -Encoding UTF8 -ErrorAction Stop
        $result.LineCount = (Get-Content $Path -Encoding UTF8 -ErrorAction SilentlyContinue).Count

        $inBlockComment = $false
        $blockCommentLines = @()
        $synopsisFound = $false
        $descriptionFound = $false

        foreach ($line in $lines) {
            $trimmed = $line.Trim()

            # 检测块注释开始 <#
            if ($trimmed -match '^<#') {
                $inBlockComment = $true
                $blockCommentLines = @()
                # 如果 <# 后面还有内容（如 <# .SYNOPSIS），也捕获
                $remaining = $trimmed -replace '^<#\s*', ''
                if ($remaining) {
                    $blockCommentLines += $remaining
                }
                continue
            }

            # 检测块注释结束 #>
            if ($inBlockComment -and $trimmed -match '#>') {
                $inBlockComment = $false
                $remaining = $trimmed -replace '#>.*$', ''
                if ($remaining.Trim()) {
                    $blockCommentLines += $remaining
                }
                # 解析块注释内容
                $parsed = Parse-BlockComment -Lines $blockCommentLines
                if ($parsed.Synopsis -and -not $synopsisFound) {
                    $result.Synopsis = $parsed.Synopsis
                    $synopsisFound = $true
                }
                if ($parsed.Description -and -not $descriptionFound) {
                    $result.Description = $parsed.Description
                    $descriptionFound = $true
                }
                continue
            }

            # 在块注释中收集行
            if ($inBlockComment) {
                $blockCommentLines += $trimmed
                continue
            }

            # 解析行内标签（单行注释 # 后面的 @tags）
            if ($trimmed -match '^#\s*@(\w+):?\s*(.*)') {
                $tagName = $matches[1].ToLower()
                $tagValue = $matches[2].Trim()

                switch ($tagName) {
                    'requires' {
                        $result.Requires = $tagValue -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ }
                    }
                    'version' {
                        $result.Version = $tagValue
                    }
                    'author' {
                        $result.Author = $tagValue
                    }
                    'description' {
                        if (-not $result.Description) {
                            $result.Description = $tagValue
                        }
                    }
                    'category' {
                        $result.Category = $tagValue
                    }
                    'alias' {
                        $result.Alias = $tagValue
                    }
                    'deprecated' {
                        $result.IsDeprecated = $true
                    }
                    default {
                        $result.CustomTags[$tagName] = $tagValue
                    }
                }
                $result.HasMetadata = $true
                continue
            }

            # 解析单行 # 注释（作为 fallback 描述）
            if ($trimmed -match '^#\s*(.+)') {
                # 不处理，只作为辅助信息
                continue
            }
        }

        # 如果块注释没有提供 Synopsis，尝试从文件名推断
        if (-not $result.Synopsis) {
            $nameWithoutExt = [System.IO.Path]::GetFileNameWithoutExtension($Path)
            $result.Synopsis = $nameWithoutExt -replace '^Invoke-', '' -replace '^Get-', '' -replace '^Set-', '' -replace '^Remove-', ''
        }

    }
    catch {
        $result.ParseErrors += "读取/解析失败: $($_.Exception.Message)"
    }

    return $result
}

function Parse-BlockComment {
    <#
    .SYNOPSIS
        解析 <# ... #> 块注释内容，提取 .SYNOPSIS 和 .DESCRIPTION
    .PARAMETER Lines
        块注释中的行数组（不含 <# 和 #>）
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$Lines
    )

    $result = @{
        Synopsis    = ""
        Description = ""
    }

    $currentSection = $null
    $sectionContent = @()

    foreach ($line in $Lines) {
        # 检测节标题
        if ($line -match '^\.(\w+)') {
            # 保存前一个节的内容
            if ($currentSection -and $sectionContent) {
                $content = ($sectionContent | Where-Object { $_ } | ForEach-Object { $_.Trim() }) -join ' '
                if ($currentSection -eq 'SYNOPSIS') {
                    $result.Synopsis = $content
                }
                elseif ($currentSection -eq 'DESCRIPTION') {
                    $result.Description = $content
                }
            }
            $currentSection = $matches[1].ToUpper()
            $sectionContent = @()
            # 提取节标题后面的内容（如 ".SYNOPSIS 描述内容"）
            $remaining = $line -replace '^\.\w+\s*', ''
            if ($remaining) {
                $sectionContent += $remaining
            }
        }
        elseif ($currentSection) {
            $sectionContent += $line
        }
    }

    # 保存最后一个节的内容
    if ($currentSection -and $sectionContent) {
        $content = ($sectionContent | Where-Object { $_ } | ForEach-Object { $_.Trim() }) -join ' '
        if ($currentSection -eq 'SYNOPSIS') {
            $result.Synopsis = $content
        }
        elseif ($currentSection -eq 'DESCRIPTION') {
            $result.Description = $content
        }
    }

    return $result
}

# ============================================================
# 公开函数：扫描目录
# ============================================================

function Invoke-ScriptScan {
    <#
    .SYNOPSIS
        扫描指定目录下的所有 .ps1/.psm1 文件，返回元数据列表
    .DESCRIPTION
        递归扫描目录，排除 manifest.json 中定义的排除模式。
        返回每个脚本文件的元数据对象，支持按分类、标签、状态筛选。
    .PARAMETER Path
        要扫描的目录路径（默认：项目根目录）
    .PARAMETER ProjectRoot
        项目根目录（用于计算相对路径，默认与 Path 相同）
    .PARAMETER ExcludePatterns
        额外的排除模式（glob 格式）
    .PARAMETER IncludeExtensions
        要扫描的文件扩展名（默认：.ps1, .psm1）
    .PARAMETER IncludePesterTests
        是否包含 Pester 测试文件（.Tests.ps1）（默认：$false）
    .PARAMETER IncludeDeprecated
        是否包含标记为 @deprecated 的脚本（默认：$true）
    .EXAMPLE
        Invoke-ScriptScan -Path ".\PowerShellScript"
    .EXAMPLE
        Invoke-ScriptScan -Path ".\PowerShellScript" -IncludePesterTests
    #>
    param(
        [Parameter(Mandatory = $true)]
        [ValidateScript({ Test-Path $_ })]
        [string]$Path,

        [string]$ProjectRoot = $Path,

        [string[]]$ExcludePatterns = @(),

        [string[]]$IncludeExtensions = @('.ps1', '.psm1'),

        [switch]$IncludePesterTests,

        [switch]$IncludeDeprecated
    )

    # 合并排除模式（默认 + 自定义）
    $defaultExcludes = @(
        '**\backups\**', '**\Temp\**', '**\node_modules\**',
        '**\bin\**', '**\obj\**', '**\.git\**', '**\Templates\**'
    )
    $allExcludes = $defaultExcludes + $ExcludePatterns | Select-Object -Unique

    # 收集所有匹配的文件
    $files = @()
    foreach ($ext in $IncludeExtensions) {
        $found = Get-ChildItem -Path $Path -Recurse -Filter "*$ext" -File -ErrorAction SilentlyContinue
        $files += $found
    }

    # 应用排除模式
    $excludedPaths = @()
    foreach ($pattern in $allExcludes) {
        $excludedPaths += Get-ChildItem -Path $Path -Recurse -Filter (Split-Path $pattern -Leaf) -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -like ($pattern -replace '\\\*\*', ':*') -or $_.FullName -match ($pattern -replace '\\\*\\', '.*') }
    }
    $excludedSet = $excludedPaths | ForEach-Object { $_.FullName } | Select-Object -Unique

    $files = $files | Where-Object { $_.FullName -notin $excludedSet }

    # 过滤 Pester 测试文件
    if (-not $IncludePesterTests) {
        $files = $files | Where-Object { $_.Name -notmatch '\.Tests\.ps1$' }
    }

    # 扫描每个文件
    $results = @()
    foreach ($file in $files) {
        $metadata = Get-ScriptMetadata -Path $file.FullName -ProjectRoot $ProjectRoot

        # 过滤弃用脚本
        if (-not $IncludeDeprecated -and $metadata.IsDeprecated) {
            continue
        }

        $results += $metadata
    }

    # 按相对路径排序
    $results = $results | Sort-Object RelativePath

    return $results
}

function Get-ScriptSummary {
    <#
    .SYNOPSIS
        生成扫描结果的摘要统计
    .PARAMETER Scripts
        Invoke-ScriptScan 返回的脚本元数据列表
    .EXAMPLE
        $scripts = Invoke-ScriptScan -Path ".\PowerShellScript"
        Get-ScriptSummary -Scripts $scripts
    #>
    param(
        [Parameter(Mandatory = $true)]
        [object[]]$Scripts
    )

    $summary = [PSCustomObject]@{
        TotalCount      = $Scripts.Count
        Ps1Count        = ($Scripts | Where-Object { $_.Extension -eq '.ps1' }).Count
        Psm1Count       = ($Scripts | Where-Object { $_.Extension -eq '.psm1' }).Count
        DeprecatedCount = ($Scripts | Where-Object { $_.IsDeprecated }).Count
        WithMetadata    = ($Scripts | Where-Object { $_.HasMetadata }).Count
        WithoutMetadata = ($Scripts | Where-Object { -not $_.HasMetadata }).Count
        TotalSizeKB     = [math]::Round(($Scripts | Measure-Object SizeKB -Sum).Sum, 2)
        Categories      = ($Scripts | Where-Object { $_.Category } | Group-Object Category | ForEach-Object {
            [PSCustomObject]@{ Name = $_.Name; Count = $_.Count }
        })
        Authors         = ($Scripts | Where-Object { $_.Author } | Group-Object Author | ForEach-Object {
            [PSCustomObject]@{ Name = $_.Name; Count = $_.Count }
        })
    }

    return $summary
}

function Show-ScriptScanResult {
    <#
    .SYNOPSIS
        格式化显示扫描结果
    .PARAMETER Scripts
        Invoke-ScriptScan 返回的脚本元数据列表
    .PARAMETER Summary
        可选：Get-ScriptSummary 返回的摘要对象
    .PARAMETER ShowAll
        是否显示所有脚本的详细信息（默认只显示摘要）
    .PARAMETER ShowWithoutMetadata
        只显示缺少元数据的脚本
    .EXAMPLE
        $scripts = Invoke-ScriptScan -Path ".\PowerShellScript"
        Show-ScriptScanResult -Scripts $scripts -ShowAll
    #>
    param(
        [Parameter(Mandatory = $true)]
        [object[]]$Scripts,

        [object]$Summary,

        [switch]$ShowAll,

        [switch]$ShowWithoutMetadata
    )

    if (-not $Summary) {
        $Summary = Get-ScriptSummary -Scripts $Scripts
    }

    Write-Host ""
    Write-Host "╔══════════════════════════════════════════════════╗" -ForegroundColor Cyan
    Write-Host "║          PScriptManager 脚本扫描结果             ║" -ForegroundColor Cyan
    Write-Host "╚══════════════════════════════════════════════════╝" -ForegroundColor Cyan
    Write-Host ""

    Write-Host "  📊 统计摘要:" -ForegroundColor Yellow
    Write-Host "    总脚本数:     $($Summary.TotalCount)" -ForegroundColor White
    Write-Host "    .ps1 文件:    $($Summary.Ps1Count)" -ForegroundColor Gray
    Write-Host "    .psm1 模块:   $($Summary.Psm1Count)" -ForegroundColor Gray
    Write-Host "    含元数据:     $($Summary.WithMetadata)" -ForegroundColor Green
    Write-Host "    缺元数据:     $($Summary.WithoutMetadata)" -ForegroundColor $(if ($Summary.WithoutMetadata -gt 0) { 'Yellow' } else { 'Green' })
    Write-Host "    已弃用:       $($Summary.DeprecatedCount)" -ForegroundColor DarkGray
    Write-Host "    总大小:       $($Summary.TotalSizeKB) KB" -ForegroundColor Gray

    if ($Summary.Categories -and $Summary.Categories.Count -gt 0) {
        Write-Host ""
        Write-Host "  📂 分类分布:" -ForegroundColor Yellow
        foreach ($cat in ($Summary.Categories | Sort-Object Count -Descending)) {
            Write-Host "    • $($cat.Name): $($cat.Count) 个脚本" -ForegroundColor White
        }
    }

    if ($ShowWithoutMetadata) {
        $noMeta = $Scripts | Where-Object { -not $_.HasMetadata }
        if ($noMeta.Count -gt 0) {
            Write-Host ""
            Write-Host "  ⚠️  缺少元数据的脚本 ($($noMeta.Count) 个):" -ForegroundColor Yellow
            foreach ($s in $noMeta) {
                Write-Host "    • $($s.RelativePath)" -ForegroundColor White
            }
        }
    }

    if ($ShowAll) {
        Write-Host ""
        Write-Host "  📄 详细列表:" -ForegroundColor Yellow
        foreach ($s in $Scripts) {
            $metaFlag = if ($s.HasMetadata) { '📋' } else { '📄' }
            $depFlag = if ($s.IsDeprecated) { ' ⚠️ 已弃用' } else { '' }
            Write-Host "  $metaFlag $($s.RelativePath)$depFlag" -ForegroundColor White
            if ($s.Synopsis) {
                Write-Host "         → $($s.Synopsis)" -ForegroundColor Gray
            }
            if ($s.Version) {
                Write-Host "         版本: $($s.Version) | 作者: $($s.Author)" -ForegroundColor DarkGray
            }
        }
    }

    Write-Host ""
    Write-Host "  📌 提示: 使用 Register-Script.ps1 将扫描结果注册到 launcher.config.json" -ForegroundColor Cyan
    Write-Host ""
}

# 导出所有公开函数
Export-ModuleMember -Function @(
    'Get-ScriptMetadata',
    'Invoke-ScriptScan',
    'Get-ScriptSummary',
    'Show-ScriptScanResult',
    'Resolve-ScriptPath',
    'Parse-BlockComment'
)


