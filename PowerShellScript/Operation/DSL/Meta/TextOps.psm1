#requires -Version 5.1
<#
.SYNOPSIS
    DSL 文本面内核（「同语义多份实现」收敛点）。

.DESCRIPTION
    把 Operation\DSL 下反复出现的四类字符串操作收敛为一处实现：
      1) 字节流 SHA-256 十六进制指纹（原散落 4 份：小写 hex，去 '-' 分隔）
      2) 逗号清单分词（split ',' → Trim → 丢空项，原散落 11 处）
      3) 行尾注释剥离（注释标记至行尾整段删除，原散落 12 处；'//' 与 '#' 两种标记）
      4) 仓库相对路径（去根前缀 → TrimStart 两种分隔符 → 统一正斜杠，原散落 6 处）

    兼容口径：本模块只使用 Windows PowerShell 5.1 即有语法/类型
    （禁三元运算符、`??`、`-Parallel`、`[IO.Path]::GetRelativePath` 等 7.x/.NET Core 专属面）；
    DSL 目录内 7.0 专属脚本亦复用本模块（其语法开销与 5.1 相同）。

    逐字符等价口径（与各原调用点对照，见 CallSite 等价性证据）：
      - 全部函数对 $null / '' 的返回与「原表达式」一致（$null 参与 -replace/-split 时按 $null→'' 处理）；
      - 不使用 [string]::Join / -join 改写原有分隔符组合；
      - 不改变大小写（Get-Sha256Hex 一律小写，与大写变体刻意不合并）。
#>

Set-StrictMode -Version Latest

<#
.SYNOPSIS
    字节流 SHA-256 指纹（小写十六进制，无连字符）。
.DESCRIPTION
    等价于原表达式：([BitConverter]::ToString($sha.ComputeHash($Bytes)) -replace '-', '').ToLowerInvariant()
    $Bytes 为 $null 时与原表达式同样抛出（不在调用面出现，保留原失败形态）。
.PARAMETER Bytes
    待摘要字节
.OUTPUTS
    [string] 64 位小写 hex
.EXAMPLE
    Get-Sha256Hex -Bytes ([Text.Encoding]::UTF8.GetBytes('probe'))
#>
function Get-Sha256Hex {
    [CmdletBinding()]
    param(
        [Parameter(Position = 0)]
        [AllowNull()]
        [byte[]]$Bytes
    )
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        return ([System.BitConverter]::ToString($sha.ComputeHash($Bytes)) -replace '-', '').ToLowerInvariant()
    }
    finally { $sha.Dispose() }
}

<#
.SYNOPSIS
    逗号清单分词：拆分为项 → 去两端空白 → 丢空项。
.DESCRIPTION
    等价于原表达式：@($Text -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    注意：只做 ',' 分隔；带其它分隔符/去空白策略的清单（如 '[,;\s]+'、'-split '\|''）语义不同，不并入。
.PARAMETER Text
    逗号分隔文本（$null 视为空串）
.OUTPUTS
    [string[]] 去空白且非空的项（可能为空，不返回 $null）
.EXAMPLE
    ConvertFrom-CommaList -Text ' a , ,b '
    # 返回 @('a','b')
#>
function ConvertFrom-CommaList {
    [CmdletBinding()]
    param(
        [Parameter(Position = 0)]
        [AllowNull()]
        [AllowEmptyString()]
        [string]$Text
    )
    if ($null -eq $Text) { $Text = '' }
    $items = [System.Collections.Generic.List[string]]::new()
    foreach ($raw in ($Text -split ',')) {
        $token = $raw.Trim()
        if ($token) { [void]$items.Add($token) }
    }
    return $items.ToArray()
}

<#
.SYNOPSIS
    剥离行尾注释（从注释标记首次出现处删到行尾），不含换行处理。
.DESCRIPTION
    等价于原表达式：$Text -replace ('//.*$'), ''
    以及 '#' 变体：$Text -replace ('#.*$'), ''
    标记经 [regex]::Escape 后拼接 '.*$'，因此 '//' / '#' / 其它字面标记均按字面匹配；
    不区分「标记是否位于字符串字面量内」（与原实现同样粗糙，语义未收紧）。
.PARAMETER Text
    单行文本（$null 视为空串）
.PARAMETER Marker
    注释标记（默认 '//'）
.OUTPUTS
    [string] 注释后的行文本（保留标记之前的空白）
.EXAMPLE
    Remove-LineComment -Text 'x = 1; // note'      # 'x = 1; '
.EXAMPLE
    Remove-LineComment -Text 'a = 1 # note' -Marker '#'
#>
function Remove-LineComment {
    [CmdletBinding()]
    param(
        [Parameter(Position = 0)]
        [AllowNull()]
        [AllowEmptyString()]
        [string]$Text,

        [Parameter(Position = 1)]
        [ValidateNotNullOrEmpty()]
        [string]$Marker = '//'
    )
    if ($null -eq $Text) { $Text = '' }
    return $Text -replace ([regex]::Escape($Marker) + '.*$'), ''
}

<#
.SYNOPSIS
    绝对路径 → 仓库相对路径（统一正斜杠）。
.DESCRIPTION
    等价于原表达式：$Path.Substring($Root.Length).TrimStart('\', '/') -replace '\\', '/'
    $Path 短于 $Root 时与原表达式一致地抛 Substring 越界（不在调用面出现）。
.PARAMETER Path
    绝对路径
.PARAMETER Root
    前缀根（按字符长度截断，不做大小写/规范化比较，与原实现一致）
.OUTPUTS
    [string] 相对路径（正斜杠）
.EXAMPLE
    Get-RepoRelativePath -Path 'D:\repo\Frame\a.ps1' -Root 'D:\repo'
    # 返回 'Frame/a.ps1'
#>
function Get-RepoRelativePath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true, Position = 0)]
        [string]$Path,

        [Parameter(Mandatory = $true, Position = 1)]
        [string]$Root
    )
    $relative = $Path.Substring($Root.Length).TrimStart([char[]]@('\', '/'))
    return $relative -replace '\\', '/'
}

Export-ModuleMember -Function Get-Sha256Hex,
                              ConvertFrom-CommaList,
                              Remove-LineComment,
                              Get-RepoRelativePath
