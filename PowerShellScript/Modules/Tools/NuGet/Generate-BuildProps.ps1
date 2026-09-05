<#
.SYNOPSIS
    从 versions.json 生成 Directory.Build.props 的版本部分（.generated.props）

.DESCRIPTION
    读取项目根目录的 versions.json，生成一个 MSBuild 导入文件
    generated/Directory.Build.props.versions.generated.props，
    其中包含所有 NuGet 包的版本变量定义。

    该文件由 Directory.Build.props 通过 <Import> 引入，
    实现版本号的生成式管理。

.PARAMETER VersionsFile
    versions.json 的路径（默认：项目根目录/versions.json）

.PARAMETER OutputFile
    生成的 .props 文件路径（默认：项目根目录/generated/Directory.Build.props.versions.generated.props）

.PARAMETER WhatIf
    预览将要生成的內容，不实际写入文件

.EXAMPLE
    # 正常生成
    .\Generate-BuildProps.ps1

.EXAMPLE
    # 预览
    .\Generate-BuildProps.ps1 -WhatIf
#>

[CmdletBinding()]
param(
    [Parameter()]
    [string]$VersionsFile = "",

    [Parameter()]
    [string]$OutputFile = "",

    [Parameter()]
    [switch]$WhatIf
)

# ============================================================
# 初始化路径
# ============================================================

$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Resolve-Path (Join-Path $scriptPath "..\..")

if (-not $VersionsFile) {
    $VersionsFile = Join-Path $projectRoot "versions.json"
}

if (-not $OutputFile) {
    $generatedDir = Join-Path $projectRoot "generated"
    $OutputFile = Join-Path $generatedDir "Directory.Build.props.versions.generated.props"
}

# ============================================================
# 颜色定义
# ============================================================

$style = @{
    Title    = @{ FG = "Cyan"; BG = "Black" }
    Success  = @{ FG = "Green"; BG = "Black" }
    Warning  = @{ FG = "Yellow"; BG = "Black" }
    Error    = @{ FG = "Red"; BG = "Black" }
    Info     = @{ FG = "Gray"; BG = "Black" }
}

function Write-Color {
    param([string]$Text, [string]$ForegroundColor = "White", [string]$BackgroundColor = "Black")
    Write-Host $Text -ForegroundColor $ForegroundColor -BackgroundColor $BackgroundColor
}

# ============================================================
# 主逻辑
# ============================================================

Write-Host ("=" * 60) -ForegroundColor Cyan
Write-Host "  生成 Directory.Build.props 版本配置" -ForegroundColor White
Write-Host ("=" * 60) -ForegroundColor Cyan
Write-Host ""

# 检查 versions.json
if (-not (Test-Path $VersionsFile)) {
    Write-Color "  [错误] 找不到版本配置文件: $VersionsFile" -ForegroundColor $style.Error.FG
    Write-Color "  请确保 versions.json 存在于项目根目录" -ForegroundColor $style.Error.FG
    exit 1
}

# 读取 versions.json
try {
    $versionsJson = Get-Content $VersionsFile -Raw -Encoding UTF8 | ConvertFrom-Json
    Write-Color "  已读取 versions.json" -ForegroundColor $style.Success.FG
} catch {
    Write-Color "  [错误] 无法解析 versions.json: $($_.Exception.Message)" -ForegroundColor $style.Error.FG
    exit 1
}

# 获取所有属性名
$propertyNames = $versionsJson.PSObject.Properties.Name | Sort-Object
Write-Color "  共发现 $($propertyNames.Count) 个包版本定义" -ForegroundColor $style.Info.FG

# 生成 .props 文件内容
$content = @"
<!--
  此文件由 Generate-BuildProps.ps1 自动生成，请勿手动编辑。
  版本号定义请修改 versions.json，然后重新运行 Generate-BuildProps.ps1。
  生成时间: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")
-->
<Project>
  <PropertyGroup>
"@

foreach ($name in $propertyNames) {
    $version = $versionsJson.$name
    $varName = "${name}Version"
    $content += "`n    <${varName}>${version}</${varName}>"
}

$content += @"

  </PropertyGroup>
</Project>
"@

# 预览模式
if ($WhatIf) {
    Write-Color "  [预览模式] 将生成以下内容到: $OutputFile" -ForegroundColor $style.Warning.FG
    Write-Host ""
    Write-Host $content
    Write-Color "  [预览模式] 未写入任何文件" -ForegroundColor $style.Warning.FG
    return
}

# 确保输出目录存在
$outputDir = Split-Path $OutputFile -Parent
if (-not (Test-Path $outputDir)) {
    New-Item -Path $outputDir -ItemType Directory -Force | Out-Null
    Write-Color "  已创建目录: $outputDir" -ForegroundColor $style.Info.FG
}

# 写入文件
try {
    # 使用 UTF8 without BOM
    $utf8NoBom = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllText($OutputFile, $content, $utf8NoBom)
    Write-Color "  [成功] 已生成: $OutputFile" -ForegroundColor $style.Success.FG
    Write-Color "  包含 $($propertyNames.Count) 个版本变量定义" -ForegroundColor $style.Info.FG
} catch {
    Write-Color "  [错误] 写入文件失败: $($_.Exception.Message)" -ForegroundColor $style.Error.FG
    exit 1
}

Write-Host ""
Write-Color "  $('=' * 56)" -ForegroundColor Cyan
Write-Color "  生成完成！" -ForegroundColor $style.Success.FG
Write-Color "  $('=' * 56)" -ForegroundColor Cyan
