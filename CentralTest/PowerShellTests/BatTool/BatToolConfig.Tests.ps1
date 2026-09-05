<#
.SYNOPSIS
    Pester 测试：BatToolConfig.psm1（PowerShell 模块）

.DESCRIPTION
    测试 Script\Modules\Core\BatToolConfig.psm1 的所有功能：
    - Get-BatToolAliases 别名导出
    - Show-BatToolAliases 别名显示
    - 路径正确性
    - 模块导出成员

.NOTES
    原 BatToolConfig.bat 已被迁移为 PowerShell 模块 BatToolConfig.psm1。
    本测试直接测试 PowerShell 模块功能。
    注意：Get-ProjectRoot 内部使用 $MyInvocation.MyCommand.Path，
    在 Pester 中通过 Import-Module 加载时该变量有效。
#>

BeforeAll {
    # 获取项目根目录（从 PowerShellTests\BatTool 上移 2 级）
    $script:ProjectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
    $script:ModulePath = Join-Path $script:ProjectRoot "Script\Modules\Core\BatToolConfig.psm1"

    # 导入被测试模块（使用 -Force 确保重新加载）
    Remove-Module BatToolConfig -ErrorAction SilentlyContinue
    Import-Module $script:ModulePath -Force -Global
}

# ============================================
#  模块加载测试
# ============================================

Describe "BatToolConfig.psm1 - 模块加载" {
    It "模块文件应存在" {
        Test-Path $script:ModulePath | Should -Be $true
    }

    It "应成功导入模块" {
        $module = Get-Module BatToolConfig
        $module | Should -Not -BeNullOrEmpty
    }

    It "应导出 Get-BatToolAliases 函数" {
        $module = Get-Module BatToolConfig
        $module.ExportedFunctions.ContainsKey('Get-BatToolAliases') | Should -Be $true
    }

    It "应导出 Show-BatToolAliases 函数" {
        $module = Get-Module BatToolConfig
        $module.ExportedFunctions.ContainsKey('Show-BatToolAliases') | Should -Be $true
    }
}

# ============================================
#  Get-BatToolAliases 测试
# ============================================

Describe "Get-BatToolAliases - 别名导出" {
    It "应返回 hashtable" {
        $aliases = Get-BatToolAliases
        $aliases | Should -BeOfType [hashtable]
    }

    It "应包含 Configs 键" {
        $aliases = Get-BatToolAliases
        $aliases.ContainsKey('Configs') | Should -Be $true
    }

    It "应包含 Scripts 键" {
        $aliases = Get-BatToolAliases
        $aliases.ContainsKey('Scripts') | Should -Be $true
    }

    It "应包含 WorkFlow 键" {
        $aliases = Get-BatToolAliases
        $aliases.ContainsKey('WorkFlow') | Should -Be $true
    }

    It "应包含 Output 键" {
        $aliases = Get-BatToolAliases
        $aliases.ContainsKey('Output') | Should -Be $true
    }

    It "应包含 Root 键" {
        $aliases = Get-BatToolAliases
        $aliases.ContainsKey('Root') | Should -Be $true
    }
}

# ============================================
#  路径正确性测试
# ============================================

Describe "Get-BatToolAliases - 路径正确性" {
    It "Root 应指向项目根目录" {
        $aliases = Get-BatToolAliases
        $aliases.Root | Should -Be $script:ProjectRoot
    }

    It "Configs 应指向 FileDriven\DrivenConfig\JsonConfig" {
        $aliases = Get-BatToolAliases
        $expected = Join-Path $script:ProjectRoot "FileDriven\DrivenConfig\JsonConfig"
        $aliases.Configs | Should -Be $expected
    }

    It "Scripts 应指向 FileDriven\DrivenConfig\Scripts" {
        $aliases = Get-BatToolAliases
        $expected = Join-Path $script:ProjectRoot "FileDriven\DrivenConfig\Scripts"
        $aliases.Scripts | Should -Be $expected
    }

    It "WorkFlow 应指向 FileDriven\DrivenConfig\WorkFlow" {
        $aliases = Get-BatToolAliases
        $expected = Join-Path $script:ProjectRoot "FileDriven\DrivenConfig\WorkFlow"
        $aliases.WorkFlow | Should -Be $expected
    }

    It "Output 应指向 BuildOutput 目录" {
        $aliases = Get-BatToolAliases
        $aliases.Output | Should -Match "BuildOutput"
    }

    It "所有路径应为绝对路径" {
        $aliases = Get-BatToolAliases
        $aliases.Values | ForEach-Object {
            [System.IO.Path]::IsPathRooted($_) | Should -Be $true
        }
    }

    It "非 Output 路径应存在" {
        $aliases = Get-BatToolAliases
        $aliases.Keys | Where-Object { $_ -ne "Output" -and $_ -ne "Root" } | ForEach-Object {
            Test-Path $aliases[$_] | Should -Be $true -Because "路径 $($aliases[$_]) 应存在"
        }
    }
}

# ============================================
#  Show-BatToolAliases 测试
# ============================================

Describe "Show-BatToolAliases - 别名显示" {
    It "应输出别名信息（不抛出异常）" {
        { Show-BatToolAliases } | Should -Not -Throw
    }
}

# ============================================
#  多次调用稳定性测试
# ============================================

Describe "Get-BatToolAliases - 稳定性" {
    It "多次调用应返回一致的结果" {
        $result1 = Get-BatToolAliases
        $result2 = Get-BatToolAliases

        $result1.Configs | Should -Be $result2.Configs
        $result1.Scripts | Should -Be $result2.Scripts
        $result1.WorkFlow | Should -Be $result2.WorkFlow
        $result1.Root | Should -Be $result2.Root
    }
}
