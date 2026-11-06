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

<#
.SYNOPSIS
    剥离代码注释（行注释 + 块注释；字符串/字符字面量内不剥），保留换行。
.DESCRIPTION
    动机（GC §5.1 T6）：EntityExistenceSingleSource / RenderNoStringConcat 各自用两条正则
    `/\*.*?\*/` + `//[^\r\n]*`，既不区分字符串字面量，也不保留行号；本函数是「注释剥离」单源。

    行为：
      · 行注释（默认 '//'）从标记删到行尾（换行保留）；
      · 块注释（默认 '/*' … '*/'）跨行删除（块内换行保留，行号不漂移）；
      · `"` / `'` 字面量内的标记不剥；支持 `\` 转义与 `""` / `''` 双写；
      · 块注释被删后以单空格占位（避免 `a/*x*/b` 粘连成 `ab`）。
.PARAMETER Text
    源文本（$null 视为空串）
.PARAMETER LineMarker
    行注释标记（默认 '//'）
.PARAMETER BlockOpen
    块注释开标记（默认 '/*'）
.PARAMETER BlockClose
    块注释闭标记（默认 '*/'）
.OUTPUTS
    [string] 去注释文本（行数与原文本一致）
.EXAMPLE
    Remove-CodeComments -Text $src
#>
function Remove-CodeComments {
    [CmdletBinding()]
    param(
        [Parameter(Position = 0)]
        [AllowNull()]
        [AllowEmptyString()]
        [string]$Text,

        [string]$LineMarker = '//',
        [string]$BlockOpen = '/*',
        [string]$BlockClose = '*/'
    )

    if ($null -eq $Text -or $Text.Length -eq 0) { return '' }
    if ([string]::IsNullOrEmpty($LineMarker)) { $LineMarker = '//' }
    if ([string]::IsNullOrEmpty($BlockOpen)) { $BlockOpen = '/*' }
    if ([string]::IsNullOrEmpty($BlockClose)) { $BlockClose = '*/' }

    $sb = [System.Text.StringBuilder]::new()
    $state = 'code'   # code | line | block | dq | sq
    $i = 0
    while ($i -lt $Text.Length) {
        $ch = $Text[$i]

        # 换行在任何状态都原样保留（保证行号不漂移）。
        if ($ch -eq "`r" -or $ch -eq "`n") {
            [void]$sb.Append($ch)
            if ($state -eq 'line') { $state = 'code' }
            $i++
            continue
        }

        switch ($state) {
            'line' { $i++; continue }

            'block' {
                if ($ch -eq $BlockClose[0] -and $Text.Substring($i).StartsWith($BlockClose, [System.StringComparison]::Ordinal)) {
                    [void]$sb.Append(' ')
                    $i += $BlockClose.Length
                    $state = 'code'
                    continue
                }
                $i++
                continue
            }

            'dq' {
                if ($ch -eq '\' -and ($i + 1) -lt $Text.Length) {
                    [void]$sb.Append($ch).Append($Text[$i + 1])
                    $i += 2
                    continue
                }
                if ($ch -eq '"') {
                    if (($i + 1) -lt $Text.Length -and $Text[$i + 1] -eq '"') {
                        [void]$sb.Append('""')
                        $i += 2
                        continue
                    }
                    [void]$sb.Append($ch)
                    $state = 'code'
                    $i++
                    continue
                }
                [void]$sb.Append($ch)
                $i++
                continue
            }

            'sq' {
                if ($ch -eq '\' -and ($i + 1) -lt $Text.Length) {
                    [void]$sb.Append($ch).Append($Text[$i + 1])
                    $i += 2
                    continue
                }
                if ($ch -eq "'") {
                    if (($i + 1) -lt $Text.Length -and $Text[$i + 1] -eq "'") {
                        [void]$sb.Append("''")
                        $i += 2
                        continue
                    }
                    [void]$sb.Append($ch)
                    $state = 'code'
                    $i++
                    continue
                }
                [void]$sb.Append($ch)
                $i++
                continue
            }

            default {
                if ($ch -eq $BlockOpen[0] -and $Text.Substring($i).StartsWith($BlockOpen, [System.StringComparison]::Ordinal)) {
                    $i += $BlockOpen.Length
                    $state = 'block'
                    continue
                }
                if ($ch -eq $LineMarker[0] -and $Text.Substring($i).StartsWith($LineMarker, [System.StringComparison]::Ordinal)) {
                    $i += $LineMarker.Length
                    $state = 'line'
                    continue
                }
                if ($ch -eq '"') { [void]$sb.Append($ch); $state = 'dq'; $i++; continue }
                if ($ch -eq "'") { [void]$sb.Append($ch); $state = 'sq'; $i++; continue }
                [void]$sb.Append($ch)
                $i++
                continue
            }
        }
    }

    return $sb.ToString()
}

<#
.SYNOPSIS
    代码行视图：行号 + 去注释后的行文本（注释剥离 + 空行过滤）。
.DESCRIPTION
    下限自 Modules\Parsing\Check-KeyGrammar.ps1 的 Get-CodeLines（原返回 `{Line; Text}`）；
    同时覆盖 GD Check-ConversationConversion / Check-FactStandard、GP Check-TextKernelPurity 的行视图变体。

    默认 = 原 KeyGrammar 语义：块/行注释剥离、跳过空白行、Text 去首尾空白；
    额外提供 `-KeepComment`（保留注释）、`-KeepBlank`（保留空白行）、`-KeepIndent`（保留缩进）三个变体开关。
    返回对象同时带 `Line`（原属性）与 `Number`（与 Get-GateCodeLine 对齐），调用方二选一即可。
.PARAMETER Path
    文件路径（优先；UTF-8 显式读取；缺件 ⇒ 空结果）
.PARAMETER Text
    裸文本（未给 Path/Lines 时使用）
.PARAMETER Lines
    行数组（优先于 Path/Text；用于已读入内存的调用方）
.PARAMETER KeepBlank
    保留空白行（默认跳过）
.PARAMETER KeepComment
    保留注释行（默认剥离注释）
.PARAMETER KeepIndent
    保留行首缩进（默认去首尾空白）
.PARAMETER LineMarker
    行注释标记（默认 '//'）
.PARAMETER BlockOpen
    块注释开标记（默认 '/*'）
.PARAMETER BlockClose
    块注释闭标记（默认 '*/'）
.OUTPUTS
    [{ Line; Number; Text }] 行号（1 起）与行文本
.EXAMPLE
    foreach ($code in (Get-CodeLines -Path $file.FullName)) { $code.Line; $code.Text }
#>
function Get-CodeLines {
    [CmdletBinding()]
    param(
        [Parameter(Position = 0)]
        [string]$Path = '',

        [AllowNull()]
        [AllowEmptyString()]
        [string]$Text = '',

        [string[]]$Lines,

        [switch]$KeepBlank,
        [switch]$KeepComment,
        [switch]$KeepIndent,

        [string]$LineMarker = '//',
        [string]$BlockOpen = '/*',
        [string]$BlockClose = '*/'
    )

    $source = $null
    if ($null -ne $Lines -and @($Lines).Count -gt 0) {
        $source = (@($Lines) -join "`n")
    }
    elseif (-not [string]::IsNullOrWhiteSpace($Path)) {
        if (-not (Test-Path -LiteralPath $Path)) { return }
        $source = Read-TextFile -Path $Path
    }
    else {
        $source = $Text
    }
    if ($null -eq $source) { return }

    if (-not $KeepComment) {
        $source = Remove-CodeComments -Text $source -LineMarker $LineMarker -BlockOpen $BlockOpen -BlockClose $BlockClose
    }

    $number = 0
    foreach ($line in ($source -split "`r?`n")) {
        $number++
        $trim = $line.Trim()
        if (-not $KeepBlank -and $trim.Length -eq 0) { continue }
        $lineText = if ($KeepIndent) { $line } else { $trim }
        [pscustomobject]@{ Line = $number; Number = $number; Text = $lineText }
    }
}

Export-ModuleMember -Function @(
    'Get-Sha256Hex',
    'ConvertFrom-CommaList',
    'Remove-LineComment',
    'Get-RepoRelativePath',
    'Get-RegexCount',
    'Read-TextFile',
    'Remove-CodeComments',
    'Get-CodeLines'
) -Alias @('Get-Count', 'Read-Text')
