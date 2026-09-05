<#
.SYNOPSIS
    Pester 测试：register_entry.ps1

.DESCRIPTION
    测试 Script\Tools\register_entry.ps1 的所有功能：
    - 参数解析
    - JSON 条目添加
    - 文件写入
    - 异常处理（文件不存在）
#>

BeforeAll {
    # 获取项目根目录（从 PowerShellTests\BatTool 上移 2 级）
    $script:ProjectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent

    # 被测试脚本路径（通过 & 调用，不 dot-source 以避免 param() 空值执行）
    $script:ScriptPath = Join-Path $script:ProjectRoot "Script\Tools\register_entry.ps1"
}


# ============================================
#  register_entry.ps1 参数解析测试
# ============================================

Describe "register_entry.ps1 - 参数解析" {
    It "应接受 EntriesFile、EntryName、EntryTarget、WorkDir 四个参数" {
        # 验证脚本参数定义 - 通过检查 param 块
        $paramBlock = Get-Content (Join-Path $script:ProjectRoot "Script\Tools\register_entry.ps1") -TotalCount 6
        $paramBlock = $paramBlock -join "`n"
        $paramBlock | Should -Match "EntriesFile"
        $paramBlock | Should -Match "EntryName"
        $paramBlock | Should -Match "EntryTarget"
        $paramBlock | Should -Match "WorkDir"
    }


}

# ============================================
#  JSON 条目添加功能测试
# ============================================

Describe "register_entry.ps1 - JSON 条目添加" {
    It "应在 entries 数组中添加新条目" {
        # 准备测试 JSON
        $testJson = @{
            entries = @(
                @{
                    name        = "现有条目"
                    description = "测试"
                    type        = "bat"
                    target      = "test.bat"
                    workDir     = "C:\test"
                    plugin      = "code-runner"
                    newWindow   = $true
                }
            )
        } | ConvertTo-Json -Depth 10

        $testFile = Join-Path $TestDrive "test_entries.json"
        Set-Content -Path $testFile -Value $testJson -Encoding UTF8

        # 执行注册
        & (Join-Path $script:ProjectRoot "Script\Tools\register_entry.ps1") `
            -EntriesFile $testFile `
            -EntryName "新条目" `
            -EntryTarget "new.bat" `
            -WorkDir "D:\work"

        # 验证结果
        $result = Get-Content $testFile -Raw | ConvertFrom-Json
        $result.entries.Count | Should -Be 2
        $result.entries[1].name | Should -Be "新条目"
        $result.entries[1].target | Should -Be "new.bat"
        $result.entries[1].workDir | Should -Be "D:\work"
        $result.entries[1].type | Should -Be "bat"
        $result.entries[1].plugin | Should -Be "code-runner"
        $result.entries[1].newWindow | Should -Be $true
    }

    It "应保留现有条目不变" {
        $testJson = @{
            entries = @(
                @{
                    name        = "条目1"
                    description = "测试1"
                    type        = "bat"
                    target      = "test1.bat"
                    workDir     = "C:\test1"
                    plugin      = "code-runner"
                    newWindow   = $true
                },
                @{
                    name        = "条目2"
                    description = "测试2"
                    type        = "bat"
                    target      = "test2.bat"
                    workDir     = "C:\test2"
                    plugin      = "code-runner"
                    newWindow   = $false
                }
            )
        } | ConvertTo-Json -Depth 10

        $testFile = Join-Path $TestDrive "test_entries2.json"
        Set-Content -Path $testFile -Value $testJson -Encoding UTF8

        # 执行注册
        & (Join-Path $script:ProjectRoot "Script\Tools\register_entry.ps1") `
            -EntriesFile $testFile `
            -EntryName "新条目" `
            -EntryTarget "new.bat" `
            -WorkDir "D:\work"

        # 验证原有条目未被修改
        $result = Get-Content $testFile -Raw | ConvertFrom-Json
        $result.entries[0].name | Should -Be "条目1"
        $result.entries[0].target | Should -Be "test1.bat"
        $result.entries[1].name | Should -Be "条目2"
        $result.entries[1].target | Should -Be "test2.bat"
    }

    It "新条目的 description 应为 'BatTool 启动项'" {
        $testJson = @{ entries = @() } | ConvertTo-Json -Depth 10
        $testFile = Join-Path $TestDrive "test_entries3.json"
        Set-Content -Path $testFile -Value $testJson -Encoding UTF8

        & (Join-Path $script:ProjectRoot "Script\Tools\register_entry.ps1") `
            -EntriesFile $testFile `
            -EntryName "测试条目" `
            -EntryTarget "test.bat" `
            -WorkDir "D:\work"

        $result = Get-Content $testFile -Raw | ConvertFrom-Json
        $result.entries[0].description | Should -Be "BatTool 启动项"
    }
}

# ============================================
#  异常处理测试
# ============================================

Describe "register_entry.ps1 - 异常处理" {
    It "EntriesFile 不存在时应报错" {
        $nonExistentFile = Join-Path $TestDrive "不存在.json"

        # 执行脚本，预期会出错
        $errorOutput = $null
        try {
            & (Join-Path $script:ProjectRoot "Script\Tools\register_entry.ps1") `
                -EntriesFile $nonExistentFile `
                -EntryName "测试" `
                -EntryTarget "test.bat" `
                -WorkDir "D:\work" 2>&1 | Out-Null
        } catch {
            $errorOutput = $_
        }

        # 由于脚本没有 try-catch，Get-Content 会抛出终止错误
        # 验证错误确实发生了
        $errorOutput | Should -Not -BeNullOrEmpty
    }
}

# ============================================
#  文件编码测试
# ============================================

Describe "register_entry.ps1 - 文件编码" {
    It "应使用 UTF8 编码写入文件" {
        $testJson = @{ entries = @() } | ConvertTo-Json -Depth 10
        $testFile = Join-Path $TestDrive "test_encoding.json"
        Set-Content -Path $testFile -Value $testJson -Encoding UTF8

        & (Join-Path $script:ProjectRoot "Script\Tools\register_entry.ps1") `
            -EntriesFile $testFile `
            -EntryName "中文条目" `
            -EntryTarget "中文文件.bat" `
            -WorkDir "D:\中文路径"

        # 验证文件可以正确读取（中文内容）
        $result = Get-Content $testFile -Raw -Encoding UTF8 | ConvertFrom-Json
        $result.entries[0].name | Should -Be "中文条目"
        $result.entries[0].target | Should -Be "中文文件.bat"
        $result.entries[0].workDir | Should -Be "D:\中文路径"
    }
}
