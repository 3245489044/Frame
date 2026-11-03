<#
.SYNOPSIS
    Usharp 源码扫描公共内核（PowerShell 侧单一实现）。

.DESCRIPTION
    收敛此前 Extract-/Remove-/Generate- 各自复制的「类型栈 + 括号计数 + 声明正则」：
      · 排除策略唯一（_deprecated / Generated / obj / bin / *.g.cs）；
      · 类型索引唯一（命名空间 + 嵌套全名 + kind/modifier/partial）；
      · 值形态分类唯一（字面量 / nameof / 引用）。

    仅供工具脚本消费；不参与运行期。
#>

Set-StrictMode -Version Latest

$script:ExcludedDirNames = @('_deprecated', 'Generated', 'obj', 'bin', '.git')

function Test-UsharpExcludedPath {
    param([Parameter(Mandatory)][string]$Path)

    if ($Path -like '*.g.cs') { return $true }
    $normalized = $Path.Replace('/', '\')
    foreach ($name in $script:ExcludedDirNames) {
        if ($normalized -match ('\\' + [regex]::Escape($name) + '\\')) { return $true }
    }
    return $false
}

function Get-UsharpSourceFiles {
    param([Parameter(Mandatory)][string]$SourceRoot)

    if (-not (Test-Path -LiteralPath $SourceRoot)) { return @() }
    Get-ChildItem -LiteralPath $SourceRoot -Recurse -File -Filter *.cs |
        Where-Object { -not (Test-UsharpExcludedPath $_.FullName) } |
        Sort-Object FullName
}

function Get-UsharpSourceIndex {
    <#
      产出类型索引：FullName 为「命名空间.外层.内层」（与 [Const] target 同坐标系）。
    #>
    param([Parameter(Mandatory)][string]$SourceRoot)

    $typeDecl = '^\s*(?:public|internal)\s+(?:(static|sealed|abstract)\s+)?(partial\s+)?(class|record|struct)\s+([A-Za-z_]\w*)'
    $namespaceDecl = '^\s*namespace\s+([A-Za-z_][\w.]*)'

    $result = New-Object System.Collections.Generic.List[object]

    foreach ($file in (Get-UsharpSourceFiles -SourceRoot $SourceRoot)) {
        $lines = [System.IO.File]::ReadAllLines($file.FullName)
        $namespace = ''
        $depth = 0
        $stack = New-Object System.Collections.Generic.List[object]

        for ($i = 0; $i -lt $lines.Count; $i++) {
            $line = $lines[$i]
            $startDepth = $depth
            if ($line -match $namespaceDecl) { $namespace = $Matches[1] }

            if ($line -match $typeDecl) {
                $modifier = if ($Matches[1]) { $Matches[1] } else { 'plain' }
                $isPartial = [bool]($Matches.ContainsKey(2) -and $Matches[2])
                $kind = $Matches[3]
                $name = $Matches[4]
                $parent = if ($stack.Count -gt 0) { $stack[$stack.Count - 1] } else { $null }
                $fullName = if ($null -ne $parent) { "$($parent.FullName).$name" }
                            elseif ($namespace) { "$namespace.$name" }
                            else { $name }

                $frame = [pscustomobject]@{
                    FullName   = $fullName
                    Name       = $name
                    Namespace  = $namespace
                    Kind       = $kind
                    Modifier   = $modifier
                    IsPartial  = $isPartial
                    Path       = $file.FullName
                    LineNumber = $i + 1
                    BodyDepth  = $startDepth + 1
                }
                $result.Add($frame)
                $stack.Add($frame)
            }

            $depth += ([regex]::Matches($line, '\{')).Count
            $depth -= ([regex]::Matches($line, '\}')).Count
            while ($stack.Count -gt 0 -and $depth -lt $stack[$stack.Count - 1].BodyDepth) {
                $stack.RemoveAt($stack.Count - 1)
            }
        }
    }

    return $result
}

function Get-UsharpMemberValueKind {
    <#
      值形态分类（与闸A C7 同一判据集）：literal / nameof / reference / invalid。
    #>
    param([string]$Expression)

    if ([string]::IsNullOrWhiteSpace($Expression)) { return 'invalid' }
    $text = $Expression.Trim()

    if ($text -match '^"([^"\\]|\\.)*"$' -or $text -match '^@"([^"]|"")*"$') { return 'literal' }
    if ($text -match '^-?(0|[1-9]\d*)$') { return 'literal' }
    if ($text -match '^nameof\(\s*[A-Za-z_]\w*(\.[A-Za-z_]\w*)+\s*\)$') { return 'nameof' }
    if ($text -match '^[A-Za-z_]\w*(\.[A-Za-z_]\w*)+$') { return 'reference' }
    return 'invalid'
}

function Get-UsharpConstUnits {
    <#
      解析 [Const] 声明面（target / kind / modifier / valueType / members / source line）。
    #>
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) { return @() }

    $units = New-Object System.Collections.Generic.List[object]
    $current = $null
    $lineNo = 0
    foreach ($line in [System.IO.File]::ReadAllLines($Path)) {
        $lineNo++
        if ($line -match '^\[Const\]\s+([^:|]+)') {
            $current = [pscustomobject]@{
                Target    = $Matches[1].Trim()
                Kind      = 'class'
                Modifier  = 'plain'
                ValueType = 'string'
                Members   = New-Object System.Collections.Generic.List[object]
                Line      = $lineNo
            }
            if ($line -match 'kind=([A-Za-z]+)') { $current.Kind = $Matches[1] }
            if ($line -match 'modifier=([A-Za-z]+)') { $current.Modifier = $Matches[1] }
            if ($line -match 'valueType=([A-Za-z]+)') { $current.ValueType = $Matches[1] }
            $units.Add($current)
        }
        elseif ($null -ne $current -and $line -match '^\s+([A-Za-z_]\w*)\s*=\s*(.+)$') {
            $current.Members.Add([pscustomobject]@{ Name = $Matches[1]; Value = $Matches[2].Trim() })
        }
    }

    return $units
}

Export-ModuleMember -Function Test-UsharpExcludedPath, Get-UsharpSourceFiles, Get-UsharpSourceIndex, Get-UsharpMemberValueKind, Get-UsharpConstUnits
