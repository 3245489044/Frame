
# .SYNOPSIS
# 扫描并显示 PowerShell 脚本信息
# .DESCRIPTION
# 扫描指定目录下的所有 .ps1/.psm1 文件，解析注释元数据，
# 并以表格形式显示扫描结果。
# .PARAMETER Path
# 要扫描的目录路径（默认：项目根目录下的 PowerShellScript）
# .PARAMETER ProjectRoot
# 项目根目录（默认自动检测）
# .PARAMETER ShowAll
# 显示所有脚本的详细信息
# .PARAMETER ShowWithoutMetadata
# 只显示缺少元数据的脚本
# .PARAMETER IncludePesterTests
# 包含 Pester 测试文件
# .PARAMETER IncludeDeprecated
# 包含已弃用的脚本
# .PARAMETER OutputJson
# 以 JSON 格式输出扫描结果
# .PARAMETER OutputFile
# 将扫描结果保存到文件
# .EXAMPLE
# .\Scan-Scripts.ps1
# .EXAMPLE
# .\Scan-Scripts.ps1 -Path "..\..\CentralTest" -ShowAll
# .EXAMPLE
# .\Scan-Scripts.ps1 -ShowWithoutMetadata
# .EXAMPLE
# .\Scan-Scripts.ps1 -OutputJson -OutputFile "scan-result.json"


param(
    [string]$Path = "",

    [string]$ProjectRoot = "",

    [switch]$ShowAll,

    [switch]$ShowWithoutMetadata,

    [switch]$IncludePesterTests,

    [switch]$IncludeDeprecated,

    [switch]$OutputJson,

    [string]$OutputFile = ""
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
    Write-Error "[Scan-Scripts] 无法加载 PScriptManager 核心模块"
    exit 1
}




# 确定扫描路径
if (-not $Path) {
    $Path = Join-Path $ProjectRoot "PowerShellScript"
}

if (-not (Test-Path $Path)) {
    Write-Error "[Scan-Scripts] 路径不存在: $Path"
    exit 1
}

# ============================================================
# 执行扫描
# ============================================================

Write-Host ""
Write-Host "  🔍 正在扫描: $Path" -ForegroundColor Cyan
Write-Host ""

$scripts = Invoke-ScriptScan -Path $Path -ProjectRoot $ProjectRoot `
    -IncludePesterTests:$IncludePesterTests `
    -IncludeDeprecated:$IncludeDeprecated

# ============================================================
# 输出结果
# ============================================================

if ($OutputJson) {
    # JSON 输出
    $jsonResult = $scripts | ConvertTo-Json -Depth 5
    if ($OutputFile) {
        $jsonResult | Set-Content $OutputFile -Encoding UTF8
        Write-Host "  ✅ 结果已保存到: $OutputFile" -ForegroundColor Green
    }
    else {
        Write-Host $jsonResult
    }
}
else {
    # 格式化输出
    $summary = Get-ScriptSummary -Scripts $scripts
    Show-ScriptScanResult -Scripts $scripts -Summary $summary `
        -ShowAll:$ShowAll `
        -ShowWithoutMetadata:$ShowWithoutMetadata
}

# 返回脚本列表供管道使用
return $scripts
