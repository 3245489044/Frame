<#
.SYNOPSIS
    BatchWrite-File 全面测试套件（合并版）
.DESCRIPTION
    整合原 Test-BatchWriteFile.ps1 + LiveTest-BatchWrite.ps1 + Test-CSharpInvoke.ps1，
    去重后保留 18 个测试场景，覆盖 BatchWrite-File 的全部功能：
    1.  单文件写入（-Path 单数）
    2.  多文件批量写入（-Path 数组 + -Content 数组）
    3.  -Paths 别名写入
    4.  单字符串 Content（非数组）
    5.  多文件 + 单 Content 复用
    6.  中文内容写入
    7.  空内容写入
    8.  模板变量替换
    9.  Merge 模式（单文件 SEARCH/REPLACE）
    10. Merge 模式（多文件）
    11. Merge 模式（多文件 + 多 Blocks）
    12. ConvertFrom-Json 模拟 invoke_ps_script
    13. 多文件 + 多 Content 精确匹配
    14. 大内容多行写入
    15. Backup 模式
    16. DryRun 模式
    17. Append 模式
    18. 边界情况：空路径、不存在文件
#>

param([switch]$Verbose)

$ErrorActionPreference = "Stop"

# 加载公共测试模块
$testHelper = Join-Path $PSScriptRoot "TestHelper.psm1"
Import-Module $testHelper -Force -ErrorAction Stop

# 加载被测试模块
Import-BasicMethodModules

# ============================================
# 测试 1：单文件写入（-Path 单数）
# ============================================
function Test-SingleFileWrite {
    Write-TestHeader "测试 1: 单文件写入（-Path 单数）"

    $testDir = Initialize-TestEnvironment -FileNames @("test1.txt")
    try {
        $r = BatchWrite-File -Path (New-TestFilePath "test1.txt") -Content "Hello World from BatchWrite!" -Verify
        if ($r.TotalFiles -ne 1) { throw "TotalFiles 应为 1，实际 $($r.TotalFiles)" }
        $c = Get-Content (New-TestFilePath "test1.txt") -Raw -Encoding UTF8
        if ($c.Trim() -ne "Hello World from BatchWrite!") { throw "内容不匹配: '$($c.Trim())'" }
        Write-TestResult -TestName "单文件写入" -Passed $true -Detail "Content: '$($c.Trim())'"
    } catch {
        Write-TestResult -TestName "单文件写入" -Passed $false -Detail $_.Exception.Message
    }
}

# ============================================
# 测试 2：多文件批量写入（-Path 数组 + -Content 数组）
# ============================================
function Test-MultiFileWrite {
    Write-TestHeader "测试 2: 多文件批量写入（-Path 数组 + -Content 数组）"

    $testDir = Initialize-TestEnvironment -FileNames @("test2a.txt", "test2b.txt")
    try {
        $r = BatchWrite-File -Path @((New-TestFilePath "test2a.txt"), (New-TestFilePath "test2b.txt")) -Content @("File A content", "File B content") -Verify
        if ($r.TotalFiles -ne 2) { throw "TotalFiles 应为 2，实际 $($r.TotalFiles)" }
        $ca = Get-Content (New-TestFilePath "test2a.txt") -Raw -Encoding UTF8
        $cb = Get-Content (New-TestFilePath "test2b.txt") -Raw -Encoding UTF8
        if ($ca.Trim() -ne "File A content") { throw "File A 内容不匹配: '$($ca.Trim())'" }
        if ($cb.Trim() -ne "File B content") { throw "File B 内容不匹配: '$($cb.Trim())'" }
        Write-TestResult -TestName "多文件批量写入" -Passed $true -Detail "File A: '$($ca.Trim())', File B: '$($cb.Trim())'"
    } catch {
        Write-TestResult -TestName "多文件批量写入" -Passed $false -Detail $_.Exception.Message
    }
}

# ============================================
# 测试 3：-Paths 别名写入
# ============================================
function Test-PathsAlias {
    Write-TestHeader "测试 3: -Paths 别名写入"

    $testDir = Initialize-TestEnvironment -FileNames @("test3.txt")
    try {
        $r = BatchWrite-File -Paths @((New-TestFilePath "test3.txt")) -Content @("通过 -Paths 写入的内容") -Verify
        if ($r.TotalFiles -ne 1) { throw "TotalFiles 应为 1，实际 $($r.TotalFiles)" }
        $c = Get-Content (New-TestFilePath "test3.txt") -Raw -Encoding UTF8
        if ($c.Trim() -ne "通过 -Paths 写入的内容") { throw "内容不匹配: '$($c.Trim())'" }
        Write-TestResult -TestName "-Paths 别名写入" -Passed $true -Detail "Content: '$($c.Trim())'"
    } catch {
        Write-TestResult -TestName "-Paths 别名写入" -Passed $false -Detail $_.Exception.Message
    }
}

# ============================================
# 测试 4：单字符串 Content（非数组）
# ============================================
function Test-SingleStringContent {
    Write-TestHeader "测试 4: 单字符串 Content（非数组）"

    $testDir = Initialize-TestEnvironment -FileNames @("test4.txt")
    try {
        $r = BatchWrite-File -Path (New-TestFilePath "test4.txt") -Content "单字符串内容" -Verify
        if ($r.TotalFiles -ne 1) { throw "TotalFiles 应为 1，实际 $($r.TotalFiles)" }
        $c = Get-Content (New-TestFilePath "test4.txt") -Raw -Encoding UTF8
        if ($c.Trim() -ne "单字符串内容") { throw "内容不匹配: '$($c.Trim())'" }
        Write-TestResult -TestName "单字符串 Content" -Passed $true -Detail "Content: '$($c.Trim())'"
    } catch {
        Write-TestResult -TestName "单字符串 Content" -Passed $false -Detail $_.Exception.Message
    }
}

# ============================================
# 测试 5：多文件 + 单 Content（复用）
# ============================================
function Test-MultiFileSingleContent {
    Write-TestHeader "测试 5: 多文件 + 单 Content（复用）"

    $testDir = Initialize-TestEnvironment -FileNames @("test5a.txt", "test5b.txt")
    try {
        $r = BatchWrite-File -Path @((New-TestFilePath "test5a.txt"), (New-TestFilePath "test5b.txt")) -Content "复用内容" -Verify
        if ($r.TotalFiles -ne 2) { throw "TotalFiles 应为 2，实际 $($r.TotalFiles)" }
        $ca = Get-Content (New-TestFilePath "test5a.txt") -Raw -Encoding UTF8
        $cb = Get-Content (New-TestFilePath "test5b.txt") -Raw -Encoding UTF8
        if ($ca.Trim() -ne "复用内容") { throw "File A 内容不匹配: '$($ca.Trim())'" }
        if ($cb.Trim() -ne "复用内容") { throw "File B 内容不匹配: '$($cb.Trim())'" }
        Write-TestResult -TestName "多文件+单Content复用" -Passed $true -Detail "File A: '$($ca.Trim())', File B: '$($cb.Trim())'"
    } catch {
        Write-TestResult -TestName "多文件+单Content复用" -Passed $false -Detail $_.Exception.Message
    }
}

# ============================================
# 测试 6：中文内容写入
# ============================================
function Test-ChineseContent {
    Write-TestHeader "测试 6: 中文内容写入"

    $testDir = Initialize-TestEnvironment -FileNames @("test6.txt")
    try {
        $r = BatchWrite-File -Path (New-TestFilePath "test6.txt") -Content "中文测试内容：你好，世界！" -Verify
        if ($r.TotalFiles -ne 1) { throw "TotalFiles 应为 1，实际 $($r.TotalFiles)" }
        $c = Get-Content (New-TestFilePath "test6.txt") -Raw -Encoding UTF8
        $trimmed = $c.Trim()
        if ($trimmed -ne "中文测试内容：你好，世界！") { throw "内容不匹配: '$trimmed'" }
        Write-TestResult -TestName "中文内容写入" -Passed $true -Detail "Content: '$trimmed' (Length: $($trimmed.Length))"
    } catch {
        Write-TestResult -TestName "中文内容写入" -Passed $false -Detail $_.Exception.Message
    }
}

# ============================================
# 测试 7：空内容写入
# ============================================
function Test-EmptyContent {
    Write-TestHeader "测试 7: 空内容写入"

    $testDir = Initialize-TestEnvironment -FileNames @("test7.txt")
    try {
        $r = BatchWrite-File -Path (New-TestFilePath "test7.txt") -Content $null -Verify
        if ($r.TotalFiles -ne 1) { throw "TotalFiles 应为 1，实际 $($r.TotalFiles)" }
        if (-not $r.Results[0].Verified) { throw "校验失败" }
        $bytes = [System.IO.File]::ReadAllBytes((New-TestFilePath "test7.txt"))
        if ($bytes.Length -ne 0) { throw "文件应为 0 字节，实际 $($bytes.Length)" }
        Write-TestResult -TestName "空内容写入" -Passed $true -Detail "File bytes: $($bytes.Length)"
    } catch {
        Write-TestResult -TestName "空内容写入" -Passed $false -Detail $_.Exception.Message
    }
}

# ============================================
# 测试 8：模板变量替换
# ============================================
function Test-TemplateVariables {
    Write-TestHeader "测试 8: 模板变量替换"

    $testDir = Initialize-TestEnvironment -FileNames @("test8.txt")
    try {
        $r = BatchWrite-File -Path (New-TestFilePath "test8.txt") -Content "Hello {{Name}}, your role is {{Role}}!" -Variables @{Name='Admin'; Role='System Manager'} -Verify
        if ($r.TotalFiles -ne 1) { throw "TotalFiles 应为 1，实际 $($r.TotalFiles)" }
        $c = Get-Content (New-TestFilePath "test8.txt") -Raw -Encoding UTF8
        $expected = "Hello Admin, your role is System Manager!"
        if ($c.Trim() -ne $expected) { throw "内容不匹配: '$($c.Trim())' (期望: '$expected')" }
        Write-TestResult -TestName "模板变量替换" -Passed $true -Detail "Content: '$($c.Trim())'"
    } catch {
        Write-TestResult -TestName "模板变量替换" -Passed $false -Detail $_.Exception.Message
    }
}

# ============================================
# 测试 9：Merge 模式（单文件 SEARCH/REPLACE）
# ============================================
function Test-MergeSingleFile {
    Write-TestHeader "测试 9: Merge 模式（单文件 SEARCH/REPLACE）"

    $testDir = Initialize-TestEnvironment -MergeFileNames @("merge9.txt")
    try {
        $r = BatchWrite-File -Path (New-TestFilePath "merge9.txt") -Merge -Blocks @(@{Search='SettingName = "default"'; Replace='SettingName = "production"'}) -Verify
        if ($r.TotalFiles -ne 1) { throw "TotalFiles 应为 1，实际 $($r.TotalFiles)" }
        $c = Get-Content (New-TestFilePath "merge9.txt") -Raw
        if ($c -notmatch 'SettingName = "production"') { throw "内容未正确替换" }
        Write-TestResult -TestName "Merge 单文件" -Passed $true -Detail "包含 'SettingName = \"production\"'"
    } catch {
        Write-TestResult -TestName "Merge 单文件" -Passed $false -Detail $_.Exception.Message
    }
}

# ============================================
# 测试 10：Merge 模式（多文件）
# ============================================
function Test-MergeMultipleFiles {
    Write-TestHeader "测试 10: Merge 模式（多文件）"

    $testDir = Initialize-TestEnvironment -MergeFileNames @("merge10a.txt", "merge10b.txt")
    try {
        $mergeFiles = @((New-TestFilePath "merge10a.txt"), (New-TestFilePath "merge10b.txt"))
        $r = BatchWrite-File -Path $mergeFiles -Merge -Blocks @(@{Search='Timeout = 30'; Replace='Timeout = 60'}) -Verify

        $passed = $true
        foreach ($f in @("merge10a.txt", "merge10b.txt")) {
            $content = Get-Content (New-TestFilePath $f) -Raw
            if ($content -notmatch 'Timeout = 60') {
                $passed = $false
                Write-Host "  [FAIL] $f 未包含 'Timeout = 60'" -ForegroundColor Red
            }
        }
        Write-TestResult -TestName "Merge 多文件" -Passed $passed -Detail "TotalFiles=$($r.TotalFiles)"
    } catch {
        Write-TestResult -TestName "Merge 多文件" -Passed $false -Detail $_.Exception.Message
    }
}

# ============================================
# 测试 11：Merge 模式（多文件 + 多 Blocks）
# ============================================
function Test-MergeMultipleBlocks {
    Write-TestHeader "测试 11: Merge 模式（多文件 + 多 Blocks）"

    $testDir = Initialize-TestEnvironment -MergeFileNames @("merge11a.txt", "merge11b.txt")
    try {
        $mergeFiles = @((New-TestFilePath "merge11a.txt"), (New-TestFilePath "merge11b.txt"))
        $blocks = @(
            @{Search='SettingName = "default"'; Replace='SettingName = "production"'},
            @{Search='DebugMode = false'; Replace='DebugMode = true'}
        )
        $r = BatchWrite-File -Path $mergeFiles -Merge -Blocks $blocks -Verify

        $passed = $true
        foreach ($f in @("merge11a.txt", "merge11b.txt")) {
            $content = Get-Content (New-TestFilePath $f) -Raw
            if ($content -notmatch 'SettingName = "production"') { $passed = $false }
            if ($content -notmatch 'DebugMode = true') { $passed = $false }
        }
        Write-TestResult -TestName "Merge 多文件+多Blocks" -Passed $passed -Detail "TotalFiles=$($r.TotalFiles)"
    } catch {
        Write-TestResult -TestName "Merge 多文件+多Blocks" -Passed $false -Detail $_.Exception.Message
    }
}

# ============================================
# 测试 12：ConvertFrom-Json 模拟 invoke_ps_script
# ============================================
function Test-InvokePsScriptSimulation {
    Write-TestHeader "测试 12: ConvertFrom-Json 模拟 invoke_ps_script"

    $testDir = Initialize-TestEnvironment -FileNames @("test12.txt")
    try {
        $jsonInput = '{ "Paths": ["test12.txt"], "Content": ["来自 JSON 的写入"] }' | ConvertFrom-Json
        $resolvedPaths = $jsonInput.Paths | ForEach-Object { Join-Path (Get-TestTempDir) $_ }
        $r = BatchWrite-File -Paths $resolvedPaths -Content $jsonInput.Content -Verify
        if ($r.TotalFiles -ne 1) { throw "TotalFiles 应为 1，实际 $($r.TotalFiles)" }
        $c = Get-Content (New-TestFilePath "test12.txt") -Raw -Encoding UTF8
        if ($c.Trim() -ne "来自 JSON 的写入") { throw "内容不匹配: '$($c.Trim())'" }
        Write-TestResult -TestName "模拟 invoke_ps_script" -Passed $true -Detail "Content: '$($c.Trim())'"
    } catch {
        Write-TestResult -TestName "模拟 invoke_ps_script" -Passed $false -Detail $_.Exception.Message
    }
}

# ============================================
# 测试 13：多文件 + 多 Content 精确匹配
# ============================================
function Test-MultiFileExactContent {
    Write-TestHeader "测试 13: 多文件 + 多 Content 精确匹配"

    $testDir = Initialize-TestEnvironment -FileNames @("test13a.txt", "test13b.txt", "test13c.txt")
    try {
        $r = BatchWrite-File -Path @((New-TestFilePath "test13a.txt"), (New-TestFilePath "test13b.txt"), (New-TestFilePath "test13c.txt")) -Content @("内容A", "内容B", "内容C") -Verify
        if ($r.TotalFiles -ne 3) { throw "TotalFiles 应为 3，实际 $($r.TotalFiles)" }
        $ca = Get-Content (New-TestFilePath "test13a.txt") -Raw -Encoding UTF8
        $cb = Get-Content (New-TestFilePath "test13b.txt") -Raw -Encoding UTF8
        $cc = Get-Content (New-TestFilePath "test13c.txt") -Raw -Encoding UTF8
        if ($ca.Trim() -ne "内容A") { throw "File A 不匹配: '$($ca.Trim())'" }
        if ($cb.Trim() -ne "内容B") { throw "File B 不匹配: '$($cb.Trim())'" }
        if ($cc.Trim() -ne "内容C") { throw "File C 不匹配: '$($cc.Trim())'" }
        Write-TestResult -TestName "多文件+多Content精确匹配" -Passed $true -Detail "A: '$($ca.Trim())', B: '$($cb.Trim())', C: '$($cc.Trim())'"
    } catch {
        Write-TestResult -TestName "多文件+多Content精确匹配" -Passed $false -Detail $_.Exception.Message
    }
}

# ============================================
# 测试 14：大内容多行写入
# ============================================
function Test-MultiLineContent {
    Write-TestHeader "测试 14: 大内容多行写入"

    $testDir = Initialize-TestEnvironment -FileNames @("test14.txt")
    try {
        $multiLine = "Line 1`nLine 2`nLine 3`nLine 4`nLine 5"
        $r = BatchWrite-File -Path (New-TestFilePath "test14.txt") -Content $multiLine -Verify
        if ($r.TotalFiles -ne 1) { throw "TotalFiles 应为 1，实际 $($r.TotalFiles)" }
        $c = Get-Content (New-TestFilePath "test14.txt") -Raw -Encoding UTF8
        $lines = $c.Trim() -split "`n"
        if ($lines.Count -ne 5) { throw "行数应为 5，实际 $($lines.Count)" }
        Write-TestResult -TestName "大内容多行写入" -Passed $true -Detail "Lines: $($lines.Count)"
    } catch {
        Write-TestResult -TestName "大内容多行写入" -Passed $false -Detail $_.Exception.Message
    }
}

# ============================================
# 测试 15：Backup 模式
# ============================================
function Test-BackupMode {
    Write-TestHeader "测试 15: Backup 模式"

    $testDir = Initialize-TestEnvironment -FileNames @("test15.txt")
    try {
        Set-Content (New-TestFilePath "test15.txt") "原始内容" -Encoding UTF8 -NoNewline
        $r = BatchWrite-File -Path (New-TestFilePath "test15.txt") -Content "修改后的内容" -Backup -Verify
        if ($r.TotalFiles -ne 1) { throw "TotalFiles 应为 1，实际 $($r.TotalFiles)" }
        if (-not $r.Results[0].Verified) { throw "校验失败" }
        if (-not $r.Results[0].Backup) { throw "备份路径为空" }
        $c = Get-Content (New-TestFilePath "test15.txt") -Raw -Encoding UTF8
        if ($c.Trim() -ne "修改后的内容") { throw "内容不匹配: '$($c.Trim())'" }
        if (-not (Test-Path $r.Results[0].Backup)) { throw "备份文件不存在: $($r.Results[0].Backup)" }
        $backupContent = Get-Content $r.Results[0].Backup -Raw -Encoding UTF8
        if ($backupContent.Trim() -ne "原始内容") { throw "备份内容不匹配: '$($backupContent.Trim())'" }
        Write-TestResult -TestName "Backup 模式" -Passed $true -Detail "Content: '$($c.Trim())', Backup: '$($backupContent.Trim())'"
        Remove-Item $r.Results[0].Backup -Force -ErrorAction SilentlyContinue
    } catch {
        Write-TestResult -TestName "Backup 模式" -Passed $false -Detail $_.Exception.Message
    }
}

# ============================================
# 测试 16：DryRun 模式
# ============================================
function Test-DryRunMode {
    Write-TestHeader "测试 16: DryRun 模式"

    $testDir = Initialize-TestEnvironment -FileNames @("test16.txt")
    try {
        $r = BatchWrite-File -Path (New-TestFilePath "test16.txt") -Content "不应写入的内容" -DryRun
        if ($r.TotalFiles -ne 1) { throw "TotalFiles 应为 1，实际 $($r.TotalFiles)" }
        if ($r.Results[0].Action -ne "Simulated") { throw "Action 应为 Simulated，实际 $($r.Results[0].Action)" }
        $exists = Test-Path (New-TestFilePath "test16.txt")
        if ($exists) { throw "DryRun 不应创建文件" }
        Write-TestResult -TestName "DryRun 模式" -Passed $true -Detail "Action: $($r.Results[0].Action), File created: $exists"
    } catch {
        Write-TestResult -TestName "DryRun 模式" -Passed $false -Detail $_.Exception.Message
    }
}

# ============================================
# 测试 17：Append 模式
# ============================================
function Test-AppendMode {
    Write-TestHeader "测试 17: Append 模式"

    $testDir = Initialize-TestEnvironment -FileNames @("test17.txt")
    try {
        Set-Content (New-TestFilePath "test17.txt") "原始内容" -Encoding UTF8 -NoNewline
        $r = BatchWrite-File -Path (New-TestFilePath "test17.txt") -Content "追加内容" -Append -Verify
        if ($r.TotalFiles -ne 1) { throw "TotalFiles 应为 1，实际 $($r.TotalFiles)" }
        $c = Get-Content (New-TestFilePath "test17.txt") -Raw -Encoding UTF8
        $expected = "原始内容追加内容"
        if ($c.Trim() -ne $expected) { throw "内容不匹配: '$($c.Trim())' (期望: '$expected')" }
        Write-TestResult -TestName "Append 模式" -Passed $true -Detail "Content: '$($c.Trim())'"
    } catch {
        Write-TestResult -TestName "Append 模式" -Passed $false -Detail $_.Exception.Message
    }
}

# ============================================
# 测试 18：边界情况 - 空路径
# ============================================
function Test-EmptyPath {
    Write-TestHeader "测试 18: 边界情况 - 空路径"

    try {
        $r = BatchWrite-File -Path @() -Content "test"
        $passed = $r.Error -eq "未提供有效的文件路径"
        Write-TestResult -TestName "空路径数组" -Passed $passed -Detail "Error: $($r.Error)"
    } catch {
        Write-TestResult -TestName "空路径数组" -Passed $false -Detail $_.Exception.Message
    }
}

# ============================================
# 测试 19：边界情况 - 不存在的文件（Merge 模式）
# ============================================
function Test-NonExistentFile {
    Write-TestHeader "测试 19: 边界情况 - 不存在的文件（Merge 模式）"

    $testDir = Initialize-TestEnvironment
    try {
        $blocks = @(@{Search = "test"; Replace = "replaced"})
        $r = BatchWrite-File -Path (New-TestFilePath "nonexistent.txt") -Merge -Blocks $blocks
        $passed = $r.Results[0].Error -match "文件不存在"
        Write-TestResult -TestName "不存在的文件" -Passed $passed -Detail "Error: $($r.Results[0].Error)"
    } catch {
        Write-TestResult -TestName "不存在的文件" -Passed $false -Detail $_.Exception.Message
    }
}

# ============================================
# 主测试流程
# ============================================
function Invoke-AllTests {
    Write-Host "========================================" -ForegroundColor Magenta
    Write-Host "  BatchWrite-File 全面测试套件（合并版）" -ForegroundColor Magenta
    Write-Host "  时间: $(Get-Date)" -ForegroundColor DarkGray
    Write-Host "========================================" -ForegroundColor Magenta

    $tests = @(
        { Test-SingleFileWrite },
        { Test-MultiFileWrite },
        { Test-PathsAlias },
        { Test-SingleStringContent },
        { Test-MultiFileSingleContent },
        { Test-ChineseContent },
        { Test-EmptyContent },
        { Test-TemplateVariables },
        { Test-MergeSingleFile },
        { Test-MergeMultipleFiles },
        { Test-MergeMultipleBlocks },
        { Test-InvokePsScriptSimulation },
        { Test-MultiFileExactContent },
        { Test-MultiLineContent },
        { Test-BackupMode },
        { Test-DryRunMode },
        { Test-AppendMode },
        { Test-EmptyPath },
        { Test-NonExistentFile }
    )

    $passedCount = 0
    $failedCount = 0

    foreach ($test in $tests) {
        try {
            $testPassed = & $test
            if ($testPassed -eq $false) {
                $failedCount++
            } else {
                $passedCount++
            }
        } catch {
            Write-Host "[FATAL] 测试执行异常: $($_.Exception.Message)" -ForegroundColor Red
            $failedCount++
        }
    }

    Write-TestSummary -PassedCount $passedCount -FailedCount $failedCount -TotalCount $tests.Count

    # 清理
    Cleanup-TestEnvironment
}

# 执行
Invoke-AllTests
