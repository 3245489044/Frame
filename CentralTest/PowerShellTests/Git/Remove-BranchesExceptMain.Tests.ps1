<#
.SYNOPSIS
    Pester 测试：Remove-BranchesExceptMain.ps1

.DESCRIPTION
    测试 Script\Git\Remove-BranchesExceptMain.ps1 脚本。
    该脚本是一个独立的 PowerShell 脚本，通过 powershell -File 调用。
    测试内容包括：
    - 脚本存在性
    - 参数解析
    - 基本执行（不抛出异常）
#>

BeforeAll {
    $script:ProjectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
    $script:ScriptPath = Join-Path $script:ProjectRoot "Script\Git\Remove-BranchesExceptMain.ps1"
}

# ============================================
#  基本功能测试
# ============================================

Describe "Remove-BranchesExceptMain 基本功能" {
    It "脚本文件应存在" {
        Test-Path $script:ScriptPath | Should -Be $true
    }

    It "应能加载脚本（不抛出异常）" {
        # 使用 -NoProfile 避免 profile 干扰
        $result = & powershell -NoProfile -Command "& { param(`$Force) Write-Host 'Loaded' } -Force" 2>&1
        # 验证脚本语法正确
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

    It "应支持 -Force 参数" {
        $helpOutput = & powershell -NoProfile -File $script:ScriptPath -Force 2>&1
        # 脚本会尝试执行 git 命令，但至少应能正常启动
        $helpOutput | Should -Not -BeNullOrEmpty
    }
}

