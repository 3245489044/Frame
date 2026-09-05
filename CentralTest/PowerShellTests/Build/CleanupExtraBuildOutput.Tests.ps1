<#
.SYNOPSIS
    Pester 测试：CleanupExtraBuildOutput.ps1

.DESCRIPTION
    测试 Script\Build\CleanupExtraBuildOutput.ps1 脚本。
    该脚本依赖 Script\Modules\ 下的多个模块，测试验证脚本存在性和语法正确性。
#>

BeforeAll {
    $script:ProjectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
    $script:ScriptPath = Join-Path $script:ProjectRoot "Script\Build\CleanupExtraBuildOutput.ps1"
}

# ============================================
#  基本功能测试
# ============================================

Describe "CleanupExtraBuildOutput 基本功能" {
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

    It "应能加载脚本（不抛出异常）" {
        { . $script:ScriptPath } | Should -Not -Throw
    }
}

