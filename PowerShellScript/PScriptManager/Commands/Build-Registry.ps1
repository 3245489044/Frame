
# .SYNOPSIS
# 构建函数注册表 — 扫描脚本并建立函数名→路径的映射
# .DESCRIPTION
# 扫描指定目录下的所有 .ps1/.psm1 文件，提取 @exports 标签和函数定义，
# 构建函数名→脚本路径的注册表，并保存到 registry.json。
#
# 核心价值：所有脚本通过函数名互相调用，完全解耦物理路径。
# 脚本移动后只需重新运行此命令更新注册表，所有调用方无需修改。
# .PARAMETER Path
# 要扫描的目录路径（默认：项目根目录下的 PowerShellScript）
# .PARAMETER ProjectRoot
# 项目根目录（默认自动检测）
# .PARAMETER Force
# 强制重新解析所有脚本的函数定义（即使已有 @exports 标签）
# .PARAMETER IncludePesterTests
# 包含 Pester 测试文件
# .PARAMETER IncludeDeprecated
# 包含已弃用的脚本
# .EXAMPLE
# .\Build-Registry.ps1
# .EXAMPLE
# .\Build-Registry.ps1 -Force
# .EXAMPLE
# .\Build-Registry.ps1 -Path "..\..\CentralTest"


param(
    [string]$Path = "",

    [string]$ProjectRoot = "",

    [switch]$Force,

    [switch]$IncludePesterTests,

    [switch]$IncludeDeprecated
)

# ============================================================
# 初始化
# ============================================================

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$frameworkDir = Resolve-Path (Join-Path $scriptDir "..")

# 自动检测项目根目录（.git 哨兵自锚定，路径无关；-ProjectRoot 可显式覆盖）
if (-not $ProjectRoot) {
    $__cand = $scriptDir
    while ($__cand -and -not (Test-Path -LiteralPath (Join-Path $__cand '.git'))) { $__cand = Split-Path -Parent $__cand }
    if (-not $__cand) { Write-Error '[Build-Registry] 无法定位仓库根（.git）'; exit 1 }
    $ProjectRoot = $__cand
}

# 加载核心模块（dot-source 方式）
$coreModules = @(
    'Core\ScriptScanner.ps1'
    'Core\ConfigGenerator.ps1'
    'Core\ManifestManager.ps1'
    'Core\ScriptRegistry.ps1'
)

$loadedCount = 0
foreach ($module in $coreModules) {
    $modulePath = Join-Path $frameworkDir $module
    if (Test-Path $modulePath) {
        . $modulePath
        $loadedCount++
    }
}

if ($loadedCount -eq 0) {
    Write-Error "[Build-Registry] 无法加载 PScriptManager 核心模块"
    exit 1
}

# 确定扫描路径
if (-not $Path) {
    $Path = Join-Path $ProjectRoot "PowerShellScript"
}

if (-not (Test-Path $Path)) {
    Write-Error "[Build-Registry] 路径不存在: $Path"
    exit 1
}

# ============================================================
# 执行扫描 + 构建注册表
# ============================================================

Write-Host ""
Write-Host "╔══════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║     PScriptManager 函数注册表构建工具            ║" -ForegroundColor Cyan
Write-Host "╚══════════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""

Write-Host "  🔍 正在扫描: $Path" -ForegroundColor Cyan
Write-Host ""

# 1. 扫描脚本
$scripts = Invoke-ScriptScan -Path $Path -ProjectRoot $ProjectRoot `
    -IncludePesterTests:$IncludePesterTests `
    -IncludeDeprecated:$IncludeDeprecated

# 显示扫描摘要
$summary = Get-ScriptSummary -Scripts $scripts
Show-ScriptScanResult -Scripts $scripts -Summary $summary -ShowWithoutMetadata

# 2. 构建注册表
Write-Host ""
Write-Host "  🏗️  正在构建函数注册表..." -ForegroundColor Yellow

$registry = Build-ScriptRegistry -Scripts $scripts -ProjectRoot $ProjectRoot -Force:$Force

# 3. 保存注册表
$manifestPath = Join-Path $frameworkDir "manifest.json"
if (Test-Path $manifestPath) {
    $manifest = Get-Content $manifestPath -Encoding UTF8 -Raw | ConvertFrom-Json
    $registryFile = Join-Path $frameworkDir $manifest.registry.file
    Set-RegistryFilePath -Path $registryFile
}
else {
    $registryFile = Join-Path $frameworkDir "registry.json"
    Set-RegistryFilePath -Path $registryFile
}

$saved = Save-Registry

if ($saved) {
    Write-Host ""
    Write-Host "  ✅ 注册表构建完成！" -ForegroundColor Green
    Write-Host ""
    Write-Host "  📌 现在可以通过函数名调用脚本了：" -ForegroundColor Cyan
    Write-Host "     Invoke-Function -Name '<函数名>' -Arguments @{...}" -ForegroundColor White
    Write-Host ""
    Write-Host "  示例：" -ForegroundColor Gray
    Write-Host "     # 查看注册表内容" -ForegroundColor Gray
    Write-Host "     Show-Registry" -ForegroundColor DarkGray
    Write-Host ""
    Write-Host "     # 调用函数（无需知道脚本路径）" -ForegroundColor Gray
    Write-Host "     Invoke-Function -Name 'Build-Project'" -ForegroundColor DarkGray
    Write-Host ""
}

return $registry
