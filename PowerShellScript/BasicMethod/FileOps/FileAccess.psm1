<#
.SYNOPSIS
    文件读写访问核心模块 — 提供文件读取、搜索、写入、替换、分析、目录列表等核心操作

.DESCRIPTION
    从原 BasicMethod-FileAccess.psm1 精简后的核心文件操作模块。
    包含：Read-File, Search-Files, Write-File, Replace-InFile, Analyze-File, List-Directory

    依赖:
        BasicMethod-FileUtils.psm1（提供 _ResolvePath、_ReadFileContent、_SplitLines、_WriteContentToFile 等）
        BasicMethod-FileDiff.psm1（提供 Show-FileDiff、Backup-File 等）

    用法:
        Import-Module .\BasicMethod-FileUtils.psm1
        Import-Module .\BasicMethod-FileDiff.psm1
        Import-Module .\BasicMethod-FileAccess.psm1
        Read-File -Path "file.txt"
        Write-File -Path "output.txt" -Content "Hello" -Backup -Verify
#>

# ============================================================
# 核心函数
# ============================================================

<#
.SYNOPSIS
    读取文件内容
.PARAMETER Path
    文件路径（相对于项目根目录或绝对路径）
.PARAMETER StartLine
    起始行号（1-based，可选）
.PARAMETER EndLine
    结束行号（可选）
.PARAMETER Raw
    返回原始字符串而非行数组
.EXAMPLE
    Read-File -Path "docs/KnowLedge/AI决策框架.md"
    Read-File -Path "CsharpPrj/BasicMethod/BasicMethod.csproj" -StartLine 1 -EndLine 20
#>
function Read-File {
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [int]$StartLine = 0,

        [int]$EndLine = 0,

        [switch]$Raw,

        [string]$Persist = "off"
    )

    $resolvedPath = _ResolvePath $Path
    $content = _ReadFileContent -Path $resolvedPath

    if ($Raw) {
        return $content
    }

    $lines = _SplitLines -Content $content
    $totalLines = $lines.Count

    if ($StartLine -gt 0 -or $EndLine -gt 0) {
        $s = if ($StartLine -gt 0) { $StartLine - 1 } else { 0 }
        $e = if ($EndLine -gt 0) { [math]::Min($EndLine, $totalLines) } else { $totalLines }
        $lines = $lines[$s..($e - 1)]
    }

    $result = @{
        Path = $resolvedPath
        TotalLines = $totalLines
        Lines = $lines
        Content = $lines -join "`n"
    }

    # Persist：读后由薄绑定组合 read→store 转 ur_resources 节点（此处仅附带文件哈希元数据，FileOps 不感知存储 schema）
    if ($Persist -eq "on" -or $Persist -eq "auto") {
        $result["Hash"] = (Get-FileHash -Path $resolvedPath -Algorithm SHA256).Hash.ToLower()
    }

    return $result
}

<#
.SYNOPSIS
    搜索文件内容（正则匹配）
.PARAMETER Path
    搜索目录路径
.PARAMETER Pattern
    正则表达式
.PARAMETER FilePattern
    文件通配符（如 *.cs, *.ps1）
.PARAMETER MaxResults
    最大结果数
.EXAMPLE
    Search-Files -Path "CsharpPrj" -Pattern "class.*Registry" -FilePattern "*.cs"
#>
function Search-Files {
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [Parameter(Mandatory)]
        [string]$Pattern,

        [string]$FilePattern = "*",

        [int]$MaxResults = 50
    )

    $resolvedPath = _ResolvePath -Path $Path
    $results = @()

    Get-ChildItem -Path $resolvedPath -Recurse -Filter $FilePattern -File -ErrorAction SilentlyContinue |
        ForEach-Object {
            $file = $_
            $matches = Select-String -Path $file.FullName -Pattern $Pattern -AllMatches -ErrorAction SilentlyContinue
            if ($matches) {
                $matchCount = ($matches | Measure-Object).Count
                $previewLines = $matches | Select-Object -First 3 | ForEach-Object { $_.Line }
                $results += [PSCustomObject]@{
                    File = $file.FullName
                    RelativePath = _GetRelativePath -Path $file.FullName
                    Matches = $matchCount
                    Preview = $previewLines -join "`n"
                }
            }

            # 提前终止：达到 MaxResults 时停止
            if ($results.Count -ge $MaxResults) { break }
        }

    return [PSCustomObject]@{
        SearchPath = $resolvedPath
        Pattern = $Pattern
        FilePattern = $FilePattern
        TotalMatches = $results.Count
        Results = $results
    }
}

<#
.SYNOPSIS
    写入文件内容
.PARAMETER Path
    文件路径
.PARAMETER Content
    写入内容
.PARAMETER Append
    追加模式（默认覆盖）
.PARAMETER Encoding
    编码（默认 UTF8，支持 Auto/UTF8/UTF8-BOM/UTF16-LE/UTF16-BE/GBK/ASCII）
.PARAMETER Backup
    写入前自动备份
.PARAMETER Verify
    写入后校验哈希
.EXAMPLE
    Write-File -Path "output.txt" -Content "Hello World"
    Write-File -Path "output.txt" -Content "data" -Encoding GBK -Backup -Verify
#>
function Write-File {
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [Parameter(Mandatory)]
        [string]$Content,

        [switch]$Append,

        [string]$Encoding = "UTF8",

        [switch]$Backup,

        [switch]$Verify
    )

    $resolvedPath = _ResolvePath -Path $Path

    return _WriteContentToFile -Path $resolvedPath -Content $Content -Encoding $Encoding -Append:$Append -Backup:$Backup -Verify:$Verify
}

<#
.SYNOPSIS
    替换文件中的内容（类似 replace_in_file）
.DESCRIPTION
    默认使用精确匹配（非正则），只替换第一个匹配。
    使用 -Regex 开关启用正则替换模式。
.PARAMETER Path
    文件路径
.PARAMETER Search
    搜索文本（精确匹配）或正则模式（配合 -Regex）
.PARAMETER Replace
    替换文本
.PARAMETER Regex
    启用正则替换模式（默认使用精确匹配）
.PARAMETER Backup
    替换前自动备份
.PARAMETER Verify
    替换后校验
.EXAMPLE
    Replace-InFile -Path "test.txt" -Search "old text" -Replace "new text"
    Replace-InFile -Path "test.cs" -Search "count\d+" -Replace "total" -Regex -Backup
#>
function Replace-InFile {
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [Parameter(Mandatory)]
        [string]$Search,

        [Parameter(Mandatory)]
        [string]$Replace,

        [switch]$Regex,

        [switch]$Backup,

        [switch]$Verify
    )

    $resolvedPath = _ResolvePath -Path $Path
    $content = _ReadFileContent -Path $resolvedPath

    if ($Regex) {
        # 正则替换模式
        $newContent = $content -replace $Search, $Replace
        $replacements = ([regex]::Matches($content, $Search)).Count
    } else {
        # 精确匹配模式（使用 _ApplySearchReplace）
        $result = _ApplySearchReplace -Content $content -Search $Search -Replace $Replace -Path $resolvedPath
        if (-not $result.Success) {
            return [PSCustomObject]@{
                Path = $resolvedPath
                Changed = $false
                Message = $result.Error
            }
        }
        $newContent = $result.Result
        $replacements = $result.Replacements
    }

    if ($content -eq $newContent) {
        return [PSCustomObject]@{
            Path = $resolvedPath
            Changed = $false
            Message = "未找到匹配内容"
        }
    }

    # 使用 _WriteContentToFile 统一写入（含编码检测、备份、校验）
    $writeResult = _WriteContentToFile -Path $resolvedPath -Content $newContent -Encoding "Auto" -Backup:$Backup -Verify:$Verify

    return [PSCustomObject]@{
        Path = $resolvedPath
        Changed = $true
        Replacements = $replacements
        Backup = $writeResult.Backup
        Verified = $writeResult.Verified
    }
}

<#
.SYNOPSIS
    分析文件统计信息
.PARAMETER Path
    文件路径
.EXAMPLE
    Analyze-File -Path "large.log"
#>
function Analyze-File {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    $resolvedPath = _ResolvePath -Path $Path
    $content = _ReadFileContent -Path $resolvedPath
    $lines = _SplitLines -Content $content
    $sizeBytes = (Get-Item $resolvedPath).Length

    return [PSCustomObject]@{
        Path = $resolvedPath
        SizeKB = [math]::Round($sizeBytes / 1024, 2)
        TotalLines = $lines.Count
        NonEmptyLines = ($lines | Where-Object { $_.Trim() -ne "" }).Count
        MaxLineLength = ($lines | ForEach-Object { $_.Length } | Measure-Object -Maximum).Maximum
        Extension = [System.IO.Path]::GetExtension($resolvedPath)
    }
}

<#
.SYNOPSIS
    列出目录内容
.PARAMETER Path
    目录路径
.PARAMETER Recursive
    是否递归
.PARAMETER Pattern
    文件通配符过滤
.EXAMPLE
    List-Directory -Path "PowerShellScript" -Recursive
#>
function List-Directory {
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [switch]$Recursive,

        [string]$Pattern = "*"
    )

    $resolvedPath = _ResolvePath -Path $Path

    $getParams = @{
        Path = $resolvedPath
        ErrorAction = "SilentlyContinue"
    }
    if ($Recursive) { $getParams["Recurse"] = $true }

    $items = Get-ChildItem @getParams -Filter $Pattern -File

    return [PSCustomObject]@{
        Path = $resolvedPath
        Recursive = $Recursive.IsPresent
        Pattern = $Pattern
        TotalFiles = ($items | Measure-Object).Count
        Files = $items | Select-Object FullName, Length, LastWriteTime
    }
}

# ============================================================
# 模块导出
# ============================================================

Export-ModuleMember -Function @(
    "Read-File",
    "Search-Files",
    "Write-File",
    "Replace-InFile",
    "Analyze-File",
    "List-Directory"
)
