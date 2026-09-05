<#
.SYNOPSIS
    文件批量操作模块 — 提供批量读取、批量写入、批量替换等高级文件操作

.DESCRIPTION
    从原 BasicMethod-FileAccess.psm1 拆分出的批量操作函数。
    包含：BatchRead-File, BatchWrite-File, BatchReplace-InFile, Invoke-ContextAwareReplace

    依赖:
        BasicMethod-FileUtils.psm1（提供 _ResolvePath、_ReadFileContent、_SplitLines、_GetRelativePath、
              _ApplyTemplateVariables、_GetFileSizeKB、_ComputeHash、_ApplySearchReplace、
              _ResolveWildcardPaths、_WriteContentToFile 等）
        BasicMethod-FileDiff.psm1（提供 Show-FileDiff、Confirm-Replacement、Backup-File 等）

    用法:
        Import-Module .\BasicMethod-FileUtils.psm1
        Import-Module .\BasicMethod-FileDiff.psm1
        Import-Module .\BasicMethod-FileBatch.psm1
        BatchRead-File -Paths @("*.cs", "*.ps1")
        BatchWrite-File -Path "output.txt" -Content "Hello"
        BatchReplace-InFile -Paths @("*.cs") -Blocks @(@{Search="old";Replace="new"})
#>

# ============================================================
# 批量操作函数
# ============================================================

<#
.SYNOPSIS
    批量读取多个文件
.DESCRIPTION
    支持通配符路径，返回每个文件的内容和元数据。
.PARAMETER Paths
    文件路径数组（支持通配符）
.PARAMETER Raw
    返回原始字符串而非行数组
.EXAMPLE
    BatchRead-File -Paths @("*.cs", "*.ps1")
    BatchRead-File -Paths @("CsharpPrj/**/*.cs")
#>
function BatchRead-File {
    param(
        [Parameter(Mandatory)]
        [string[]]$Paths,

        [switch]$Raw
    )

    $results = @()

    foreach ($rawPath in $Paths) {
        $files = _ResolveWildcardPaths -RawPath $rawPath
        foreach ($file in $files) {
            $content = _ReadFileContent -Path $file
            $lines = _SplitLines -Content $content

            $results += [PSCustomObject]@{
                File = $file
                RelativePath = _GetRelativePath -Path $file
                TotalLines = $lines.Count
                Content = if ($Raw) { $content } else { $lines }
                SizeKB = _GetFileSizeKB -Path $file
            }
        }
    }

    return [PSCustomObject]@{
        TotalFiles = $results.Count
        Files = $results
    }
}

<#
.SYNOPSIS
    批量写入文件
.DESCRIPTION
    支持字典写入（键=路径，值=内容）、合并写入（-Merge）、模板变量替换（-Variables）。
.PARAMETER Path
    单个文件路径
.PARAMETER Content
    单个文件内容
.PARAMETER Merge
    合并模式：将内容合并到现有文件（使用 SEARCH/REPLACE 块）
.PARAMETER Blocks
    SEARCH/REPLACE 块数组（用于 -Merge 模式）
.PARAMETER Variables
    模板变量哈希表（如 @{ Name = "World" }，内容中的 {{Name}} 会被替换）
.PARAMETER Encoding
    编码（默认 UTF8）
.PARAMETER Backup
    写入前自动备份
.PARAMETER Verify
    写入后校验
.PARAMETER ShowDiff
    显示差异预览
.PARAMETER Interactive
    交互式确认
.EXAMPLE
    # 单文件写入
    BatchWrite-File -Path "output.txt" -Content "Hello"

    # 批量写入（字典）
    $files = @{
        "file1.txt" = "content1"
        "file2.txt" = "content2"
    }
    BatchWrite-File -Path $files.Keys -Content $files.Values

    # 合并写入（使用 SEARCH/REPLACE 块）
    BatchWrite-File -Path "test.txt" -Merge -Blocks @(@{Search="old";Replace="new"})

    # 模板变量替换
    BatchWrite-File -Path "template.txt" -Content "Hello {{Name}}" -Variables @{Name="World"}
#>
function BatchWrite-File {
    param(
        [Parameter(ValueFromPipeline = $true, Position = 0)]
        [Alias('Paths')]
        [string[]]$Path,

        [Parameter(Position = 1)]
        [AllowEmptyString()]
        [object]$Content,

        [switch]$Merge,

        [array]$Blocks = @(),

        [hashtable]$Variables = @{},

        [string]$Encoding = "UTF8",

        [switch]$Backup,

        [switch]$Verify,

        [switch]$ShowDiff,

        [switch]$Interactive,

        [switch]$Append,

        [switch]$DryRun
    )

    # 使用 ArrayList 避免数组类型问题
    $results = [System.Collections.ArrayList]@()

    # ★ 统一处理路径：兼容 -Path（单数）和 -Paths（复数别名）
    # PowerShell 的 Alias 属性会自动将 -Paths 绑定到 $Path 变量
    $rawPaths = $Path

    # 如果 $Path 为 null 或空数组，尝试从 PSBoundParameters 获取原始值
    # 这解决了某些情况下 Alias 绑定在模块作用域中可能失败的问题
    if ($null -eq $rawPaths -or ($rawPaths -is [array] -and $rawPaths.Count -eq 0)) {
        if ($PSBoundParameters.ContainsKey('Path')) {
            $rawPaths = $PSBoundParameters['Path']
        }
    }

    # 确保是数组
    $pathArray = if ($null -eq $rawPaths) { @() } elseif ($rawPaths -is [array]) { $rawPaths } else { @($rawPaths) }

    # 过滤并转换为字符串数组
    $pathArray = @($pathArray | Where-Object {
        $_ -is [string] -and $_.Trim().Length -gt 0
    })

    if ($pathArray.Count -eq 0) {
        return [PSCustomObject]@{
            TotalFiles = 0
            Results = @()
            Error = "未提供有效的文件路径"
        }
    }

    if ($Merge) {
        # 合并模式：支持批量处理多个文件
        foreach ($p in $pathArray) {
            $targetPath = _ResolvePath $p
            if (-not (Test-Path $targetPath)) {
                $null = $results.Add([PSCustomObject]@{
                    Path = $targetPath
                    Success = $false
                    Error = "文件不存在: $targetPath"
                })
                continue
            }

            $fileContent = _ReadFileContent -Path $targetPath
            $fileModified = $false
            $hasBlockError = $false
            $blockErrors = @()

            foreach ($block in $Blocks) {
                $search = $block.Search
                $replace = $block.Replace

                # 模板变量替换
                $replace = _ApplyTemplateVariables -Content $replace -Variables $Variables

                $result = _ApplySearchReplace -Content $fileContent -Search $search -Replace $replace -Path $targetPath
                if (-not $result.Success) {
                    $hasBlockError = $true
                    $blockErrors += $result.Error
                    # 继续尝试下一个 block，不中断
                    continue
                }
                $fileContent = $result.Result
                $fileModified = $true
            }

            if ($hasBlockError) {
                # 有 block 匹配失败：不写入文件，报告所有错误
                $null = $results.Add([PSCustomObject]@{
                    Path = $targetPath
                    Success = $false
                    Error = "部分 block 匹配失败: $($blockErrors -join '; ')"
                    PartialModified = $fileModified
                })
            } elseif ($fileModified) {
                # 所有 block 都成功，写入合并结果
                $writeResult = _WriteContentToFile -Path $targetPath -Content $fileContent -Encoding $Encoding -Backup:$Backup -Verify:$Verify -ShowDiff:$ShowDiff
                $null = $results.Add($writeResult)
            }
        }
    } else {
        # 批量写入模式
        # 处理 Content：支持数组或单个字符串
        # 注意：PowerShell 中 [string] 实现了 IEnumerable，$str -is [array] 为 $true
        # 必须使用 GetType().IsArray 来正确判断
        # 注意：PowerShell 的数组展开（array unwrapping）问题：
        # 当 $Content 是 System.String[] 且只有一个元素时，直接赋值会展开为 System.String
        # 使用 ArrayList::new($Content) 来防止展开
        if ($null -eq $Content) {
            $contentArray = [System.Collections.ArrayList]::new(@(""))
        } elseif ($Content.GetType().IsArray) {
            $contentArray = [System.Collections.ArrayList]::new($Content)
        } else {
            $contentArray = [System.Collections.ArrayList]::new(@($Content))
        }

        for ($i = 0; $i -lt $pathArray.Count; $i++) {
            $p = $pathArray[$i]
            $c = if ($i -lt $contentArray.Count) { $contentArray[$i] } else { $contentArray[0] }

            # 确保是字符串
            $c = if ($null -eq $c) { "" } else { [string]$c }

            # 模板变量替换
            $c = _ApplyTemplateVariables -Content $c -Variables $Variables

            $resolvedPath = _ResolvePath -Path $p

            # 交互式确认
            if ($Interactive -and (Test-Path $resolvedPath)) {
                $oldContent = _ReadFileContent -Path $resolvedPath
                $confirm = Confirm-Replacement -Path $resolvedPath -MatchLine "[批量写入]" -Context @($oldContent.Substring(0, [math]::Min(200, $oldContent.Length)))
                if (-not $confirm.Confirmed) {
                    $null = $results.Add([PSCustomObject]@{
                        Path = $resolvedPath
                        Action = "Skipped"
                        Reason = "用户取消"
                    })
                    continue
                }
            }

            $writeResult = _WriteContentToFile -Path $resolvedPath -Content $c -Encoding $Encoding -Backup:$Backup -Verify:$Verify -ShowDiff:$ShowDiff -Append:$Append -DryRun:$DryRun
            $null = $results.Add($writeResult)
        }
    }

    return [PSCustomObject]@{
        TotalFiles = $results.Count
        Results = $results
    }
}

<#
.SYNOPSIS
    批量替换文件内容
.DESCRIPTION
    支持 SEARCH/REPLACE 块精确匹配、正则替换、上下文感知替换。
.PARAMETER Paths
    文件路径数组（支持通配符）
.PARAMETER Blocks
    SEARCH/REPLACE 块数组（@(@{Search="";Replace=""}, ...)）
.PARAMETER Pattern
    正则模式（与 -Blocks 互斥）
.PARAMETER Replacement
    替换文本（与 -Pattern 配合使用）
.PARAMETER ContextAware
    启用上下文感知替换（使用 Invoke-ContextAwareReplace）
.PARAMETER ShowDiff
    显示差异预览
.PARAMETER Interactive
    交互式确认
.PARAMETER Escape
    自动转义正则特殊字符
.PARAMETER Backup
    替换前自动备份
.PARAMETER Verify
    替换后校验
.EXAMPLE
    # 使用 SEARCH/REPLACE 块
    BatchReplace-InFile -Paths @("*.cs") -Blocks @(@{Search="old";Replace="new"})

    # 使用正则替换
    BatchReplace-InFile -Paths @("*.txt") -Pattern "\d+" -Replacement "NUMBER"

    # 上下文感知替换
    BatchReplace-InFile -Paths @("*.cs") -Blocks @(@{Search="count";Replace="total"}) -ContextAware
#>
function BatchReplace-InFile {
    param(
        [Parameter(Mandatory)]
        [string[]]$Paths,

        [array]$Blocks = @(),

        [string]$Pattern = "",

        [string]$Replacement = "",

        [switch]$ContextAware,

        [switch]$ShowDiff,

        [switch]$Interactive,

        [switch]$Escape,

        [switch]$Backup,

        [switch]$Verify
    )

    $results = @()
    $allFiles = @()

    # 解析所有路径
    foreach ($rawPath in $Paths) {
        $allFiles += _ResolveWildcardPaths -RawPath $rawPath
    }
    $allFiles = $allFiles | Select-Object -Unique

    $totalMatches = 0
    $applyAll = $false

    foreach ($file in $allFiles) {
        $content = _ReadFileContent -Path $file
        $modified = $false
        $fileResult = @()

        if ($Blocks.Count -gt 0) {
            foreach ($block in $Blocks) {
                $search = $block.Search
                $replace = $block.Replace

                if ($ContextAware) {
                    # 上下文感知替换
                    $contextResult = Invoke-ContextAwareReplace -Content $content -Search $search -Replace $replace -Path $file
                    if ($contextResult.Success) {
                        $content = $contextResult.Result
                        $modified = $true
                        $fileResult += $contextResult
                    }
                } else {
                    # 精确匹配替换（只替换第一个匹配）
                    $result = _ApplySearchReplace -Content $content -Search $search -Replace $replace -Path $file
                    if ($result.Success) {
                        $content = $result.Result
                        $modified = $true
                        $fileResult += $result
                    }
                }
            }
        } elseif ($Pattern) {
            # 正则替换
            $searchPattern = if ($Escape) { [regex]::Escape($Pattern) } else { $Pattern }
            $newContent = $content -replace $searchPattern, $Replacement
            if ($newContent -ne $content) {
                $modified = $true
                $fileResult += @{ Type = "regex"; Replacements = ([regex]::Matches($content, $searchPattern)).Count }
                $content = $newContent
            }
        }

        if ($modified) {
            $totalMatches++

            # 交互式确认
            if ($Interactive -and -not $applyAll) {
                $confirm = Confirm-Replacement -Path $file -MatchLine "[替换预览]" -Context @($content.Substring(0, [math]::Min(200, $content.Length))) -MatchIndex $totalMatches -TotalMatches $allFiles.Count
                if (-not $confirm.Confirmed) {
                    continue
                }
            }

            # 差异预览
            if ($ShowDiff) {
                $oldContent = _ReadFileContent -Path $file
                Show-FileDiff -OldContent $oldContent -NewContent $content -Label $file
            }

            # 使用 _WriteContentToFile 统一写入（含编码检测、备份、校验）
            $writeResult = _WriteContentToFile -Path $file -Content $content -Encoding "Auto" -Backup:$Backup -Verify:$Verify

            $results += [PSCustomObject]@{
                File = $file
                RelativePath = _GetRelativePath -Path $file
                Changes = $fileResult.Count
                Backup = $writeResult.Backup
                Verified = $writeResult.Verified
            }
        }
    }

    return [PSCustomObject]@{
        TotalFiles = $allFiles.Count
        ModifiedFiles = $results.Count
        Results = $results
    }
}

<#
.SYNOPSIS
    上下文感知替换
.DESCRIPTION
    在文件内容中查找匹配行，并替换其周围的上下文。
    比精确匹配更灵活，能处理缩进变化和微小差异。
.PARAMETER Content
    文件内容
.PARAMETER Search
    搜索文本
.PARAMETER Replace
    替换文本
.PARAMETER Path
    文件路径（用于错误信息）
.EXAMPLE
    Invoke-ContextAwareReplace -Content $content -Search "old code" -Replace "new code"
#>
function Invoke-ContextAwareReplace {
    param(
        [Parameter(Mandatory)]
        [string]$Content,

        [Parameter(Mandatory)]
        [string]$Search,

        [Parameter(Mandatory)]
        [string]$Replace,

        [string]$Path = ""
    )

    $lines = _SplitLines -Content $Content
    $searchLines = _SplitLines -Content $Search
    $replaceLines = _SplitLines -Content $Replace

    # 尝试精确匹配
    $index = $Content.IndexOf($Search, [System.StringComparison]::Ordinal)
    if ($index -ge 0) {
        $result = $Content.Substring(0, $index) + $Replace + $Content.Substring($index + $Search.Length)
        return @{ Success = $true; Result = $result; Method = "exact" }
    }

    # 尝试模糊匹配（忽略缩进差异）
    $searchTrimmed = $searchLines | ForEach-Object { $_.Trim() }
    for ($i = 0; $i -le $lines.Count - $searchLines.Count; $i++) {
        $match = $true
        for ($j = 0; $j -lt $searchLines.Count; $j++) {
            if ($lines[$i + $j].Trim() -ne $searchTrimmed[$j]) {
                $match = $false
                break
            }
        }
        if ($match) {
            # 保留原始缩进
            $originalIndent = ""
            if ($lines[$i] -match "^(\s*)") {
                $originalIndent = $matches[1]
            }
            $adjustedReplace = $replaceLines -join "`n"
            if ($originalIndent) {
                $adjustedReplace = ($replaceLines | ForEach-Object { $originalIndent + $_ }) -join "`n"
            }

            $before = $lines[0..($i - 1)] -join "`n"
            $after = $lines[($i + $searchLines.Count)..($lines.Count - 1)] -join "`n"
            $result = $before
            if ($before) { $result += "`n" }
            $result += $adjustedReplace
            if ($after) { $result += "`n" + $after }

            return @{ Success = $true; Result = $result; Method = "fuzzy" }
        }
    }

    return @{ Success = $false; Error = "未找到匹配内容（精确和模糊匹配均失败）" }
}

# ============================================================
# 模块导出
# ============================================================

Export-ModuleMember -Function @(
    "BatchRead-File",
    "BatchWrite-File",
    "BatchReplace-InFile",
    "Invoke-ContextAwareReplace"
)
