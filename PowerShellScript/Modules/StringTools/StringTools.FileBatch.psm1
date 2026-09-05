# StringTools.FileBatch.psm1 - 文件批量字符串处理模块
# 提供文件级别的批量替换、提取、变换操作
# 设计原则：配置驱动、管道兼容、安全备份
#
# ★ 依赖：Core\FileSystemHelpers.psm1（Backup-File）
#          Core\OutputHelpers.psm1（Write-Success, Write-Warning, Write-Error 等）

# ============================================
#  内部辅助函数
# ============================================

<#
.SYNOPSIS
    获取文件列表（支持排除模式）
.DESCRIPTION
    递归获取指定目录下的文件列表，支持包含和排除模式。
.PARAMETER Path
    根目录路径
.PARAMETER Include
    包含的文件模式（如 "*.cs", "*.ps1"），默认所有文件
.PARAMETER Exclude
    排除的文件模式（如 "*AssemblyInfo*"）
.PARAMETER ExcludeDir
    排除的目录名（如 ".git", "node_modules", "bin", "obj"）
.PARAMETER Recurse
    是否递归子目录（默认：$true）
.OUTPUTS
    [System.IO.FileInfo[]]
#>
function Get-FileList {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true, Position = 0)]
        [string]$Path,

        [Parameter()]
        [string[]]$Include = @("*"),

        [Parameter()]
        [string[]]$Exclude,

        [Parameter()]
        [string[]]$ExcludeDir = @('.git', 'node_modules', 'bin', 'obj', 'BuildOutput', '.vs', 'backups'),

        [Parameter()]
        [switch]$Recurse = $true
    )
    $files = @()

    foreach ($pattern in $Include) {
        $found = Get-ChildItem -Path $Path -Filter $pattern -Recurse:$Recurse -File -ErrorAction SilentlyContinue
        $files += $found
    }

    # 排除目录过滤
    if ($ExcludeDir) {
        $files = $files | Where-Object {
            $fullPath = $_.FullName
            $shouldExclude = $false
            foreach ($exDir in $ExcludeDir) {
                if ($fullPath -match [regex]::Escape("\$exDir\") -or $fullPath -match [regex]::Escape("/$exDir/")) {
                    $shouldExclude = $true
                    break
                }
            }
            -not $shouldExclude
        }
    }

    # 排除文件模式
    if ($Exclude) {
        $files = $files | Where-Object {
            $name = $_.Name
            $shouldExclude = $false
            foreach ($exPattern in $Exclude) {
                if ($name -like $exPattern) {
                    $shouldExclude = $true
                    break
                }
            }
            -not $shouldExclude
        }
    }

    return $files | Sort-Object FullName -Unique
}

<#
.SYNOPSIS
    备份文件到指定目录（委托给 Core\FileSystemHelpers\Backup-File）
.DESCRIPTION
    创建带时间戳的文件备份，返回备份路径。
    ★ 优化：内部委托给 Core\FileSystemHelpers.psm1 的 Backup-File 函数，
      消除重复实现。保持向后兼容的 API 签名。
.PARAMETER FilePath
    要备份的文件路径
.PARAMETER BackupDir
    备份目录（默认：目标文件所在目录下的 backups 子目录）
.OUTPUTS
    [string] 备份文件路径
#>
function Backup-FileWithTimestamp {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath,

        [Parameter()]
        [string]$BackupDir
    )
    # ★ 委托给 Core 库的 Backup-File（增强版已支持自定义备份目录）
    return Backup-File -filePath $FilePath -backupDir $BackupDir
}

# ============================================
#  文件内容替换
# ============================================

<#
.SYNOPSIS
    批量替换文件内容
.DESCRIPTION
    对指定文件列表执行内容替换操作，支持简单替换和正则替换。
    自动备份原文件，支持预览模式。
.PARAMETER Path
    要处理的文件或目录路径
.PARAMETER Files
    要处理的文件列表（与 Path 二选一）
.PARAMETER OldValue
    要替换的旧字符串（简单替换模式）
.PARAMETER NewValue
    替换后的新字符串（简单替换模式）
.PARAMETER Pattern
    正则表达式模式（正则替换模式）
.PARAMETER Replacement
    正则替换字符串（正则替换模式）
.PARAMETER Rules
    替换规则数组，每个规则支持：
    - Type: "simple" 或 "regex"
    - Old/New (simple 模式) 或 Pattern/Replacement (regex 模式)
    - SkipLines: 可选，要跳过的行前缀
.PARAMETER ConfigPath
    JSON 配置文件路径（包含 rules 数组）
.PARAMETER FilePattern
    文件匹配模式（如 "*.cs", "*.ps1"），默认所有文件
.PARAMETER ExcludePattern
    排除的文件模式
.PARAMETER ExcludeDir
    排除的目录名
.PARAMETER Backup
    是否备份原文件（默认：$true）
.PARAMETER BackupDir
    备份目录（默认：各文件所在目录下的 backups 子目录）
.PARAMETER WhatIf
    预览模式，只显示将要修改的文件和行数，不实际修改
.PARAMETER Verbose
    详细输出模式（由 [CmdletBinding()] 自动提供）
.OUTPUTS
    [PSCustomObject[]] 包含 FilePath, Modified, Changes 的处理结果
.EXAMPLE
    # 简单替换：将文件中的 "WorkflowContext" 替换为 "PipelineContext"
    Invoke-FileReplace -Path "src/" -OldValue "WorkflowContext" -NewValue "PipelineContext" -FilePattern "*.cs"

.EXAMPLE
    # 正则替换：使用正则模式替换
    Invoke-FileReplace -Path "src/" -Pattern "(?<![a-zA-Z.])OldName(?![a-zA-Z])" -Replacement "NewName" -FilePattern "*.cs"

.EXAMPLE
    # 多规则替换
    Invoke-FileReplace -Path "src/" -Rules @(
        @{ Type = "simple"; Old = "A"; New = "B" },
        @{ Type = "regex"; Pattern = "\d+\.\d+"; Replacement = "0.0" }
    ) -FilePattern "*.cs"

.EXAMPLE
    # 使用 JSON 配置文件
    Invoke-FileReplace -Path "src/" -ConfigPath "replace-rules.json"

.EXAMPLE
    # 预览模式（不实际修改）
    Invoke-FileReplace -Path "src/" -OldValue "Old" -NewValue "New" -WhatIf
#>
# 内部辅助：对单行应用替换规则（支持 SkipLines）
function Invoke-ReplaceLineWithSkip {
    param(
        [string]$Line,
        [string]$Type,
        [string]$Old,
        [string]$New,
        [string]$Pattern,
        [string]$Replacement,
        [string[]]$SkipLines
    )
    # 检查是否需要跳过
    if ($SkipLines -and $SkipLines.Count -gt 0) {
        foreach ($prefix in $SkipLines) {
            if ($Line.TrimStart().StartsWith($prefix)) {
                return $Line, $false  # 返回原行，未修改
            }
        }
    }

    if ($Type -eq 'simple') {
        $replaced = $Line -replace [regex]::Escape($Old), $New
    } else {
        $replaced = $Line -replace $Pattern, $Replacement
    }
    return $replaced, ($replaced -ne $Line)
}

# 内部辅助：对文本内容应用一条替换规则，返回 (新内容, 替换次数)
function Invoke-ReplaceRuleOnContent {
    param(
        [string]$Content,
        [object]$Rule
    )
    $type = if ($Rule.Type) { $Rule.Type } else { 'simple' }
    $skipLines = if ($Rule.SkipLines) { $Rule.SkipLines } else { @() }

    if ($type -eq 'simple') {
        $old = $Rule.Old
        $new = $Rule.New
        if ($skipLines.Count -gt 0) {
            $lines = $Content -split "`r`n|`n"
            $newLines = @()
            $count = 0
            foreach ($line in $lines) {
                $replacedLine, $changed = Invoke-ReplaceLineWithSkip -Line $line -Type 'simple' -Old $old -New $new -SkipLines $skipLines
                $newLines += $replacedLine
                if ($changed) { $count++ }
            }
            return ($newLines -join "`r`n"), $count
        } else {
            $replaced = $Content -replace [regex]::Escape($old), $new
            if ($replaced -ne $Content) {
                $count = [regex]::Matches($Content, [regex]::Escape($old)).Count
                return $replaced, $count
            }
            return $Content, 0
        }
    }
    elseif ($type -eq 'regex') {
        $pattern = $Rule.Pattern
        $replacement = $Rule.Replacement
        if ($skipLines.Count -gt 0) {
            $lines = $Content -split "`r`n|`n"
            $newLines = @()
            $count = 0
            foreach ($line in $lines) {
                $replacedLine, $changed = Invoke-ReplaceLineWithSkip -Line $line -Type 'regex' -Pattern $pattern -Replacement $replacement -SkipLines $skipLines
                $newLines += $replacedLine
                if ($changed) { $count++ }
            }
            return ($newLines -join "`r`n"), $count
        } else {
            $replaced = $Content -replace $pattern, $replacement
            if ($replaced -ne $Content) {
                $count = [regex]::Matches($Content, $pattern).Count
                return $replaced, $count
            }
            return $Content, 0
        }
    }
    return $Content, 0
}

function Invoke-FileReplace {
    [CmdletBinding(DefaultParameterSetName = 'Simple')]
    param(
        [Parameter(ParameterSetName = 'Simple', Position = 0)]
        [Parameter(ParameterSetName = 'Regex')]
        [Parameter(ParameterSetName = 'Multi')]
        [Parameter(ParameterSetName = 'Config')]
        [string]$Path,

        [Parameter(ParameterSetName = 'Simple')]
        [Parameter(ParameterSetName = 'Regex')]
        [Parameter(ParameterSetName = 'Multi')]
        [Parameter(ParameterSetName = 'Config')]
        [System.IO.FileInfo[]]$Files,

        # 简单替换参数
        [Parameter(ParameterSetName = 'Simple', Mandatory = $true)]
        [string]$OldValue,

        [Parameter(ParameterSetName = 'Simple', Mandatory = $true)]
        [string]$NewValue,

        # 正则替换参数
        [Parameter(ParameterSetName = 'Regex', Mandatory = $true)]
        [string]$Pattern,

        [Parameter(ParameterSetName = 'Regex', Mandatory = $true)]
        [string]$Replacement,

        # 多规则参数
        [Parameter(ParameterSetName = 'Multi', Mandatory = $true)]
        [object[]]$Rules,

        # 配置文件参数
        [Parameter(ParameterSetName = 'Config', Mandatory = $true)]
        [string]$ConfigPath,

        # 通用参数
        [Parameter()]
        [string[]]$FilePattern = @("*"),

        [Parameter()]
        [string[]]$ExcludePattern,

        [Parameter()]
        [string[]]$ExcludeDir = @('.git', 'node_modules', 'bin', 'obj', 'BuildOutput', '.vs', 'backups'),

        [Parameter()]
        [switch]$Backup = $true,

        [Parameter()]
        [string]$BackupDir,

        [Parameter()]
        [switch]$WhatIf
    )
    begin {
        # 从配置文件加载规则
        if ($PSCmdlet.ParameterSetName -eq 'Config') {
            if (-not (Test-Path $ConfigPath)) {
                Write-Error "配置文件不存在: $ConfigPath"
                return
            }
            $config = Get-Content $ConfigPath -Raw | ConvertFrom-Json
            $Rules = $config.rules
            if (-not $Rules -or $Rules.Count -eq 0) {
                Write-Warning "配置文件中没有定义规则"
                return
            }
        }

        # 获取文件列表
        $fileList = if ($Files) {
            $Files
        } elseif ($Path) {
            Get-FileList -Path $Path -Include $FilePattern -Exclude $ExcludePattern -ExcludeDir $ExcludeDir
        } else {
            Write-Error "请指定 Path 或 Files 参数"
            return
        }

        $totalFiles = $fileList.Count
        $processedCount = 0
        $modifiedCount = 0
        $totalChanges = 0
    }
    process {
        foreach ($file in $fileList) {
            $filePath = if ($file -is [System.IO.FileInfo]) { $file.FullName } else { $file.ToString() }

            if (-not (Test-Path $filePath)) {
                Write-Warning "文件不存在: $filePath"
                continue
            }

            $content = Get-Content -Path $filePath -Raw
            if ([string]::IsNullOrEmpty($content)) { continue }

            $changeCount = 0
            $newContent = $content

            # 应用替换规则
            if ($PSCmdlet.ParameterSetName -eq 'Simple') {
                $newContent = $content -replace [regex]::Escape($OldValue), $NewValue
                if ($newContent -ne $content) {
                    $changeCount = [regex]::Matches($content, [regex]::Escape($OldValue)).Count
                }
            }
            elseif ($PSCmdlet.ParameterSetName -eq 'Regex') {
                $newContent = $content -replace $Pattern, $Replacement
                if ($newContent -ne $content) {
                    $changeCount = [regex]::Matches($content, $Pattern).Count
                }
            }
            elseif ($PSCmdlet.ParameterSetName -eq 'Multi' -or $PSCmdlet.ParameterSetName -eq 'Config') {
                $newContent = $content
                $totalRuleChanges = 0
                foreach ($rule in $Rules) {
                    $newContent, $ruleChanges = Invoke-ReplaceRuleOnContent -Content $newContent -Rule $rule
                    $totalRuleChanges += $ruleChanges
                }
                $changeCount = $totalRuleChanges
            }

            # 输出结果
            if ($changeCount -gt 0) {
                $modifiedCount++
                $totalChanges += $changeCount

                $result = [PSCustomObject]@{
                    FilePath   = $filePath
                    Modified   = $true
                    Changes    = $changeCount
                    BackupPath = $null
                }

                if (-not $WhatIf) {
                    # 备份
                    if ($Backup) {
                        $result.BackupPath = Backup-FileWithTimestamp -FilePath $filePath -BackupDir $BackupDir
                    }
                    # 写回文件
                    [System.IO.File]::WriteAllText($filePath, $newContent, [System.Text.UTF8Encoding]::new($false))
                }

                Write-Output $result
            }

            $processedCount++
            if ($VerbosePreference -ne 'SilentlyContinue' -and $processedCount % 10 -eq 0) {
                Write-Progress -Activity "文件替换处理" -Status "已处理 $processedCount / $totalFiles 个文件" -PercentComplete ($processedCount / $totalFiles * 100)
            }
        }
    }
    end {
        if ($WhatIf) {
            Write-Warning "[预览] 将修改 $modifiedCount / $totalFiles 个文件，共 $totalChanges 处变更"
        } else {
            Write-Success "已修改 $modifiedCount / $totalFiles 个文件，共 $totalChanges 处变更"
        }
    }
}

# ============================================
#  文件内容提取
# ============================================

<#
.SYNOPSIS
    从文件中批量提取正则匹配内容
.DESCRIPTION
    遍历文件列表，从每个文件中提取正则匹配的内容。
.PARAMETER Path
    要处理的目录路径
.PARAMETER Files
    要处理的文件列表（与 Path 二选一）
.PARAMETER Pattern
    正则表达式模式
.PARAMETER GroupName
    要提取的捕获组名称或索引
.PARAMETER FilePattern
    文件匹配模式
.PARAMETER Unique
    是否去重（默认：$false）
.PARAMETER GroupByFile
    是否按文件分组返回（默认：$false）
.OUTPUTS
    [string[]] 或 [hashtable]（GroupByFile 时）
.EXAMPLE
    Invoke-FileRegexExtract -Path "src/" -Pattern "<Version>(.*?)</Version>" -GroupName 1 -FilePattern "*.csproj"
#>
function Invoke-FileRegexExtract {
    [CmdletBinding()]
    param(
        [Parameter(Position = 0)]
        [string]$Path,

        [Parameter()]
        [System.IO.FileInfo[]]$Files,

        [Parameter(Mandatory = $true, Position = 1)]
        [string]$Pattern,

        [Parameter()]
        [object]$GroupName,

        [Parameter()]
        [string[]]$FilePattern = @("*"),

        [Parameter()]
        [switch]$Unique,

        [Parameter()]
        [switch]$GroupByFile
    )
    # 获取文件列表
    $fileList = if ($Files) {
        $Files
    } elseif ($Path) {
        Get-FileList -Path $Path -Include $FilePattern
    } else {
        Write-Error "请指定 Path 或 Files 参数"
        return
    }

    $result = if ($GroupByFile) { @{} } else { @() }

    foreach ($file in $fileList) {
        $filePath = if ($file -is [System.IO.FileInfo]) { $file.FullName } else { $file.ToString() }
        if (-not (Test-Path $filePath)) { continue }

        $content = Get-Content -Path $filePath -Raw
        if ([string]::IsNullOrEmpty($content)) { continue }

        $matches = [regex]::Matches($content, $Pattern)
        $fileResults = @()

        foreach ($match in $matches) {
            $value = if ($GroupName -ne $null) {
                $match.Groups[$GroupName].Value
            } else {
                $match.Value
            }
            $fileResults += $value
        }

        if ($GroupByFile) {
            $result[$filePath] = $fileResults
        } else {
            $result += $fileResults
        }
    }

    if ($Unique -and -not $GroupByFile) {
        $result = $result | Select-Object -Unique
    }

    return $result
}

# ============================================
#  文件变换管道
# ============================================

<#
.SYNOPSIS
    文件内容变换管道
.DESCRIPTION
    读取文件内容，通过 ScriptBlock 变换后写回。
    支持预览模式和备份。
.PARAMETER Path
    要处理的目录路径
.PARAMETER Files
    要处理的文件列表
.PARAMETER ScriptBlock
    变换脚本块，接收文件内容字符串，返回变换后的字符串
.PARAMETER FilePattern
    文件匹配模式
.PARAMETER Backup
    是否备份（默认：$true）
.PARAMETER WhatIf
    预览模式
.OUTPUTS
    [PSCustomObject[]] 处理结果
.EXAMPLE
    # 将所有文件中的制表符替换为4个空格
    Invoke-FileTransform -Path "src/" -ScriptBlock { param($content) $content -replace "`t", "    " } -FilePattern "*.cs"
#>
function Invoke-FileTransform {
    [CmdletBinding()]
    param(
        [Parameter(Position = 0)]
        [string]$Path,

        [Parameter()]
        [System.IO.FileInfo[]]$Files,

        [Parameter(Mandatory = $true)]
        [scriptblock]$ScriptBlock,

        [Parameter()]
        [string[]]$FilePattern = @("*"),

        [Parameter()]
        [switch]$Backup = $true,

        [Parameter()]
        [switch]$WhatIf
    )
    # 获取文件列表
    $fileList = if ($Files) {
        $Files
    } elseif ($Path) {
        Get-FileList -Path $Path -Include $FilePattern
    } else {
        Write-Error "请指定 Path 或 Files 参数"
        return
    }

    $results = @()

    foreach ($file in $fileList) {
        $filePath = if ($file -is [System.IO.FileInfo]) { $file.FullName } else { $file.ToString() }
        if (-not (Test-Path $filePath)) { continue }

        $content = Get-Content -Path $filePath -Raw
        if ([string]::IsNullOrEmpty($content)) { continue }

        $newContent = & $ScriptBlock $content

        if ($newContent -ne $content) {
            $result = [PSCustomObject]@{
                FilePath   = $filePath
                Modified   = $true
                BackupPath = $null
            }

            if (-not $WhatIf) {
                if ($Backup) {
                    $result.BackupPath = Backup-FileWithTimestamp -FilePath $filePath
                }
                [System.IO.File]::WriteAllText($filePath, $newContent, [System.Text.UTF8Encoding]::new($false))
            }

            $results += $result
        }
    }

    return $results
}

# ============================================
#  导出公共函数
# ============================================

Export-ModuleMember -Function Get-FileList,
                              Backup-FileWithTimestamp,
                              Invoke-FileReplace,
                              Invoke-FileRegexExtract,
                              Invoke-FileTransform
