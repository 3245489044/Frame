# MarkdownLintFix.psm1
# 通用 Markdown 文档格式修复模块
# 支持解析 VS Code markdownlint 输出的警告 JSON，自动修复对应问题

# ============================================================
# 内部辅助函数
# ============================================================

function Split-Lines {
    <#
    .SYNOPSIS
        安全分割文本为行数组（处理 CRLF/LF）
    #>
    param([string]$Content)
    if (-not $Content) { return @() }
    return [regex]::Split($Content, "`r?`n")
}

function Join-Lines {
    <#
    .SYNOPSIS
        将行数组合并为文本（使用 LF）
    #>
    param([string[]]$Lines)
    return $Lines -join "`n"
}

function New-FixResult {
    <#
    .SYNOPSIS
        创建统一的修复结果对象
    #>
    param(
        [bool]$Modified,
        [string]$Content = "",
        [string]$Rule = "",
        [string]$Message = ""
    )
    return @{
        Modified = $Modified
        Content  = $Content
        Rule     = $Rule
        Message  = $Message
    }
}

function Add-BlankLinesAround {
    <#
    .SYNOPSIS
        通用"前后加空行"修复函数
    .DESCRIPTION
        根据正则模式匹配行，在匹配行前后添加空行（如果缺少空行）。
        被 Fix-MD032、Fix-MD058、Fix-MD031 复用。
    #>
    param(
        [string]$Content,
        [string]$Pattern,
        [string]$Rule,
        [string]$Message,
        [switch]$WhatIf
    )

    $modified = $false
    $lines = Split-Lines $Content

    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match $Pattern) {
            # 前加空行
            if ($i -gt 0 -and $lines[$i - 1] -match '\S' -and $lines[$i - 1] -notmatch $Pattern) {
                $lines = $lines[0..($i - 1)] + @('') + $lines[$i..($lines.Count - 1)]
                $modified = $true
                $i++ # 跳过刚插入的空行
            }
        }
    }

    if ($modified) {
        $newContent = Join-Lines $lines
        if ($WhatIf) {
            Write-Host "  [$Rule] $Message" -ForegroundColor Magenta
        }
        return New-FixResult -Modified $true -Content $newContent -Rule $Rule -Message $Message
    }

    return New-FixResult -Modified $false -Rule $Rule
}

# ============================================================
# 检测函数
# ============================================================

function Detect-MarkdownLintIssues {
    <#
    .SYNOPSIS
        自动检测 Markdown 文件中的 markdownlint 问题
    #>
    param(
        [string]$Content,
        [string]$FilePath
    )

    $issues = @()
    $lines = Split-Lines $Content

    # MD041: 首行不是 H1（跳过 YAML front matter）
    $contentStart = 0
    if ($lines.Count -gt 0 -and $lines[0] -match '^---$') {
        for ($i = 1; $i -lt $lines.Count; $i++) {
            if ($lines[$i] -match '^---$') {
                $contentStart = $i + 1
                break
            }
        }
    }
    if ($contentStart -lt $lines.Count -and $lines[$contentStart] -notmatch '^# ') {
        $issues += [PSCustomObject]@{
            resource    = $FilePath
            code        = @{ value = 'MD041' }
            message     = 'First line should be a top-level heading'
            startLineNumber = $contentStart + 1
        }
    }

    # MD034: 裸 URL（排除链接语法和反引号中的 URL）
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        # 跳过链接语法行 [text](url) 和图片 ![alt](url)
        if ($line -match '^\s*\[.*\]\(https?://' -or $line -match '^\s*!\[.*\]\(https?://') { continue }
        # 跳过反引号包裹的行
        if ($line -match '^\s*`.*https?://.*`') { continue }
        if ($line -match '(^|\s)https?://[^\s`"<>)]+' -and $line -notmatch '\]\(') {
            $issues += [PSCustomObject]@{
                resource    = $FilePath
                code        = @{ value = 'MD034' }
                message     = 'Bare URL used'
                startLineNumber = $i + 1
            }
        }
    }

    # MD060: 表格列样式（| 两侧缺少空格）
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match '^\|.*\|$') {
            if ($lines[$i] -match '\|[^ ]' -or $lines[$i] -match '[^ ]\|') {
                $issues += [PSCustomObject]@{
                    resource    = $FilePath
                    code        = @{ value = 'MD060' }
                    message     = 'Table column style'
                    startLineNumber = $i + 1
                }
            }
        }
    }

    # MD032: 列表周围缺少空行
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match '^(- |\* |\d+\. )') {
            if ($i -gt 0 -and $lines[$i - 1] -match '\S' -and $lines[$i - 1] -notmatch '^(- |\* |\d+\. )') {
                $issues += [PSCustomObject]@{
                    resource    = $FilePath
                    code        = @{ value = 'MD032' }
                    message     = 'Lists should be surrounded by blank lines'
                    startLineNumber = $i + 1
                }
            }
        }
    }

    # MD058: 表格周围缺少空行
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match '^\|.*\|$') {
            if ($i -gt 0 -and $lines[$i - 1] -match '\S' -and $lines[$i - 1] -notmatch '^\|') {
                $issues += [PSCustomObject]@{
                    resource    = $FilePath
                    code        = @{ value = 'MD058' }
                    message     = 'Tables should be surrounded by blank lines'
                    startLineNumber = $i + 1
                }
            }
        }
    }

    # MD031: 代码块周围缺少空行
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match '^```') {
            if ($i -gt 0 -and $lines[$i - 1] -match '\S' -and $lines[$i - 1] -notmatch '^```') {
                $issues += [PSCustomObject]@{
                    resource    = $FilePath
                    code        = @{ value = 'MD031' }
                    message     = 'Fenced code blocks should be surrounded by blank lines'
                    startLineNumber = $i + 1
                }
            }
        }
    }

    # MD040: 代码块未指定语言（目录树/文件结构）
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match '^```$' -and $i -lt $lines.Count - 1) {
            $nextLine = $lines[$i + 1].Trim()
            if ($nextLine -match '^[/\\├└│]' -or $nextLine -match '^\w+/\w+') {
                $issues += [PSCustomObject]@{
                    resource    = $FilePath
                    code        = @{ value = 'MD040' }
                    message     = 'Fenced code blocks should have a language specified'
                    startLineNumber = $i + 1
                }
            }
        }
    }

    # MD038: 反引号代码内前后空格（排除表格行，避免与 MD056 混淆）
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        # 跳过表格行（以 | 开头和结尾）
        if ($line -match '^\|.*\|$') { continue }
        # 匹配反引号代码内前导或后置空格
        if ($line -match '` [^`]+`' -or $line -match '`[^`]+ `') {
            $issues += [PSCustomObject]@{
                resource    = $FilePath
                code        = @{ value = 'MD038' }
                message     = 'Spaces inside code span elements'
                startLineNumber = $i + 1
            }
        }
    }

    # MD056: 表格列数不匹配
    $inTable = $false
    $expectedCols = 0
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i].Trim()
        if ($line -match '^\|.*\|$') {
            # 分隔行（|---|---|）
            if ($line -match '^\|[\s:-]+\|[\s:-]+\|') {
                $cols = ($line -split '\|' | Where-Object { $_ -match '\S' }).Count
                $expectedCols = $cols
                $inTable = $true
                continue
            }
            if ($inTable) {
                $cols = ($line -split '\|' | Where-Object { $_ -match '\S' }).Count
                if ($cols -ne $expectedCols) {
                    $issues += [PSCustomObject]@{
                        resource    = $FilePath
                        code        = @{ value = 'MD056' }
                        message     = "Table column count [Expected: $expectedCols; Actual: $cols; Too many cells, extra data will be missing]"
                        startLineNumber = $i + 1
                    }
                }
            }
        }
        else {
            $inTable = $false
        }
    }

    # MD012: 连续空行
    $blankCount = 0
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i].Trim() -eq '') {
            $blankCount++
            if ($blankCount -gt 1) {
                $issues += [PSCustomObject]@{
                    resource    = $FilePath
                    code        = @{ value = 'MD012' }
                    message     = 'Multiple consecutive blank lines'
                    startLineNumber = $i + 1
                }
            }
        }
        else {
            $blankCount = 0
        }
    }

    return $issues
}

# ============================================================
# 修复函数
# ============================================================

function Fix-MD041 {
    <#
    .SYNOPSIS
        修复 MD041: 确保文件首行是 H1 标题（跳过 YAML front matter）
    #>
    param(
        [string]$Content,
        [string]$FilePath,
        [switch]$WhatIf
    )

    $lines = Split-Lines $Content
    if ($lines.Count -eq 0) { return New-FixResult -Modified $false -Rule 'MD041' }

    # 跳过 YAML front matter
    $contentStart = 0
    $hasFrontMatter = $false
    if ($lines[0] -match '^---$') {
        $hasFrontMatter = $true
        for ($i = 1; $i -lt $lines.Count; $i++) {
            if ($lines[$i] -match '^---$') {
                $contentStart = $i + 1
                break
            }
        }
    }

    # 如果内容首行已经是 H1，无需修复
    if ($contentStart -lt $lines.Count -and $lines[$contentStart] -match '^# ') {
        return New-FixResult -Modified $false -Rule 'MD041'
    }

    # 查找第一个 H1 标题
    $h1Index = -1
    for ($i = $contentStart; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match '^# ') {
            $h1Index = $i
            break
        }
    }

    if ($h1Index -le $contentStart) { return New-FixResult -Modified $false -Rule 'MD041' }

    # 将 H1 之前的内容（非 front matter）移到 H1 后面
    $frontMatter = if ($hasFrontMatter) { $lines[0..($contentStart - 1)] } else { @() }
    $beforeH1 = $lines[$contentStart..($h1Index - 1)]
    $h1Line = $lines[$h1Index]
    $afterH1 = $lines[($h1Index + 1)..($lines.Count - 1)]

    $newParts = @()
    if ($frontMatter) { $newParts += Join-Lines $frontMatter }
    $newParts += $h1Line
    $newParts += ""
    if ($beforeH1) { $newParts += Join-Lines $beforeH1; $newParts += "" }
    if ($afterH1) { $newParts += Join-Lines $afterH1 }

    $newContent = Join-Lines $newParts

    if ($WhatIf) {
        Write-Host "  [MD041] 将引用块移到标题 '$h1Line' 后面" -ForegroundColor Magenta
    }
    return New-FixResult -Modified $true -Content $newContent -Rule 'MD041' -Message "将引用块移到标题后面"
}

function Fix-MD034 {
    <#
    .SYNOPSIS
        修复 MD034: 裸 URL 加反引号包裹（排除链接语法中的 URL）
    #>
    param(
        [string]$Content,
        [string]$FilePath,
        [switch]$WhatIf
    )

    $modified = $false
    $lines = Split-Lines $Content

    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        # 跳过链接语法行 [text](url) 和图片 ![alt](url)
        if ($line -match '^\s*\[.*\]\(https?://' -or $line -match '^\s*!\[.*\]\(https?://') { continue }
        # 跳过已用反引号包裹的行
        if ($line -match '^\s*`.*https?://.*`') { continue }

        $newLine = $line -replace '(?<!`)(https?://[^\s`"<>)]+)(?!`)', '`$1`'
        if ($newLine -ne $line) {
            $lines[$i] = $newLine
            $modified = $true
        }
    }

    if ($modified) {
        $newContent = Join-Lines $lines
        if ($WhatIf) {
            Write-Host "  [MD034] 裸 URL 已加反引号包裹" -ForegroundColor Magenta
        }
        return New-FixResult -Modified $true -Content $newContent -Rule 'MD034' -Message "裸 URL 已加反引号包裹"
    }

    return New-FixResult -Modified $false -Rule 'MD034'
}

function Fix-MD060 {
    <#
    .SYNOPSIS
        修复 MD060: 统一表格 | 两侧空格
    #>
    param(
        [string]$Content,
        [string]$FilePath,
        [switch]$WhatIf
    )

    $modified = $false
    $lines = Split-Lines $Content

    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match '^\|.*\|$') {
            $original = $lines[$i]
            $cells = $original -split '\|'
            $fixedCells = @()
            for ($j = 0; $j -lt $cells.Length; $j++) {
                if ($j -eq 0 -or $j -eq $cells.Length - 1) {
                    $fixedCells += $cells[$j]
                }
                else {
                    $fixedCells += ' ' + $cells[$j].Trim() + ' '
                }
            }
            $fixed = $fixedCells -join '|'
            if ($fixed -ne $original) {
                $lines[$i] = $fixed
                $modified = $true
            }
        }
    }

    if ($modified) {
        $newContent = Join-Lines $lines
        if ($WhatIf) {
            Write-Host "  [MD060] 表格列样式已统一" -ForegroundColor Magenta
        }
        return New-FixResult -Modified $true -Content $newContent -Rule 'MD060' -Message "表格列样式已统一"
    }

    return New-FixResult -Modified $false -Rule 'MD060'
}

function Fix-MD032 {
    <#
    .SYNOPSIS
        修复 MD032: 列表前后加空行
    #>
    param(
        [string]$Content,
        [string]$FilePath,
        [switch]$WhatIf
    )
    return Add-BlankLinesAround -Content $Content -Pattern '^(- |\* |\d+\. )' -Rule 'MD032' -Message "列表前后已加空行" -WhatIf:$WhatIf
}

function Fix-MD058 {
    <#
    .SYNOPSIS
        修复 MD058: 表格前后加空行
    #>
    param(
        [string]$Content,
        [string]$FilePath,
        [switch]$WhatIf
    )
    return Add-BlankLinesAround -Content $Content -Pattern '^\|.*\|$' -Rule 'MD058' -Message "表格前后已加空行" -WhatIf:$WhatIf
}

function Fix-MD031 {
    <#
    .SYNOPSIS
        修复 MD031: 代码块前后加空行
    #>
    param(
        [string]$Content,
        [string]$FilePath,
        [switch]$WhatIf
    )
    return Add-BlankLinesAround -Content $Content -Pattern '^```' -Rule 'MD031' -Message "代码块前后已加空行" -WhatIf:$WhatIf
}

function Fix-MD040 {
    <#
    .SYNOPSIS
        修复 MD040: 代码块加语言标记
    #>
    param(
        [string]$Content,
        [string]$FilePath,
        [switch]$WhatIf
    )

    $modified = $false
    $lines = Split-Lines $Content

    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match '^```$' -and $i -lt $lines.Count - 1) {
            $nextLine = $lines[$i + 1].Trim()
            # 判断是否是文件结构/目录树
            if ($nextLine -match '^[/\\├└│]' -or $nextLine -match '^\w+/\w+') {
                $lines[$i] = '```text'
                $modified = $true
            }
        }
    }

    if ($modified) {
        $newContent = Join-Lines $lines
        if ($WhatIf) {
            Write-Host "  [MD040] 代码块已添加 text 语言标记" -ForegroundColor Magenta
        }
        return New-FixResult -Modified $true -Content $newContent -Rule 'MD040' -Message "代码块已添加 text 语言标记"
    }

    return New-FixResult -Modified $false -Rule 'MD040'
}

function Fix-MD038 {
    <#
    .SYNOPSIS
        修复 MD038: 去掉反引号代码内前后空格
    #>
    param(
        [string]$Content,
        [string]$FilePath,
        [switch]$WhatIf
    )

    $modified = $false
    $lines = Split-Lines $Content

    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        # 跳过表格行（以 | 开头和结尾）
        if ($line -match '^\|.*\|$') { continue }

        # 修复反引号内前导空格：` text` → `text`
        $newLine = $line -replace '` ([^`]+)`', '`$1`'
        # 修复反引号内后置空格：`text ` → `text`
        $newLine = $newLine -replace '`([^`]+) `', '`$1`'

        if ($newLine -ne $line) {
            $lines[$i] = $newLine
            $modified = $true
        }
    }

    if ($modified) {
        $newContent = Join-Lines $lines
        if ($WhatIf) {
            Write-Host "  [MD038] 反引号代码内空格已去除" -ForegroundColor Magenta
        }
        return New-FixResult -Modified $true -Content $newContent -Rule 'MD038' -Message "反引号代码内空格已去除"
    }

    return New-FixResult -Modified $false -Rule 'MD038'
}

function Fix-MD056 {
    <#
    .SYNOPSIS
        修复 MD056: 表格列数不匹配
    .DESCRIPTION
        将表格单元格中反引号内的 | 替换为 HTML 实体 &#124;，
        避免被 markdownlint 解析器误判为列分隔符。
    #>
    param(
        [string]$Content,
        [string]$FilePath,
        [switch]$WhatIf
    )

    $modified = $false
    $lines = Split-Lines $Content

    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        if ($line -match '^\|.*\|$') {
            # 跳过分隔行
            if ($line -match '^\|[\s:-]+\|[\s:-]+\|') { continue }

            $original = $line
            # 将表格行中反引号内的 | 替换为 &#124;
            $newLine = $line -replace '`([^`]*)\|([^`]*)`', '`$1&#124;$2`'
            if ($newLine -ne $original) {
                $lines[$i] = $newLine
                $modified = $true
            }
        }
    }

    if ($modified) {
        $newContent = Join-Lines $lines
        if ($WhatIf) {
            Write-Host "  [MD056] 表格中反引号内的 | 已替换为 &#124;" -ForegroundColor Magenta
        }
        return New-FixResult -Modified $true -Content $newContent -Rule 'MD056' -Message "表格中反引号内的 | 已替换为 &#124;"
    }

    return New-FixResult -Modified $false -Rule 'MD056'
}

function Fix-MD012 {
    <#
    .SYNOPSIS
        修复 MD012: 将连续空行压缩为单个空行
    #>
    param(
        [string]$Content,
        [string]$FilePath,
        [switch]$WhatIf
    )

    $modified = $false
    $lines = Split-Lines $Content
    $newLines = @()
    $blankCount = 0

    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i].Trim() -eq '') {
            $blankCount++
            if ($blankCount -eq 1) {
                $newLines += $lines[$i]
            }
            else {
                $modified = $true
            }
        }
        else {
            $blankCount = 0
            $newLines += $lines[$i]
        }
    }

    if ($modified) {
        $newContent = Join-Lines $newLines
        if ($WhatIf) {
            Write-Host "  [MD012] 连续空行已压缩" -ForegroundColor Magenta
        }
        return New-FixResult -Modified $true -Content $newContent -Rule 'MD012' -Message "连续空行已压缩"
    }

    return New-FixResult -Modified $false -Rule 'MD012'
}

# ============================================================
# 主入口函数
# ============================================================

function Invoke-MarkdownLintFix {
    <#
    .SYNOPSIS
        修复 Markdown 文件的 markdownlint 格式警告

    .PARAMETER Path
        要修复的文件路径（支持单个文件或目录）

    .PARAMETER Recurse
        是否递归处理目录下的所有 .md 文件

    .PARAMETER WarningsFile
        markdownlint 警告 JSON 文件路径（VS Code 问题面板导出格式）

    .PARAMETER Warnings
        直接传入警告对象数组

    .PARAMETER WhatIf
        预览模式，只显示要修改的内容，不实际写入

    .PARAMETER NoBackup
        是否不创建 .bak 备份文件（默认创建备份）

    .PARAMETER Rules
        要修复的规则列表，默认修复所有支持的规则
        支持：MD041, MD034, MD060, MD032, MD058, MD031, MD040, MD038, MD056, MD012

    .EXAMPLE
        Invoke-MarkdownLintFix -Path "docs/Introduction.md"

    .EXAMPLE
        Invoke-MarkdownLintFix -Path "docs/" -Recurse -WhatIf

    .EXAMPLE
        Invoke-MarkdownLintFix -WarningsFile "warnings.json"
    #>

    [CmdletBinding(DefaultParameterSetName = 'Path')]
    param(
        [Parameter(ParameterSetName = 'Path', Position = 0)]
        [string]$Path,

        [Parameter(ParameterSetName = 'Path')]
        [switch]$Recurse,

        [Parameter(ParameterSetName = 'WarningsFile')]
        [string]$WarningsFile,

        [Parameter(ParameterSetName = 'WarningsObject')]
        [array]$Warnings,

        [Parameter()]
        [switch]$WhatIf,

        [Parameter()]
        [switch]$NoBackup,

        [Parameter()]
        [string[]]$Rules = @('MD041', 'MD034', 'MD060', 'MD032', 'MD058', 'MD031', 'MD040', 'MD038', 'MD056', 'MD012')
    )

    # 收集所有需要修复的警告
    $allWarnings = @()

    if ($PSCmdlet.ParameterSetName -eq 'WarningsFile') {
        if (-not (Test-Path $WarningsFile)) {
            Write-Error "警告文件不存在: $WarningsFile"
            return
        }
        $raw = Get-Content $WarningsFile -Raw | ConvertFrom-Json
        $allWarnings = $raw
    }
    elseif ($PSCmdlet.ParameterSetName -eq 'WarningsObject') {
        $allWarnings = $Warnings
    }
    elseif ($PSCmdlet.ParameterSetName -eq 'Path') {
        if (-not $Path) {
            Write-Error "请指定 -Path 或 -WarningsFile"
            return
        }
        # 扫描文件，自动检测 markdownlint 问题
        $files = @()
        if (Test-Path $Path -PathType Container) {
            $files = Get-ChildItem $Path -Filter '*.md' -Recurse:$Recurse
        }
        else {
            $files = Get-ChildItem $Path
        }

        foreach ($file in $files) {
            Write-Verbose "扫描文件: $($file.FullName)"
            $content = Get-Content $file.FullName -Raw
            $detected = Detect-MarkdownLintIssues -Content $content -FilePath $file.FullName
            $allWarnings += $detected
        }
    }

    if ($allWarnings.Count -eq 0) {
        Write-Host "没有发现需要修复的警告。" -ForegroundColor Green
        return
    }

    # 按文件分组
    $grouped = $allWarnings | Group-Object resource

    $totalFixed = 0
    $totalSkipped = 0

    foreach ($group in $grouped) {
        $filePath = $group.Name
        if (-not (Test-Path $filePath)) {
            Write-Warning "文件不存在，跳过: $filePath"
            $totalSkipped += $group.Count
            continue
        }

        Write-Host "`n处理文件: $filePath" -ForegroundColor Cyan

        # 备份
        if (-not $WhatIf -and -not $NoBackup) {
            $backupPath = "$filePath.bak"
            Copy-Item $filePath $backupPath -Force
            Write-Verbose "已创建备份: $backupPath"
        }

        # 读取文件内容
        $content = Get-Content $filePath -Raw

        # 按规则分组修复
        $ruleGroups = $group.Group | Group-Object { $_.code.value }

        foreach ($ruleGroup in $ruleGroups) {
            $rule = $ruleGroup.Name
            if ($rule -notin $Rules) {
                Write-Verbose "  跳过规则 $rule（未在 -Rules 中指定）"
                continue
            }

            Write-Host "  修复规则 $rule ..." -ForegroundColor Yellow

            $fixResult = $null
            switch ($rule) {
                'MD041' { $fixResult = Fix-MD041 -Content $content -FilePath $filePath -WhatIf:$WhatIf }
                'MD034' { $fixResult = Fix-MD034 -Content $content -FilePath $filePath -WhatIf:$WhatIf }
                'MD060' { $fixResult = Fix-MD060 -Content $content -FilePath $filePath -WhatIf:$WhatIf }
                'MD032' { $fixResult = Fix-MD032 -Content $content -FilePath $filePath -WhatIf:$WhatIf }
                'MD058' { $fixResult = Fix-MD058 -Content $content -FilePath $filePath -WhatIf:$WhatIf }
                'MD031' { $fixResult = Fix-MD031 -Content $content -FilePath $filePath -WhatIf:$WhatIf }
                'MD040' { $fixResult = Fix-MD040 -Content $content -FilePath $filePath -WhatIf:$WhatIf }
                'MD038' { $fixResult = Fix-MD038 -Content $content -FilePath $filePath -WhatIf:$WhatIf }
                'MD056' { $fixResult = Fix-MD056 -Content $content -FilePath $filePath -WhatIf:$WhatIf }
                'MD012' { $fixResult = Fix-MD012 -Content $content -FilePath $filePath -WhatIf:$WhatIf }
                default {
                    Write-Warning "  不支持的规则: $rule"
                    $totalSkipped += $ruleGroup.Count
                    continue
                }
            }

            if ($fixResult -and $fixResult.Modified) {
                $content = $fixResult.Content
                $totalFixed += $ruleGroup.Count
                Write-Host "    ✓ 已修复 $($ruleGroup.Count) 个警告" -ForegroundColor Green
            }
            else {
                $totalSkipped += $ruleGroup.Count
                Write-Host "    - 无需修复或修复失败" -ForegroundColor Gray
            }
        }

        # 写入文件（修复 Bug 1.1：去掉 -NoNewline）
        if (-not $WhatIf) {
            Set-Content $filePath -Value $content -Encoding UTF8
        }
    }

    # 输出统计
    Write-Host "`n========== 修复统计 ==========" -ForegroundColor Cyan
    Write-Host "总计警告: $($allWarnings.Count)" -ForegroundColor White
    Write-Host "已修复: $totalFixed" -ForegroundColor Green
    Write-Host "跳过: $totalSkipped" -ForegroundColor Yellow
    if ($WhatIf) {
        Write-Host "（预览模式，未实际写入文件）" -ForegroundColor Magenta
    }
}

# 导出函数
Export-ModuleMember -Function Invoke-MarkdownLintFix
