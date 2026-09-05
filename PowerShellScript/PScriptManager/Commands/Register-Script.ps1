
# .SYNOPSIS
# 注册脚本到 launcher.config.json
# .DESCRIPTION
# 扫描指定目录下的 PowerShell 脚本，并将结果自动注册到
# launcher.config.json 配置文件中。

# 支持：
# - 新增脚本自动注册
# - 已注册脚本的元数据更新
# - 弃用脚本标记移除
# - 分类自动创建
# - 差异预览（安全模式）
# .PARAMETER Path
# 要扫描的目录路径（默认：项目根目录下的 PowerShellScript）
# .PARAMETER ProjectRoot
# 项目根目录（默认自动检测）
# .PARAMETER DryRun
# 预览模式，只显示差异不实际写入
# .PARAMETER RemoveDeprecated
# 从配置中移除标记为 @deprecated 的脚本
# .PARAMETER IncludePesterTests
# 包含 Pester 测试文件
# .PARAMETER ConfigPath
# 指定 launcher.config.json 的路径（默认自动检测）
# .EXAMPLE
# .\Register-Script.ps1 -DryRun
# .EXAMPLE
# .\Register-Script.ps1
# .EXAMPLE
# .\Register-Script.ps1 -Path "..\..\CentralTest" -DryRun
# .EXAMPLE
# .\Register-Script.ps1 -RemoveDeprecated


param(
    [string]$Path = "",

    [string]$ProjectRoot = "",

    [switch]$DryRun,

    [switch]$RemoveDeprecated,

    [switch]$IncludePesterTests,

    [string]$ConfigPath = ""
)

# ============================================================
# 初始化
# ============================================================

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$frameworkDir = Resolve-Path (Join-Path $scriptDir "..")

# 自动检测项目根目录
if (-not $ProjectRoot) {
    if ($global:PScriptManagerProjectRoot) {
        $ProjectRoot = $global:PScriptManagerProjectRoot
    }
    else {
        $ProjectRoot = Resolve-Path (Join-Path $frameworkDir "..\..")
    }
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
    Write-Error "[Register-Script] 无法加载 PScriptManager 核心模块"
    exit 1
}




# 确定扫描路径
if (-not $Path) {
    $Path = Join-Path $ProjectRoot "PowerShellScript"
}

if (-not (Test-Path $Path)) {
    Write-Error "[Register-Script] 路径不存在: $Path"
    exit 1
}

# 确定配置文件路径
if (-not $ConfigPath) {
    $configCandidates = @(
        Join-Path $ProjectRoot "PowerShellScript\launcher.config.json"
        Join-Path $ProjectRoot "launcher.config.json"
    )
    foreach ($candidate in $configCandidates) {
        if (Test-Path $candidate) {
            $ConfigPath = $candidate
            break
        }
    }
    if (-not $ConfigPath) {
        $ConfigPath = $configCandidates[0]
    }
}

# ============================================================
# 执行扫描 + 注册
# ============================================================

Write-Host ""
Write-Host "  🔍 正在扫描: $Path" -ForegroundColor Cyan
Write-Host "  📝 配置文件: $ConfigPath" -ForegroundColor Gray
if ($DryRun) {
    Write-Host "  📋 模式: 预览（不会写入）" -ForegroundColor Yellow
}
Write-Host ""

# 扫描脚本
$scripts = Invoke-ScriptScan -Path $Path -ProjectRoot $ProjectRoot `
    -IncludePesterTests:$IncludePesterTests

# 显示扫描摘要
$summary = Get-ScriptSummary -Scripts $scripts
Show-ScriptScanResult -Scripts $scripts -Summary $summary -ShowWithoutMetadata

# 注册到配置
$result = Register-ScriptsToConfig -Scripts $scripts `
    -ConfigPath $ConfigPath `
    -ProjectRoot $ProjectRoot `
    -DryRun:$DryRun `
    -RemoveDeprecated:$RemoveDeprecated

# 返回结果供管道使用
return $result
