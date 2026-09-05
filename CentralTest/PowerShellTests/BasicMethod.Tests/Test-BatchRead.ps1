<#
.SYNOPSIS
    BatchRead-File 批量读取功能测试脚本
.DESCRIPTION
    测试 BatchRead-File 的 5 种场景：
    1. 批量读取 .csproj 文件
    2. 批量读取 .ps1 文件
    3. 批量读取 .md 文件
    4. 混合通配符批量读取
    5. Raw 模式读取
#>

$ErrorActionPreference = "Stop"

# 加载公共测试模块
$testHelper = Join-Path $PSScriptRoot "TestHelper.psm1"
Import-Module $testHelper -Force -ErrorAction Stop

# 加载被测试模块
Import-BasicMethodModules

Write-Host "============================================"
Write-Host "  MCP 批量读取功能测试"
Write-Host "============================================"
Write-Host ""

# ===== 测试1：批量读取 .csproj 文件 =====
Write-Host "=== 测试1: BatchRead-File 批量读取 .csproj 文件 ==="
try {
    $result = BatchRead-File -Paths @("CsharpPrj/**/*.csproj")
    Write-Host "结果: 成功"
    Write-Host "找到 $($result.TotalFiles) 个 .csproj 文件"
    $result.Files | ForEach-Object {
        Write-Host "  - $($_.RelativePath) ($($_.TotalLines) 行, $($_.SizeKB) KB)"
    }
} catch {
    Write-Host "结果: 失败 - $_"
}
Write-Host ""

# ===== 测试2：批量读取 .ps1 文件 =====
Write-Host "=== 测试2: BatchRead-File 批量读取 .ps1 文件 ==="
try {
    $result2 = BatchRead-File -Paths @("PowerShellScript/AIDecisionFramework/*.ps1")
    Write-Host "结果: 成功"
    Write-Host "找到 $($result2.TotalFiles) 个 .ps1 文件"
    $result2.Files | ForEach-Object {
        Write-Host "  - $($_.RelativePath) ($($_.TotalLines) 行, $($_.SizeKB) KB)"
    }
} catch {
    Write-Host "结果: 失败 - $_"
}
Write-Host ""

# ===== 测试3：批量读取 .md 文件 =====
Write-Host "=== 测试3: BatchRead-File 批量读取 .md 文件 ==="
try {
    $result3 = BatchRead-File -Paths @("docs/*.md")
    Write-Host "结果: 成功"
    Write-Host "找到 $($result3.TotalFiles) 个 .md 文件"
    $result3.Files | ForEach-Object {
        Write-Host "  - $($_.RelativePath) ($($_.TotalLines) 行, $($_.SizeKB) KB)"
    }
} catch {
    Write-Host "结果: 失败 - $_"
}
Write-Host ""

# ===== 测试4：混合通配符批量读取 =====
Write-Host "=== 测试4: 混合通配符批量读取 ==="
try {
    $result4 = BatchRead-File -Paths @("*.md", "*.ps1")
    Write-Host "结果: 成功"
    Write-Host "找到 $($result4.TotalFiles) 个文件（混合 *.md + *.ps1）"
    $result4.Files | ForEach-Object {
        Write-Host "  - $($_.RelativePath) ($($_.TotalLines) 行, $($_.SizeKB) KB)"
    }
} catch {
    Write-Host "结果: 失败 - $_"
}
Write-Host ""

# ===== 测试5：Raw 模式读取 =====
Write-Host "=== 测试5: Raw 模式读取（返回原始字符串）==="
try {
    $result5 = BatchRead-File -Paths @("README.md") -Raw
    Write-Host "结果: 成功"
    Write-Host "找到 $($result5.TotalFiles) 个文件"
    $result5.Files | ForEach-Object {
        $preview = $_.Content.Substring(0, [Math]::Min(100, $_.Content.Length))
        Write-Host "  - $($_.RelativePath) ($($_.TotalLines) 行, $($_.SizeKB) KB)"
        Write-Host "    预览: $preview..."
    }
} catch {
    Write-Host "结果: 失败 - $_"
}
Write-Host ""

Write-Host "============================================"
Write-Host "  批量读取测试完成"
Write-Host "============================================"
