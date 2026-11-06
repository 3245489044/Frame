#requires -Version 5.1
<#
.SYNOPSIS
    门禁块解析原语（方法体 / 花括号配平 / 声明块 / 初始化体成员）。

.DESCRIPTION
    收敛各域门禁内联的「从源码文本取结构化块」私有实现。本模块**只做块边界与行级形状**，
    不携带任何域词表（`ZOMBIE`、`[Segment]`、`[ReadModel]` 字段语义一律留在域内）。

    来源与合并结论（详见 Docs\analysis\2026\11\11.6\powershell\gate-extraction\RefactorPlan.md §4）：
      · Get-GateMethodBody         ← GP\Check-FrameFactCoverage.ps1:65-80 Get-MethodBody（逐字符等价）
      · Get-GateInitializerMember  ← GP\Check-TestDoubleParity.ps1:85-93 Get-InitializerMember（逐字符等价）
      · Get-GateDeclarationBlock   ← GD\Check-RelationGraphParity.ps1:37-56 Get-DeclarationMap
                                    + GD\Check-GraphDeclarationParity.ps1:45-65 Get-EdgeDeclarations（同语义合并）
      · Get-GateBalancedBlock      ← 上述两处花括号扫描的共用配平原语
      · Test-ZombieBlock           ← 不合并（两条行正则的域判据，无配平语义）
      · Get-SegmentFieldMap        ← 不合并（[Segment]/[ReadModel] 类型化字段视图，正则与通用取字段不等价）

    兼容口径：仅使用 Windows PowerShell 5.1 即有语法/类型；本模块零 IO、零裁决语义。

.NOTES
    用法：
        Import-Module (Join-Path $PSRoot 'Frame\PowerShellScript\Baselines\GateParsing.psm1') -Force
        $body  = Get-GateMethodBody -Text $src -Method 'Resolve'
        $edges = Get-GateDeclarationBlock -Text $ushar -Prefix '[Edge]'
#>

Set-StrictMode -Version Latest

<#
.SYNOPSIS
    花括号配平扫描：从 StartIndex 起取首个开括号 → 计数归零 → 返回含括号子串。
.PARAMETER Text
    源文本（$null 视为空串）
.PARAMETER StartIndex
    起始搜索位置（含）；超出文本长度 ⇒ $null
.PARAMETER Open
    开括号字符（默认 '{'）
.PARAMETER Close
    闭括号字符（默认 '}'）
.OUTPUTS
    [string] 配平块（含首尾括号）；未找到或未闭合 ⇒ $null
#>
function Get-GateBalancedBlock {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true, Position = 0)]
        [AllowNull()]
        [AllowEmptyString()]
        [string]$Text,

        [Parameter(Mandatory = $true, Position = 1)]
        [ValidateRange(0, 2147483647)]
        [int]$StartIndex,

        [char]$Open = '{',
        [char]$Close = '}'
    )

    if ($null -eq $Text -or $StartIndex -ge $Text.Length) { return $null }
    $start = $Text.IndexOf($Open, $StartIndex)
    if ($start -lt 0) { return $null }

    $depth = 0
    for ($i = $start; $i -lt $Text.Length; $i++) {
        $ch = $Text[$i]
        if ($ch -eq $Open) {
            $depth++
        }
        elseif ($ch -eq $Close) {
            $depth--
            if ($depth -eq 0) { return $Text.Substring($start, $i - $start + 1) }
        }
    }
    return $null
}

<#
.SYNOPSIS
    方法体提取：签名行（访问修饰符 + 方法名）→ 首个 '{' → 花括号配平。
.DESCRIPTION
    等价于 GP\Check-FrameFactCoverage.ps1 的 Get-MethodBody：表达式体方法同样取到初始化器闭合处。
.PARAMETER Text
    源文本
.PARAMETER Method
    方法名（按字面匹配，内部 [regex]::Escape）
.PARAMETER AccessPattern
    访问修饰符正则片段（默认 public|private|internal|protected）
.OUTPUTS
    [string] 方法体（含花括号）；未命中 ⇒ $null
#>
function Get-GateMethodBody {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true, Position = 0)]
        [AllowNull()]
        [AllowEmptyString()]
        [string]$Text,

        [Parameter(Mandatory = $true, Position = 1)]
        [string]$Method,

        [string]$AccessPattern = 'public|private|internal|protected'
    )

    if ($null -eq $Text -or $Method.Length -eq 0) { return $null }
    $m = [regex]::Match($Text, '(?m)^\s*(?:' + $AccessPattern + ')[^\r\n]*\b' + [regex]::Escape($Method) + '\s*\(')
    if (-not $m.Success) { return $null }
    return Get-GateBalancedBlock -Text $Text -StartIndex $m.Index
}

<#
.SYNOPSIS
    声明块解析：[Prefix] 名称: 正文 + 缩进续行合并为一行。
.DESCRIPTION
    行首前缀按 Ordinal 匹配；空行与 '#' 注释行跳过；其它 '[' 段落重置当前条目；
    续行以 ' | ' 追加（保持与 Get-DeclarationMap / Get-EdgeDeclarations 的文本形状一致）。

    与两份源实现的唯一收口差异：Check-GraphDeclarationParity 原版不跳 '#' 注释行（会把注释并入续行），
    本函数按 Check-RelationGraphParity 的口径统一跳过（收紧，避免注释文本污染字段取值）。

.PARAMETER Text
    声明文本（$null 视为空串）
.PARAMETER Prefix
    声明前缀（如 '[Edge]'、'[Relation]'）
.OUTPUTS
    [System.Collections.Specialized.OrderedDictionary] 名称 → 合并正文（保持声明序）
.NOTES
    迁移提示：OrderedDictionary 用 .Contains(key)，不是 Hashtable 的 .ContainsKey(key)。
#>
function Get-GateDeclarationBlock {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true, Position = 0)]
        [AllowNull()]
        [AllowEmptyString()]
        [string]$Text,

        [Parameter(Mandatory = $true, Position = 1)]
        [ValidateNotNullOrEmpty()]
        [string]$Prefix
    )

    $map = [ordered]@{}
    if ($null -eq $Text) { return $map }

    $current = $null
    foreach ($line in ($Text -split "`r?`n")) {
        $trim = $line.Trim()
        if ($trim.Length -eq 0 -or $trim.StartsWith('#')) { continue }

        if ($trim.StartsWith($Prefix, [System.StringComparison]::Ordinal)) {
            $rest = $trim.Substring($Prefix.Length).Trim()
            $nameEnd = $rest.IndexOf(':')
            if ($nameEnd -le 0) { $current = $null; continue }
            $current = $rest.Substring(0, $nameEnd).Trim()
            $map[$current] = $rest
            continue
        }

        if ($trim.StartsWith('[')) { $current = $null; continue }
        if ($null -ne $current) { $map[$current] = $map[$current] + ' | ' + $trim }
    }
    return $map
}

<#
.SYNOPSIS
    初始化体顶层成员名：剥离嵌套块后提取 name= 成员。
.DESCRIPTION
    等价于 GP\Check-TestDoubleParity.ps1 的 Get-InitializerMember：反复把 '{...}' 压成空格，
    防止子初始化器成员被误归因到父类型。
.PARAMETER Body
    初始化体文本
.OUTPUTS
    [string[]] 顶层成员名（可能为空数组）
#>
function Get-GateInitializerMember {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true, Position = 0)]
        [AllowNull()]
        [AllowEmptyString()]
        [string]$Body
    )

    if ($null -eq $Body -or [string]::IsNullOrWhiteSpace($Body)) { return @() }

    $flat = $Body
    $prev = $null
    while ($prev -ne $flat) {
        $prev = $flat
        $flat = [regex]::Replace($flat, '\{[^{}]*\}', ' ')
    }

    $names = [System.Collections.Generic.List[string]]::new()
    foreach ($m in [regex]::Matches($flat, '(?:^|,|\{|;)\s*(?<m>[A-Za-z_][A-Za-z0-9_]*)\s*=(?![=>])')) {
        $names.Add($m.Groups['m'].Value)
    }
    return $names.ToArray()
}

Export-ModuleMember -Function @(
    'Get-GateBalancedBlock',
    'Get-GateMethodBody',
    'Get-GateDeclarationBlock',
    'Get-GateInitializerMember'
)
