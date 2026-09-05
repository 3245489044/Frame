<#
.SYNOPSIS
    硬编码 NuGet 版本号转换工具 — 将 .csproj 中硬编码的 PackageReference Version 替换为 Directory.Build.props 变量引用

.DESCRIPTION
    扫描项目中所有 .csproj 文件的 PackageReference，检测硬编码的版本号，
    自动在 Directory.Build.props 中创建对应的版本变量，并将 .csproj 中的版本号替换为 $(VarName) 引用。

    版本管理模式：版本号定义在 Directory.Build.props 中，各 .csproj 通过 $(VarName) 引用

.PARAMETER All
    一键转换所有硬编码版本号，无需逐个确认

.PARAMETER CheckOnly
    仅检查硬编码版本号并列出，不执行实际转换

.PARAMETER PackageName
    指定要转换的包名（支持通配符 *），例如：
    -PackageName "FluentValidation"
    -PackageName "Microsoft.*"

.PARAMETER WhatIf
    预览将要执行的变更，不实际修改文件

.PARAMETER SkipBuild
    跳过转换后的编译验证

.PARAMETER Concurrency
    并行扫描的并发数（默认 4）

.EXAMPLE
    # 交互模式：列出硬编码版本号，逐个确认
    .\Convert-HardcodedVersions.ps1

.EXAMPLE
    # 一键全转换
    .\Convert-HardcodedVersions.ps1 -All

.EXAMPLE
    # 仅检查硬编码版本号
    .\Convert-HardcodedVersions.ps1 -CheckOnly

.EXAMPLE
    # 转换指定包
    .\Convert-HardcodedVersions.ps1 -PackageName "FluentValidation"

.EXAMPLE
    # 预览变更
    .\Convert-HardcodedVersions.ps1 -All -WhatIf
#>

[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [switch]$All,

    [Parameter()]
    [switch]$CheckOnly,

    [Parameter()]
    [string]$PackageName = "",

    [Parameter()]
    [switch]$SkipBuild,

    [Parameter()]
    [switch]$WhatIf,

    [Parameter()]
    [int]$Concurrency = 4
)

# ============================================================
# 初始化
# ============================================================

$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Resolve-Path (Join-Path $scriptPath "..\..")
$buildPropsPath = Join-Path $projectRoot "Directory.Build.props"
$slnPath = Join-Path $projectRoot "CsharpPrj\MainProj.sln"

# 加载模块（复用 Update-NuGetPackages.ps1 的模块化基础函数）
$moduleDir = Resolve-Path (Join-Path $scriptPath "..\Modules")
$global:SkipModules = @{
    'BuildOutput\BuildOutputCleaner.psm1' = $true
    'FileDriven\FileDrivenTestUtils.ps1'  = $true
    'FileDriven\ScriptRunner.ps1'         = $true
}
. "$moduleDir\Import-Modules.ps1"
Remove-Variable -Name 'SkipModules' -Scope Global -ErrorAction SilentlyContinue

# ============================================================
# 辅助函数
# ============================================================

function Write-Color {
    param([string]$Text, [string]$ForegroundColor = "White", [string]$BackgroundColor = "Black")
    Write-Host $Text -ForegroundColor $ForegroundColor -BackgroundColor $BackgroundColor
}

function Write-Section {
    param([string]$Title)
    Write-Host ""
    Write-Host ("=" * 60) -ForegroundColor Cyan
    Write-Host "  $Title" -ForegroundColor White
    Write-Host ("=" * 60) -ForegroundColor Cyan
}

function Get-RelativePath {
    param([string]$Path, [string]$BasePath)
    if (-not $BasePath.EndsWith('\')) { $BasePath += '\' }
    if ($Path -like "$BasePath*") {
        return $Path.Substring($BasePath.Length)
    }
    return $Path
}

<#
.SYNOPSIS
    解析 Directory.Build.props，返回 { 变量名 → 版本号 } 的映射表
#>
function Get-BuildPropsVersionMap {
    if (-not (Test-Path $buildPropsPath)) {
        return @{}
    }

    $content = Get-Content $buildPropsPath -Raw -Encoding UTF8
    $map = @{}

    # 匹配 <SomeNameVersion>1.2.3</SomeNameVersion>
    $pattern = '<([A-Za-z][A-Za-z0-9]*)>\s*([\d]+\.[\d]+\.[\d]+(?:-[a-zA-Z0-9.-]+)?)\s*</\1>'
    $matches = [regex]::Matches($content, $pattern)

    foreach ($match in $matches) {
        $varName = $match.Groups[1].Value
        $version = $match.Groups[2].Value
        $map[$varName] = $version
    }

    return $map
}

<#
.SYNOPSIS
    从 Directory.Build.props 解析所有版本变量，建立 包名 → 变量名 的映射
#>
function Get-PackageVariableMapping {
    $mapping = @{}

    if (-not (Test-Path $buildPropsPath)) {
        return $mapping
    }

    $content = Get-Content $buildPropsPath -Raw -Encoding UTF8

    # 匹配 <SomeNameVersion>1.2.3</SomeNameVersion>
    $pattern = '<([A-Za-z][A-Za-z0-9]*)>\s*([\d]+\.[\d]+\.[\d]+(?:-[a-zA-Z0-9.-]+)?)\s*</\1>'
    $matches = [regex]::Matches($content, $pattern)

    foreach ($match in $matches) {
        $varName = $match.Groups[1].Value
        $version = $match.Groups[2].Value

        # 变量名必须以 Version 结尾才可能是 NuGet 包版本变量
        if (-not $varName.EndsWith("Version")) { continue }

        # 去掉 "Version" 后缀得到基础名
        $baseName = $varName.Substring(0, $varName.Length - "Version".Length)

        # 尝试反向推导包名：将驼峰/帕斯卡命名转回点分隔
        $possiblePkgNames = @()

        # 方法1: 在每对大写字母间插入点（帕斯卡→点分隔）
        $pascalToDot = $baseName -replace '([A-Z])([A-Z][a-z])', '$1.$2' -replace '([a-z])([A-Z])', '$1.$2'
        $possiblePkgNames += $pascalToDot

        # 方法2: 尝试在数字边界插入点 (如 SQLite3 → SQLite.3)
        $pascalToDot2 = $pascalToDot -replace '([a-zA-Z])(\d)', '$1.$2'
        $possiblePkgNames += $pascalToDot2

        # 方法3: 原始基础名
        $possiblePkgNames += $baseName

        # 去重
        $possiblePkgNames = $possiblePkgNames | Select-Object -Unique

        foreach ($pkgName in $possiblePkgNames) {
            if (-not $mapping.ContainsKey($pkgName)) {
                $mapping[$pkgName] = @{
                    VarName = $varName
                    CurrentVersion = $version
                }
            }
        }
    }

    return $mapping
}

<#
.SYNOPSIS
    根据包名在变量映射中查找对应的变量信息
    使用精确匹配 → 去点匹配 → 完整段匹配 的优先级
#>
function Find-VariableForPackage {
    param(
        [string]$PackageName,
        [hashtable]$VariableMap
    )

    # 1. 精确匹配
    if ($VariableMap.ContainsKey($PackageName)) {
        return $VariableMap[$PackageName]
    }

    # 2. 去点匹配（包名去掉所有点后匹配）
    $noDot = $PackageName -replace '\.', ''
    foreach ($key in $VariableMap.Keys) {
        $keyNoDot = $key -replace '\.', ''
        if ($keyNoDot -eq $noDot) {
            return $VariableMap[$key]
        }
    }

    # 3. 完整段匹配：包名的每个段都必须在变量映射的包名中出现
    $segments = $PackageName -split '\.'
    foreach ($key in $VariableMap.Keys) {
        $keyNoDot = $key -replace '\.', ''
        $keySegments = $key -split '\.'
        if ($keySegments.Count -eq $segments.Count) {
            $match = $true
            for ($i = 0; $i -lt $segments.Count; $i++) {
                if ($segments[$i] -ne $keySegments[$i]) {
                    $match = $false
                    break
                }
            }
            if ($match) {
                return $VariableMap[$key]
            }
        }
    }

    # 4. 完整去点段匹配
    foreach ($key in $VariableMap.Keys) {
        $keyNoDot = $key -replace '\.', ''
        $keySegments = $key -split '\.'
        if ($keySegments.Count -eq $segments.Count -and $keyNoDot -eq $noDot) {
            return $VariableMap[$key]
        }
    }

    return $null
}

<#
.SYNOPSIS
    将包名转换为 Directory.Build.props 中使用的变量名
    例如: "FluentValidation" → "FluentValidationVersion"
          "Microsoft.Extensions.Logging.Console" → "MicrosoftExtensionsLoggingConsoleVersion"
.DESCRIPTION
    使用 StringTools 的 Convert-NameCase 函数进行命名风格转换。
    如果 StringTools 模块已加载，优先使用 Convert-NameCase 确保一致性；
    否则回退到简单的去点拼接。
#>
function Convert-PackageNameToVariableName {
    param([string]$PackageName)

    # 检查 StringTools 是否已加载（通过 Import-Modules.ps1 统一加载）
    $stringToolsLoaded = (Get-Command -Name 'Convert-NameCase' -ErrorAction SilentlyContinue) -ne $null

    if ($stringToolsLoaded) {
        # ★ 使用 StringTools 的 Convert-NameCase 进行命名风格转换
        #   将点分隔的包名转为 PascalCase，再拼接 Version 后缀
        #   例如: "microsoft.extensions.logging" → "MicrosoftExtensionsLoggingVersion"
        $baseName = Convert-NameCase -InputObject $PackageName -ToCase "Pascal"
        return "${baseName}Version"
    } else {
        # 回退：去掉点号，保留帕斯卡命名
        $baseName = $PackageName -replace '\.', ''
        return "${baseName}Version"
    }
}

<#
.SYNOPSIS
    在 Directory.Build.props 中添加新的版本变量
#>
function Add-BuildPropsVariable {
    param(
        [string]$VariableName,
        [string]$Version
    )

    $content = Get-Content $buildPropsPath -Raw -Encoding UTF8

    # 检查变量是否已存在
    if ($content -match "<$VariableName>") {
        return @{ Added = $false; Reason = "变量已存在" }
    }

    # 在最后一个版本变量后面插入（在 </PropertyGroup> 之前）
    # 找到最后一个 <...Version>...</...Version> 行
    $insertPattern = '(</[A-Za-z][A-Za-z0-9]*Version>\s*\r?\n)(?=\s*<!--|</PropertyGroup>)'
    $replacement = "`${1}    <$VariableName>$Version</$VariableName>`r`n"

    if ($content -match $insertPattern) {
        $newContent = $content -replace $insertPattern, $replacement
        if (-not $WhatIf) {
            [System.IO.File]::WriteAllText($buildPropsPath, $newContent, [System.Text.UTF8Encoding]::new($false))
        }
        return @{ Added = $true; VariableName = $VariableName; Version = $Version }
    }

    # 如果找不到合适的插入点，在第一个 </PropertyGroup> 前插入
    $insertPattern2 = '(\s*)(</PropertyGroup>)'
    $replacement2 = "`${1}    <$VariableName>$Version</$VariableName>`r`n`${1}  `${2}"

    if ($content -match $insertPattern2) {
        $newContent = $content -replace $insertPattern2, $replacement2
        if (-not $WhatIf) {
            [System.IO.File]::WriteAllText($buildPropsPath, $newContent, [System.Text.UTF8Encoding]::new($false))
        }
        return @{ Added = $true; VariableName = $VariableName; Version = $Version }
    }

    return @{ Added = $false; Reason = "未找到插入位置" }
}

<#
.SYNOPSIS
    扫描所有 .csproj 文件，找出硬编码的 PackageReference 版本号
    返回: { PackageName, Version, FilePath, Projects[] }
#>
function Find-HardcodedPackageVersions {
    $allCsproj = Get-CsprojFiles -rootDir $projectRoot -excludePaths @('backups', 'node_modules', 'bin', 'obj', '.git', 'BuildOutput')

    $hardcodedMap = @{}  # 包名 → { Version, Projects[] }

    foreach ($csproj in $allCsproj) {
        $content = Get-Content $csproj.FullName -Raw -Encoding UTF8

        # 匹配硬编码版本号的 PackageReference（Version 不以 $( 开头）
        $pattern = '<PackageReference\s+Include="([^"]+)"\s+Version="([^"]+)"\s*/>'
        $matches = [regex]::Matches($content, $pattern)

        foreach ($match in $matches) {
            $pkgName = $match.Groups[1].Value
            $version = $match.Groups[2].Value

            # 跳过已经是变量引用的
            if ($version.StartsWith('$(')) { continue }

            # 跳过框架内置包（如 System.Text.Json 在 net10.0 中内置）
            if ($pkgName -eq 'System.Text.Json') { continue }

            if (-not $hardcodedMap.ContainsKey($pkgName)) {
                $hardcodedMap[$pkgName] = @{
                    PackageName = $pkgName
                    Version = $version
                    Projects = @()
                }
            }

            # 同一个包在不同项目中可能有不同版本，记录所有
            $entry = $hardcodedMap[$pkgName]
            $entry.Projects += @{
                FilePath = $csproj.FullName
                Version = $version
            }

            # 如果版本不一致，标记
            if ($entry.Version -ne $version) {
                $entry.Version = "$($entry.Version)|$version"  # 标记多版本
            }
        }
    }

    return $hardcodedMap.Values | Sort-Object PackageName
}

<#
.SYNOPSIS
    替换 .csproj 中的硬编码版本号为变量引用
#>
function Convert-CsprojToVariableRef {
    param(
        [string]$FilePath,
        [string]$PackageName,
        [string]$VariableName
    )

    $content = Get-Content $FilePath -Raw -Encoding UTF8

    # 替换 <PackageReference Include="PackageName" Version="xxx" /> → <PackageReference Include="PackageName" Version="$(VarName)" />
    $pattern = '(<PackageReference\s+Include="' + [regex]::Escape($PackageName) + '"\s+)Version="[^"]+"(\s*/>)'
    $replacement = '${1}Version="$(' + $VariableName + ')"${2}'

    if ($content -match $pattern) {
        $newContent = $content -replace $pattern, $replacement
        if (-not $WhatIf) {
            [System.IO.File]::WriteAllText($FilePath, $newContent, [System.Text.UTF8Encoding]::new($false))
        }
        return $true
    }

    return $false
}

<#
.SYNOPSIS
    执行 dotnet build 验证编译
#>
function Test-Build {
    Write-Section "编译验证"

    Push-Location $projectRoot
    try {
        Write-Color "  正在执行 dotnet build ..." -ForegroundColor Gray
        $buildOutput = dotnet build $slnPath -c Debug --no-restore 2>&1
        $buildSuccess = $LASTEXITCODE -eq 0

        # 只输出关键信息
        $buildOutput | ForEach-Object {
            if ($_ -match "(error|Error|失败|FAILED|Build succeeded|生成成功|0 个错误)") {
                Write-Host "  $_"
            }
        }

        if ($buildSuccess) {
            Write-Color "  [成功] 编译通过！" -ForegroundColor Green
        }
        else {
            Write-Color "  [失败] 编译出错，请检查上面的错误信息" -ForegroundColor Red
        }

        return $buildSuccess
    }
    finally {
        Pop-Location
    }
}

# ============================================================
# 主逻辑
# ============================================================

function Invoke-ConvertHardcodedVersions {
    Write-Section "硬编码 NuGet 版本号转换工具"
    Write-Color "  项目根目录: $projectRoot" -ForegroundColor Gray
    Write-Color "  配置文件: Directory.Build.props" -ForegroundColor Gray
    if ($WhatIf) {
        Write-Color "  [预览模式] 不会实际修改任何文件" -ForegroundColor Yellow
    }
    Write-Host ""

    # ---- 步骤1: 扫描所有硬编码版本号 ----
    Write-Section "扫描硬编码的 PackageReference 版本号"

    $hardcodedPackages = Find-HardcodedPackageVersions
    Write-Color "  发现 $($hardcodedPackages.Count) 个硬编码版本号的包" -ForegroundColor Yellow

    # ---- 建立包名→变量名映射 ----
    $pkgVarMap = Get-PackageVariableMapping
    Write-Color "  从 Directory.Build.props 中解析到 $($pkgVarMap.Count) 个可能的包-变量映射关系" -ForegroundColor Gray

    # ---- 分类：已有变量 vs 需要新建变量 ----
    $hasVarPackages = @()   # 已有变量，直接替换
    $needVarPackages = @()  # 需要新建变量

    foreach ($pkg in $hardcodedPackages) {
        $varInfo = Find-VariableForPackage -PackageName $pkg.PackageName -VariableMap $pkgVarMap
        if ($null -ne $varInfo) {
            $hasVarPackages += [PSCustomObject]@{
                PackageName  = $pkg.PackageName
                Version      = $pkg.Version
                VarName      = $varInfo.VarName
                VarVersion   = $varInfo.CurrentVersion
                Projects     = $pkg.Projects
                ProjectCount = $pkg.Projects.Count
            }
        }
        else {
            $needVarPackages += [PSCustomObject]@{
                PackageName  = $pkg.PackageName
                Version      = $pkg.Version
                VarName      = Convert-PackageNameToVariableName -PackageName $pkg.PackageName
                Projects     = $pkg.Projects
                ProjectCount = $pkg.Projects.Count
            }
        }
    }

    # ---- 显示分类结果 ----
    Write-Section "分类统计"

    Write-Color "  [A] 已在 Directory.Build.props 中有对应变量（可直接替换）" -ForegroundColor Cyan
    Write-Host ""
    if ($hasVarPackages.Count -gt 0) {
        Write-Host ("  {0,-42} {1,-14} {2,-30} {3}" -f "包名", "当前版本", "对应变量", "引用项目数")
        Write-Host ("  $('-' * 95)")
        foreach ($pkg in $hasVarPackages) {
            $color = if ($pkg.Version -ne $pkg.VarVersion) { "Yellow" } else { "Gray" }
            Write-Host ("  {0,-42} {1,-14} {2,-30} {3}" -f $pkg.PackageName, $pkg.Version, "`$($($pkg.VarName)) = $($pkg.VarVersion)", $pkg.ProjectCount) -ForegroundColor $color
        }
    }
    else {
        Write-Color "  （无）" -ForegroundColor Gray
    }

    Write-Host ""
    Write-Color "  [B] 需要在 Directory.Build.props 中新建变量" -ForegroundColor Cyan
    Write-Host ""
    if ($needVarPackages.Count -gt 0) {
        Write-Host ("  {0,-42} {1,-14} {2,-30} {3}" -f "包名", "当前版本", "建议变量名", "引用项目数")
        Write-Host ("  $('-' * 95)")
        foreach ($pkg in $needVarPackages) {
            Write-Host ("  {0,-42} {1,-14} {2,-30} {3}" -f $pkg.PackageName, $pkg.Version, "`$($($pkg.VarName))", $pkg.ProjectCount) -ForegroundColor Yellow
        }
    }
    else {
        Write-Color "  （无）" -ForegroundColor Gray
    }

    Write-Host ""

    # 如果指定了包名过滤
    if (-not [string]::IsNullOrEmpty($PackageName)) {
        $pattern = "^$($PackageName -replace '\*', '.*')$"
        $hasVarPackages = $hasVarPackages | Where-Object { $_.PackageName -match $pattern }
        $needVarPackages = $needVarPackages | Where-Object { $_.PackageName -match $pattern }

        Write-Color "  过滤条件: $PackageName (匹配 A 类: $($hasVarPackages.Count) 个, B 类: $($needVarPackages.Count) 个)" -ForegroundColor Gray
    }

    $totalCount = $hasVarPackages.Count + $needVarPackages.Count
    if ($totalCount -eq 0) {
        Write-Color "  [信息] 没有需要转换的硬编码版本号！" -ForegroundColor Green
        return
    }

    if ($CheckOnly) {
        Write-Color "  [信息] 检查完成（-CheckOnly 模式，未执行转换）" -ForegroundColor Gray
        return
    }

    # ---- 步骤2: 执行转换 ----
    Write-Section "执行转换"

    $results = @()
    $convertIndex = 0

    # 2a) 处理 A 类：已有变量，直接替换 .csproj
    foreach ($pkg in $hasVarPackages) {
        $convertIndex++
        $pkgName = $pkg.PackageName
        $varName = $pkg.VarName

        # 交互模式：逐个确认
        $shouldConvert = $All
        if (-not $All -and -not $WhatIf) {
            $prompt = "  转换 [$convertIndex/$totalCount] $pkgName → `$$varName？"
            $prompt += " [Y/n]"
            $choice = Read-Host $prompt
            if ($choice -eq "" -or $choice -eq "y" -or $choice -eq "Y") {
                $shouldConvert = $true
            }
            else {
                $results += [PSCustomObject]@{
                    PackageName = $pkgName
                    OldVersion  = $pkg.Version
                    NewRef      = "`$($varName)"
                    Status      = "跳过"
                    Type        = "A"
                }
                continue
            }
        }

        if ($WhatIf) {
            Write-Color "  [预览] $pkgName : $($pkg.Version) → `$$varName (影响 $($pkg.ProjectCount) 个项目)" -ForegroundColor Yellow
            $results += [PSCustomObject]@{
                PackageName = $pkgName
                OldVersion  = $pkg.Version
                NewRef      = "`$($varName)"
                Status      = "预览"
                Type        = "A"
            }
            continue
        }

        # 执行替换
        $allSucceeded = $true
        $failedProjects = @()
        foreach ($proj in $pkg.Projects) {
            $success = Convert-CsprojToVariableRef -FilePath $proj.FilePath -PackageName $pkgName -VariableName $varName
            if (-not $success) {
                $allSucceeded = $false
                $failedProjects += $proj.FilePath
            }
        }

        if ($allSucceeded) {
            Write-Color "  [成功] $pkgName : $($pkg.Version) → `$$varName (已更新 $($pkg.ProjectCount) 个项目)" -ForegroundColor Green
            $results += [PSCustomObject]@{
                PackageName = $pkgName
                OldVersion  = $pkg.Version
                NewRef      = "`$($varName)"
                Status      = "成功"
                Type        = "A"
            }
        }
        else {
            Write-Color "  [失败] $pkgName : $($failedProjects.Count)/$($pkg.ProjectCount) 个项目替换失败" -ForegroundColor Red
            $results += [PSCustomObject]@{
                PackageName = $pkgName
                OldVersion  = $pkg.Version
                NewRef      = "`$($varName)"
                Status      = "失败"
                Type        = "A"
            }
        }
    }

    # 2b) 处理 B 类：需要新建变量
    foreach ($pkg in $needVarPackages) {
        $convertIndex++
        $pkgName = $pkg.PackageName
        $varName = $pkg.VarName
        $version = $pkg.Version

        # 如果版本不一致（多版本），取第一个版本
        if ($version -match '\|') {
            $versions = $version -split '\|'
            $version = $versions[0]
            Write-Color "  [警告] $pkgName 存在多个版本 ($($versions -join ', '))，使用 $version" -ForegroundColor Yellow
        }

        # 交互模式：逐个确认
        $shouldConvert = $All
        if (-not $All -and -not $WhatIf) {
            $prompt = "  新建 [$convertIndex/$totalCount] `$$varName = $version（用于 $pkgName）？"
            $prompt += " [Y/n]"
            $choice = Read-Host $prompt
            if ($choice -eq "" -or $choice -eq "y" -or $choice -eq "Y") {
                $shouldConvert = $true
            }
            else {
                $results += [PSCustomObject]@{
                    PackageName = $pkgName
                    OldVersion  = $version
                    NewRef      = "`$($varName)"
                    Status      = "跳过"
                    Type        = "B"
                }
                continue
            }
        }

        if ($WhatIf) {
            Write-Color "  [预览] 新建 `$$varName = $version（用于 $pkgName，影响 $($pkg.ProjectCount) 个项目）" -ForegroundColor Yellow
            $results += [PSCustomObject]@{
                PackageName = $pkgName
                OldVersion  = $version
                NewRef      = "`$($varName)"
                Status      = "预览"
                Type        = "B"
            }
            continue
        }

        # 1) 在 Directory.Build.props 中添加变量
        $addResult = Add-BuildPropsVariable -VariableName $varName -Version $version
        if (-not $addResult.Added) {
            Write-Color "  [失败] $pkgName : 无法在 Directory.Build.props 中添加变量 `$$varName - $($addResult.Reason)" -ForegroundColor Red
            $results += [PSCustomObject]@{
                PackageName = $pkgName
                OldVersion  = $version
                NewRef      = "`$($varName)"
                Status      = "失败"
                Type        = "B"
            }
            continue
        }

        # 2) 替换所有 .csproj 中的硬编码版本号
        $allSucceeded = $true
        $failedProjects = @()
        foreach ($proj in $pkg.Projects) {
            $success = Convert-CsprojToVariableRef -FilePath $proj.FilePath -PackageName $pkgName -VariableName $varName
            if (-not $success) {
                $allSucceeded = $false
                $failedProjects += $proj.FilePath
            }
        }

        if ($allSucceeded) {
            Write-Color "  [成功] 新建 `$$varName = $version（用于 $pkgName，已更新 $($pkg.ProjectCount) 个项目）" -ForegroundColor Green
            $results += [PSCustomObject]@{
                PackageName = $pkgName
                OldVersion  = $version
                NewRef      = "`$($varName)"
                Status      = "成功"
                Type        = "B"
            }
        }
        else {
            Write-Color "  [失败] $pkgName : 变量已创建，但 $($failedProjects.Count)/$($pkg.ProjectCount) 个项目替换失败" -ForegroundColor Red
            $results += [PSCustomObject]@{
                PackageName = $pkgName
                OldVersion  = $version
                NewRef      = "`$($varName)"
                Status      = "部分成功"
                Type        = "B"
            }
        }
    }

    # ---- 步骤3: 恢复包 ----
    if (-not $WhatIf -and ($results | Where-Object { $_.Status -eq "成功" -or $_.Status -eq "部分成功" }).Count -gt 0) {
        Write-Section "恢复 NuGet 包"
        Write-Color "  正在执行 dotnet restore ..." -ForegroundColor Gray
        Push-Location $projectRoot
        try {
            dotnet restore $slnPath 2>&1 | Out-Null
            if ($LASTEXITCODE -eq 0) {
                Write-Color "  [成功] 包恢复完成" -ForegroundColor Green
            }
            else {
                Write-Color "  [警告] 包恢复可能存在问题" -ForegroundColor Yellow
            }
        }
        finally {
            Pop-Location
        }
    }

    # ---- 步骤4: 编译验证 ----
    $buildSuccess = $true
    if (-not $SkipBuild -and -not $WhatIf -and ($results | Where-Object { $_.Status -eq "成功" -or $_.Status -eq "部分成功" }).Count -gt 0) {
        $buildSuccess = Test-Build
    }
    elseif ($WhatIf) {
        Write-Color "  [预览模式] 跳过编译验证" -ForegroundColor Yellow
    }
    elseif ($SkipBuild) {
        Write-Color "  [信息] 已跳过编译验证（-SkipBuild）" -ForegroundColor Gray
    }

    # ---- 步骤5: 输出报告 ----
    Write-Section "转换摘要"

    $successCount = ($results | Where-Object { $_.Status -eq "成功" }).Count
    $partialCount = ($results | Where-Object { $_.Status -eq "部分成功" }).Count
    $skipCount = ($results | Where-Object { $_.Status -eq "跳过" }).Count
    $failCount = ($results | Where-Object { $_.Status -eq "失败" }).Count
    $previewCount = ($results | Where-Object { $_.Status -eq "预览" }).Count

    $aCount = ($results | Where-Object { $_.Type -eq "A" }).Count
    $bCount = ($results | Where-Object { $_.Type -eq "B" }).Count

    Write-Color "  总计: $($results.Count) 个包" -ForegroundColor White
    Write-Color "    A类（已有变量）: $aCount" -ForegroundColor Cyan
    Write-Color "    B类（新建变量）: $bCount" -ForegroundColor Cyan
    Write-Host ""
    if ($successCount -gt 0) { Write-Color "  成功: $successCount" -ForegroundColor Green }
    if ($partialCount -gt 0) { Write-Color "  部分成功: $partialCount" -ForegroundColor Yellow }
    if ($skipCount -gt 0) { Write-Color "  跳过: $skipCount" -ForegroundColor Yellow }
    if ($failCount -gt 0) { Write-Color "  失败: $failCount" -ForegroundColor Red }
    if ($previewCount -gt 0) { Write-Color "  预览: $previewCount" -ForegroundColor Yellow }

    if ($buildSuccess -eq $false) {
        Write-Color "  编译验证: 失败 ❌" -ForegroundColor Red
    }
    elseif (-not $SkipBuild -and -not $WhatIf -and ($successCount + $partialCount) -gt 0) {
        Write-Color "  编译验证: 通过 ✅" -ForegroundColor Green
    }

    Write-Host ""
    Write-Color "  $('=' * 56)" -ForegroundColor Cyan
    Write-Color "  转换完成！" -ForegroundColor Green
    Write-Color "  $('=' * 56)" -ForegroundColor Cyan
}

# ============================================================
# 入口
# ============================================================

Invoke-ConvertHardcodedVersions
