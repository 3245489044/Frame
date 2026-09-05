<#
.SYNOPSIS
    Pester 测试：fix_coderunner_config.ps1

.DESCRIPTION
    测试 Script\Tools\fix_coderunner_config.ps1 的所有功能：
    - executorMap 创建
    - bat 执行器配置
    - runInTerminal 设置
    - 保留现有配置
#>

BeforeAll {
    # 获取项目根目录（从 PowerShellTests\BatTool 上移 2 级）
    $script:ProjectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent

    # 被测试脚本路径（通过 & 调用，不 dot-source 以避免 param() 空值执行）
    $script:ScriptPath = Join-Path $script:ProjectRoot "Script\Tools\fix_coderunner_config.ps1"
}


# ============================================
#  参数解析测试
# ============================================

Describe "fix_coderunner_config.ps1 - 参数解析" {
    It "应接受 SettingsFile 参数" {
        $paramBlock = Get-Content (Join-Path $script:ProjectRoot "Script\Tools\fix_coderunner_config.ps1") -TotalCount 4
        $paramBlock = $paramBlock -join "`n"
        $paramBlock | Should -Match "SettingsFile"
    }


}

# ============================================
#  executorMap 创建测试
# ============================================

Describe "fix_coderunner_config.ps1 - executorMap 创建" {
    It "settings.json 中没有 executorMap 时应创建" {
        # 准备测试 JSON：没有 code-runner.executorMap
        $testJson = @{
            "editor.fontSize" = 14
            "files.autoSave"  = "onFocusChange"
        } | ConvertTo-Json -Depth 10

        $testFile = Join-Path $TestDrive "settings_no_executor.json"
        Set-Content -Path $testFile -Value $testJson -Encoding UTF8

        # 执行修复
        & (Join-Path $script:ProjectRoot "Script\Tools\fix_coderunner_config.ps1") `
            -SettingsFile $testFile

        # 验证 executorMap 已创建
        $result = Get-Content $testFile -Raw | ConvertFrom-Json
        $result.'code-runner.executorMap' | Should -Not -BeNullOrEmpty
        $result.'code-runner.executorMap'.bat | Should -Match 'start "Code Runner"'
    }

    It "settings.json 中已有 executorMap 时应保留其他配置" {
        # 准备测试 JSON：已有 executorMap 和其他语言配置
        $testJson = @{
            "code-runner.executorMap" = @{
                "python" = "python -u $fullFileName"
                "javascript" = "node $fullFileName"
            }
            "editor.fontSize" = 14
        } | ConvertTo-Json -Depth 10

        $testFile = Join-Path $TestDrive "settings_with_executor.json"
        Set-Content -Path $testFile -Value $testJson -Encoding UTF8

        # 执行修复
        & (Join-Path $script:ProjectRoot "Script\Tools\fix_coderunner_config.ps1") `
            -SettingsFile $testFile

        # 验证原有配置保留，bat 配置已添加
        $result = Get-Content $testFile -Raw | ConvertFrom-Json
        $result.'code-runner.executorMap'.python | Should -Be "python -u $fullFileName"
        $result.'code-runner.executorMap'.javascript | Should -Be "node $fullFileName"
        $result.'code-runner.executorMap'.bat | Should -Match 'start "Code Runner"'
    }
}

# ============================================
#  bat 执行器配置测试
# ============================================

Describe "fix_coderunner_config.ps1 - bat 执行器配置" {
    It "bat 执行器应使用 start 命令包装" {
        $testJson = @{} | ConvertTo-Json -Depth 10
        $testFile = Join-Path $TestDrive "settings_bat.json"
        Set-Content -Path $testFile -Value $testJson -Encoding UTF8

        & (Join-Path $script:ProjectRoot "Script\Tools\fix_coderunner_config.ps1") `
            -SettingsFile $testFile

        $result = Get-Content $testFile -Raw | ConvertFrom-Json
        $result.'code-runner.executorMap'.bat | Should -Match '^start "Code Runner"'
        $result.'code-runner.executorMap'.bat | Should -Match 'cmd /c'
        $result.'code-runner.executorMap'.bat | Should -Match '\$fullFileName'
    }

    It "bat 执行器应支持中文文件名" {
        $testJson = @{} | ConvertTo-Json -Depth 10
        $testFile = Join-Path $TestDrive "settings_chinese.json"
        Set-Content -Path $testFile -Value $testJson -Encoding UTF8

        & (Join-Path $script:ProjectRoot "Script\Tools\fix_coderunner_config.ps1") `
            -SettingsFile $testFile

        $result = Get-Content $testFile -Raw | ConvertFrom-Json
        # 验证使用完整路径（$fullFileName），支持中文文件名
        $result.'code-runner.executorMap'.bat | Should -Match '\$fullFileName'
    }
}

# ============================================
#  runInTerminal 设置测试
# ============================================

Describe "fix_coderunner_config.ps1 - runInTerminal 设置" {
    It "应将 runInTerminal 设置为 false" {
        $testJson = @{
            "code-runner.runInTerminal" = $true
        } | ConvertTo-Json -Depth 10

        $testFile = Join-Path $TestDrive "settings_terminal.json"
        Set-Content -Path $testFile -Value $testJson -Encoding UTF8

        & (Join-Path $script:ProjectRoot "Script\Tools\fix_coderunner_config.ps1") `
            -SettingsFile $testFile

        $result = Get-Content $testFile -Raw | ConvertFrom-Json
        $result.'code-runner.runInTerminal' | Should -Be $false
    }

    It "settings.json 中没有 runInTerminal 时应添加并设置为 false" {
        $testJson = @{} | ConvertTo-Json -Depth 10
        $testFile = Join-Path $TestDrive "settings_no_terminal.json"
        Set-Content -Path $testFile -Value $testJson -Encoding UTF8

        & (Join-Path $script:ProjectRoot "Script\Tools\fix_coderunner_config.ps1") `
            -SettingsFile $testFile

        $result = Get-Content $testFile -Raw | ConvertFrom-Json
        $result.'code-runner.runInTerminal' | Should -Be $false
    }
}

# ============================================
#  保留现有配置测试
# ============================================

Describe "fix_coderunner_config.ps1 - 保留现有配置" {
    It "应保留 settings.json 中的其他配置项" {
        $testJson = @{
            "editor.fontSize" = 16
            "files.autoSave" = "afterDelay"
            "workbench.colorTheme" = "Dark+"
        } | ConvertTo-Json -Depth 10

        $testFile = Join-Path $TestDrive "settings_preserve.json"
        Set-Content -Path $testFile -Value $testJson -Encoding UTF8

        & (Join-Path $script:ProjectRoot "Script\Tools\fix_coderunner_config.ps1") `
            -SettingsFile $testFile

        $result = Get-Content $testFile -Raw | ConvertFrom-Json
        $result.'editor.fontSize' | Should -Be 16
        $result.'files.autoSave' | Should -Be "afterDelay"
        $result.'workbench.colorTheme' | Should -Be "Dark+"
    }
}

# ============================================
#  异常处理测试
# ============================================

Describe "fix_coderunner_config.ps1 - 异常处理" {
    It "SettingsFile 不存在时应报错" {
        $nonExistentFile = Join-Path $TestDrive "不存在的settings.json"

        $errorOutput = $null
        try {
            & (Join-Path $script:ProjectRoot "Script\Tools\fix_coderunner_config.ps1") `
                -SettingsFile $nonExistentFile 2>&1 | Out-Null
        } catch {
            $errorOutput = $_
        }

        $errorOutput | Should -Not -BeNullOrEmpty
    }
}
