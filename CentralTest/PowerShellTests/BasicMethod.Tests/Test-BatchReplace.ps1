<#
.SYNOPSIS
    BatchReplace-InFile 批量替换功能测试脚本
.DESCRIPTION
    测试 BatchReplace-InFile 和 BatchWrite-File 的 4 种场景：
    1. BatchReplace-InFile SEARCH/REPLACE 块
    2. BatchReplace-InFile 正则替换
    3. BatchWrite-File 合并模式
    4. BatchWrite-File 模板变量替换
#>

$ErrorActionPreference = "Stop"

# 加载公共测试模块
$testHelper = Join-Path $PSScriptRoot "TestHelper.psm1"
Import-Module $testHelper -Force -ErrorAction Stop

# 加载被测试模块
Import-BasicMethodModules

Write-Host "============================================"
Write-Host "  MCP 批量替换功能测试"
Write-Host "============================================"
Write-Host ""

# 创建临时测试文件
$TestDir = Join-Path (Resolve-Path ".") "CentralTest\PowerShellTests\temp"
if (-not (Test-Path $TestDir)) { New-Item -Path $TestDir -ItemType Directory -Force | Out-Null }

# 创建3个测试文件
$testFiles = @(
    @{Path = "test1.txt"; Content = "Hello World`nThis is a test file`nGoodbye World"},
    @{Path = "test2.txt"; Content = "Hello World`nAnother test file`nGoodbye World"},
    @{Path = "test3.txt"; Content = "Hello World`nThird test file`nGoodbye World"}
)

foreach ($tf in $testFiles) {
    $fullPath = Join-Path $TestDir $tf.Path
    Set-Content -Path $fullPath -Value $tf.Content -Encoding UTF8
    Write-Host "创建测试文件: $($tf.Path)"
}
Write-Host ""

# ===== 测试1：BatchReplace-InFile 使用 SEARCH/REPLACE 块 =====
Write-Host "=== 测试1: BatchReplace-InFile SEARCH/REPLACE 块 ==="
try {
    $blocks = @(
        @{Search = "Hello World"; Replace = "Hi Universe"}
    )
    $result = BatchReplace-InFile -Paths @("$TestDir/*.txt") -Blocks $blocks -Backup
    Write-Host "结果: 成功"
    Write-Host "处理文件数: $($result.TotalFiles)"
    Write-Host "修改文件数: $($result.ModifiedFiles)"
    $result.Results | ForEach-Object {
        Write-Host "  - $($_.RelativePath) (备份: $($_.Backup -ne ''))"
    }
} catch {
    Write-Host "结果: 失败 - $_"
}
Write-Host ""

# 验证修改结果
Write-Host "=== 验证修改结果 ==="
Get-ChildItem -Path $TestDir -Filter "*.txt" -File | ForEach-Object {
    $content = Get-Content $_.FullName -Raw
    Write-Host "  $($_.Name): $($content.Trim())"
}
Write-Host ""

# ===== 测试2：BatchReplace-InFile 正则替换 =====
Write-Host "=== 测试2: BatchReplace-InFile 正则替换 ==="
try {
    $result2 = BatchReplace-InFile -Paths @("$TestDir/*.txt") -Pattern "\bWorld\b" -Replacement "Earth" -Backup
    Write-Host "结果: 成功"
    Write-Host "处理文件数: $($result2.TotalFiles)"
    Write-Host "修改文件数: $($result2.ModifiedFiles)"
} catch {
    Write-Host "结果: 失败 - $_"
}
Write-Host ""

# 验证正则替换结果
Write-Host "=== 验证正则替换结果 ==="
Get-ChildItem -Path $TestDir -Filter "*.txt" -File | ForEach-Object {
    $content = Get-Content $_.FullName -Raw
    Write-Host "  $($_.Name): $($content.Trim())"
}
Write-Host ""

# ===== 测试3：BatchWrite-File 合并模式 =====
Write-Host "=== 测试3: BatchWrite-File 合并模式 ==="
try {
    $mergeResult = BatchWrite-File -Path "$TestDir\test1.txt" -Merge -Blocks @(
        @{Search = "Hi Universe"; Replace = "Hello Multiverse"}
    ) -Backup -ShowDiff
    Write-Host "结果: 成功"
    Write-Host "合并完成"
} catch {
    Write-Host "结果: 失败 - $_"
}
Write-Host ""

# 验证合并结果
Write-Host "=== 验证合并结果 ==="
$content = Get-Content "$TestDir\test1.txt" -Raw
Write-Host "  test1.txt: $($content.Trim())"
Write-Host ""

# ===== 测试4：BatchWrite-File 模板变量替换 =====
Write-Host "=== 测试4: BatchWrite-File 模板变量替换 ==="
try {
    $templateResult = BatchWrite-File -Path "$TestDir\template_output.txt" -Content "Hello {{Name}}!`nYour role is {{Role}}." -Variables @{Name = "Admin"; Role = "System Manager"}
    Write-Host "结果: 成功"
    $content = Get-Content "$TestDir\template_output.txt" -Raw
    Write-Host "  输出: $($content.Trim())"
} catch {
    Write-Host "结果: 失败 - $_"
}
Write-Host ""

# ===== 清理临时文件 =====
Write-Host "=== 清理临时文件 ==="
Remove-Item -Path "$TestDir\*.txt" -Force
Remove-Item -Path "$TestDir\*.bak*" -Force -ErrorAction SilentlyContinue
Write-Host "临时文件已清理"
Write-Host ""

Write-Host "============================================"
Write-Host "  批量替换测试完成"
Write-Host "============================================"
