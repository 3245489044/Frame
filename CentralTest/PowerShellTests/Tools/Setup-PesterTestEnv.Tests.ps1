<#
.SYNOPSIS
    Pester 测试：Setup-PesterTestEnv.ps1

.DESCRIPTION
    测试 Script\Tools\Setup-PesterTestEnv.ps1 脚本。
    该脚本需要管理员权限，测试只验证脚本存在性和语法正确性。
#>

BeforeAll {
    $script:ProjectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
    $script:ScriptPath = Join-Path $script:ProjectRoot "Script\Tools\Setup-PesterTestEnv.ps1"
}

# ============================================
#  基本功能测试
# ============================================

Describe "Setup-PesterTestEnv 基本功能" {
    It "脚本文件应存在" {
        Test-Path $script:ScriptPath | Should -Be $true
    }

    It "脚本语法应正确" {
        $syntaxCheck = & powershell -NoProfile -Command "
            `$errors = @()
            `$content = Get-Content '$script:ScriptPath' -Raw
            try {
                `$null = [System.Management.Automation.Language.Parser]::ParseInput(`$content, [ref]`$null, [ref]`$errors)
                if (`$errors.Count -eq 0) { Write-Host 'Syntax OK' } else { Write-Host 'Syntax Errors: ' + `$errors.Count }
            } catch {
                Write-Host 'Parse Error: ' + `$_.Message
            }
        " 2>&1
        $syntaxCheck | Should -Match "Syntax OK"
    }

    It "非管理员运行时应输出错误" {
        $output = & powershell -NoProfile -File $script:ScriptPath 2>&1
        $output | Where-Object { $_ -match "请以管理员身份运行" } | Should -Not -BeNullOrEmpty
    }
}

