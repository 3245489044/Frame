<#
.SYNOPSIS
    NuGet 包批量升级工具 — 自动扫描所有 .csproj 并升级 Directory.Build.props 中的版本号

.DESCRIPTION
    扫描项目中所有 .csproj 文件的 NuGet 包引用，检测可升级的包，
    自动更新 Directory.Build.props 中的集中式版本号定义，并执行编译验证。

    版本管理模式：版本号定义在 Directory.Build.props 中，各 .csproj 通过 $(VarName) 引用

.PARAMETER All
    一键升级所有可升级的包，无需逐个确认

.PARAMETER CheckOnly
    仅检查可升级的包并列出，不执行实际升级

.PARAMETER PackageName
    指定要升级的包名（支持通配符 *），例如：
    -PackageName "Newtonsoft.Json"
    -PackageName "Microsoft.Extensions.*"

.PARAMETER SkipBuild
    跳过升级后的编译验证

.PARAMETER WhatIf
    预览将要执行的变更，不实际修改文件

.PARAMETER Concurrency
    并行扫描的并发数（默认 4），扫描 60+ 项目时建议 4-8

.PARAMETER VulnerableCheck
    检查有已知安全漏洞的 NuGet 包（NU1901-NU1904），不执行升级

.EXAMPLE
    # 交互模式：列出可升级包，逐个确认
    .\Update-NuGetPackages.ps1

.EXAMPLE
    # 一键全升级
    .\Update-NuGetPackages.ps1 -All

.EXAMPLE
    # 仅检查可升级包
    .\Update-NuGetPackages.ps1 -CheckOnly

.EXAMPLE
    # 升级指定包
    .\Update-NuGetPackages.ps1 -PackageName "Newtonsoft.Json"

.EXAMPLE
    # 预览变更
    .\Update-NuGetPackages.ps1 -All -WhatIf

.EXAMPLE
    # 检查有安全漏洞的包
    .\Update-NuGetPackages.ps1 -VulnerableCheck
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
    [int]$Concurrency = 4,

    [Parameter()]
    [switch]$VulnerableCheck
)

# ============================================================
# 初始化
# ============================================================

$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Resolve-Path (Join-Path $scriptPath "..\..")
$buildPropsPath = Join-Path $projectRoot "Directory.Build.props"
$versionsJsonPath = Join-Path $projectRoot "versions.json"
$generateScriptPath = Join-Path $scriptPath "Generate-BuildProps.ps1"
$slnPath = Join-Path $projectRoot "CsharpPrj\MainProj.sln"

# 加载模块（复用模块化的 csproj 遍历等基础函数）
$moduleDir = Resolve-Path (Join-Path $scriptPath "..\Modules")
$global:SkipModules = @{
    'BuildOutput\BuildOutputCleaner.psm1' = $true
    'FileDriven\FileDrivenTestUtils.ps1'  = $true
    'FileDriven\ScriptRunner.ps1'         = $true
}
. "$moduleDir\Import-Modules.ps1"
Remove-Variable -Name 'SkipModules' -Scope Global -ErrorAction SilentlyContinue

# 颜色定义
$style = @{
    Title    = @{ FG = "Cyan"; BG = "Black" }
    Success  = @{ FG = "Green"; BG = "Black" }
    Warning  = @{ FG = "Yellow"; BG = "Black" }
    Error    = @{ FG = "Red"; BG = "Black" }
    Info     = @{ FG = "Gray"; BG = "Black" }
    Highlight = @{ FG = "White"; BG = "Black" }
}

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

# ============================================================
# 辅助函数
# ============================================================

function Get-RelativePath {
    param([string]$Path, [string]$BasePath)
    if (-not $BasePath.EndsWith('\')) { $BasePath += '\' }
    if ($Path -like "$BasePath*") {
        return $Path.Substring($BasePath.Length)
    }
    return $Path
}

function Get-OutdatedPackages {
    <#
    .SYNOPSIS
        对指定 .csproj 执行 dotnet list package --outdated，返回可升级包列表
    #>
    param([string]$ProjectPath)

    $result = @()

    try {
        $output = dotnet list $ProjectPath package --outdated 2>&1
        $lines = $output -split "`n"

        $currentProject = ""
        $inSection = $false
        foreach ($line in $lines) {
            $trimmed = $line.Trim()

            # 检测项目标题行: Project `XXX` has the following updates to its packages
            if ($trimmed -match "^Project\s+'([^']+)'\s+has the following updates") {
                $currentProject = $matches[1]
                $inSection = $false
                continue
            }

            # 检测 "has no updates" 行
            if ($trimmed -match "^Project\s+'([^']+)'\s+has no updates") {
                $currentProject = ""
                $inSection = $false
                continue
            }

            # 检测输出节开始 [netX.Y-...] 或 [netX.Y-platform...]
            if ($trimmed -match "^\[net") {
                $inSection = $true
                continue
            }

            if (-not $inSection) { continue }

            # 跳过空行、分隔线和表头
            if ([string]::IsNullOrWhiteSpace($trimmed) -or $trimmed -match "^[-=]+$" -or $trimmed -match "^(Top-level|Package|---)") {
                continue
            }

            # 解析包信息行：> 包名  Requested  Resolved  Latest
            if ($trimmed -match "^>\s+(\S+)\s+(\S+)\s+(\S+)\s+(\S+)") {
                $result += [PSCustomObject]@{
                    PackageName      = $matches[1]
                    RequestedVersion = $matches[2]
                    LatestVersion    = $matches[4]
                    ProjectPath      = $ProjectPath
                    ProjectName      = $currentProject
                }
            }
        }
    }
    catch {
        Write-Warning "[扫描失败] $(Get-RelativePath -Path $ProjectPath -BasePath $projectRoot) : $($_.Exception.Message)"
    }

    return $result
}

function Get-BuildPropsVersionMap {
    <#
    .SYNOPSIS
        从 versions.json 读取版本映射，返回 { 变量名 → 版本号 } 的映射表
    #>
    if (-not (Test-Path $versionsJsonPath)) {
        Write-Color "  [警告] 找不到 versions.json: $versionsJsonPath" -ForegroundColor $style.Warning.FG
        return @{}
    }

    try {
        $versionsJson = Get-Content $versionsJsonPath -Raw -Encoding UTF8 | ConvertFrom-Json
    } catch {
        Write-Color "  [错误] 无法解析 versions.json: $($_.Exception.Message)" -ForegroundColor $style.Error.FG
        return @{}
    }

    $map = @{}
    foreach ($prop in $versionsJson.PSObject.Properties) {
        $varName = $prop.Name + "Version"
        $map[$varName] = $prop.Value
    }

    return $map
}

function Update-BuildPropsVersion {
    <#
    .SYNOPSIS
        更新 versions.json 中指定包的版本号，然后重新生成 .props 文件
    #>
    param(
        [string]$VariableName,
        [string]$NewVersion
    )

    if (-not (Test-Path $versionsJsonPath)) {
        return @{ Updated = $false; Error = "versions.json 不存在" }
    }

    # 从变量名反推包名（去掉 Version 后缀）
    if (-not $VariableName.EndsWith("Version")) {
        return @{ Updated = $false; Error = "变量名必须以 Version 结尾" }
    }
    $packageKey = $VariableName.Substring(0, $VariableName.Length - "Version".Length)

    try {
        # 读取当前 versions.json
        $versionsJson = Get-Content $versionsJsonPath -Raw -Encoding UTF8 | ConvertFrom-Json
        $oldVersion = $versionsJson.$packageKey

        if (-not $oldVersion) {
            return @{ Updated = $false; Error = "在 versions.json 中找不到键 '$packageKey'" }
        }

        # 更新版本号
        if (-not $WhatIf) {
            # 使用 PowerShell 更新 JSON 对象
            $versionsJson.$packageKey = $NewVersion

            # 序列化为 JSON（保持排序和格式）
            $sortedProperties = $versionsJson.PSObject.Properties | Sort-Object Name
            $orderedJson = @{}
            foreach ($prop in $sortedProperties) {
                $orderedJson[$prop.Name] = $prop.Value
            }

            $jsonContent = $orderedJson | ConvertTo-Json -Depth 3
            $utf8NoBom = New-Object System.Text.UTF8Encoding $false
            [System.IO.File]::WriteAllText($versionsJsonPath, $jsonContent, $utf8NoBom)

            # 重新生成 .props 文件
            & $generateScriptPath
        }

        return @{
            Updated    = $true
            OldVersion = $oldVersion
            NewVersion = $NewVersion
        }
    }
    catch {
        return @{ Updated = $false; Error = $_.Exception.Message }
    }
}

function Get-PackageVariableMapping {
    <#
    .SYNOPSIS
        从 Directory.Build.props 及其 Import 的 generated 文件解析所有版本变量，
        建立 包名 → 变量名 的映射
    #>
    $mapping = @{}

    # 收集所有需要扫描的 props 文件
    $filesToScan = @()
    if (Test-Path $buildPropsPath) {
        $filesToScan += $buildPropsPath
    }

    # 也扫描 generated 目录下的自动生成文件（版本号实际定义在此）
    $generatedPropsDir = Join-Path (Split-Path $buildPropsPath) "generated"
    if (Test-Path $generatedPropsDir) {
        $generatedPropsFiles = Get-ChildItem $generatedPropsDir -Filter "*.props" -File
        foreach ($genFile in $generatedPropsFiles) {
            $filesToScan += $genFile.FullName
        }
    }

    if ($filesToScan.Count -eq 0) {
        return $mapping
    }

    # 匹配 <SomeNameVersion>1.2.3</SomeNameVersion>
    $pattern = '<([A-Za-z][A-Za-z0-9]*)>\s*([\d]+\.[\d]+\.[\d]+(?:-[a-zA-Z0-9.-]+)?)\s*</\1>'

    foreach ($filePath in $filesToScan) {
        $content = Get-Content $filePath -Raw -Encoding UTF8
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
    }

    return $mapping
}

function Find-VariableForPackage {
    <#
    .SYNOPSIS
        根据包名在变量映射中查找对应的变量信息
        使用精确匹配 → 去点匹配 → 完整段匹配 的优先级
    #>
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
    #    例如 Microsoft.Extensions.Logging → MicrosoftExtensionsLogging
    #    但 Microsoft.Extensions.Logging.Console 不应匹配 MicrosoftExtensionsLogging
    $segments = $PackageName -split '\.'
    foreach ($key in $VariableMap.Keys) {
        $keyNoDot = $key -replace '\.', ''
        # 检查变量映射的包名是否由相同的段组成（顺序一致）
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

    # 4. 完整去点段匹配：去点后检查段数一致
    foreach ($key in $VariableMap.Keys) {
        $keyNoDot = $key -replace '\.', ''
        $keySegments = $key -split '\.'
        if ($keySegments.Count -eq $segments.Count -and $keyNoDot -eq $noDot) {
            return $VariableMap[$key]
        }
    }

    return $null
}



function Test-Build {
    <#
    .SYNOPSIS
        执行 dotnet build 验证编译
    #>
    Write-Section "编译验证"

    Push-Location $projectRoot
    try {
        Write-Color "  正在执行 dotnet build ..." -ForegroundColor $style.Info.FG
        $buildOutput = dotnet build $slnPath -c Debug --no-restore 2>&1
        $buildSuccess = $LASTEXITCODE -eq 0

        # 只输出关键信息
        $buildOutput | ForEach-Object {
            if ($_ -match "(error|Error|失败|FAILED|Build succeeded|生成成功|0 个错误)") {
                Write-Host "  $_"
            }
        }

        if ($buildSuccess) {
            Write-Color "  [成功] 编译通过！" -ForegroundColor $style.Success.FG
        }
        else {
            Write-Color "  [失败] 编译出错，请检查上面的错误信息" -ForegroundColor $style.Error.FG
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

function Invoke-UpdateNuGetPackages {
    Write-Section "NuGet 包批量升级工具"
    Write-Color "  项目根目录: $projectRoot" -ForegroundColor $style.Info.FG
    Write-Color "  配置文件: Directory.Build.props" -ForegroundColor $style.Info.FG
    if ($WhatIf) {
        Write-Color "  [预览模式] 不会实际修改任何文件" -ForegroundColor $style.Warning.FG
    }
    Write-Host ""

    # ---- 步骤0: 安全漏洞检查模式 ----
    if ($VulnerableCheck) {
        Write-Section "扫描有安全漏洞的 NuGet 包"

        Write-Color "  正在对解决方案执行 dotnet list package --vulnerable ..." -ForegroundColor $style.Info.FG

        $allVulnerabilities = @()
        try {
            Push-Location $projectRoot
            $output = dotnet list $slnPath package --vulnerable 2>&1
            Pop-Location

            $lines = $output -split "`n"
            foreach ($line in $lines) {
                $trimmed = $line.Trim()

                # 匹配 NU1901-NU1904 警告行
                # 格式: <csproj_path> : warning NU190X: Package '<name>' <version> has a known <severity> severity vulnerability, <url> [<sln_path>]
                if ($trimmed -match "^([A-Za-z]:\\.+?\.csproj)\s*:\s*warning NU190[1-4]:\s+Package\s+'([^']+)'\s+(\S+)\s+has a known\s+(\S+)\s+severity") {
                    $projPath = $matches[1]
                    $pkgName = $matches[2]
                    $pkgVersion = $matches[3]
                    $severity = $matches[4]

                    $allVulnerabilities += [PSCustomObject]@{
                        PackageName    = $pkgName
                        Version        = $pkgVersion
                        Severity       = $severity
                        ProjectPath    = $projPath
                        ProjectRelPath = Get-RelativePath -Path $projPath -BasePath $projectRoot
                        AdvisoryUrl    = if ($trimmed -match "(https://github.com/advisories/\S+)") { $matches[1] } else { "" }
                    }
                }
            }
        }
        catch {
            Pop-Location
            Write-Color "  [错误] dotnet list 执行失败: $($_.Exception.Message)" -ForegroundColor $style.Error.FG
            return
        }

        # 去重并汇总
        if ($allVulnerabilities.Count -eq 0) {
            Write-Color "  [成功] 未发现已知安全漏洞！" -ForegroundColor $style.Success.FG
            return
        }

        Write-Host ""
        Write-Host ("  {0,-40} {1,-12} {2,-10} {3}" -f "包名", "版本", "严重程度", "引用项目数")
        Write-Host ("  $('-' * 82)")

        $grouped = $allVulnerabilities | Group-Object PackageName | Sort-Object { $_.Group[0].Severity } -Descending
        $severityColor = @{
            "critical" = "Red"
            "high"     = "Red"
            "moderate" = "Yellow"
            "low"      = "Gray"
            "unknown"  = "Gray"
        }

        foreach ($group in $grouped) {
            $first = $group.Group[0]
            $color = $severityColor[$first.Severity]
            if (-not $color) { $color = "White" }
            $projectCount = ($group.Group | ForEach-Object { $_.ProjectRelPath } | Select-Object -Unique).Count
            Write-Host ("  {0,-40} {1,-12} {2,-10} {3}" -f $first.PackageName, $first.Version, $first.Severity, $projectCount) -ForegroundColor $color
        }

        Write-Host ("  $('-' * 82)")
        Write-Color "  总计: $($grouped.Count) 个有漏洞的包" -ForegroundColor $style.Warning.FG

        # 显示详细引用信息
        Write-Host ""
        Write-Color "  详细引用信息:" -ForegroundColor $style.Info.FG
        foreach ($group in $grouped) {
            $first = $group.Group[0]
            $color = $severityColor[$first.Severity]
            Write-Host ("  [{0}] {1} ({2})" -f $first.Severity.Substring(0,1).ToUpper(), $first.PackageName, $first.Version) -ForegroundColor $color
            $uniqueProjects = $group.Group | ForEach-Object { $_.ProjectRelPath } | Select-Object -Unique | Sort-Object
            foreach ($proj in $uniqueProjects) {
                Write-Host ("       └─ $proj") -ForegroundColor Gray
            }
            if ($first.AdvisoryUrl) {
                Write-Host ("       漏洞详情: $($first.AdvisoryUrl)") -ForegroundColor DarkGray
            }
        }

        Write-Host ""
        Write-Color "  [提示] 使用 dotnet list <项目> package --vulnerable 查看详细信息" -ForegroundColor $style.Info.FG
        Write-Color "  [提示] 部分漏洞可能没有可用的修复版本，请参考 NuGet 官方建议" -ForegroundColor $style.Info.FG
        return
    }

    # ---- 步骤1: 扫描所有可升级的 NuGet 包 ----
    Write-Section "扫描可升级的 NuGet 包"

    # 1a) 解析 .sln 中的项目路径（用于后续排除）
    $slnProjects = @()
    if (Test-Path $slnPath) {
        $slnContent = Get-Content $slnPath -Raw
        $slnMatches = [regex]::Matches($slnContent, 'Project\("[^"]+"\)\s*=\s*"[^"]*",\s*"([^"]+\.csproj)"')
        foreach ($m in $slnMatches) {
            $relPath = $m.Groups[1].Value
            $fullPath = [System.IO.Path]::GetFullPath((Join-Path $projectRoot $relPath))
            $slnProjects += $fullPath
        }
    }
    Write-Color "  解决方案包含 $($slnProjects.Count) 个项目" -ForegroundColor $style.Info.FG

    # 1b) 解决方案级单次查询（快路径）
    Write-Color "  正在对解决方案执行 dotnet list package --outdated ..." -ForegroundColor $style.Info.FG

    $allOutdated = @()
    try {
        Push-Location $projectRoot
        $output = dotnet list $slnPath package --outdated --no-restore 2>&1
        Pop-Location
    } catch {
        Pop-Location
        Write-Color "  [错误] dotnet list 执行失败: $($_.Exception.Message)" -ForegroundColor $style.Error.FG
        return
    }

    $lines = $output -split "`n"
    $currentProject = ""
    $inSection = $false

    foreach ($line in $lines) {
        $trimmed = $line.Trim()
        if ($trimmed -match "^Project\s+'([^']+)'\s+has the following updates") {
            $currentProject = $matches[1]
            $inSection = $false
            continue
        }
        if ($trimmed -match "^Project\s+'([^']+)'\s+has no updates") {
            $currentProject = ""
            $inSection = $false
            continue
        }
        if ($trimmed -match "^\[net") {
            $inSection = $true
            continue
        }
        if (-not $inSection) { continue }
        if ([string]::IsNullOrWhiteSpace($trimmed) -or $trimmed -match "^[-=]+$" -or $trimmed -match "^(Top-level|Package|---)") {
            continue
        }
        if ($trimmed -match "^>\s+(\S+)\s+(\S+)\s+(\S+)\s+(\S+)") {
            $allOutdated += [PSCustomObject]@{
                PackageName      = $matches[1]
                RequestedVersion = $matches[2]
                LatestVersion    = $matches[4]
                ProjectPath      = $currentProject
                ProjectName      = $currentProject
            }
        }
    }

    # 1c) 补全不在 .sln 中的项目（慢路径）
    $allCsproj = Get-CsprojFiles -rootDir $projectRoot -excludePaths @('backups', 'node_modules', 'bin', 'obj', '.git', 'BuildOutput') | ForEach-Object { $_.FullName }
    $extraCsproj = $allCsproj | Where-Object { $_ -notin $slnProjects }

    if ($extraCsproj.Count -gt 0) {
        Write-Color "  发现 $($extraCsproj.Count) 个不在 .sln 中的项目，正在补全扫描..." -ForegroundColor $style.Warning.FG

        # 使用 Runspace 线程池并发扫描
        $runspacePool = [runspacefactory]::CreateRunspacePool(1, $Concurrency)
        $runspacePool.Open()
        $jobs = @()

        foreach ($csproj in $extraCsproj) {
            $ps = [powershell]::Create()
            $ps.RunspacePool = $runspacePool
            $null = $ps.AddScript({
                param($path, $root)
                Push-Location $root
                try {
                    $out = dotnet list $path package --outdated --no-restore 2>&1
                    Pop-Location
                    $result = @()
                    $curProj = ""
                    $inSec = $false
                    foreach ($l in ($out -split "`n")) {
                        $t = $l.Trim()
                        if ($t -match "^Project\s+'([^']+)'\s+has the following updates") { $curProj = $matches[1]; $inSec = $false; continue }
                        if ($t -match "^Project\s+'([^']+)'\s+has no updates") { $curProj = ""; $inSec = $false; continue }
                        if ($t -match "^\[net") { $inSec = $true; continue }
                        if (-not $inSec) { continue }
                        if ([string]::IsNullOrWhiteSpace($t) -or $t -match "^[-=]+$" -or $t -match "^(Top-level|Package|---)") { continue }
                        if ($t -match "^>\s+(\S+)\s+(\S+)\s+(\S+)\s+(\S+)") {
                            $result += [PSCustomObject]@{
                                PackageName      = $matches[1]
                                RequestedVersion = $matches[2]
                                LatestVersion    = $matches[4]
                                ProjectPath      = $path
                                ProjectName      = $curProj
                            }
                        }
                    }
                    return $result
                } catch {
                    Pop-Location
                    return @()
                }
            }).AddArgument($csproj).AddArgument($projectRoot)
            $jobs += @{ Handle = $ps.BeginInvoke(); PS = $ps; Path = $csproj }
        }

        # 收集结果
        foreach ($job in $jobs) {
            try {
                $extraResult = $job.PS.EndInvoke($job.Handle)
                if ($extraResult) { $allOutdated += $extraResult }
            } catch {
                Write-Warning "  扫描失败: $(Get-RelativePath -Path $job.Path -BasePath $projectRoot)"
            }
            $job.PS.Dispose()
        }
        $runspacePool.Close()
        $runspacePool.Dispose()
    }

    Write-Color "  扫描完成，共扫描 $($allCsproj.Count) 个项目" -ForegroundColor $style.Success.FG

    # 去重（同一个包可能被多个项目引用）
    $uniquePackages = $allOutdated | Group-Object PackageName | ForEach-Object {
        $first = $_.Group[0]
        $projects = $_.Group | ForEach-Object { $_.ProjectPath } | Select-Object -Unique
        [PSCustomObject]@{
            PackageName      = $first.PackageName
            RequestedVersion = $first.RequestedVersion
            LatestVersion    = $first.LatestVersion
            ProjectCount     = $projects.Count
            Projects         = $projects
        }
    } | Sort-Object PackageName

    # ---- 建立包名→变量名映射 ----
    Write-Section "建立版本变量映射"

    $pkgVarMap = Get-PackageVariableMapping
    Write-Color "  从 Directory.Build.props 中解析到 $($pkgVarMap.Count) 个可能的包-变量映射关系" -ForegroundColor $style.Info.FG

    # ---- 显示 Directory.Build.props 中所有包的版本状态 ----
    Write-Section "Directory.Build.props 包版本状态"

    $propsVersionMap = Get-BuildPropsVersionMap
    $propsPkgCount = 0
    $upToDateCount = 0
    $outdatedCount = 0

    Write-Host ("  {0,-42} {1,-14} {2,-14} {3}" -f "变量名", "当前版本", "最新版本", "状态")
    Write-Host ("  $('-' * 82)")

    foreach ($varName in ($propsVersionMap.Keys | Sort-Object)) {
        $currentVer = $propsVersionMap[$varName]
        $propsPkgCount++

        # 通过变量名反向查找包名
        $matchedPkg = $null
        foreach ($pkg in $uniquePackages) {
            $varInfo = Find-VariableForPackage -PackageName $pkg.PackageName -VariableMap $pkgVarMap
            if ($null -ne $varInfo -and $varInfo.VarName -eq $varName) {
                $matchedPkg = $pkg
                break
            }
        }


        if ($null -ne $matchedPkg) {
            $latestVer = $matchedPkg.LatestVersion
            Write-Host ("  {0,-42} {1,-14} {2,-14} {3}" -f $varName, $currentVer, $latestVer, "🔄 可升级") -ForegroundColor Yellow
            $outdatedCount++
        }
        else {
            Write-Host ("  {0,-42} {1,-14} {2,-14} {3}" -f $varName, $currentVer, "-", "✅ 已最新") -ForegroundColor Green
            $upToDateCount++
        }
    }

    Write-Host ("  $('-' * 82)")
    Write-Host ("  总计: $propsPkgCount 个包 | 可升级: $outdatedCount | 已最新: $upToDateCount") -ForegroundColor White
    Write-Host ""

    # 如果指定了包名过滤
    if (-not [string]::IsNullOrEmpty($PackageName)) {
        $pattern = "^$($PackageName -replace '\*', '.*')$"
        $filtered = $uniquePackages | Where-Object { $_.PackageName -match $pattern }

        if ($filtered.Count -eq 0) {
            Write-Color "  [警告] 没有找到匹配 '$PackageName' 的可升级包" -ForegroundColor $style.Warning.FG
            return
        }

        Write-Color "  过滤条件: $PackageName (匹配 $($filtered.Count) 个包)" -ForegroundColor $style.Info.FG
        $uniquePackages = $filtered
    }

    # 显示可升级包列表
    Write-Section "可升级包列表"
    $index = 1
    foreach ($pkg in $uniquePackages) {
        $color = if ($pkg.ProjectCount -gt 1) { $style.Warning.FG } else { $style.Highlight.FG }
        Write-Color ("  [{0,2}] {1,-40} {2,-12} → {3,-12} (引用数: {4})" -f $index, $pkg.PackageName, $pkg.RequestedVersion, $pkg.LatestVersion, $pkg.ProjectCount) -ForegroundColor $color
        $index++
    }
    Write-Host ""

    if ($CheckOnly) {
        Write-Color "  [信息] 检查完成（-CheckOnly 模式，未执行升级）" -ForegroundColor $style.Info.FG
        return
    }

    if ($uniquePackages.Count -eq 0) {
        Write-Color "  [信息] 所有 NuGet 包已是最新版本！" -ForegroundColor $style.Success.FG
        return
    }

    # ---- 步骤2: 执行升级 ----
    Write-Section "执行升级"

    $results = @()
    $upgradeIndex = 0

    foreach ($pkg in $uniquePackages) {
        $upgradeIndex++
        $pkgName = $pkg.PackageName
        $oldVer = $pkg.RequestedVersion
        $newVer = $pkg.LatestVersion

        # 查找对应的变量名
        $varInfo = Find-VariableForPackage -PackageName $pkgName -VariableMap $pkgVarMap


        $isCentralManaged = $null -ne $varInfo

        # 交互模式：逐个确认
        $shouldUpgrade = $All
        if (-not $All -and -not $WhatIf) {
            $prompt = "  升级 [$upgradeIndex/$($uniquePackages.Count)] $pkgName ($oldVer → $newVer)？"
            if ($isCentralManaged) {
                $prompt += " [变量: $($varInfo.VarName)]"
            }
            $prompt += " [Y/n]"

            $choice = Read-Host $prompt
            if ($choice -eq "" -or $choice -eq "y" -or $choice -eq "Y") {
                $shouldUpgrade = $true
            }
            else {
                $results += [PSCustomObject]@{
                    PackageName = $pkgName
                    OldVersion  = $oldVer
                    NewVersion  = $newVer
                    Status      = "跳过"
                }
                continue
            }
        }

        if ($WhatIf) {
            if ($isCentralManaged) {
                Write-Color "  [预览] $pkgName : `$$($varInfo.VarName) = $oldVer → $newVer" -ForegroundColor $style.Warning.FG
            }
            else {
                Write-Color "  [预览] $pkgName : $oldVer → $newVer (未在 Directory.Build.props 中找到对应变量)" -ForegroundColor $style.Warning.FG
            }
            $results += [PSCustomObject]@{
                PackageName = $pkgName
                OldVersion  = $oldVer
                NewVersion  = $newVer
                Status      = "预览"
            }
            continue
        }

        if ($isCentralManaged) {
            # 集中式管理：更新 Directory.Build.props
            $updateResult = Update-BuildPropsVersion -VariableName $varInfo.VarName -NewVersion $newVer

            if ($updateResult.Updated) {
                Write-Color "  [成功] $pkgName : `$$($varInfo.VarName) = $oldVer → $newVer" -ForegroundColor $style.Success.FG
                $results += [PSCustomObject]@{
                    PackageName = $pkgName
                    OldVersion  = $oldVer
                    NewVersion  = $newVer
                    Status      = "成功"
                }
            }
            else {
                Write-Color "  [失败] $pkgName : 未在 Directory.Build.props 中找到变量 `$$($varInfo.VarName)" -ForegroundColor $style.Error.FG
                $results += [PSCustomObject]@{
                    PackageName = $pkgName
                    OldVersion  = $oldVer
                    NewVersion  = $newVer
                    Status      = "失败"
                }
            }
        }
        else {
            # 未找到对应变量，版本号内联在 .csproj 中
            # 遍历所有引用了该包的项目，逐个执行 dotnet add package
            Write-Color "  [信息] $pkgName : 未在 Directory.Build.props 中找到对应变量，逐个升级引用的 .csproj ..." -ForegroundColor $style.Info.FG

            $allSucceeded = $true
            $failedProjects = @()

            foreach ($projPath in $pkg.Projects) {
                try {
                    Push-Location $projectRoot
                    Write-Color "    处理: $(Get-RelativePath -Path $projPath -BasePath $projectRoot)" -ForegroundColor $style.Info.FG
                    $addOutput = dotnet add "$projPath" package $pkgName --version $newVer 2>&1
                    $projSuccess = $LASTEXITCODE -eq 0
                    Pop-Location

                    if (-not $projSuccess) {
                        $allSucceeded = $false
                        $failedProjects += $projPath
                        Write-Color "    [失败] $(Get-RelativePath -Path $projPath -BasePath $projectRoot)" -ForegroundColor $style.Error.FG
                    }
                }
                catch {
                    Pop-Location
                    $allSucceeded = $false
                    $failedProjects += $projPath
                    Write-Color "    [失败] $(Get-RelativePath -Path $projPath -BasePath $projectRoot) : $($_.Exception.Message)" -ForegroundColor $style.Error.FG
                }
            }

            if ($allSucceeded) {
                Write-Color "  [成功] $pkgName : $oldVer → $newVer (已更新 $($pkg.Projects.Count) 个项目)" -ForegroundColor $style.Success.FG
                $results += [PSCustomObject]@{
                    PackageName = $pkgName
                    OldVersion  = $oldVer
                    NewVersion  = $newVer
                    Status      = "成功"
                }
            }
            else {
                Write-Color "  [失败] $pkgName : $($failedProjects.Count)/$($pkg.Projects.Count) 个项目升级失败" -ForegroundColor $style.Error.FG
                $results += [PSCustomObject]@{
                    PackageName = $pkgName
                    OldVersion  = $oldVer
                    NewVersion  = $newVer
                    Status      = "失败"
                }
            }
        }
    }

    # ---- 步骤4: 恢复包 ----
    if (-not $WhatIf -and ($results | Where-Object { $_.Status -eq "成功" }).Count -gt 0) {
        Write-Section "恢复 NuGet 包"
        Write-Color "  正在执行 dotnet restore ..." -ForegroundColor $style.Info.FG
        Push-Location $projectRoot
        try {
            dotnet restore $slnPath 2>&1 | Out-Null
            if ($LASTEXITCODE -eq 0) {
                Write-Color "  [成功] 包恢复完成" -ForegroundColor $style.Success.FG
            }
            else {
                Write-Color "  [警告] 包恢复可能存在问题" -ForegroundColor $style.Warning.FG
            }
        }
        finally {
            Pop-Location
        }
    }

    # ---- 步骤5: 编译验证 ----
    $buildSuccess = $true
    if (-not $SkipBuild -and -not $WhatIf -and ($results | Where-Object { $_.Status -eq "成功" }).Count -gt 0) {
        $buildSuccess = Test-Build

        # 编译失败时自动回滚
        if (-not $buildSuccess) {
            Write-Color "  [回滚] 编译失败，正在回滚版本变更..." -ForegroundColor $style.Warning.FG
            $rollbackCount = 0
            foreach ($r in ($results | Where-Object { $_.Status -eq "成功" })) {
                $varInfo = Find-VariableForPackage -PackageName $r.PackageName -VariableMap $pkgVarMap
                if ($null -ne $varInfo) {
                    # 集中式管理：回滚 versions.json 中的版本号
                    $rollbackResult = Update-BuildPropsVersion -VariableName $varInfo.VarName -NewVersion $r.OldVersion
                    if ($rollbackResult.Updated) {
                        Write-Color "  [回滚成功] $($r.PackageName) : `$$($varInfo.VarName) = $($r.NewVersion) → $($r.OldVersion)" -ForegroundColor $style.Success.FG
                        $rollbackCount++
                    }
                }
                else {
                    # 非集中式管理：回滚每个 .csproj 中的内联版本号
                    $pkgName = $r.PackageName
                    $oldVer = $r.OldVersion
                    $rollbackPkg = $uniquePackages | Where-Object { $_.PackageName -eq $pkgName } | Select-Object -First 1
                    if ($null -ne $rollbackPkg) {
                        $allRolledBack = $true
                        foreach ($projPath in $rollbackPkg.Projects) {
                            try {
                                Push-Location $projectRoot
                                $rollbackOutput = dotnet add "$projPath" package $pkgName --version $oldVer 2>&1
                                $projSuccess = $LASTEXITCODE -eq 0
                                Pop-Location
                                if (-not $projSuccess) {
                                    $allRolledBack = $false
                                }
                            }
                            catch {
                                Pop-Location
                                $allRolledBack = $false
                            }
                        }
                        if ($allRolledBack) {
                            Write-Color "  [回滚成功] $pkgName : $($r.NewVersion) → $oldVer (已回滚 $($rollbackPkg.Projects.Count) 个项目)" -ForegroundColor $style.Success.FG
                            $rollbackCount++
                        }
                        else {
                            Write-Color "  [回滚失败] $pkgName : 部分项目回滚失败，请手动检查" -ForegroundColor $style.Error.FG
                        }
                    }
                }
            }
            if ($rollbackCount -gt 0) {
                Write-Color "  [完成] 已回滚 $rollbackCount 个包到升级前版本" -ForegroundColor $style.Info.FG
            }
        }
    }
    elseif ($WhatIf) {
        Write-Color "  [预览模式] 跳过编译验证" -ForegroundColor $style.Warning.FG
    }
    elseif ($SkipBuild) {
        Write-Color "  [信息] 已跳过编译验证（-SkipBuild）" -ForegroundColor $style.Info.FG
    }

    # ---- 步骤6: 输出报告 ----
    Write-Section "升级摘要"

    $successCount = ($results | Where-Object { $_.Status -eq "成功" }).Count
    $skipCount = ($results | Where-Object { $_.Status -eq "跳过" }).Count
    $failCount = ($results | Where-Object { $_.Status -eq "失败" }).Count
    $manualCount = ($results | Where-Object { $_.Status -eq "手动" }).Count
    $previewCount = ($results | Where-Object { $_.Status -eq "预览" }).Count

    Write-Color "  总计: $($results.Count) 个包" -ForegroundColor $style.Highlight.FG
    if ($successCount -gt 0) { Write-Color "  成功: $successCount" -ForegroundColor $style.Success.FG }
    if ($skipCount -gt 0) { Write-Color "  跳过: $skipCount" -ForegroundColor $style.Warning.FG }
    if ($failCount -gt 0) { Write-Color "  失败: $failCount" -ForegroundColor $style.Error.FG }
    if ($manualCount -gt 0) { Write-Color "  手动: $manualCount (版本号内联在 .csproj 中，需手动更新)" -ForegroundColor $style.Warning.FG }
    if ($previewCount -gt 0) { Write-Color "  预览: $previewCount" -ForegroundColor $style.Warning.FG }

    if ($buildSuccess -eq $false) {
        Write-Color "  编译验证: 失败 ❌" -ForegroundColor $style.Error.FG
    }
    elseif (-not $SkipBuild -and -not $WhatIf -and $successCount -gt 0) {
        Write-Color "  编译验证: 通过 ✅" -ForegroundColor $style.Success.FG
    }

    Write-Host ""
    Write-Color "  $('=' * 56)" -ForegroundColor Cyan
    Write-Color "  升级完成！" -ForegroundColor $style.Success.FG
    Write-Color "  $('=' * 56)" -ForegroundColor Cyan
}

# ============================================================
# 入口
# ============================================================

Invoke-UpdateNuGetPackages
