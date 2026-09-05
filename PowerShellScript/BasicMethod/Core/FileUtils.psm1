<#
.SYNOPSIS
    文件工具函数模块 — 提供编码检测、路径解析、哈希计算等底层工具函数

.DESCRIPTION
    从 BasicMethod-FileAccess.psm1 拆分出的底层工具函数。
    包含：编码检测、路径解析、内容分割、模板变量、哈希计算、通配符路径解析等。

    用法:
        Import-Module .\BasicMethod-FileUtils.psm1
        Get-ProjectRoot
        Get-FileEncoding -Path "test.txt"
#>

# ============================================================
# 模块内部辅助：获取项目根目录
# ============================================================

<#
.SYNOPSIS
    获取项目根目录绝对路径
.DESCRIPTION
    优先委托给 ProjectPaths 模块（如果已加载），否则使用自包含实现。
    自包含实现从模块所在目录向上查找 .git 目录或 .gitignore 文件。
    调用方也可以通过 -ForcePath 参数显式指定项目根目录。
.PARAMETER ForcePath
    强制指定项目根目录（可选），指定后跳过自动检测。
.EXAMPLE
    Get-ProjectRoot
    Get-ProjectRoot -ForcePath "D:\Project\Program\WorkFlowPrj"
#>
$Script:BasicMethodFileUtils_ProjectRoot = $null

function Get-ProjectRoot {
    param(
        [string]$ForcePath = ""
    )

    if ($ForcePath) {
        $Script:BasicMethodFileUtils_ProjectRoot = $ForcePath
        return $ForcePath
    }

    if ($Script:BasicMethodFileUtils_ProjectRoot) {
        return $Script:BasicMethodFileUtils_ProjectRoot
    }

    # ★ 优先委托给 ProjectPaths 模块（如果已加载）
    $projectPathsModule = Get-Module -Name ProjectPaths -ErrorAction SilentlyContinue
    if ($projectPathsModule -and (Get-Command -Module ProjectPaths -Name Get-ProjectRoot -ErrorAction SilentlyContinue)) {
        $root = Get-ProjectRoot -StartPath $PSScriptRoot
        $Script:BasicMethodFileUtils_ProjectRoot = $root
        return $root
    }

    # ★ 自包含实现（作为回退，避免循环依赖）
    $currentDir = if ($PSScriptRoot) { $PSScriptRoot } else { (Get-Location).Path }
    $maxDepth = 10
    $depth = 0

    while ($currentDir -and $depth -lt $maxDepth) {
        if (Test-Path (Join-Path $currentDir ".git") -PathType Container) {
            $Script:BasicMethodFileUtils_ProjectRoot = $currentDir
            return $currentDir
        }
        if (Test-Path (Join-Path $currentDir ".gitignore") -PathType Leaf) {
            $Script:BasicMethodFileUtils_ProjectRoot = $currentDir
            return $currentDir
        }
        $parent = Split-Path $currentDir -Parent
        if ($parent -eq $currentDir) { break }
        $currentDir = $parent
        $depth++
    }

    # 回退：返回 PowerShellScript 的上级目录
    $fallback = Resolve-Path "$PSScriptRoot\..\.." -ErrorAction SilentlyContinue
    if ($fallback) {
        $Script:BasicMethodFileUtils_ProjectRoot = $fallback.Path
        return $fallback.Path
    }

    return $PSScriptRoot
}

# ============================================================
# 编码检测
# ============================================================

<#
.SYNOPSIS
    检测文件编码
.DESCRIPTION
    通过读取文件头 BOM 检测编码，无 BOM 时默认 UTF8。
    支持：UTF8-BOM、UTF16-LE、UTF16-BE、UTF32、UTF8-NoBOM
.EXAMPLE
    Get-FileEncoding -Path "test.txt"
#>
function Get-FileEncoding {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    if (-not (Test-Path $Path)) {
        return [PSCustomObject]@{
            Path = $Path
            Encoding = "UTF8"
            HasBOM = $false
            BOM = @()
        }
    }

    $bytes = [System.IO.File]::ReadAllBytes((Resolve-Path $Path).Path)
    $encoding = "UTF8"
    $hasBOM = $false
    $bom = @()

    if ($bytes.Length -ge 4 -and $bytes[0] -eq 0xFF -and $bytes[1] -eq 0xFE -and $bytes[2] -eq 0x00 -and $bytes[3] -eq 0x00) {
        $encoding = "UTF32-LE"; $hasBOM = $true; $bom = @(0xFF, 0xFE, 0x00, 0x00)
    } elseif ($bytes.Length -ge 4 -and $bytes[0] -eq 0x00 -and $bytes[1] -eq 0x00 -and $bytes[2] -eq 0xFE -and $bytes[3] -eq 0xFF) {
        $encoding = "UTF32-BE"; $hasBOM = $true; $bom = @(0x00, 0x00, 0xFE, 0xFF)
    } elseif ($bytes.Length -ge 2 -and $bytes[0] -eq 0xFE -and $bytes[1] -eq 0xFF) {
        $encoding = "UTF16-BE"; $hasBOM = $true; $bom = @(0xFE, 0xFF)
    } elseif ($bytes.Length -ge 2 -and $bytes[0] -eq 0xFF -and $bytes[1] -eq 0xFE) {
        $encoding = "UTF16-LE"; $hasBOM = $true; $bom = @(0xFF, 0xFE)
    } elseif ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
        $encoding = "UTF8-BOM"; $hasBOM = $true; $bom = @(0xEF, 0xBB, 0xBF)
    }

    return [PSCustomObject]@{
        Path = (Resolve-Path $Path).Path
        Encoding = $encoding
        HasBOM = $hasBOM
        BOM = $bom
    }
}

<#
.SYNOPSIS
    将编码名称转为 PowerShell 编码对象
.DESCRIPTION
    支持：Auto（自动检测）、UTF8、UTF8-BOM、UTF16-LE、UTF16-BE、UTF32、ASCII、GBK、Default
.EXAMPLE
    Get-PSEncoding -Encoding "UTF8-BOM"
#>
function Get-PSEncoding {
    param(
        [string]$Encoding = "UTF8"
    )

    # 返回编码名称字符串，兼容 Set-Content -Encoding 参数
    switch ($Encoding) {
        "UTF8" { return "UTF8" }
        "UTF8-BOM" { return "UTF8" }  # Set-Content 默认带 BOM
        "UTF16-LE" { return "Unicode" }
        "UTF16-BE" { return "BigEndianUnicode" }
        "UTF32" { return "UTF32" }
        "ASCII" { return "ASCII" }
        "Default" { return "Default" }
        "GBK" {
            try {
                # 验证 GBK 编码可用
                $null = [System.Text.Encoding]::GetEncoding("GBK")
                return "GBK"
            } catch {
                Write-Warning "GBK 编码不可用，回退到 UTF8"
                return "UTF8"
            }
        }
        "Auto" { return $null }  # 调用方处理
        default { return "UTF8" }
    }
}

# ============================================================
# 内部辅助函数
# ============================================================

<#
.SYNOPSIS
    内部：解析文件路径（绝对路径直接返回，相对路径拼接项目根目录）
.DESCRIPTION
    统一路径解析逻辑，被多个核心函数共用。
.PARAMETER Path
    文件路径（绝对路径或相对路径）
.OUTPUTS
    解析后的绝对路径字符串
#>
function _ResolvePath {
    param(
        [Parameter(Mandatory)]
        [Alias('TargetPath')]
        [string]$Path
    )

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return $Path
    }
    return Join-Path (Get-ProjectRoot) $Path
}

<#
.SYNOPSIS
    内部：读取文件内容（统一封装）
.DESCRIPTION
    统一文件读取逻辑，被多个核心函数共用。
.PARAMETER Path
    文件绝对路径
.OUTPUTS
    文件内容的原始字符串
#>
function _ReadFileContent {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    return Get-Content -Path $Path -Raw
}

<#
.SYNOPSIS
    内部：将内容按行分割（统一封装）
.DESCRIPTION
    统一内容分割逻辑，处理 Windows 和 Unix 换行符。
.PARAMETER Content
    要分割的字符串内容
.OUTPUTS
    字符串数组
#>
function _SplitLines {
    param(
        [Parameter(Mandatory)]
        [string]$Content
    )

    return $Content -split "`r`n|`n"
}

<#
.SYNOPSIS
    内部：计算相对路径
.DESCRIPTION
    将绝对路径转为相对于项目根目录的路径，统一被多个函数共用。
.PARAMETER Path
    文件绝对路径
.PARAMETER Root
    项目根目录（默认自动获取）
.OUTPUTS
    相对路径字符串（以 \ 开头）
#>
function _GetRelativePath {
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [string]$Root = ""
    )

    if (-not $Root) { $Root = Get-ProjectRoot }
    return $Path.Replace($Root, "").TrimStart("\")
}

<#
.SYNOPSIS
    内部：应用模板变量替换
.DESCRIPTION
    将内容中的 {{Key}} 替换为对应的值，被 BatchWrite-File 的多个模式共用。
.PARAMETER Content
    原始内容
.PARAMETER Variables
    变量哈希表（如 @{ Name = "World" }）
.OUTPUTS
    替换后的字符串
#>
function _ApplyTemplateVariables {
    param(
        [Parameter(Mandatory)]
        [AllowEmptyString()]
        [string]$Content,

        [hashtable]$Variables = @{}
    )

    foreach ($kv in $Variables.GetEnumerator()) {
        $Content = $Content.Replace("{{$($kv.Key)}}", $kv.Value)
    }
    return $Content
}

<#
.SYNOPSIS
    内部：获取文件大小（KB）
.DESCRIPTION
    统一获取文件大小的 KB 表示，被 Analyze-File 和 BatchRead-File 共用。
.PARAMETER Path
    文件路径
.OUTPUTS
    保留两位小数的 KB 值
#>
function _GetFileSizeKB {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    return [math]::Round((Get-Item $Path).Length / 1024, 2)
}

<#
.SYNOPSIS
    内部：计算字符串的 SHA256 哈希
.DESCRIPTION
    统一哈希计算逻辑，被 _WriteContentToFile 的 Verify 功能使用。
.PARAMETER Content
    要计算哈希的字符串内容
.PARAMETER Encoding
    编码对象（默认 UTF8）
.OUTPUTS
    小写十六进制哈希字符串
#>
function _ComputeHash {
    param(
        [Parameter(Mandatory)]
        [object]$Content,

        [System.Text.Encoding]$Encoding = $null
    )

    # 统一转为字符串（兼容 [object] 参数类型）
    $Content = if ($null -eq $Content) { "" } else { [string]$Content }
    if (-not $Encoding) { $Encoding = [System.Text.Encoding]::UTF8 }
    $bytes = $Encoding.GetBytes($Content)
    $hashAlgo = [System.Security.Cryptography.SHA256]::Create()
    return [System.BitConverter]::ToString($hashAlgo.ComputeHash($bytes)).Replace("-", "").ToLower()
}

<#
.SYNOPSIS
    内部：应用 SEARCH/REPLACE 精确匹配（非正则，只替换第一个匹配）
.DESCRIPTION
    被 Replace-InFile、BatchWrite-File -Merge、BatchReplace-InFile -Blocks 共用。
.PARAMETER Content
    原始内容
.PARAMETER Search
    搜索文本（精确匹配）
.PARAMETER Replace
    替换文本
.PARAMETER Path
    文件路径（用于错误信息）
.OUTPUTS
    成功：@{ Success = $true; Result = "替换后的内容"; Replacements = 1 }
    失败：@{ Success = $false; Error = "错误信息" }
#>
function _ApplySearchReplace {
    param(
        [Parameter(Mandatory)]
        [string]$Content,

        [Parameter(Mandatory)]
        [string]$Search,

        [Parameter(Mandatory)]
        [string]$Replace,

        [string]$Path = ""
    )

    $index = $Content.IndexOf($Search, [System.StringComparison]::Ordinal)
    if ($index -lt 0) {
        $pathInfo = if ($Path) { "`n文件: $Path" } else { "" }
        return @{ Success = $false; Error = "未找到匹配内容${pathInfo}`n搜索: $Search" }
    }

    $result = $Content.Substring(0, $index) + $Replace + $Content.Substring($index + $Search.Length)
    return @{ Success = $true; Result = $result; Replacements = 1 }
}

<#
.SYNOPSIS
    内部：解析通配符路径，返回匹配的文件绝对路径列表
.DESCRIPTION
    被 BatchRead-File 和 BatchReplace-InFile 共用。
.PARAMETER RawPath
    原始路径（支持通配符 * 和 ?）
.PARAMETER FilePattern
    文件通配符过滤（如 *.cs）
.OUTPUTS
    匹配的文件绝对路径数组
#>
function _ResolveWildcardPaths {
    param(
        [Parameter(Mandatory)]
        [string]$RawPath,

        [string]$FilePattern = "*"
    )

    if ($RawPath.Contains('*') -or $RawPath.Contains('?')) {
        $searchRoot = Split-Path $RawPath -Parent
        $searchPattern = Split-Path $RawPath -Leaf
        if (-not $searchRoot -or $searchRoot -eq $RawPath) { $searchRoot = Get-ProjectRoot }
        $resolvedRoot = Resolve-Path $searchRoot -ErrorAction SilentlyContinue
        if (-not $resolvedRoot) { return @() }
        return Get-ChildItem -Path $resolvedRoot.Path -Recurse -Filter $searchPattern -File -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -like $FilePattern } |
            Select-Object -ExpandProperty FullName
    } else {
        try {
            $resolved = Resolve-Path $RawPath -ErrorAction Stop
            return @($resolved.Path)
        } catch {
            return @()
        }
    }
}

<#
.SYNOPSIS
    内部：写入文件内容（含备份、校验、差异预览）
.DESCRIPTION
    被 Write-File 和 BatchWrite-File 共用。
.PARAMETER Path
    文件路径（已解析的绝对路径）
.PARAMETER Content
    写入内容
.PARAMETER Encoding
    编码名称
.PARAMETER Append
    是否追加
.PARAMETER Backup
    是否备份
.PARAMETER Verify
    是否校验
.PARAMETER DryRun
    是否模拟运行
.PARAMETER ShowDiff
    是否显示差异
.PARAMETER OldContent
    原内容（用于差异预览，不传则自动读取）
.OUTPUTS
    @{ Path; Action; Encoding; SizeBytes; Backup; Verified }
#>
function _WriteContentToFile {
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [Parameter(Mandatory)]
        [object]$Content,

        [string]$Encoding = "UTF8",

        [switch]$Append,

        [switch]$Backup,

        [switch]$Verify,

        [switch]$DryRun,

        [switch]$ShowDiff,

        [string]$OldContent = ""
    )

    # 统一转为字符串（兼容 [object] 参数类型，处理空字符串边界情况）
    $Content = if ($null -eq $Content) { "" } else { [string]$Content }

    $dir = Split-Path $Path -Parent

    # 编码检测（返回编码名称字符串，兼容 Set-Content -Encoding）
    $encodingName = if ($Encoding -eq "Auto" -and (Test-Path $Path)) {
        $detected = Get-FileEncoding -Path $Path
        Get-PSEncoding -Encoding $detected.Encoding
    } else {
        Get-PSEncoding -Encoding $Encoding
    }

    # 获取 .NET 编码对象（用于哈希计算和字节统计）
    # 注意：PowerShell 5.1 的 Set-Content -Encoding UTF8 会写入 BOM
    # 使用 .NET API 时，[System.Text.Encoding]::UTF8 默认也带 BOM
    # 使用 UTF8Encoding($false) 创建无 BOM 的 UTF8 编码
    $dotNetEncoding = if ($encodingName) {
        try {
            if ($encodingName -eq "UTF8") {
                [System.Text.UTF8Encoding]::new($false)
            } else {
                [System.Text.Encoding]::GetEncoding($encodingName)
            }
        } catch {
            [System.Text.UTF8Encoding]::new($false)
        }
    } else {
        [System.Text.UTF8Encoding]::new($false)
    }

    # 差异预览
    if ($ShowDiff -and -not $DryRun -and (Test-Path $Path)) {
        $oldForDiff = if ($OldContent) { $OldContent } else { _ReadFileContent -Path $Path }
        # 调用 Show-FileDiff（由 BasicMethod-FileDiff.psm1 提供）
        Show-FileDiff -OldContent $oldForDiff -NewContent $Content -Label $Path
    }

    # 备份
    $backupInfo = $null
    if ($Backup -and -not $DryRun -and (Test-Path $Path)) {
        $backupInfo = Backup-File -Path $Path -Quiet
    }

    # 写入前计算预期哈希（用于校验）
    $expectedHash = if ($Verify -and -not $DryRun) {
        _ComputeHash -Content $Content -Encoding $dotNetEncoding
    } else { $null }

    # 写入
    if (-not $DryRun) {
        if ($dir -and -not (Test-Path $dir)) {
            New-Item -Path $dir -ItemType Directory -Force | Out-Null
        }
        if ($Append) {
            Add-Content -Path $Path -Value $Content -Encoding $encodingName
        } else {
            # 使用 .NET API 替代 Set-Content，避免 PowerShell 5.1 的 BOM 问题
            # Set-Content -Encoding UTF8 在 PS5.1 中会写入 BOM，导致空内容校验失败
            # WriteAllText 使用 .NET 编码对象，行为可预测
            [System.IO.File]::WriteAllText($Path, $Content, $dotNetEncoding)
        }
    }

    # 校验
    $verified = $null
    if ($Verify -and -not $DryRun) {
        $actualHash = Get-FileHash -Path $Path -Algorithm SHA256
        $verified = $actualHash.Hash.ToLower() -eq $expectedHash
        if (-not $verified) {
            Write-Warning "写入校验失败: $Path"
        }
    }

    return [PSCustomObject]@{
        Path = $Path
        Action = if ($DryRun) { "Simulated" } elseif ($Append) { "Append" } else { "Overwrite" }
        Encoding = if ($Encoding -eq "Auto") { (Get-FileEncoding -Path $Path).Encoding } else { $Encoding }
        SizeBytes = $dotNetEncoding.GetByteCount($Content)
        Backup = if ($backupInfo) { $backupInfo.BackupPath } else { $null }
        Verified = $verified
    }
}

# ============================================================
# 模块导出
# ============================================================
# 设计说明：
# 公开函数（标准 PowerShell 动词）— 供外部直接调用
# 内部函数（_ 前缀）— 供同目录其他模块内部使用，通过 _init.ps1 统一加载后可见
# ============================================================

# 公开 API（标准 PowerShell 命名规范）
Export-ModuleMember -Function @(
    "Get-ProjectRoot",
    "Get-FileEncoding",
    "Get-PSEncoding"
)

# 内部 API（_ 前缀，供同目录其他模块使用）
# 这些函数以下划线开头，表示内部实现细节。
# 同目录模块通过 _init.ps1 统一加载后可直接调用。
# 外部调用方不应直接依赖这些函数。
Export-ModuleMember -Function @(
    "_ResolvePath",
    "_ReadFileContent",
    "_SplitLines",
    "_GetRelativePath",
    "_ApplyTemplateVariables",
    "_GetFileSizeKB",
    "_ComputeHash",
    "_ApplySearchReplace",
    "_ResolveWildcardPaths",
    "_WriteContentToFile"
)
