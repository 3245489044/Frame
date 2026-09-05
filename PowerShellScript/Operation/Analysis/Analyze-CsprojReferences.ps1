<#
.SYNOPSIS
    csproj 引用静态分析工具 — 解析项目文件中的 ProjectReference、PackageReference 和 Compile Include

.DESCRIPTION
    分析指定 .csproj 文件或目录中的项目引用关系，支持：
    - 解析 ProjectReference（项目引用），递归分析引用树
    - 解析 PackageReference（NuGet 包引用）
    - 解析 Compile Include（源码文件引用）
    - 检测循环引用
    - 树形展示引用层级
    - 导出 JSON 报告

    用法:
        Analyze-CsprojReferences.ps1 <路径>             分析指定 csproj 或目录
        Analyze-CsprojReferences.ps1 <路径> -Recursive   递归分析所有项目引用
        Analyze-CsprojReferences.ps1 <路径> -ShowFiles   显示源码文件列表
        Analyze-CsprojReferences.ps1 <路径> -Json        以 JSON 格式输出
        Analyze-CsprojReferences.ps1 <路径> -OutputFile report.json  导出报告
        Analyze-CsprojReferences.ps1 --help              显示帮助

    示例:
        .\Script\Analyze-CsprojReferences.ps1 Share\ParameterResolver\ParameterResolver.csproj
        .\Script\Analyze-CsprojReferences.ps1 CentralConsole -Recursive
        .\Script\Analyze-CsprojReferences.ps1 . -Recursive -OutputFile report.json
#>

param(
    [Parameter(Position=0, ValueFromPipeline=$true)]
    [string]$Path = "",

    [string]$OutputFile = "",

    [switch]$Recursive,

    [switch]$ShowFiles,

    [switch]$Json,

    [switch]$Help
)

# ============================================
#  辅助函数
# ============================================

function Write-Header {
    param([string]$Text)
    $line = "=" * 44
    Write-Host ""
    Write-Host $line -ForegroundColor Cyan
    Write-Host "  $Text" -ForegroundColor Cyan
    Write-Host $line -ForegroundColor Cyan
    Write-Host ""
}

function Write-SubHeader {
    param([string]$Text)
    Write-Host ""
    Write-Host "-- $Text" -ForegroundColor Yellow
    Write-Host ""
}

function Write-Info {
    param([string]$Label, [string]$Value)
    Write-Host ("  {0,-20} {1}" -f $Label, $Value)
}

function Write-TreeLine {
    param([string]$Prefix, [string]$Text, [string]$Color = "White")
    Write-Host ("  {0}{1}" -f $Prefix, $Text) -ForegroundColor $Color
}

# ============================================
#  核心解析函数
# ============================================

function Get-NormalizedPath {
    <#
    .SYNOPSIS
        将路径转换为标准格式（统一分隔符、解析相对路径）
    #>
    param([string]$Path, [string]$BaseDir)

    if ([string]::IsNullOrWhiteSpace($Path)) { return "" }

    # 如果是绝对路径，直接标准化
    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }

    # 相对路径，基于 BaseDir 解析
    if (-not [string]::IsNullOrWhiteSpace($BaseDir)) {
        $fullPath = Join-Path $BaseDir $Path
        return [System.IO.Path]::GetFullPath($fullPath)
    }

    return $Path
}

function Read-CsprojContent {
    <#
    .SYNOPSIS
        读取 csproj 文件内容，返回 XmlDocument 和原始文本行
    #>
    param([string]$FilePath)

    if (-not (Test-Path $FilePath)) {
        Write-Host "  [错误] 文件不存在: $FilePath" -ForegroundColor Red
        return $null
    }

    try {
        $xml = New-Object System.Xml.XmlDocument
        $xml.PreserveWhitespace = $true
        $xml.Load($FilePath)
        $content = Get-Content $FilePath -Raw
        $lines = Get-Content $FilePath
        return @{
            Xml = $xml
            Content = $content
            Lines = $lines
            FilePath = $FilePath
        }
    }
    catch {
        Write-Host "  [错误] 无法解析 XML: $_" -ForegroundColor Red
        return $null
    }
}

function Get-ProjectProperty {
    <#
    .SYNOPSIS
        获取 csproj 中的属性值
    #>
    param(
        [System.Xml.XmlDocument]$Xml,
        [string]$PropertyName
    )

    # 尝试无命名空间查询
    $node = $Xml.SelectSingleNode("//PropertyGroup/$PropertyName")
    if ($node -ne $null) {
        return $node.InnerText.Trim()
    }

    # 尝试 MSBuild 命名空间查询
    $ns = New-Object System.Xml.XmlNamespaceManager($Xml.NameTable)
    $ns.AddNamespace("msb", "http://schemas.microsoft.com/developer/msbuild/2003")
    $node = $Xml.SelectSingleNode("//msb:PropertyGroup/msb:$PropertyName", $ns)
    if ($node -ne $null) {
        return $node.InnerText.Trim()
    }

    return ""
}

function Get-ProjectReferences {
    <#
    .SYNOPSIS
        解析 ProjectReference 节点
    .DESCRIPTION
        返回包含 Include 路径和解析后绝对路径的对象数组
    #>
    param(
        [System.Xml.XmlDocument]$Xml,
        [string]$CsprojDir
    )

    $results = @()

    # 无命名空间查询
    $nodes = $Xml.SelectNodes("//ProjectReference")
    if ($nodes -eq $null -or $nodes.Count -eq 0) {
        # MSBuild 命名空间查询
        $ns = New-Object System.Xml.XmlNamespaceManager($Xml.NameTable)
        $ns.AddNamespace("msb", "http://schemas.microsoft.com/developer/msbuild/2003")
        $nodes = $Xml.SelectNodes("//msb:ProjectReference", $ns)
    }

    if ($nodes -ne $null) {
        foreach ($node in $nodes) {
            $include = $node.GetAttribute("Include")
            if ([string]::IsNullOrWhiteSpace($include)) { continue }

            $resolvedPath = Get-NormalizedPath -Path $include -BaseDir $CsprojDir

            $results += [PSCustomObject]@{
                Include      = $include
                ResolvedPath = $resolvedPath
                Exists       = Test-Path $resolvedPath
                Name         = [System.IO.Path]::GetFileName($include)
            }
        }
    }

    return $results
}

function Get-PackageReferences {
    <#
    .SYNOPSIS
        解析 PackageReference 节点
    .DESCRIPTION
        返回包含包名和版本的对象数组
    #>
    param(
        [System.Xml.XmlDocument]$Xml
    )

    $results = @()

    # 无命名空间查询
    $nodes = $Xml.SelectNodes("//PackageReference")
    if ($nodes -eq $null -or $nodes.Count -eq 0) {
        $ns = New-Object System.Xml.XmlNamespaceManager($Xml.NameTable)
        $ns.AddNamespace("msb", "http://schemas.microsoft.com/developer/msbuild/2003")
        $nodes = $Xml.SelectNodes("//msb:PackageReference", $ns)
    }

    if ($nodes -ne $null) {
        foreach ($node in $nodes) {
            $name = $node.GetAttribute("Include")
            if ([string]::IsNullOrWhiteSpace($name)) { continue }

            # 尝试从 Version 属性获取版本
            $version = $node.GetAttribute("Version")
            if ([string]::IsNullOrWhiteSpace($version)) {
                # 尝试从子元素 Version 获取
                $verNode = $node.SelectSingleNode("Version")
                if ($verNode -ne $null) {
                    $version = $verNode.InnerText.Trim()
                }
                else {
                    # 尝试 MSBuild 命名空间的子元素
                    $ns = New-Object System.Xml.XmlNamespaceManager($Xml.NameTable)
                    $ns.AddNamespace("msb", "http://schemas.microsoft.com/developer/msbuild/2003")
                    $verNode = $node.SelectSingleNode("msb:Version", $ns)
                    if ($verNode -ne $null) {
                        $version = $verNode.InnerText.Trim()
                    }
                }
            }

            $results += [PSCustomObject]@{
                Name    = $name
                Version = if ([string]::IsNullOrWhiteSpace($version)) { "(未指定)" } else { $version }
            }
        }
    }

    return $results
}

function Get-CompileIncludes {
    <#
    .SYNOPSIS
        解析 Compile Include 节点
    .DESCRIPTION
        返回所有显式 Compile Include 的文件路径列表
    #>
    param(
        [System.Xml.XmlDocument]$Xml,
        [string]$CsprojDir
    )

    $results = @()

    # 无命名空间查询
    $nodes = $Xml.SelectNodes("//Compile[@Include]")
    if ($nodes -eq $null -or $nodes.Count -eq 0) {
        $ns = New-Object System.Xml.XmlNamespaceManager($Xml.NameTable)
        $ns.AddNamespace("msb", "http://schemas.microsoft.com/developer/msbuild/2003")
        $nodes = $Xml.SelectNodes("//msb:Compile[@Include]", $ns)
    }

    if ($nodes -ne $null) {
        foreach ($node in $nodes) {
            $include = $node.GetAttribute("Include")
            if ([string]::IsNullOrWhiteSpace($include)) { continue }

            $resolvedPath = Get-NormalizedPath -Path $include -BaseDir $CsprojDir

            $results += [PSCustomObject]@{
                Include      = $include
                ResolvedPath = $resolvedPath
                Exists       = Test-Path $resolvedPath
            }
        }
    }

    return $results
}

function Get-ReferenceRemoveItems {
    <#
    .SYNOPSIS
        解析 Compile Remove / Content Remove 等移除项
    .DESCRIPTION
        用于了解项目排除了哪些文件
    #>
    param(
        [System.Xml.XmlDocument]$Xml
    )

    $results = @()

    $removePatterns = @("Compile", "Content", "EmbeddedResource", "None")
    foreach ($pattern in $removePatterns) {
        $nodes = $Xml.SelectNodes("//$pattern[@Remove]")
        if ($nodes -eq $null -or $nodes.Count -eq 0) {
            $ns = New-Object System.Xml.XmlNamespaceManager($Xml.NameTable)
            $ns.AddNamespace("msb", "http://schemas.microsoft.com/developer/msbuild/2003")
            $nodes = $Xml.SelectNodes("//msb:$pattern[@Remove]", $ns)
        }

        if ($nodes -ne $null) {
            foreach ($node in $nodes) {
                $remove = $node.GetAttribute("Remove")
                if (-not [string]::IsNullOrWhiteSpace($remove)) {
                    $results += [PSCustomObject]@{
                        Type   = $pattern
                        Remove = $remove
                    }
                }
            }
        }
    }

    return $results
}

function Get-ReferenceInfo {
    <#
    .SYNOPSIS
        获取单个 csproj 文件的完整引用信息
    #>
    param(
        [string]$FilePath,
        [int]$Depth = 0,
        [System.Collections.Generic.HashSet[string]]$VisitedPaths = $null
    )

    if ($VisitedPaths -eq $null) {
        $VisitedPaths = [System.Collections.Generic.HashSet[string]]::new()
    }

    $normalizedPath = [System.IO.Path]::GetFullPath($FilePath)

    # 检测循环引用
    $circularRef = $false
    if ($VisitedPaths.Contains($normalizedPath)) {
        $circularRef = $true
    }
    $VisitedPaths.Add($normalizedPath) | Out-Null

    $csprojData = Read-CsprojContent -FilePath $FilePath
    if ($csprojData -eq $null) { return $null }

    $csprojDir = Split-Path $FilePath -Parent
    $xml = $csprojData.Xml

    # 获取基本属性
    $targetFramework = Get-ProjectProperty -Xml $xml -PropertyName "TargetFramework"
    $outputType = Get-ProjectProperty -Xml $xml -PropertyName "OutputType"
    $assemblyName = Get-ProjectProperty -Xml $xml -PropertyName "AssemblyName"
    $rootNamespace = Get-ProjectProperty -Xml $xml -PropertyName "RootNamespace"

    if ([string]::IsNullOrWhiteSpace($assemblyName)) {
        $assemblyName = [System.IO.Path]::GetFileNameWithoutExtension($FilePath)
    }

    # 解析引用
    $projectRefs = Get-ProjectReferences -Xml $xml -CsprojDir $csprojDir
    $packageRefs = Get-PackageReferences -Xml $xml
    $compileFiles = Get-CompileIncludes -Xml $xml -CsprojDir $csprojDir
    $removeItems = Get-ReferenceRemoveItems -Xml $xml

    # 构建结果对象
    $result = [PSCustomObject]@{
        FilePath        = $normalizedPath
        FileName        = [System.IO.Path]::GetFileName($FilePath)
        AssemblyName    = $assemblyName
        TargetFramework = if ([string]::IsNullOrWhiteSpace($targetFramework)) { "(未指定)" } else { $targetFramework }
        OutputType      = if ([string]::IsNullOrWhiteSpace($outputType)) { "Library" } else { $outputType }
        RootNamespace   = if ([string]::IsNullOrWhiteSpace($rootNamespace)) { "(未指定)" } else { $rootNamespace }
        ProjectReferences = $projectRefs
        PackageReferences = $packageRefs
        CompileFiles    = $compileFiles
        RemoveItems     = $removeItems
        CircularRef     = $circularRef
        Depth           = $Depth
        SubReferences   = @()
    }

    # 递归分析子项目引用
    if ($Recursive -and -not $circularRef) {
        $subResults = @()
        foreach ($projRef in $projectRefs) {
            if ($projRef.Exists) {
                $subResult = Get-ReferenceInfo -FilePath $projRef.ResolvedPath -Depth ($Depth + 1) -VisitedPaths $VisitedPaths
                if ($subResult -ne $null) {
                    $subResults += $subResult
                }
            }
        }
        $result.SubReferences = $subResults
    }

    return $result
}

# ============================================
#  展示函数
# ============================================

function Show-ReferenceTree {
    <#
    .SYNOPSIS
        以树形结构展示引用信息
    #>
    param(
        [PSObject]$Info,
        [string]$Indent = ""
    )

    $color = if ($Info.CircularRef) { "Red" } else { "White" }
    $circularTag = if ($Info.CircularRef) { " [循环引用!]" } else { "" }

    Write-TreeLine -Prefix $Indent -Text "$($Info.FileName)$circularTag" -Color $color

    if ($Info.CircularRef) { return }

    $childIndent = $Indent + "  "

    # 显示项目引用
    if ($Info.ProjectReferences.Count -gt 0) {
        Write-TreeLine -Prefix $childIndent -Text "├─ 项目引用 ($($Info.ProjectReferences.Count)):" -Color "Yellow"
        foreach ($projRef in $Info.ProjectReferences) {
            $existsMark = if ($projRef.Exists) { "" } else { " [文件不存在!]" }
            Write-TreeLine -Prefix $childIndent + "│  " -Text "├─ $($projRef.Name)$existsMark" -Color "Gray"

            # 显示子项目的递归引用
            $subMatch = $Info.SubReferences | Where-Object { $_.FileName -eq $projRef.Name }
            if ($subMatch -ne $null) {
                Show-ReferenceTree -Info $subMatch -Indent ($childIndent + "│  ")
            }
        }
    }

    # 显示 NuGet 包引用
    if ($Info.PackageReferences.Count -gt 0) {
        Write-TreeLine -Prefix $childIndent -Text "├─ NuGet 包 ($($Info.PackageReferences.Count)):" -Color "Yellow"
        foreach ($pkg in $Info.PackageReferences) {
            Write-TreeLine -Prefix $childIndent + "│  " -Text "├─ $($pkg.Name) v$($pkg.Version)" -Color "Gray"
        }
    }

    # 显示源码文件
    if ($Info.CompileFiles.Count -gt 0) {
        Write-TreeLine -Prefix $childIndent -Text "└─ 源码文件 ($($Info.CompileFiles.Count)):" -Color "Yellow"
        $maxShow = [Math]::Min($Info.CompileFiles.Count, 10)
        for ($i = 0; $i -lt $maxShow; $i++) {
            $file = $Info.CompileFiles[$i]
            $existsMark = if ($file.Exists) { "" } else { " [文件不存在!]" }
            $isLast = ($i -eq $maxShow - 1 -and $maxShow -eq $Info.CompileFiles.Count)
            $prefix = if ($isLast) { "└─" } else { "├─" }
            Write-TreeLine -Prefix $childIndent + "   " -Text "$prefix $($file.Include)$existsMark" -Color "Gray"
        }
        if ($Info.CompileFiles.Count -gt 10) {
            Write-TreeLine -Prefix $childIndent + "   " -Text "└─ ... 还有 $($Info.CompileFiles.Count - 10) 个文件" -Color "DarkGray"
        }
    }
    else {
        Write-TreeLine -Prefix $childIndent -Text "└─ 源码文件: (使用 SDK 默认编译)" -Color "DarkGray"
    }
}

function Show-DetailedReport {
    <#
    .SYNOPSIS
        显示详细的引用分析报告
    #>
    param([PSObject]$Info)

    Write-Header "csproj 引用分析报告"

    Write-Info "项目文件" $Info.FileName
    Write-Info "程序集名称" $Info.AssemblyName
    Write-Info "目标框架" $Info.TargetFramework
    Write-Info "输出类型" $Info.OutputType
    Write-Info "根命名空间" $Info.RootNamespace
    Write-Info "完整路径" $Info.FilePath

    if ($Info.CircularRef) {
        Write-Host ""
        Write-Host "  ⚠ 检测到循环引用！" -ForegroundColor Red
        return
    }

    # 项目引用
    Write-SubHeader "项目引用 ($($Info.ProjectReferences.Count))"
    if ($Info.ProjectReferences.Count -gt 0) {
        for ($i = 0; $i -lt $Info.ProjectReferences.Count; $i++) {
            $ref = $Info.ProjectReferences[$i]
            $status = if ($ref.Exists) { "✓" } else { "✗ 文件不存在" }
            Write-Host ("  [{0,2}] {1}" -f ($i + 1), $ref.Name) -ForegroundColor White
            Write-Host ("       路径: {0}" -f $ref.ResolvedPath) -ForegroundColor Gray
            Write-Host ("       状态: {0}" -f $status) -ForegroundColor $(if ($ref.Exists) { "Green" } else { "Red" })
            Write-Host ""
        }
    }
    else {
        Write-Host "  (无项目引用)" -ForegroundColor DarkGray
    }

    # NuGet 包引用
    Write-SubHeader "NuGet 包引用 ($($Info.PackageReferences.Count))"
    if ($Info.PackageReferences.Count -gt 0) {
        for ($i = 0; $i -lt $Info.PackageReferences.Count; $i++) {
            $pkg = $Info.PackageReferences[$i]
            Write-Host ("  [{0,2}] {1,-45} v{2}" -f ($i + 1), $pkg.Name, $pkg.Version) -ForegroundColor White
        }
        Write-Host ""
    }
    else {
        Write-Host "  (无 NuGet 包引用)" -ForegroundColor DarkGray
    }

    # 源码文件
    Write-SubHeader "源码文件 ($($Info.CompileFiles.Count))"
    if ($Info.CompileFiles.Count -gt 0) {
        if ($ShowFiles) {
            for ($i = 0; $i -lt $Info.CompileFiles.Count; $i++) {
                $file = $Info.CompileFiles[$i]
                $status = if ($file.Exists) { "" } else { " [文件不存在!]" }
                Write-Host ("  [{0,2}] {1}{2}" -f ($i + 1), $file.Include, $status) -ForegroundColor White
            }
        }
        else {
            Write-Host ("  共 {0} 个文件（使用 --ShowFiles 查看详情）" -f $Info.CompileFiles.Count) -ForegroundColor Gray
        }
        Write-Host ""
    }
    else {
        Write-Host "  (无显式 Compile Include，使用 SDK 默认编译)" -ForegroundColor DarkGray
    }

    # 移除项
    if ($Info.RemoveItems.Count -gt 0) {
        Write-SubHeader "移除项 ($($Info.RemoveItems.Count))"
        foreach ($item in $Info.RemoveItems) {
            Write-Host ("  [{0,-15}] {1}" -f $item.Type, $item.Remove) -ForegroundColor DarkGray
        }
        Write-Host ""
    }

    # 递归引用树
    if ($Recursive -and $Info.SubReferences.Count -gt 0) {
        Write-SubHeader "引用树"
        Show-ReferenceTree -Info $Info
        Write-Host ""
    }
}

function Show-JsonOutput {
    <#
    .SYNOPSIS
        以 JSON 格式输出引用信息
    #>
    param([PSObject]$Info)

    $jsonObj = @{
        project          = $Info.FileName
        assemblyName     = $Info.AssemblyName
        targetFramework  = $Info.TargetFramework
        outputType       = $Info.OutputType
        rootNamespace    = $Info.RootNamespace
        filePath         = $Info.FilePath
        circularRef      = $Info.CircularRef
        projectReferences = $Info.ProjectReferences | ForEach-Object {
            @{
                name         = $_.Name
                include      = $_.Include
                resolvedPath = $_.ResolvedPath
                exists       = $_.Exists
            }
        }
        packageReferences = $Info.PackageReferences | ForEach-Object {
            @{
                name    = $_.Name
                version = $_.Version
            }
        }
        compileFiles = if ($ShowFiles) {
            $Info.CompileFiles | ForEach-Object {
                @{
                    include      = $_.Include
                    resolvedPath = $_.ResolvedPath
                    exists       = $_.Exists
                }
            }
        }
        else {
            @{ count = $Info.CompileFiles.Count }
        }
        removeItems = $Info.RemoveItems | ForEach-Object {
            @{
                type   = $_.Type
                remove = $_.Remove
            }
        }
    }

    if ($Recursive -and $Info.SubReferences.Count -gt 0) {
        $jsonObj.subReferences = $Info.SubReferences | ForEach-Object { ConvertTo-JsonObject -Info $_ }
    }

    return $jsonObj
}

function ConvertTo-JsonObject {
    <#
    .SYNOPSIS
        递归将引用信息转换为 JSON 对象
    #>
    param([PSObject]$Info)

    $obj = @{
        project          = $Info.FileName
        assemblyName     = $Info.AssemblyName
        targetFramework  = $Info.TargetFramework
        outputType       = $Info.OutputType
        filePath         = $Info.FilePath
        circularRef      = $Info.CircularRef
        projectReferences = $Info.ProjectReferences | ForEach-Object {
            @{
                name         = $_.Name
                include      = $_.Include
                resolvedPath = $_.ResolvedPath
                exists       = $_.Exists
            }
        }
        packageReferences = $Info.PackageReferences | ForEach-Object {
            @{
                name    = $_.Name
                version = $_.Version
            }
        }
        compileFiles = @{ count = $Info.CompileFiles.Count }
    }

    if ($Info.SubReferences.Count -gt 0) {
        $obj.subReferences = $Info.SubReferences | ForEach-Object { ConvertTo-JsonObject -Info $_ }
    }

    return $obj
}

# ============================================
#  主逻辑
# ============================================

function Show-Help {
    <#
    .SYNOPSIS
        显示帮助信息
    #>
    $scriptName = if ($MyInvocation.MyCommand.Path) { Split-Path -Leaf $MyInvocation.MyCommand.Path } else { "Analyze-CsprojReferences.ps1" }

    Write-Header "csproj 引用分析工具 - 帮助"
    Write-Host ""
    Write-Host "  用法:"
    Write-Host "    $scriptName <路径>              分析指定 csproj 或目录"
    Write-Host "    $scriptName <路径> -Recursive    递归分析所有项目引用"
    Write-Host "    $scriptName <路径> -ShowFiles    显示源码文件列表"
    Write-Host "    $scriptName <路径> -Json         以 JSON 格式输出"
    Write-Host "    $scriptName <路径> -OutputFile   导出 JSON 报告"
    Write-Host "    $scriptName --help               显示本帮助"
    Write-Host ""
    Write-Host "  参数:"
    Write-Host "    -Path           csproj 文件路径或包含 csproj 的目录路径"
    Write-Host "    -Recursive      递归分析所有引用的项目"
    Write-Host "    -ShowFiles      显示 Compile Include 文件列表"
    Write-Host "    -Json           以 JSON 格式输出到控制台"
    Write-Host "    -OutputFile     导出 JSON 报告到文件"
    Write-Host "    -Help           显示帮助"
    Write-Host ""
    Write-Host "  示例:"
    Write-Host "    $scriptName Share\ParameterResolver\ParameterResolver.csproj"
    Write-Host "    $scriptName CentralConsole -Recursive"
    Write-Host "    $scriptName . -Recursive -OutputFile report.json"
    Write-Host "    $scriptName BasicMethod -Recursive -ShowFiles"
    Write-Host "    $scriptName BasicMethod\BasicMethod.csproj -Json"
    Write-Host ""
}

function Find-CsprojFiles {
    <#
    .SYNOPSIS
        根据输入路径查找 csproj 文件
    .DESCRIPTION
        如果是文件直接返回，如果是目录则扫描其中的 csproj 文件
    #>
    param([string]$InputPath)

    $resolvedPath = Resolve-Path $InputPath -ErrorAction SilentlyContinue
    if (-not $resolvedPath) {
        Write-Host "[错误] 路径不存在: $InputPath" -ForegroundColor Red
        return @()
    }

    $fullPath = $resolvedPath.Path

    if (Test-Path $fullPath -PathType Leaf) {
        # 是文件
        if ($fullPath -like "*.csproj") {
            return @($fullPath)
        }
        else {
            Write-Host "[错误] 指定的文件不是 .csproj 文件: $fullPath" -ForegroundColor Red
            return @()
        }
    }
    elseif (Test-Path $fullPath -PathType Container) {
        # 是目录，扫描 csproj 文件
        $files = Get-ChildItem -Path $fullPath -Filter "*.csproj" -File
        if ($files.Count -eq 0) {
            Write-Host "[错误] 目录中未找到 .csproj 文件: $fullPath" -ForegroundColor Red
            return @()
        }
        return @($files | ForEach-Object { $_.FullName })
    }
    else {
        Write-Host "[错误] 路径无效: $InputPath" -ForegroundColor Red
        return @()
    }
}

# ============================================
#  入口
# ============================================

# 处理 --help
if ($Help) {
    Show-Help
    exit 0
}

# 检查路径参数
if ([string]::IsNullOrWhiteSpace($Path)) {
    Write-Host "[错误] 请指定 .csproj 文件路径或目录路径" -ForegroundColor Red
    Write-Host "  用法: .\Script\Analyze-CsprojReferences.ps1 <路径> [参数]"
    Write-Host "  使用 --help 查看详细帮助"
    exit 1
}

# 查找 csproj 文件
$csprojFiles = Find-CsprojFiles -InputPath $Path
if ($csprojFiles.Count -eq 0) {
    exit 1
}

# 分析每个 csproj 文件
$allResults = @()
foreach ($csprojFile in $csprojFiles) {
    $info = Get-ReferenceInfo -FilePath $csprojFile
    if ($info -ne $null) {
        $allResults += $info
    }
}

# 输出结果
if ($Json) {
    # JSON 输出到控制台
    if ($allResults.Count -eq 1) {
        $jsonObj = ConvertTo-JsonObject -Info $allResults[0]
        $jsonStr = $jsonObj | ConvertTo-Json -Depth 10
    }
    else {
        $jsonArr = $allResults | ForEach-Object { ConvertTo-JsonObject -Info $_ }
        $jsonStr = $jsonArr | ConvertTo-Json -Depth 10
    }
    Write-Host $jsonStr
}
else {
    # 控制台输出
    foreach ($info in $allResults) {
        Show-DetailedReport -Info $info
    }
}

# 导出 JSON 文件
if (-not [string]::IsNullOrWhiteSpace($OutputFile)) {
    if ($allResults.Count -eq 1) {
        $jsonObj = ConvertTo-JsonObject -Info $allResults[0]
        $jsonStr = $jsonObj | ConvertTo-Json -Depth 10
    }
    else {
        $jsonArr = $allResults | ForEach-Object { ConvertTo-JsonObject -Info $_ }
        $jsonStr = $jsonArr | ConvertTo-Json -Depth 10
    }

    $outputDir = Split-Path $OutputFile -Parent
    if (-not [string]::IsNullOrWhiteSpace($outputDir) -and -not (Test-Path $outputDir)) {
        New-Item -Path $outputDir -ItemType Directory -Force | Out-Null
    }

    $jsonStr | Out-File -FilePath $OutputFile -Encoding utf8
    Write-Host ""
    Write-Host "  报告已导出到: $OutputFile" -ForegroundColor Green
}
