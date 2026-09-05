# StringTools.StructuredText.psm1 - 结构化文本解析模块
# 提供 .sln 文件、dotnet list 输出、键值对配置等结构化文本的解析功能

# ============================================
#  .sln 文件解析
# ============================================

<#
.SYNOPSIS
    解析 .sln 解决方案文件
.DESCRIPTION
    将 .sln 文件解析为结构化对象，包含项目列表、虚拟文件夹、嵌套关系、配置信息。
.PARAMETER Path
    .sln 文件路径
.PARAMETER Content
    .sln 文件内容字符串（与 Path 二选一）
.OUTPUTS
    [PSCustomObject] 包含 Projects, Folders, NestedProjects, ConfigLines 等属性
.EXAMPLE
    $sln = ConvertFrom-SlnFile -Path "WorkFlowPrj.sln"
    $sln.Projects | Select-Object Name, Path
#>
function ConvertFrom-SlnFile {
    [CmdletBinding(DefaultParameterSetName = 'Path')]
    param(
        [Parameter(ParameterSetName = 'Path', Position = 0)]
        [string]$Path,

        [Parameter(ParameterSetName = 'Content')]
        [string]$Content
    )
    # 读取内容
    if ($PSCmdlet.ParameterSetName -eq 'Path') {
        if (-not (Test-Path $Path)) {
            Write-Error "文件不存在: $Path"
            return $null
        }
        $Content = Get-Content -Path $Path -Raw
    }

    $lines = $Content -split "`r`n|`n"

    $result = @{
        Projects = @()          # 实际项目
        Folders = @{}           # @{ Guid = @{ Name; Path } }
        NestedProjects = @{}    # @{ ChildGuid = ParentGuid }
        ConfigLines = @()       # 配置行
        HeaderLines = @()       # 文件头
        SolutionFolderGuid = "{2150E333-8FDC-42A3-9474-1A3956D46DE8}"
        CSharpProjectGuid = "{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}"
    }

    $inGlobal = $false
    $inNestedProjects = $false
    $globalLines = @()
    $headerLines = @()

    $projectRegex = [regex]'^Project\("(?<TypeGuid>[^"]+)"\)\s*=\s*"(?<Name>[^"]+)",\s*"(?<Path>[^"]+)",\s*"(?<Guid>[^"]+)"'
    $nestedRegex = [regex]'^\s+\{(?<Child>[^}]+)\}\s*=\s*\{(?<Parent>[^}]+)\}'

    foreach ($line in $lines) {
        if (-not $inGlobal) {
            if ($line -match "^Global$") {
                $inGlobal = $true
                $headerLines += $line
            } else {
                $headerLines += $line
                $projMatch = $projectRegex.Match($line)
                if ($projMatch.Success) {
                    $typeGuid = $projMatch.Groups['TypeGuid'].Value
                    $projGuid = $projMatch.Groups['Guid'].Value
                    $projName = $projMatch.Groups['Name'].Value
                    $projPath = $projMatch.Groups['Path'].Value

                    if ($typeGuid -eq $result.SolutionFolderGuid) {
                        $result.Folders[$projGuid] = @{ Name = $projName; Path = $projPath }
                    } else {
                        $result.Projects += [PSCustomObject]@{
                            Name     = $projName
                            Path     = $projPath
                            Guid     = $projGuid
                            TypeGuid = $typeGuid
                        }
                    }
                }
            }
        } else {
            if ($line -match "^EndGlobal$") { $inGlobal = $false; continue }

            if ($line -match 'GlobalSection\(NestedProjects\)') {
                $inNestedProjects = $true; continue
            }
            if ($inNestedProjects) {
                if ($line -match 'EndGlobalSection') { $inNestedProjects = $false; continue }
                $nestedMatch = $nestedRegex.Match($line)
                if ($nestedMatch.Success) {
                    $childGuid = "{" + $nestedMatch.Groups['Child'].Value + "}"
                    $parentGuid = "{" + $nestedMatch.Groups['Parent'].Value + "}"
                    $result.NestedProjects[$childGuid] = $parentGuid
                }
                continue
            }

            $globalLines += $line
        }
    }

    $result.ConfigLines = $globalLines
    $result.HeaderLines = $headerLines

    return [PSCustomObject]$result
}

<#
.SYNOPSIS
    从 .sln 文件中提取项目 GUID 映射
.DESCRIPTION
    返回项目名称到 GUID 的映射表，便于快速查找。
.PARAMETER SlnObject
    ConvertFrom-SlnFile 返回的对象
.OUTPUTS
    [hashtable] @{ ProjectName = Guid }
#>
function Get-SlnProjectGuidMap {
    param(
        [Parameter(Mandatory = $true)]
        [PSCustomObject]$SlnObject
    )
    $map = @{}
    foreach ($proj in $SlnObject.Projects) {
        $map[$proj.Name] = $proj.Guid
    }
    return $map
}

<#
.SYNOPSIS
    从 .sln 文件中提取项目路径映射
.DESCRIPTION
    返回项目名称到相对路径的映射表。
.PARAMETER SlnObject
    ConvertFrom-SlnFile 返回的对象
.OUTPUTS
    [hashtable] @{ ProjectName = RelativePath }
#>
function Get-SlnProjectPathMap {
    param(
        [Parameter(Mandatory = $true)]
        [PSCustomObject]$SlnObject
    )
    $map = @{}
    foreach ($proj in $SlnObject.Projects) {
        $map[$proj.Name] = $proj.Path
    }
    return $map
}

# ============================================
#  dotnet list 输出解析
# ============================================

<#
.SYNOPSIS
    解析 dotnet list package 命令的输出
.DESCRIPTION
    将 dotnet list package 的表格输出解析为结构化对象数组。
.PARAMETER InputObject
    dotnet list 命令的输出文本（支持管道输入）
.OUTPUTS
    [PSCustomObject[]] 包含 ProjectName, PackageName, RequestedVersion, ResolvedVersion
.EXAMPLE
    dotnet list package --outdated 2>&1 | ConvertFrom-DotnetListOutput
#>
function ConvertFrom-DotnetListOutput {
    [CmdletBinding()]
    param(
        [Parameter(ValueFromPipeline = $true, Position = 0)]
        [string]$InputObject
    )
    process {
        if ([string]::IsNullOrEmpty($InputObject)) { return }

        $lines = $InputObject -split "`r`n|`n"
        $results = @()
        $currentProject = $null
        $inTable = $false
        $headerPassed = $false

        foreach ($line in $lines) {
            $trimmed = $line.Trim()

            # 检测项目标题行
            if ($trimmed -match '^Project\s+`(.+)`') {
                $currentProject = $matches[1]
                $inTable = $false
                $headerPassed = $false
                continue
            }

            # 检测表头
            if ($trimmed -match '^>\s+(Top-level|Transitive)\s+Package') {
                $inTable = $true
                $headerPassed = $false
                continue
            }

            # 检测表头分隔行
            if ($inTable -and $trimmed -match '^-{5,}') {
                $headerPassed = $true
                continue
            }

            # 检测带 > 前缀的分隔行
            if ($inTable -and $trimmed -match '^>\s*-{5,}') {
                $headerPassed = $true
                continue
            }

            # 解析数据行
            if ($inTable -and $headerPassed -and $trimmed -ne '' -and $trimmed -notmatch '^The following') {
                # 格式：> PackageName Requested Resolved
                $parts = $trimmed -split '\s{2,}'
                if ($parts.Count -ge 3) {
                    $results += [PSCustomObject]@{
                        ProjectName     = $currentProject
                        PackageName     = $parts[0].TrimStart('>').Trim()
                        RequestedVersion = $parts[1].Trim()
                        ResolvedVersion  = $parts[2].Trim()
                    }
                }
            }
        }

        return $results
    }
}

# ============================================
#  键值对文本解析
# ============================================

<#
.SYNOPSIS
    解析键值对格式的文本
.DESCRIPTION
    将 "key=value" 格式的文本解析为哈希表，支持注释和空行。
.PARAMETER InputObject
    键值对文本（支持管道输入）
.PARAMETER Delimiter
    键值分隔符（默认："="）
.PARAMETER CommentChar
    注释字符（默认："#"）
.PARAMETER TrimValues
    是否修剪值两端的空白（默认：$true）
.PARAMETER AllowDuplicate
    是否允许重复键（默认：$false，重复时后值覆盖前值）
.OUTPUTS
    [hashtable] 或 [PSCustomObject[]]（AllowDuplicate 时）
.EXAMPLE
    ConvertFrom-KeyValueText -InputObject "name=John`nage=30" -Delimiter "="
    # 返回 @{ name = "John"; age = "30" }
#>
function ConvertFrom-KeyValueText {
    [CmdletBinding()]
    param(
        [Parameter(ValueFromPipeline = $true, Position = 0)]
        [string]$InputObject,

        [Parameter(Position = 1)]
        [string]$Delimiter = "=",

        [Parameter()]
        [string]$CommentChar = "#",

        [Parameter()]
        [switch]$TrimValues = $true,

        [Parameter()]
        [switch]$AllowDuplicate
    )
    process {
        if ([string]::IsNullOrEmpty($InputObject)) { return @{} }

        $lines = $InputObject -split "`r`n|`n"
        $result = if ($AllowDuplicate) { @() } else { @{} }

        foreach ($line in $lines) {
            $trimmed = $line.Trim()

            # 跳过空行和注释
            if ([string]::IsNullOrEmpty($trimmed)) { continue }
            if ($CommentChar -and $trimmed.StartsWith($CommentChar)) { continue }

            # 移除行内注释
            if ($CommentChar) {
                $commentIndex = $trimmed.IndexOf($CommentChar)
                if ($commentIndex -ge 0) {
                    $trimmed = $trimmed.Substring(0, $commentIndex).Trim()
                }
            }

            $delimiterIndex = $trimmed.IndexOf($Delimiter)
            if ($delimiterIndex -lt 0) { continue }

            $key = $trimmed.Substring(0, $delimiterIndex).Trim()
            $value = $trimmed.Substring($delimiterIndex + 1)
            if ($TrimValues) { $value = $value.Trim() }

            if ($AllowDuplicate) {
                $result += [PSCustomObject]@{ Key = $key; Value = $value }
            } else {
                $result[$key] = $value
            }
        }

        return $result
    }
}

# ============================================
#  导出公共函数
# ============================================

Export-ModuleMember -Function ConvertFrom-SlnFile,
                              Get-SlnProjectGuidMap,
                              Get-SlnProjectPathMap,
                              ConvertFrom-DotnetListOutput,
                              ConvertFrom-KeyValueText
