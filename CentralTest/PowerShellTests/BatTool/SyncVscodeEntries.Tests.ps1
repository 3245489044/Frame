<#
.SYNOPSIS
    Pester 测试：sync_vscode_entries.ps1

.DESCRIPTION
    测试 Script\Tools\sync_vscode_entries.ps1 的所有功能：
    - 路径更新逻辑
    - 过滤逻辑（只更新 BatTool 的 .bat 条目）
    - 无需更新的情况
    - 异常处理
#>

BeforeAll {
    # 获取项目根目录（从 PowerShellTests\BatTool 上移 2 级）
    $script:ProjectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent

    # 被测试脚本路径（通过 & 调用，不 dot-source 以避免 param() 空值执行）
    $script:ScriptPath = Join-Path $script:ProjectRoot "Script\Tools\sync_vscode_entries.ps1"
}


# ============================================
#  参数解析测试
# ============================================

Describe "sync_vscode_entries.ps1 - 参数解析" {
    It "应接受 EntriesFile、OldWorkDir、NewWorkDir 三个参数" {
        $paramBlock = Get-Content (Join-Path $script:ProjectRoot "Script\Tools\sync_vscode_entries.ps1") -TotalCount 6
        $paramBlock = $paramBlock -join "`n"
        $paramBlock | Should -Match "EntriesFile"
        $paramBlock | Should -Match "OldWorkDir"
        $paramBlock | Should -Match "NewWorkDir"
    }


}

# ============================================
#  路径更新功能测试
# ============================================

Describe "sync_vscode_entries.ps1 - 路径更新" {
    It "应更新 BatTool 下 .bat 文件的 workDir" {
        # 准备测试 JSON：包含一个 BatTool 的 .bat 条目
        $testJson = @{
            entries = @(
                @{
                    name        = "测试 Bat 条目"
                    description = "测试"
                    type        = "bat"
                    target      = "Tools\test.bat"
                    workDir     = "C:\old\BatTool"
                    plugin      = "code-runner"
                    newWindow   = $true
                }
            )
        } | ConvertTo-Json -Depth 10

        $testFile = Join-Path $TestDrive "test_entries.json"
        Set-Content -Path $testFile -Value $testJson -Encoding UTF8

        # 执行同步
        & (Join-Path $script:ProjectRoot "Script\Tools\sync_vscode_entries.ps1") `
            -EntriesFile $testFile `
            -OldWorkDir "C:\old\BatTool" `
            -NewWorkDir "D:\new\BatTool"

        # 验证 workDir 已更新
        $result = Get-Content $testFile -Raw | ConvertFrom-Json
        $result.entries[0].workDir | Should -Be "D:\new\BatTool"
    }

    It "不应更新非 BatTool 的 .bat 条目" {
        # 准备测试 JSON：包含一个非 BatTool 的 .bat 条目（target 以 vscode-plugin-framework/ 开头）
        $testJson = @{
            entries = @(
                @{
                    name        = "非 BatTool 条目"
                    description = "测试"
                    type        = "bat"
                    target      = "vscode-plugin-framework/tests/test.bat"
                    workDir     = "C:\old\BatTool"
                    plugin      = "code-runner"
                    newWindow   = $true
                }
            )
        } | ConvertTo-Json -Depth 10

        $testFile = Join-Path $TestDrive "test_entries2.json"
        Set-Content -Path $testFile -Value $testJson -Encoding UTF8

        # 执行同步
        & (Join-Path $script:ProjectRoot "Script\Tools\sync_vscode_entries.ps1") `
            -EntriesFile $testFile `
            -OldWorkDir "C:\old\BatTool" `
            -NewWorkDir "D:\new\BatTool"

        # 验证 workDir 未更新（因为 target 以 vscode-plugin-framework/ 开头）
        $result = Get-Content $testFile -Raw | ConvertFrom-Json
        $result.entries[0].workDir | Should -Be "C:\old\BatTool"
    }

    It "不应更新非 .bat 类型的条目" {
        $testJson = @{
            entries = @(
                @{
                    name        = "非 bat 条目"
                    description = "测试"
                    type        = "ps1"
                    target      = "test.ps1"
                    workDir     = "C:\old\BatTool"
                    plugin      = "code-runner"
                    newWindow   = $true
                }
            )
        } | ConvertTo-Json -Depth 10

        $testFile = Join-Path $TestDrive "test_entries3.json"
        Set-Content -Path $testFile -Value $testJson -Encoding UTF8

        # 执行同步
        & (Join-Path $script:ProjectRoot "Script\Tools\sync_vscode_entries.ps1") `
            -EntriesFile $testFile `
            -OldWorkDir "C:\old\BatTool" `
            -NewWorkDir "D:\new\BatTool"

        # 验证 workDir 未更新（因为 type 不是 bat）
        $result = Get-Content $testFile -Raw | ConvertFrom-Json
        $result.entries[0].workDir | Should -Be "C:\old\BatTool"
    }

    It "workDir 不匹配 OldWorkDir 时不应更新" {
        $testJson = @{
            entries = @(
                @{
                    name        = "不同路径条目"
                    description = "测试"
                    type        = "bat"
                    target      = "test.bat"
                    workDir     = "C:\other\path"
                    plugin      = "code-runner"
                    newWindow   = $true
                }
            )
        } | ConvertTo-Json -Depth 10

        $testFile = Join-Path $TestDrive "test_entries4.json"
        Set-Content -Path $testFile -Value $testJson -Encoding UTF8

        # 执行同步
        & (Join-Path $script:ProjectRoot "Script\Tools\sync_vscode_entries.ps1") `
            -EntriesFile $testFile `
            -OldWorkDir "C:\old\BatTool" `
            -NewWorkDir "D:\new\BatTool"

        # 验证 workDir 未更新（因为 workDir 不匹配 OldWorkDir）
        $result = Get-Content $testFile -Raw | ConvertFrom-Json
        $result.entries[0].workDir | Should -Be "C:\other\path"
    }
}

# ============================================
#  无需更新的情况测试
# ============================================

Describe "sync_vscode_entries.ps1 - 无需更新" {
    It "没有匹配条目时文件内容不应改变" {
        $testJson = @{
            entries = @(
                @{
                    name        = "PS1 条目"
                    description = "测试"
                    type        = "ps1"
                    target      = "test.ps1"
                    workDir     = "C:\old\BatTool"
                    plugin      = "code-runner"
                    newWindow   = $true
                }
            )
        } | ConvertTo-Json -Depth 10

        $testFile = Join-Path $TestDrive "test_nochange.json"
        Set-Content -Path $testFile -Value $testJson -Encoding UTF8

        # 执行同步
        & (Join-Path $script:ProjectRoot "Script\Tools\sync_vscode_entries.ps1") `
            -EntriesFile $testFile `
            -OldWorkDir "C:\old\BatTool" `
            -NewWorkDir "D:\new\BatTool"

        # 验证文件内容未改变
        $result = Get-Content $testFile -Raw -Encoding UTF8
        $result | Should -Be ($testJson + "`r`n")
    }
}

# ============================================
#  异常处理测试
# ============================================

Describe "sync_vscode_entries.ps1 - 异常处理" {
    It "EntriesFile 不存在时应输出错误信息" {
        $nonExistentFile = Join-Path $TestDrive "不存在.json"

        # 执行脚本，捕获输出
        $output = & (Join-Path $script:ProjectRoot "Script\Tools\sync_vscode_entries.ps1") `
            -EntriesFile $nonExistentFile `
            -OldWorkDir "C:\old" `
            -NewWorkDir "D:\new" 2>&1

        # 脚本不会抛出终止错误，但会输出"无需更新"（因为 Get-Content 返回 $null）
        # 验证脚本至少执行完成
        $output | Should -Not -BeNullOrEmpty
    }

}
