# StringTools.Core.psm1 - 核心字符串/正则操作工具模块
# 提供统一的字符串处理、正则匹配/替换、括号感知处理、命名转换等功能
# 设计原则：配置驱动、管道兼容、向后兼容

# ============================================
#  内部辅助函数
# ============================================

<#
.SYNOPSIS
    跟踪括号深度和引号状态（内部辅助函数）
.DESCRIPTION
    统一管理括号 {}()[] 的嵌套深度和单/双引号的开关状态。
    供 Invoke-BracketAwareSplit, Invoke-BracketAwareReplace, Invoke-RemoveInsideSymbols 使用。
#>
function Test-IsInsideBracketOrQuote {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [char]$Char,

        [Parameter(Mandatory = $true)]
        [ref]$InSingleQuote,

        [Parameter(Mandatory = $true)]
        [ref]$InDoubleQuote,

        [Parameter(Mandatory = $true)]
        [hashtable]$Depth,

        [Parameter()]
        [switch]$TrackOnly
    )
    # 引号状态跟踪
    if ($Char -eq "'" -and -not $InDoubleQuote.Value) {
        $InSingleQuote.Value = -not $InSingleQuote.Value
        return $true
    }
    if ($Char -eq '"' -and -not $InSingleQuote.Value) {
        $InDoubleQuote.Value = -not $InDoubleQuote.Value
        return $true
    }

    if (-not $InSingleQuote.Value -and -not $InDoubleQuote.Value) {
        if ($Char -eq '{') { $Depth['{']++ }
        elseif ($Char -eq '}') { $Depth['{']-- }
        elseif ($Char -eq '(') { $Depth['(']++ }
        elseif ($Char -eq ')') { $Depth['(']-- }
        elseif ($Char -eq '[') { $Depth['[']++ }
        elseif ($Char -eq ']') { $Depth['[']-- }
    }

    # 是否在括号或引号内部
    return ($InSingleQuote.Value -or $InDoubleQuote.Value -or $Depth['{'] -gt 0 -or $Depth['('] -gt 0 -or $Depth['['] -gt 0)
}

# ============================================
#  简单字符串替换
# ============================================

<#
.SYNOPSIS
    简单文本替换（支持 -replace 语义）
.DESCRIPTION
    对输入字符串执行简单文本替换，支持单个或批量替换规则。
    与 PowerShell 的 -replace 操作符兼容，但提供更结构化的接口。
.PARAMETER InputObject
    要处理的字符串（支持管道输入）
.PARAMETER OldValue
    要替换的旧字符串
.PARAMETER NewValue
    替换后的新字符串
.PARAMETER Rules
    替换规则数组，每个规则包含 Old 和 New 属性
.PARAMETER CaseSensitive
    是否区分大小写（默认：$false）
.PARAMETER SkipLines
    要跳过的行前缀数组（如 @("using ", "// ")）
.OUTPUTS
    [string] 替换后的字符串
.EXAMPLE
    "Hello World" | Invoke-SimpleReplace -OldValue "World" -NewValue "PowerShell"
.EXAMPLE
    Invoke-SimpleReplace -InputObject $content -Rules @(
        @{ Old = "A"; New = "B" },
        @{ Old = "C"; New = "D" }
    )
.EXAMPLE
    Invoke-SimpleReplace -InputObject $content -OldValue "Old" -NewValue "New" -SkipLines @("using ", "// ")
#>
function Invoke-SimpleReplace {
    [CmdletBinding(DefaultParameterSetName = 'Single')]
    param(
        [Parameter(ValueFromPipeline = $true, Position = 0)]
        [string]$InputObject,

        [Parameter(ParameterSetName = 'Single', Position = 1)]
        [string]$OldValue,

        [Parameter(ParameterSetName = 'Single', Position = 2)]
        [string]$NewValue,

        [Parameter(ParameterSetName = 'Multi')]
        [object[]]$Rules,

        [Parameter()]
        [switch]$CaseSensitive,

        [Parameter()]
        [string[]]$SkipLines
    )
    process {
        if ($null -eq $InputObject) { return }

        # 有 SkipLines 时逐行处理
        if ($SkipLines -and $SkipLines.Count -gt 0) {
            $lines = $InputObject -split "`r`n|`n"
            $newLines = @()
            foreach ($line in $lines) {
                $shouldSkip = $false
                foreach ($prefix in $SkipLines) {
                    if ($line.TrimStart().StartsWith($prefix)) {
                        $shouldSkip = $true
                        break
                    }
                }
                if ($shouldSkip) {
                    $newLines += $line
                } else {
                    $newLines += Invoke-SimpleReplaceInternal -InputObject $line -OldValue $OldValue -NewValue $NewValue -Rules $Rules -CaseSensitive:$CaseSensitive
                }
            }
            return $newLines -join "`r`n"
        }

        return Invoke-SimpleReplaceInternal -InputObject $InputObject -OldValue $OldValue -NewValue $NewValue -Rules $Rules -CaseSensitive:$CaseSensitive
    }
}

# 内部实现：执行实际替换（不含 SkipLines 逻辑）
function Invoke-SimpleReplaceInternal {
    param(
        [string]$InputObject,
        [string]$OldValue,
        [string]$NewValue,
        [object[]]$Rules,
        [switch]$CaseSensitive
    )
    $result = $InputObject

    # 多规则模式
    if ($Rules -and $Rules.Count -gt 0) {
        foreach ($rule in $Rules) {
            $old = $rule.Old
            $new = $rule.New
            if ($CaseSensitive) {
                $result = $result.Replace($old, $new)
            } else {
                $result = $result -replace [regex]::Escape($old), $new
            }
        }
        return $result
    }

    # 单规则模式
    if ($OldValue -ne $null) {
        if ($CaseSensitive) {
            $result = $result.Replace($OldValue, $NewValue)
        } else {
            $result = $result -replace [regex]::Escape($OldValue), $NewValue
        }
    }

    return $result
}

# ============================================
#  正则匹配提取
# ============================================

<#
.SYNOPSIS
    从字符串中提取正则匹配的内容
.DESCRIPTION
    支持命名捕获组和索引捕获组，返回匹配结果对象。
    可指定提取第几个匹配或所有匹配。
.PARAMETER InputObject
    要处理的字符串（支持管道输入）
.PARAMETER Pattern
    正则表达式模式
.PARAMETER GroupName
    要提取的捕获组名称（可选，默认返回整个匹配）
.PARAMETER AllMatches
    是否返回所有匹配（默认：$true）
.PARAMETER FirstMatch
    是否只返回第一个匹配
.PARAMETER IgnoreCase
    是否忽略大小写（默认：$true）
.OUTPUTS
    [string[]] 匹配结果数组
.EXAMPLE
    "Version 1.0.0 and 2.0.0" | Invoke-RegexExtract -Pattern "(\d+\.\d+\.\d+)"
    # 返回 @("1.0.0", "2.0.0")
.EXAMPLE
    Invoke-RegexExtract -InputObject $xmlContent -Pattern "<Version>(.*?)</Version>" -GroupName 1
    # 返回所有版本号
#>
function Invoke-RegexExtract {
    [CmdletBinding()]
    param(
        [Parameter(ValueFromPipeline = $true, Position = 0)]
        [string]$InputObject,

        [Parameter(Mandatory = $true, Position = 1)]
        [string]$Pattern,

        [Parameter()]
        [object]$GroupName,

        [Parameter()]
        [switch]$AllMatches,

        [Parameter()]
        [switch]$FirstMatch,

        [Parameter()]
        [switch]$IgnoreCase = $true
    )
    process {
        if ($null -eq $InputObject) { return }

        $options = [System.Text.RegularExpressions.RegexOptions]::None
        if ($IgnoreCase) { $options = [System.Text.RegularExpressions.RegexOptions]::IgnoreCase }

        $matches = [regex]::Matches($InputObject, $Pattern, $options)

        if ($matches.Count -eq 0) { return }

        $results = @()
        foreach ($match in $matches) {
            if ($GroupName -ne $null) {
                $group = $match.Groups[$GroupName]
                $results += $group.Value
            } else {
                $results += $match.Value
            }
        }

        if ($FirstMatch) {
            return $results[0]
        }

        return $results
    }
}

<#
.SYNOPSIS
    从字符串中提取正则匹配的命名捕获组，返回哈希表
.DESCRIPTION
    将正则匹配结果按命名捕获组组织为哈希表，便于后续处理。
.PARAMETER InputObject
    要处理的字符串（支持管道输入）
.PARAMETER Pattern
    包含命名捕获组的正则表达式
.PARAMETER FirstMatch
    是否只返回第一个匹配的哈希表（默认：$true）
.OUTPUTS
    [hashtable] 或 [hashtable[]]
.EXAMPLE
    Invoke-RegexExtractToHashtable -InputObject $line -Pattern "(?<Name>\w+)=(?<Value>\d+)"
    # 返回 @{ Name = "xxx"; Value = "123" }
#>
function Invoke-RegexExtractToHashtable {
    [CmdletBinding()]
    param(
        [Parameter(ValueFromPipeline = $true, Position = 0)]
        [string]$InputObject,

        [Parameter(Mandatory = $true, Position = 1)]
        [string]$Pattern,

        [Parameter()]
        [switch]$FirstMatch = $true,

        [Parameter()]
        [switch]$IgnoreCase = $true
    )
    process {
        if ($null -eq $InputObject) { return }

        $options = [System.Text.RegularExpressions.RegexOptions]::None
        if ($IgnoreCase) { $options = [System.Text.RegularExpressions.RegexOptions]::IgnoreCase }

        $matches = [regex]::Matches($InputObject, $Pattern, $options)
        if ($matches.Count -eq 0) { return }

        $results = @()
        foreach ($match in $matches) {
            $hash = @{}
            foreach ($groupName in $match.Groups.Keys) {
                if ($groupName -is [string] -and $groupName -ne '0') {
                    $hash[$groupName] = $match.Groups[$groupName].Value
                }
            }
            $results += $hash
        }

        if ($FirstMatch) {
            return $results[0]
        }
        return $results
    }
}

# ============================================
#  正则替换
# ============================================

<#
.SYNOPSIS
    正则替换字符串内容
.DESCRIPTION
    支持标准正则替换，以及通过 ScriptBlock 回调动态生成替换内容。
.PARAMETER InputObject
    要处理的字符串（支持管道输入）
.PARAMETER Pattern
    正则表达式模式
.PARAMETER Replacement
    替换字符串（支持 $1、${name} 等反向引用）
.PARAMETER ScriptBlock
    替换回调脚本块，接收 [System.Text.RegularExpressions.Match] 对象，返回替换字符串
.PARAMETER IgnoreCase
    是否忽略大小写（默认：$true）
.PARAMETER Count
    最大替换次数（默认：-1 表示全部替换）
.OUTPUTS
    [string] 替换后的字符串
.EXAMPLE
    Invoke-RegexReplace -InputObject $text -Pattern "\d+" -Replacement "NUM"
.EXAMPLE
    Invoke-RegexReplace -InputObject $text -Pattern "(\w+)=(\w+)" -ScriptBlock {
        param($match)
        return "$($match.Groups[2].Value)=$($match.Groups[1].Value)"
    }
#>
function Invoke-RegexReplace {
    [CmdletBinding(DefaultParameterSetName = 'String')]
    param(
        [Parameter(ValueFromPipeline = $true, Position = 0)]
        [string]$InputObject,

        [Parameter(Mandatory = $true, Position = 1)]
        [string]$Pattern,

        [Parameter(ParameterSetName = 'String', Position = 2)]
        [string]$Replacement,

        [Parameter(ParameterSetName = 'ScriptBlock')]
        [scriptblock]$ScriptBlock,

        [Parameter()]
        [switch]$IgnoreCase = $true,

        [Parameter()]
        [int]$Count = -1
    )
    process {
        if ($null -eq $InputObject) { return }

        $options = [System.Text.RegularExpressions.RegexOptions]::None
        if ($IgnoreCase) { $options = [System.Text.RegularExpressions.RegexOptions]::IgnoreCase }

        if ($PSCmdlet.ParameterSetName -eq 'ScriptBlock' -and $ScriptBlock) {
            # 使用 MatchEvaluator 回调
            $evaluator = {
                param($match)
                & $ScriptBlock $match
            }.GetNewClosure()
            if ($Count -gt 0) {
                $regex = [regex]::new($Pattern, $options)
                return $regex.Replace($InputObject, $evaluator, $Count)
            }
            return [regex]::Replace($InputObject, $Pattern, $evaluator, $options)
        }

        if ($Count -gt 0) {
            $regex = [regex]::new($Pattern, $options)
            return $regex.Replace($InputObject, $Replacement, $Count)
        }
        return $InputObject -replace $Pattern, $Replacement
    }
}

# ============================================
#  括号/引号感知处理
# ============================================

<#
.SYNOPSIS
    括号感知分割字符串
.DESCRIPTION
    按分隔符分割字符串，但跳过括号 {}、()、[] 和引号 ""、'' 内的内容。
    移植自 StringProcess.cs 的 SplitExcludingBrackets 方法。
.PARAMETER InputObject
    要分割的字符串（支持管道输入）
.PARAMETER Delimiter
    分隔符（默认：","）
.PARAMETER RemoveEmptyEntries
    是否移除空条目（默认：$true）
.PARAMETER TrimEntries
    是否修剪条目两端的空白（默认：$true）
.PARAMETER EscapeChar
    转义字符（默认：[char]0 表示不启用）。启用后，转义字符与其后一个字符原样进入当前片段，
    不参与括号/引号状态机 —— 用于 C#/JSON 这类以反斜杠转义的字符串文法（-EscapeChar '\'）。
.OUTPUTS
    [string[]] 分割后的字符串数组
.EXAMPLE
    Invoke-BracketAwareSplit -InputObject "a, b, {c, d}, e"
    # 返回 @("a", "b", "{c, d}", "e")
.EXAMPLE
    Invoke-BracketAwareSplit -InputObject '"a\,b", c' -EscapeChar '\'
    # 返回 @('"a\,b"', "c")（转义逗号不切分）
#>
function Invoke-BracketAwareSplit {
    [CmdletBinding()]
    param(
        [Parameter(ValueFromPipeline = $true, Position = 0)]
        [string]$InputObject,

        [Parameter(Position = 1)]
        [string]$Delimiter = ",",

        [Parameter()]
        [switch]$RemoveEmptyEntries = $true,

        [Parameter()]
        [switch]$TrimEntries = $true,

        [Parameter()]
        [char]$EscapeChar = [char]0
    )
    process {
        if ($null -eq $InputObject) { return }

        $result = [System.Collections.ArrayList]::new()
        $current = [System.Text.StringBuilder]::new()
        $depth = @{ '{' = 0; '(' = 0; '[' = 0 }
        $inSingleQuote = $false
        $inDoubleQuote = $false
        $delimiterChar = $Delimiter

        for ($i = 0; $i -lt $InputObject.Length; $i++) {
            $c = $InputObject[$i]

            # 转义字符：与被转义字符原样进入当前片段，不参与括号/引号状态机（C#/JSON 字符串语义）
            if ($EscapeChar -ne [char]0 -and $c -eq $EscapeChar -and ($i + 1) -lt $InputObject.Length) {
                [void]$current.Append($c)
                $i++
                [void]$current.Append($InputObject[$i])
                continue
            }

            $isInside = Test-IsInsideBracketOrQuote -Char $c -InSingleQuote ([ref]$inSingleQuote) -InDoubleQuote ([ref]$inDoubleQuote) -Depth $depth

            if (-not $isInside -and $c -eq $delimiterChar) {
                $entry = $current.ToString()
                if ($TrimEntries) { $entry = $entry.Trim() }
                if (-not $RemoveEmptyEntries -or $entry -ne '') {
                    [void]$result.Add($entry)
                }
                $current = [System.Text.StringBuilder]::new()
                continue
            }

            [void]$current.Append($c)
        }

        # 处理最后一段
        $lastEntry = $current.ToString()
        if ($TrimEntries) { $lastEntry = $lastEntry.Trim() }
        if (-not $RemoveEmptyEntries -or $lastEntry -ne '') {
            [void]$result.Add($lastEntry)
        }

        return $result.ToArray()
    }
}

<#
.SYNOPSIS
    括号感知替换 - 只替换括号外的内容
.DESCRIPTION
    对字符串中不在括号 {}、()、[] 和引号内的部分执行替换操作。
.PARAMETER InputObject
    要处理的字符串（支持管道输入）
.PARAMETER Pattern
    要匹配的正则模式
.PARAMETER Replacement
    替换字符串
.PARAMETER IgnoreCase
    是否忽略大小写（默认：$true）
.OUTPUTS
    [string] 替换后的字符串
.EXAMPLE
    Invoke-BracketAwareReplace -InputObject "a, {b, c}" -Pattern "," -Replacement ";"
    # 返回 "a; {b, c}"
#>
function Invoke-BracketAwareReplace {
    [CmdletBinding()]
    param(
        [Parameter(ValueFromPipeline = $true, Position = 0)]
        [string]$InputObject,

        [Parameter(Mandatory = $true, Position = 1)]
        [string]$Pattern,

        [Parameter(Position = 2)]
        [string]$Replacement,

        [Parameter()]
        [switch]$IgnoreCase = $true
    )
    process {
        if ($null -eq $InputObject) { return }

        $result = [System.Text.StringBuilder]::new()
        $outsideBuffer = [System.Text.StringBuilder]::new()
        $depth = @{ '{' = 0; '(' = 0; '[' = 0 }
        $inSingleQuote = $false
        $inDoubleQuote = $false

        # 遍历字符串，收集括号外的内容到 outsideBuffer，括号内的内容直接追加到 result
        for ($i = 0; $i -lt $InputObject.Length; $i++) {
            $c = $InputObject[$i]

            $isInside = Test-IsInsideBracketOrQuote -Char $c -InSingleQuote ([ref]$inSingleQuote) -InDoubleQuote ([ref]$inDoubleQuote) -Depth $depth

            if ($isInside) {
                # 括号/引号内的内容：先 flush outsideBuffer，再追加当前字符
                if ($outsideBuffer.Length -gt 0) {
                    $replaced = $outsideBuffer.ToString() -replace $Pattern, $Replacement
                    [void]$result.Append($replaced)
                    $outsideBuffer = [System.Text.StringBuilder]::new()
                }
                [void]$result.Append($c)
            } else {
                [void]$outsideBuffer.Append($c)
            }
        }

        # 处理最后一段括号外内容
        if ($outsideBuffer.Length -gt 0) {
            $replaced = $outsideBuffer.ToString() -replace $Pattern, $Replacement
            [void]$result.Append($replaced)
        }

        return $result.ToString()
    }
}

<#
.SYNOPSIS
    移除括号/引号内的内容
.DESCRIPTION
    移除字符串中指定符号对内的内容，保留符号对外的内容。
    移植自 StringProcess.cs 的 RemoveInsideSymbols 方法。
.PARAMETER InputObject
    要处理的字符串（支持管道输入）
.PARAMETER OpenSymbol
    开始符号（默认："{"）
.PARAMETER CloseSymbol
    结束符号（默认："}"）
.PARAMETER IncludeSymbols
    是否连符号一起移除（默认：$true）
.OUTPUTS
    [string] 处理后的字符串
.EXAMPLE
    Invoke-RemoveInsideSymbols -InputObject "a {b c} d" -OpenSymbol "{" -CloseSymbol "}"
    # 返回 "a  d"
#>
function Invoke-RemoveInsideSymbols {
    [CmdletBinding()]
    param(
        [Parameter(ValueFromPipeline = $true, Position = 0)]
        [string]$InputObject,

        [Parameter(Position = 1)]
        [string]$OpenSymbol = "{",

        [Parameter(Position = 2)]
        [string]$CloseSymbol = "}",

        [Parameter()]
        [switch]$IncludeSymbols
    )
    process {
        if ($null -eq $InputObject) { return }

        $result = [System.Text.StringBuilder]::new()
        $depth = 0
        $inSingleQuote = $false
        $inDoubleQuote = $false
        $bracketDepth = @{ '{' = 0; '(' = 0; '[' = 0 }

        for ($i = 0; $i -lt $InputObject.Length; $i++) {
            $c = $InputObject[$i]

            # 使用辅助函数跟踪引号和通用括号，但 OpenSymbol/CloseSymbol 用自定义逻辑
            $isQuote = $false
            if ($c -eq "'" -and -not $inDoubleQuote) {
                $inSingleQuote = -not $inSingleQuote
                $isQuote = $true
            }
            elseif ($c -eq '"' -and -not $inSingleQuote) {
                $inDoubleQuote = -not $inDoubleQuote
                $isQuote = $true
            }

            if ($isQuote) {
                if ($depth -eq 0) { [void]$result.Append($c) }
                continue
            }

            if (-not $inSingleQuote -and -not $inDoubleQuote) {
                if ($c -eq $OpenSymbol) {
                    $depth++
                    if ($IncludeSymbols) { [void]$result.Append($c) }
                    continue
                }
                if ($c -eq $CloseSymbol -and $depth -gt 0) {
                    $depth--
                    if ($IncludeSymbols) { [void]$result.Append($c) }
                    continue
                }
            }

            if ($depth -eq 0) {
                [void]$result.Append($c)
            }
        }

        return $result.ToString()
    }
}

# ============================================
#  命名风格转换
# ============================================

<#
.SYNOPSIS
    命名风格转换
.DESCRIPTION
    在 PascalCase、camelCase、snake_case、kebab-case、点分隔 等命名风格之间转换。
.PARAMETER InputObject
    要转换的字符串（支持管道输入）
.PARAMETER ToCase
    目标命名风格：Pascal, Camel, Snake, Kebab, Dot, Title
.PARAMETER FromCase
    源命名风格（可选，自动检测）
.OUTPUTS
    [string] 转换后的字符串
.EXAMPLE
    Convert-NameCase -InputObject "hello_world" -ToCase "Pascal"
    # 返回 "HelloWorld"
.EXAMPLE
    Convert-NameCase -InputObject "MyVariableName" -ToCase "Snake"
    # 返回 "my_variable_name"
#>
function Convert-NameCase {
    [CmdletBinding()]
    param(
        [Parameter(ValueFromPipeline = $true, Position = 0)]
        [string]$InputObject,

        [Parameter(Mandatory = $true, Position = 1)]
        [ValidateSet('Pascal', 'Camel', 'Snake', 'Kebab', 'Dot', 'Title')]
        [string]$ToCase,

        [Parameter()]
        [ValidateSet('Pascal', 'Camel', 'Snake', 'Kebab', 'Dot', 'Auto')]
        [string]$FromCase = 'Auto'
    )
    process {
        if ([string]::IsNullOrEmpty($InputObject)) { return $InputObject }

        # 1. 将输入拆分为单词数组
        $words = @()
        if ($FromCase -eq 'Auto') {
            # 自动检测：尝试多种分隔模式
            if ($InputObject -match '[-_.]') {
                # 分隔符分隔：snake_case, kebab-case, dot.case
                $words = $InputObject -split '[-_.]' | Where-Object { $_ -ne '' }
            } elseif ($InputObject -cmatch '[a-z][A-Z]') {
                # PascalCase 或 camelCase
                $words = [regex]::Split($InputObject, '(?<=[a-z])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])') |
                    Where-Object { $_ -ne '' }
            } else {
                $words = @($InputObject)
            }
        } else {
            switch ($FromCase) {
                'Pascal' { $words = [regex]::Split($InputObject, '(?<=[a-z])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])') | Where-Object { $_ -ne '' } }
                'Camel'  { $words = [regex]::Split($InputObject, '(?<=[a-z])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])') | Where-Object { $_ -ne '' } }
                'Snake'  { $words = $InputObject -split '_' | Where-Object { $_ -ne '' } }
                'Kebab'  { $words = $InputObject -split '-' | Where-Object { $_ -ne '' } }
                'Dot'    { $words = $InputObject -split '\.' | Where-Object { $_ -ne '' } }
            }
        }

        if ($words.Count -eq 0) { return $InputObject }

        # 2. 转换为目标风格
        switch ($ToCase) {
            'Pascal' {
                $result = ($words | ForEach-Object {
                    if ($_ -eq '') { '' } else { $_.Substring(0, 1).ToUpper() + $_.Substring(1).ToLower() }
                }) -join ''
            }
            'Camel' {
                $first = $words[0].ToLower()
                $rest = ($words | Select-Object -Skip 1 | ForEach-Object {
                    $_.Substring(0, 1).ToUpper() + $_.Substring(1).ToLower()
                }) -join ''
                $result = $first + $rest
            }
            'Snake' {
                $result = ($words | ForEach-Object { $_.ToLower() }) -join '_'
            }
            'Kebab' {
                $result = ($words | ForEach-Object { $_.ToLower() }) -join '-'
            }
            'Dot' {
                $result = ($words | ForEach-Object { $_.ToLower() }) -join '.'
            }
            'Title' {
                $result = ($words | ForEach-Object {
                    $_.Substring(0, 1).ToUpper() + $_.Substring(1).ToLower()
                }) -join ' '
            }
        }

        return $result
    }
}

# ============================================
#  占位符替换（模板引擎基础）
# ============================================

<#
.SYNOPSIS
    替换字符串中的占位符 {key} 为对应的值
.DESCRIPTION
    支持嵌套占位符和默认值语法 {key|defaultValue}。
    移植自 StringProcess.cs 的 ReplaceBracesParameters 方法。
.PARAMETER InputObject
    包含占位符的模板字符串（支持管道输入）
.PARAMETER Parameters
    参数哈希表，键为占位符名称，值为替换内容
.PARAMETER OpenDelimiter
    占位符开始分隔符（默认："{"）
.PARAMETER CloseDelimiter
    占位符结束分隔符（默认："}"）
.PARAMETER DefaultSeparator
    默认值分隔符（默认："|"）
.PARAMETER MaxRecursionDepth
    最大递归深度（默认：5，防止循环引用）
.OUTPUTS
    [string] 替换后的字符串
.EXAMPLE
    Invoke-ReplacePlaceholder -InputObject "Hello {name}!" -Parameters @{ name = "World" }
    # 返回 "Hello World!"
.EXAMPLE
    Invoke-ReplacePlaceholder -InputObject "{greeting} {name|Guest}!" -Parameters @{ greeting = "Hi" }
    # 返回 "Hi Guest!"
#>
function Invoke-ReplacePlaceholder {
    [CmdletBinding()]
    param(
        [Parameter(ValueFromPipeline = $true, Position = 0)]
        [string]$InputObject,

        [Parameter(Mandatory = $true, Position = 1)]
        [hashtable]$Parameters,

        [Parameter()]
        [string]$OpenDelimiter = "{",

        [Parameter()]
        [string]$CloseDelimiter = "}",

        [Parameter()]
        [string]$DefaultSeparator = "|",

        [Parameter()]
        [int]$MaxRecursionDepth = 5
    )
    process {
        if ([string]::IsNullOrEmpty($InputObject)) { return $InputObject }

        $result = $InputObject
        $depth = 0

        # 转义分隔符用于正则
        $escapedOpen = [regex]::Escape($OpenDelimiter)
        $escapedClose = [regex]::Escape($CloseDelimiter)
        $escapedSep = [regex]::Escape($DefaultSeparator)

        while ($result -match "$escapedOpen([^$escapedClose$escapedOpen]+(?:$escapedSep[^$escapedClose]*)?)$escapedClose" -and $depth -lt $MaxRecursionDepth) {
            $fullMatch = $Matches[0]
            $keyWithDefault = $Matches[1]

            $key = $keyWithDefault
            $defaultValue = $null

            if ($keyWithDefault -match "^(.+?)$escapedSep(.+)$") {
                $key = $Matches[1].Trim()
                $defaultValue = $Matches[2].Trim()
            }

            $replacement = if ($Parameters.ContainsKey($key)) {
                $Parameters[$key]
            } elseif ($defaultValue -ne $null) {
                $defaultValue
            } else {
                $fullMatch  # 保留原样
            }

            $result = $result.Replace($fullMatch, $replacement)
            $depth++
        }

        return $result
    }
}

# ============================================
#  行过滤工具
# ============================================

<#
.SYNOPSIS
    过滤字符串中的行
.DESCRIPTION
    根据行前缀或正则模式过滤行，支持包含和排除模式。
.PARAMETER InputObject
    多行字符串（支持管道输入）
.PARAMETER SkipLines
    要跳过的行前缀数组
.PARAMETER SkipPattern
    要跳过的正则模式
.PARAMETER KeepLines
    只保留的行前缀数组（与 SkipLines 互斥）
.PARAMETER TrimLines
    是否修剪行两端空白（默认：$false）
.OUTPUTS
    [string] 过滤后的多行字符串
.EXAMPLE
    Invoke-FilterLines -InputObject $content -SkipLines @("using ", "// ", "#")
#>
function Invoke-FilterLines {
    [CmdletBinding()]
    param(
        [Parameter(ValueFromPipeline = $true, Position = 0)]
        [string]$InputObject,

        [Parameter()]
        [string[]]$SkipLines,

        [Parameter()]
        [string]$SkipPattern,

        [Parameter()]
        [string[]]$KeepLines,

        [Parameter()]
        [switch]$TrimLines
    )
    process {
        if ($null -eq $InputObject) { return }

        $lines = $InputObject -split "`r`n|`n"
        $result = [System.Collections.ArrayList]::new()

        foreach ($line in $lines) {
            $processedLine = if ($TrimLines) { $line.Trim() } else { $line }

            $shouldSkip = $false

            # SkipLines 模式
            if ($SkipLines) {
                foreach ($prefix in $SkipLines) {
                    if ($processedLine.StartsWith($prefix)) {
                        $shouldSkip = $true
                        break
                    }
                }
            }

            # SkipPattern 模式
            if (-not $shouldSkip -and $SkipPattern) {
                if ($processedLine -match $SkipPattern) {
                    $shouldSkip = $true
                }
            }

            # KeepLines 模式
            if ($KeepLines -and -not $shouldSkip) {
                $shouldKeep = $false
                foreach ($prefix in $KeepLines) {
                    if ($processedLine.StartsWith($prefix)) {
                        $shouldKeep = $true
                        break
                    }
                }
                if (-not $shouldKeep) { $shouldSkip = $true }
            }

            if (-not $shouldSkip) {
                [void]$result.Add($processedLine)
            }
        }

        return $result.ToArray()
    }
}

# ============================================
#  模板占位符替换
# ============================================

<#
.SYNOPSIS
    批量替换模板占位符（默认 @@NAME@@）
.DESCRIPTION
    用哈希表参数替换字符串中的占位符，前后缀可定制（默认 @@）。
    未出现在 Parameters 中的占位符保持原样。
.PARAMETER InputObject
    模板字符串（支持管道输入）
.PARAMETER Parameters
    占位符名 → 替换值 的哈希表
.PARAMETER Prefix
    占位符前缀（默认：@@）
.PARAMETER Suffix
    占位符后缀（默认：@@）
.OUTPUTS
    [string] 替换后的字符串
.EXAMPLE
    Invoke-TemplateReplace -InputObject "Hello @@NAME@@" -Parameters @{ NAME = "World" }
    # 返回 "Hello World"
#>
function Invoke-TemplateReplace {
    [CmdletBinding()]
    param(
        [Parameter(ValueFromPipeline = $true, Position = 0)]
        [string]$InputObject,

        [Parameter(Mandatory = $true)]
        [hashtable]$Parameters,

        [Parameter()]
        [string]$Prefix = '@@',

        [Parameter()]
        [string]$Suffix = '@@'
    )
    process {
        if ([string]::IsNullOrEmpty($InputObject)) { return $InputObject }
        $result = $InputObject
        foreach ($key in $Parameters.Keys) {
            $placeholder = "$Prefix$key$Suffix"
            if ($result.IndexOf($placeholder) -ge 0) {
                $result = $result.Replace($placeholder, [string]$Parameters[$key])
            }
        }
        return $result
    }
}

# ============================================
#  复合键格式化
# ============================================

<#
.SYNOPSIS
    将多段文本按位置分隔符连接为复合键
.DESCRIPTION
    按位序在相邻段之间插入分隔符，用于 LIB/STYP/NAME → LIB:STYP.NAME 一类的键格式化。
    分隔符不足时复用最后一个；段数 ≤1 时原样返回。
.PARAMETER Segments
    分段数组（顺序即连接顺序）
.PARAMETER Separators
    分隔符数组（第 i 个分隔符位于第 i 与 i+1 段之间）
.OUTPUTS
    [string] 复合键
.EXAMPLE
    ConvertTo-CompoundKey -Segments @('axioms','AX','StateSnapshot') -Separators @(':','.')
    # 返回 'axioms:AX.StateSnapshot'
#>
function ConvertTo-CompoundKey {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true, Position = 0)]
        [string[]]$Segments,

        [Parameter(Mandatory = $true, Position = 1)]
        [string[]]$Separators
    )
    if ($Segments.Count -eq 0) { return '' }
    if ($Segments.Count -le 1) { return $Segments[0] }
    $result = $Segments[0]
    for ($i = 1; $i -lt $Segments.Count; $i++) {
        $sepIndex = [Math]::Min($i - 1, $Separators.Count - 1)
        $result += $Separators[$sepIndex] + $Segments[$i]
    }
    return $result
}

# ============================================
#  导出公共函数
# ============================================

Export-ModuleMember -Function Invoke-SimpleReplace,
                              Invoke-RegexExtract,
                              Invoke-RegexExtractToHashtable,
                              Invoke-RegexReplace,
                              Invoke-BracketAwareSplit,
                              Invoke-BracketAwareReplace,
                              Invoke-RemoveInsideSymbols,
                              Convert-NameCase,
                              Invoke-ReplacePlaceholder,
                              Invoke-FilterLines,
                              Invoke-TemplateReplace,
                              ConvertTo-CompoundKey
  
# ============================================  
#  标识符与键片段（追加）  
# ============================================  
  
function Test-ValidIdentifier {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true, Position = 0)]
        [AllowEmptyString()]
        [string]$Text,

        [Parameter()]
        [switch]$AllowDot,

        [Parameter()]
        [switch]$AllowLeadingDigit
    )
    if ([string]::IsNullOrEmpty($Text)) { return $false }
    $chars = 'A-Za-z0-9_'
    if ($AllowDot) { $chars += '.' }
    $chars += '-'
    $head = $(if ($AllowLeadingDigit) { '[A-Za-z0-9_]' } else { '[A-Za-z_]' })
    return $Text -match ('^' + $head + '[' + $chars + ']*$')
}

<#
.SYNOPSIS
    判定是否为数值字面量
.DESCRIPTION
    统一数值字面量判定（整型/浮点/指数），消除各调用点各自书写的重复正则。
.PARAMETER Text
    要判定的文本
.PARAMETER IntegerOnly
    仅接受整数（^-?\d+$）
.OUTPUTS
    [bool]
.EXAMPLE
    Test-NumericLiteral -Text '1.5e-3'                # True
    Test-NumericLiteral -Text '1.5' -IntegerOnly      # False
#>
function Test-NumericLiteral {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true, Position = 0)]
        [AllowEmptyString()]
        [string]$Text,

        [Parameter()]
        [switch]$IntegerOnly
    )
    if ([string]::IsNullOrEmpty($Text)) { return $false }
    if ($IntegerOnly) { return $Text -match '^-?\d+$' }
    return $Text -match '^-?(\d+\.\d*|\.\d+|\d+)([eE][+-]?\d+)?$'
}

<#
.SYNOPSIS
    压缩文本（移除全部空白字符）
.DESCRIPTION
    归一化比较用：移除所有空白（含换行/制表符），供"表达式文本全等比较"这类场景共用。
.PARAMETER Text
    要压缩的文本（$null 视为空串）
.OUTPUTS
    [string]
.EXAMPLE
    Get-CompactText -Text "a + ( b )"   # "a+(b)"
#>
function Get-CompactText {
    [CmdletBinding()]
    param(
        [Parameter(Position = 0)]
        [string]$Text
    )
    if ($null -eq $Text) { return '' }
    return ($Text -replace '\s+', '')
}
  
function Get-NameHead {  
    [CmdletBinding()]  
    param(  
        [Parameter(Mandatory = $true, Position = 0)]  
        [string]$Name  
    )  
    $idx = $Name.IndexOf('.')  
    if ($idx -ge 0) { return $Name.Substring(0, $idx) }  
    return $Name  
}  
  
# ============================================  
#  前缀匹配（追加）  
# ============================================  
  
function Get-FirstMatchingPrefix {  
    [CmdletBinding()]  
    param(  
        [Parameter(Mandatory = $true, Position = 0)]  
        [AllowEmptyString()]  
        [string]$Text,  
  
        [Parameter(Mandatory = $true)]  
        [string[]]$Prefixes,  
  
        [Parameter()]  
        [string]$BoundaryPattern = '\s|\]|$'  
    )  
    # 前缀须锚定行首（^）：前缀前缀必须是行的第一个非空白 token。
    # 原实现用未锚定 -match，会把行内后续出现的其它前缀（如 `[Def]`）误判为首前缀，
    # 导致 `[DR] AxiomAxisDeclaration ... [Def] GlobalDecisionKey` 被错误解析为 Def.AxiomAxisDeclaration。
    foreach ($p in $Prefixes) {  
        if ($Text -match ('^\s*' + [regex]::Escape($p) + '(?=' + $BoundaryPattern + ')')) {
            return $p  
        }  
    }  
    return $null  
}  
  
# 追加导出（累积语义: 与文件头 Export-ModuleMember 叠加）  
Export-ModuleMember -Function Test-ValidIdentifier,
                              Test-NumericLiteral,
                              Get-CompactText,
                              Get-NameHead,
                              Get-FirstMatchingPrefix
