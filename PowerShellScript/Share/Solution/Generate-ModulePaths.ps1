<#
.SYNOPSIS
    从 modules.json 生成 CsharpPrj 模块路径属性（.generated.props）

.DESCRIPTION
    读取 Frame/CsharpPrj/modules.json（模块目录单一真源），生成
    Frame/CsharpPrj/generated/Directory.Build.props.modules.generated.props，
    其中为每个模块定义两个 MSBuild 属性：

      Mod_<ProjectName>  = 该 csproj 所在目录（绝对路径，尾随 "\"）
      Proj_<ProjectName> = 该 csproj 完整路径（绝对路径）

    该文件由 CsharpPrj/Directory.Build.props 通过 <Import> 引入，
    使全部 csproj 的项目引用可写为：

      <ProjectReference Include="$(Proj_StateManagement)" />

    从而移动/重命名任何模块目录只需改 modules.json 一处，csproj 零改动。

.PARAMETER ManifestFile
    modules.json 的路径（默认：Frame/CsharpPrj/modules.json）

.PARAMETER OutputFile
    生成的 .props 路径（默认：Frame/CsharpPrj/generated/Directory.Build.props.modules.generated.props）

.PARAMETER WhatIf
    预览将要生成的内容，不实际写入文件

.EXAMPLE
    .\Generate-ModulePaths.ps1

.EXAMPLE
    .\Generate-ModulePaths.ps1 -WhatIf
#>

[CmdletBinding()]
param(
    [Parameter()]
    [string]$ManifestFile = "",

    [Parameter()]
    [string]$OutputFile = "",

    [Parameter()]
    [switch]$WhatIf
)

$ErrorActionPreference = "Stop"

# ============================================================
# 加载共享模块 + 路径初始化
# ============================================================

Import-Module (Join-Path $PSScriptRoot "ModulePaths.Common.psm1") -Force -ErrorAction Stop

$csharpPrj = Get-CsharpPrjRoot

if (-not $ManifestFile) {
    $ManifestFile = Get-ModuleManifestPath
}

if (-not $OutputFile) {
    $OutputFile = Get-ModuleGeneratedPropsPath
}

# ============================================================
# 主逻辑
# ============================================================

Write-Host ("=" * 60) -ForegroundColor Cyan
Write-Host "  生成 CsharpPrj 模块路径属性" -ForegroundColor White
Write-Host ("=" * 60) -ForegroundColor Cyan
Write-Host ""

if (-not (Test-Path $ManifestFile)) {
    Write-Host "  [错误] 找不到模块清单: $ManifestFile" -ForegroundColor Red
    exit 1
}

try {
    $manifest = Get-Content $ManifestFile -Raw -Encoding UTF8 | ConvertFrom-Json
} catch {
    Write-Host "  [错误] 无法解析 modules.json: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
Write-Host "  已读取 modules.json" -ForegroundColor Green

$roots = $manifest.roots
$projects = $manifest.projects

if (-not $roots -or -not $projects) {
    Write-Host "  [错误] modules.json 缺少 roots 或 projects 节点" -ForegroundColor Red
    exit 1
}

# --- 收集并校验属性名唯一性 ---
$entries = @()          # @{ Kind; Name; Path; Proj; Safe }
$seenSafe = @{}

foreach ($root in $roots.PSObject.Properties) {
    $entries += [PSCustomObject]@{
        Kind = "root"
        Name = $root.Name
        Path = $root.Value.path
        Safe = Get-SafeModuleName $root.Name
    }
}

foreach ($proj in $projects.PSObject.Properties) {
    $dirRel = [System.IO.Path]::GetDirectoryName($proj.Value)
    if ([string]::IsNullOrEmpty($dirRel)) { $dirRel = "" }
    $entries += [PSCustomObject]@{
        Kind = "project"
        Name = $proj.Name
        Path = $dirRel
        Proj = $proj.Value
        Safe = Get-SafeModuleName $proj.Name
    }
}

foreach ($e in $entries) {
    # 根目录用 ModDir_ 前缀，项目用 Mod_ / Proj_ 前缀，命名空间互不重叠
    $propPrefix = if ($e.Kind -eq "root") { "ModDir_" } else { "Mod_" }
    $key = "$propPrefix$($e.Safe)"
    if ($seenSafe.ContainsKey($key)) {
        Write-Host "  [错误] 属性名冲突: '$($seenSafe[$key])' 与 '$($e.Name)' 均净化为 '$key'" -ForegroundColor Red
        exit 1
    }
    $seenSafe[$key] = $e.Name
}
Write-Host "  根目录 $($roots.PSObject.Properties.Name.Count) 个，模块 $($projects.PSObject.Properties.Name.Count) 个，属性名唯一" -ForegroundColor Gray

# --- 校验清单与磁盘一致 ---
$missing = @()
foreach ($proj in $projects.PSObject.Properties) {
    $full = Join-Path $csharpPrj $proj.Value
    if (-not (Test-Path $full)) { $missing += $proj.Value }
}
if ($missing.Count -gt 0) {
    Write-Host "  [错误] modules.json 指向不存在的项目 ($($missing.Count) 个):" -ForegroundColor Red
    $missing | ForEach-Object { Write-Host "      $_" -ForegroundColor Red }
    Write-Host "  请运行 Test-ProjectReferences.ps1 -SyncManifest 重新同步清单" -ForegroundColor Yellow
    exit 1
}
Write-Host "  清单与磁盘一致" -ForegroundColor Green
Write-Host ""

# --- 生成内容 ---
$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine("<!--")
[void]$sb.AppendLine("  此文件由 Generate-ModulePaths.ps1 自动生成，请勿手动编辑。")
[void]$sb.AppendLine("  模块目录定义请修改 Frame/CsharpPrj/modules.json，然后重新运行 Generate-ModulePaths.ps1。")
[void]$sb.AppendLine("  生成时间: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')")
[void]$sb.AppendLine("-->")
[void]$sb.AppendLine("<Project>")
[void]$sb.AppendLine("")
[void]$sb.AppendLine("  <PropertyGroup Label=`"ModuleRoot`">")
[void]$sb.AppendLine("    <CsharpPrjDir>`$([System.IO.Path]::GetFullPath('`$(MSBuildThisFileDirectory)..\'))</CsharpPrjDir>")
[void]$sb.AppendLine("  </PropertyGroup>")
[void]$sb.AppendLine("")

[void]$sb.AppendLine("  <PropertyGroup Label=`"ModuleRootDirs`">")
foreach ($root in $roots.PSObject.Properties) {
    $safe = Get-SafeModuleName $root.Name
    [void]$sb.AppendLine("    <ModDir_$safe>`$(CsharpPrjDir)$($root.Value.path)\</ModDir_$safe>")
}
[void]$sb.AppendLine("  </PropertyGroup>")
[void]$sb.AppendLine("")

[void]$sb.AppendLine("  <PropertyGroup Label=`"ModuleDirs`">")
foreach ($proj in $projects.PSObject.Properties) {
    $safe = Get-SafeModuleName $proj.Name
    $dirRel = [System.IO.Path]::GetDirectoryName($proj.Value)
    if ([string]::IsNullOrEmpty($dirRel)) { $dirRel = "" }
    [void]$sb.AppendLine("    <Mod_$safe>`$(CsharpPrjDir)$dirRel\</Mod_$safe>")
}
[void]$sb.AppendLine("  </PropertyGroup>")
[void]$sb.AppendLine("")

[void]$sb.AppendLine("  <PropertyGroup Label=`"ModuleProjects`">")
foreach ($proj in $projects.PSObject.Properties) {
    $safe = Get-SafeModuleName $proj.Name
    [void]$sb.AppendLine("    <Proj_$safe>`$(CsharpPrjDir)$($proj.Value)</Proj_$safe>")
}
[void]$sb.AppendLine("  </PropertyGroup>")
[void]$sb.AppendLine("")
[void]$sb.AppendLine("</Project>")

$content = $sb.ToString()

if ($WhatIf) {
    Write-Host "  [预览模式] 将生成到: $OutputFile" -ForegroundColor Yellow
    Write-Host ""
    Write-Host $content
    Write-Host "  [预览模式] 未写入任何文件" -ForegroundColor Yellow
    return
}

$outputDir = Split-Path $OutputFile -Parent
if (-not (Test-Path $outputDir)) {
    New-Item -Path $outputDir -ItemType Directory -Force | Out-Null
    Write-Host "  已创建目录: $outputDir" -ForegroundColor Gray
}

$utf8NoBom = New-Object System.Text.UTF8Encoding $false
[System.IO.File]::WriteAllText($OutputFile, $content, $utf8NoBom)

Write-Host "  [成功] 已生成: $OutputFile" -ForegroundColor Green
Write-Host "  包含 $($roots.PSObject.Properties.Name.Count) 个根目录变量、$($projects.PSObject.Properties.Name.Count) 个模块目录变量、$($projects.PSObject.Properties.Name.Count) 个完整路径变量" -ForegroundColor Gray
Write-Host ""
Write-Host ("=" * 60) -ForegroundColor Cyan
Write-Host "  生成完成！" -ForegroundColor Green
Write-Host ("=" * 60) -ForegroundColor Cyan
