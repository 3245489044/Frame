# StringTools.Markdown.psm1 - Markdown 文本行处理模块
# 提供 Markdown 表格行/代码围栏判定与反引号内联代码 token 提取
#
# ★ 依赖：StringTools.Core.psm1（Invoke-RegexExtract）

# ============================================
#  行分类判定
# ============================================

<#
.SYNOPSIS
    判定一行是否为 Markdown 表格行
.DESCRIPTION
    表格行以 `|` 开头（可含前导空白）。用于跳过迁移对照表/速查表等展示性表格。
.PARAMETER InputObject
    要判定的行文本（支持管道输入）
.OUTPUTS
    [bool]
.EXAMPLE
    Test-MarkdownTableLine -InputObject "| 类型 | 说明 |"
#>
function Test-MarkdownTableLine {
    [CmdletBinding()]
    param(
        [Parameter(ValueFromPipeline = $true, Position = 0)]
        [string]$InputObject
    )
    process {
        if ($null -eq $InputObject) { return $false }
        return $InputObject -match '^\s*\|'
    }
}

<#
.SYNOPSIS
    判定一行是否为代码围栏行（开/关围栏）
.DESCRIPTION
    代码围栏以 ``` 或 ~~~ 开头（可含前导空白）。调用方负责维护围栏开关状态。
.PARAMETER InputObject
    要判定的行文本（支持管道输入）
.OUTPUTS
    [bool]
.EXAMPLE
    Test-CodeFenceLine -InputObject '```powershell'
#>
function Test-CodeFenceLine {
    [CmdletBinding()]
    param(
        [Parameter(ValueFromPipeline = $true, Position = 0)]
        [string]$InputObject
    )
    process {
        if ($null -eq $InputObject) { return $false }
        return $InputObject -match '^\s*(```|~~~)'
    }
}

# ============================================
#  内联代码 token 提取
# ============================================

<#
.SYNOPSIS
    提取字符串中的全部反引号包裹 token（Markdown 内联代码）
.DESCRIPTION
    提取形如 `item` 的反引号包裹内容，返回去除两端空白的 token 数组。
    内部复用 Core 的 Invoke-RegexExtract 提取捕获组 1。
.PARAMETER InputObject
    要处理的文本（支持管道输入）
.OUTPUTS
    [string[]] 反引号包裹的 token 数组
.EXAMPLE
    Get-BacktickTokens -InputObject '`A` 与 `B` / `C`'
    # 返回 @('A', 'B', 'C')
#>
function Get-BacktickTokens {
    [CmdletBinding()]
    param(
        [Parameter(ValueFromPipeline = $true, Position = 0)]
        [string]$InputObject
    )
    process {
        if ([string]::IsNullOrEmpty($InputObject)) { return @() }
        $tokens = @(Invoke-RegexExtract -InputObject $InputObject -Pattern '`([^`]+)`' -GroupName 1)
        return @($tokens | ForEach-Object { $_.Trim() })
    }
}

# ============================================
#  导出公共函数
# ============================================

Export-ModuleMember -Function Test-MarkdownTableLine,
                              Test-CodeFenceLine,
                              Get-BacktickTokens
  
# ============================================  
#  表格单元格与章节标题（追加）  
# ============================================  
  
function Get-MarkdownFirstCell {  
    [CmdletBinding()]  
    param(  
        [Parameter(Mandatory = $true, Position = 0)]  
        [AllowEmptyString()][string]$Line  
    )  
    if ($Line -match '^\|\s*([^|]+?)\s*\|') { return $Matches[1] }  
    return $null  
}  
  
function Test-MarkdownHeadingLine {  
    [CmdletBinding()]  
    param(  
        [Parameter(Mandatory = $true, Position = 0)]  
        [AllowEmptyString()][string]$Line,  
  
        [Parameter()]  
        [int]$MinLevel = 1  
    )  
    if ($Line -notmatch '^#{1,6}\s') { return $false }  
    if ($MinLevel -le 1) { return $true }  
    return $Line -match ('^#{' + $MinLevel + ',6}\s')  
}  
  
function Test-MarkdownSectionHeader {  
    [CmdletBinding()]  
    param(  
        [Parameter(Mandatory = $true, Position = 0)]  
        [AllowEmptyString()][string]$Line,  
  
        [Parameter(Mandatory = $true)]  
        [string]$Number,  
  
        [Parameter()]  
        [int]$Level = 3  
    )  
    $escaped = [regex]::Escape($Number)  
    return $Line -match ('^#{' + $Level + '}\s+' + $escaped + '\s')  
}  
  
# 追加导出（累积语义）  
Export-ModuleMember -Function Get-MarkdownFirstCell,  
                              Test-MarkdownHeadingLine,  
                              Test-MarkdownSectionHeader  
