# XmlHelpers.psm1 - XML 操作工具模块
# 提供 XML 文件的解析、检测、元素移除、值替换、元素插入等通用功能
# 适用于 .csproj / .config 等 MSBuild XML 格式文件

# XML 命名空间常量
$script:MsbuildNs = @{default = 'http://schemas.microsoft.com/developer/msbuild/2003'}

<#
.SYNOPSIS
    使用 XML 解析读取文件，返回 xml 对象和原始内容
.DESCRIPTION
    尝试用 XmlDocument 解析文件内容，解析失败时返回 $null 和原始内容
.PARAMETER filePath
    要读取的文件路径
.OUTPUTS
    [System.Xml.XmlDocument, string] 返回 xml 对象和原始内容
#>
function Read-XmlFile {
    param([string]$filePath)
    $content = Get-Content -Path $filePath -Raw
    try {
        $xml = New-Object System.Xml.XmlDocument
        $xml.PreserveWhitespace = $true
        $xml.LoadXml($content)
        return $xml, $content
    } catch {
        return $null, $content
    }
}

<#
.SYNOPSIS
    检查字符串是否包含有效的 XML 元素（排除注释中的匹配）
.DESCRIPTION
    先尝试 XML 解析，失败后回退到逐行检测（排除注释）
.PARAMETER content
    XML 文本内容
.PARAMETER elementName
    要检测的元素名称（不含命名空间前缀）
.OUTPUTS
    [bool] 存在返回 $true，否则 $false
#>
function Test-XmlElementExists {
    param([string]$content, [string]$elementName)
    try {
        [xml]$xml = $content
        return ($xml.SelectNodes("//default:$elementName", $script:MsbuildNs).Count -gt 0)
    } catch {
        # XML 解析失败时，逐行检测（排除注释）
        return (Test-ContentHasElement -content $content -elementName $elementName)
    }
}

<#
.SYNOPSIS
    在文本内容中检测 XML 元素（排除注释）
.DESCRIPTION
    逐行扫描文本，跳过 XML 注释块，检测指定元素是否存在
.PARAMETER content
    XML 文本内容
.PARAMETER elementName
    要检测的元素名称
.OUTPUTS
    [bool] 存在返回 $true，否则 $false
#>
function Test-ContentHasElement {
    param([string]$content, [string]$elementName)
    $lines = $content -split "`r`n|`n"
    $inComment = $false
    foreach ($line in $lines) {
        if ($inComment) {
            if ($line -match '-->') { $inComment = $false }
            continue
        }
        if ($line -match '<!--') {
            if ($line -notmatch '-->') { $inComment = $true }
            continue
        }
        if ($line -match "<$elementName>") {
            return $true
        }
    }
    return $false
}

<#
.SYNOPSIS
    从文本内容中移除指定 XML 元素的行（排除注释中的匹配）
.DESCRIPTION
    支持单行和多行 XML 元素格式，自动跳过注释内容
    例如：<OutputPath>bin\Debug\</OutputPath>（单行）
          <OutputPath>\n  bin\Debug\\n</OutputPath>（多行）
.PARAMETER content
    XML 文本内容
.PARAMETER elementName
    要移除的元素名称
.OUTPUTS
    [string] 移除指定元素后的文本内容
#>
function Remove-XmlElementLines {
    param([string]$content, [string]$elementName)
    $lines = $content -split "`r`n|`n"
    $newLines = @()
    $inComment = $false
    $skipNextContent = $false  # 标记是否在跳过元素内容
    foreach ($line in $lines) {
        if ($inComment) {
            if ($line -match '-->') { $inComment = $false }
            $newLines += $line
            continue
        }
        if ($line -match '<!--') {
            if ($line -notmatch '-->') { $inComment = $true }
            $newLines += $line
            continue
        }
        # 处理多行元素：如果上一行是开始标签且没有闭合，跳过中间内容直到结束标签
        if ($skipNextContent) {
            if ($line -match "</$elementName>") {
                $skipNextContent = $false
            }
            continue
        }
        # 检查是否在同一行内闭合（如 <OutputPath>bin\Debug\</OutputPath>）
        if ($line -match "<$elementName>[^<]*</$elementName>") {
            continue
        }
        # 检查单独的开始标签（多行元素）
        if ($line -match "<$elementName>") {
            if ($line -match "</$elementName>") {
                # 同一行内闭合，已在上面的条件处理
                continue
            } else {
                # 多行元素，跳过此行并标记
                $skipNextContent = $true
                continue
            }
        }
        # 检查单独的结束标签
        if ($line -match "</$elementName>") {
            continue
        }
        $newLines += $line
    }
    return $newLines -join "`r`n"
}

<#
.SYNOPSIS
    获取 XML 命名空间管理器
.DESCRIPTION
    为 MSBuild XML 文档创建命名空间管理器
.PARAMETER xml
    XmlDocument 对象
.OUTPUTS
    [System.Xml.XmlNamespaceManager]
#>
function Get-MsbuildNamespaceManager {
    param([System.Xml.XmlDocument]$xml)
    $nsmgr = New-Object System.Xml.XmlNamespaceManager($xml.NameTable)
    $nsmgr.AddNamespace("default", "http://schemas.microsoft.com/developer/msbuild/2003")
    return $nsmgr
}

<#
.SYNOPSIS
    在 XML 文档中选择 MSBuild 命名空间下的节点
.PARAMETER xml
    XmlDocument 对象
.PARAMETER xpath
    XPath 表达式（元素名不需要加命名空间前缀）
.OUTPUTS
    [System.Xml.XmlNodeList]
#>
function Select-MsbuildNodes {
    param(
        [System.Xml.XmlDocument]$xml,
        [string]$xpath
    )
    $nsmgr = Get-MsbuildNamespaceManager -xml $xml
    # 自动为无前缀的元素名添加 default 命名空间前缀
    # 只替换路径轴中的元素名，跳过谓词（[...]）和函数调用中的内容
    $prefixedXpath = $xpath -replace '(?<!\[)(/(\w[\w-]*))(?![^\[]*\])', '/default:$2'
    if ($prefixedXpath -notmatch '^(//|/)') {
        $prefixedXpath = "//default:$prefixedXpath"
    }
    # 使用 Select-Xml cmdlet 来支持命名空间
    $result = Select-Xml -Xml $xml -XPath $prefixedXpath -Namespace $script:MsbuildNs
    return $result | ForEach-Object { $_.Node }
}

<#
.SYNOPSIS
    更新 XML 元素的值（使用正则替换）
.DESCRIPTION
    在 XML 文本内容中，将指定元素的旧值替换为新值。
    支持 MSBuild 命名空间格式的 XML。
    当 XML 解析成功时使用 XmlDocument 操作，失败时回退到正则替换。
.PARAMETER content
    XML 文本内容
.PARAMETER elementName
    要更新的元素名称
.PARAMETER oldValue
    旧的元素值
.PARAMETER newValue
    新的元素值
.OUTPUTS
    [string] 更新后的 XML 文本内容
.EXAMPLE
    Update-XmlElementValue -content $xmlContent -elementName "TargetFramework" -oldValue "net10.0-windows7.0" -newValue "net10.0-windows"
#>
function Update-XmlElementValue {
    param(
        [string]$content,
        [string]$elementName,
        [string]$oldValue,
        [string]$newValue
    )
    $escapedOld = [regex]::Escape($oldValue)
    $pattern = "<$elementName>$escapedOld</$elementName>"
    $replacement = "<$elementName>$newValue</$elementName>"
    return $content -replace $pattern, $replacement
}

<#
.SYNOPSIS
    在指定 XML 兄弟元素后插入新元素
.DESCRIPTION
    在 XmlDocument 中，在指定名称的兄弟元素后插入一个新元素。
    常用于在 TargetFramework 节点后补充 UseWindowsForms 等配置。
.PARAMETER xml
    XmlDocument 对象
.PARAMETER afterElementName
    参考元素名称（新元素将插入到该元素之后）
.PARAMETER newElementName
    要创建的新元素名称
.PARAMETER newElementValue
    新元素的文本值
.PARAMETER namespaceUri
    元素的命名空间 URI（可选，默认为 MSBuild 命名空间）
.OUTPUTS
    [bool] 插入成功返回 $true，未找到参考元素返回 $false
.EXAMPLE
    Add-XmlElementAfterSibling -xml $xml -afterElementName "TargetFramework" -newElementName "UseWindowsForms" -newElementValue "true"
#>
function Add-XmlElementAfterSibling {
    param(
        [System.Xml.XmlDocument]$xml,
        [string]$afterElementName,
        [string]$newElementName,
        [string]$newElementValue,
        [string]$namespaceUri = "http://schemas.microsoft.com/developer/msbuild/2003"
    )
    $nsmgr = Get-MsbuildNamespaceManager -xml $xml
    $propertyGroups = $xml.SelectNodes("//default:PropertyGroup", $nsmgr)
    foreach ($pg in $propertyGroups) {
        $refNode = $pg.SelectSingleNode("default:$afterElementName", $nsmgr)
        if ($refNode -ne $null) {
            $newNode = $xml.CreateElement($newElementName, $namespaceUri)
            $newNode.InnerText = $newElementValue
            [void]$pg.InsertAfter($newNode, $refNode)
            return $true
        }
    }
    return $false
}

<#
.SYNOPSIS
    分析 .csproj 中指定属性的配置情况
.DESCRIPTION
    遍历所有 .csproj 文件，分析指定属性的配置情况。
    返回包含缺失、单值、多值、带条件等分类统计信息的哈希表。
    这是 Get-TargetFrameworkConfiguration 的通用化版本。
.PARAMETER csprojFiles
    csproj 文件列表（System.IO.FileInfo[]）
.PARAMETER propertyName
    要分析的属性名称（如 "TargetFramework"、"OutputType" 等）
.PARAMETER multiPropertyName
    多值属性的名称（如 "TargetFrameworks"），可选
.OUTPUTS
    [hashtable] 包含 missing, hasValue, hasMultiValue, hasConditional 等分类的哈希表
.EXAMPLE
    Get-CsprojPropertyValues -csprojFiles $files -propertyName "TargetFramework" -multiPropertyName "TargetFrameworks"
#>
function Get-CsprojPropertyValues {
    <#
    .SYNOPSIS
        分析 .csproj 中指定属性的配置情况
    .DESCRIPTION
        支持两种调用方式：
        1. 批量分析：传入文件列表和属性名，返回分类统计
        2. 单文件分析：传入单个文件路径，返回该文件的属性值哈希表
    .PARAMETER csprojFiles
        csproj 文件列表（System.IO.FileInfo[]）
    .PARAMETER csprojPath
        单个 csproj 文件路径（string），与 csprojFiles 二选一
    .PARAMETER propertyName
        要分析的属性名称（如 "TargetFramework"、"OutputType" 等）
    .PARAMETER multiPropertyName
        多值属性的名称（如 "TargetFrameworks"），可选
    .OUTPUTS
        批量模式：[hashtable] 包含 missing, hasValue, hasMultiValue, hasConditional, values
        单文件模式：[hashtable] 包含 TargetFramework, TargetFrameworks, TargetFramework_Conditional 等
    #>
    param(
        [Parameter(ParameterSetName='Batch')]
        [System.IO.FileInfo[]]$csprojFiles,

        [Parameter(ParameterSetName='Single')]
        [string]$csprojPath,

        [Parameter(Mandatory=$false)]
        [string]$propertyName = "TargetFramework",

        [Parameter(Mandatory=$false)]
        [string]$multiPropertyName = "TargetFrameworks"
    )

    # ★ 单文件模式：返回该文件的属性值哈希表
    if ($PSCmdlet.ParameterSetName -eq 'Single') {
        if (-not (Test-Path $csprojPath)) {
            Write-Warning "文件不存在: $csprojPath"
            return $null
        }
        $content = Get-Content -Path $csprojPath -Raw
        $result = @{
            TargetFramework = $null
            TargetFrameworks = $null
            TargetFramework_Conditional = $null
            OutputType = $null
            UseWindowsForms = $null
        }

        # 提取 TargetFramework（非条件性的）
        if ($content -match '<TargetFramework>(.*?)</TargetFramework>') {
            $result.TargetFramework = $Matches[1]
        }
        # 提取 TargetFrameworks（多框架）
        if ($content -match '<TargetFrameworks>(.*?)</TargetFrameworks>') {
            $result.TargetFrameworks = $Matches[1]
        }
        # 检测条件性 TargetFramework
        if ($content -match '<TargetFramework\s+Condition=') {
            $result.TargetFramework_Conditional = 'true'
        }
        # 提取 OutputType
        if ($content -match '<OutputType>(.*?)</OutputType>') {
            $result.OutputType = $Matches[1]
        }
        # 提取 UseWindowsForms
        if ($content -match '<UseWindowsForms>(.*?)</UseWindowsForms>') {
            $result.UseWindowsForms = $Matches[1]
        }

        return $result
    }

    # ★ 批量模式：返回分类统计
    $result = @{
        missing = @()
        hasValue = @()
        hasMultiValue = @()
        hasConditional = @()
        values = @{}  # 属性值 -> 文件列表的映射
    }

    foreach ($csproj in $csprojFiles) {
        $content = Get-Content -Path $csproj.FullName -Raw

        $hasMulti = $false
        $hasSingle = $false
        $hasConditional = $false

        if ($multiPropertyName -and ($content -match "<$multiPropertyName>")) {
            $hasMulti = $true
            # 提取多属性值
            if ($content -match "<$multiPropertyName>(.*?)</$multiPropertyName>") {
                $val = $Matches[1]
                if (-not $result.values.ContainsKey($val)) {
                    $result.values[$val] = @()
                }
                $result.values[$val] += $csproj
            }
        }

        if ($content -match "<$propertyName\s+Condition=") {
            $hasConditional = $true
        }

        if ($content -match "<$propertyName>") {
            $hasSingle = $true
            # 提取属性值
            if ($content -match "<$propertyName>(.*?)</$propertyName>") {
                $val = $Matches[1]
                if (-not $result.values.ContainsKey($val)) {
                    $result.values[$val] = @()
                }
                $result.values[$val] += $csproj
            }
        }

        if ($hasMulti) {
            $result.hasMultiValue += $csproj
        } elseif ($hasSingle) {
            $result.hasValue += $csproj
            if ($hasConditional) {
                $result.hasConditional += $csproj
            }
        } else {
            $result.missing += $csproj
        }
    }

    return $result
}

# 导出公共函数
Export-ModuleMember -Function Read-XmlFile,
                              Test-XmlElementExists,
                              Test-ContentHasElement,
                              Remove-XmlElementLines,
                              Get-MsbuildNamespaceManager,
                              Select-MsbuildNodes,
                              Update-XmlElementValue,
                              Add-XmlElementAfterSibling,
                              Get-CsprojPropertyValues
