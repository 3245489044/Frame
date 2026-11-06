#requires -Version 5.1
<#
.SYNOPSIS
    DSL 文本面内核（「同语义多份实现」收敛点）。

.DESCRIPTION
    把 Operation\DSL 下反复出现的字符串/文本操作收敛为一处实现：
      1) 字节流 SHA-256 十六进制指纹（原散落 4 份：小写 hex，去 '-' 分隔）
      2) 逗号清单分词（split ',' → Trim → 丢空项，原散落 11 处）
      3) 行尾注释剥离（注释标记至行尾整段删除，原散落 12 处；'//' 与 '#' 两种标记）
      4) 仓库相对路径（去根前缀 → TrimStart 两种分隔符 → 统一正斜杠，原散落 6 处）
      5) 正则计数（原散落 15 份 Get-Count + 63 文件内联 [regex]::Matches）
      6) 整文件读取（原散落 11 份 Read-Text，两种缺件语义）

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

<#
.SYNOPSIS
    正则命中计数（大小写敏感，与 [regex]::Matches 原式一致）。
.DESCRIPTION
    等价于原表达式：([regex]::Matches($Text, $Pattern)).Count
    $Text 为 $null 时按空串处理 → 0；$Pattern 为空 ⇒ 参数绑定失败（不静默返回 0）。
.PARAMETER Text
    待扫描文本（$null 视为空串）
.PARAMETER Pattern
    正则表达式（非法正则按其原失败形态抛出）
.OUTPUTS
    [int] 命中次数
.EXAMPLE
    Get-RegexCount -Text $src -Pattern '^\[Gather\]'
#>
function Get-RegexCount {
    [CmdletBinding()]
    param(
        [Parameter(Position = 0)]
        [AllowNull()]
        [AllowEmptyString()]
        [string]$Text,

        [Parameter(Position = 1, Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$Pattern
    )
    if ($null -eq $Text) { return 0 }
    return [regex]::Matches($Text, $Pattern).Count
}

<#
.SYNOPSIS
    整文件读取（UTF-8 显式编码）。
.DESCRIPTION
    统一 11 份 Read-Text 的两种缺件语义：默认缺件返回 ''（兼容多数调用点），
    `-Strict` 改为抛错（对齐「缺件即不可判定」的调用点）。
    显式按 UTF-8 读取，消除 Windows PowerShell 5.1 `Get-Content` 默认编码（ANSI）与 7.x（UTF-8）的差异；
    带 BOM 的文件由 ReadAllText 自动识别。
.PARAMETER Path
    文件绝对路径
.PARAMETER Encoding
    文本编码（默认 UTF8）
.PARAMETER Strict
    缺件即抛错（门禁前置缺失语义）
.OUTPUTS
    [string] 文件全文；非 -Strict 且缺件 ⇒ ''
.EXAMPLE
    Read-TextFile -Path $p -Strict
#>
function Read-TextFile {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true, Position = 0)]
        [string]$Path,

        [ValidateSet('UTF8', 'Default', 'ASCII', 'Unicode', 'BigEndianUnicode', 'UTF32')]
        [string]$Encoding = 'UTF8',

        [switch]$Strict
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        if ($Strict) { throw "文本文件缺失：$Path" }
        return ''
    }

    $enc = switch ($Encoding) {
        'UTF8' { [System.Text.Encoding]::UTF8 }
        'Default' { [System.Text.Encoding]::Default }
        'ASCII' { [System.Text.Encoding]::ASCII }
        'Unicode' { [System.Text.Encoding]::Unicode }
        'BigEndianUnicode' { [System.Text.Encoding]::BigEndianUnicode }
        'UTF32' { [System.Text.Encoding]::UTF32 }
    }
    return [System.IO.File]::ReadAllText($Path, $enc)
}

# 兼容别名：域侧既有调用名（15 份 Get-Count / 11 份 Read-Text）无需改名即可接管。
New-Alias -Name Get-Count -Value Get-RegexCount
New-Alias -Name Read-Text -Value Read-TextFile

Export-ModuleMember -Function @(
    'Get-Sha256Hex',
    'ConvertFrom-CommaList',
    'Remove-LineComment',
    'Get-RepoRelativePath',
    'Get-RegexCount',
    'Read-TextFile'
) -Alias @('Get-Count', 'Read-Text')
