<#
.SYNOPSIS
    Pester 测试：ProjectPaths.psm1

.DESCRIPTION
    测试 Script\Modules\Core\ProjectPaths.psm1 中的所有函数：
    - Get-ProjectRoot
    - Reset-ProjectRootCache
    - Get-ProjectDirectories
    - Get-PathAliases
    - Set-AliasEnvironmentFromPaths
    - Get-LauncherOutputDir
#>

BeforeAll {
    $script:ModulePath = Join-Path (Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent) "Script\Modules\Core\ProjectPaths.psm1"
    Import-Module $script:ModulePath -Force
    $script:ProjectRoot = Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent
}

# ============================================
#  Get-ProjectRoot 测试
# ============================================

Describe "Get-ProjectRoot" {
    BeforeEach {
        Reset-ProjectRootCache
    }

    It "应返回项目根目录" {
        $root = Get-ProjectRoot
        $root | Should -Not -BeNullOrEmpty
        Test-Path (Join-Path $root "WorkFlowPrj.sln") | Should -Be $true
    }

    It "应缓存结果" {
        $root1 = Get-ProjectRoot
        $root2 = Get-ProjectRoot
        $root1 | Should -Be $root2
    }

    It "ForceRescan 应重新扫描" {
        $root1 = Get-ProjectRoot
        Reset-ProjectRootCache
        $root2 = Get-ProjectRoot -ForceRescan
        $root1 | Should -Be $root2
    }

    It "应支持指定起始路径" {
        $root = Get-ProjectRoot -StartPath $script:ProjectRoot
        $root | Should -Be $script:ProjectRoot
    }
}

# ============================================
#  Reset-ProjectRootCache 测试
# ============================================

Describe "Reset-ProjectRootCache" {
    It "应清除缓存" {
        Get-ProjectRoot | Out-Null
        Reset-ProjectRootCache
        # 再次调用应重新检测
        $root = Get-ProjectRoot
        $root | Should -Not -BeNullOrEmpty
    }
}

# ============================================
#  Get-ProjectDirectories 测试
# ============================================

Describe "Get-ProjectDirectories" {
    It "应返回包含所有必需键的哈希表" {
        $dirs = Get-ProjectDirectories
        $dirs | Should -Not -BeNullOrEmpty
        $dirs.ContainsKey("ProjectRoot") | Should -Be $true
        $dirs.ContainsKey("BasicMethod") | Should -Be $true
        $dirs.ContainsKey("FileDriven") | Should -Be $true
        $dirs.ContainsKey("Script") | Should -Be $true
        $dirs.ContainsKey("PowerShellTests") | Should -Be $true
    }

    It "应正确计算子目录路径" {
        $dirs = Get-ProjectDirectories
        $dirs.BasicMethod | Should -Be (Join-Path $dirs.ProjectRoot "BasicMethod")
        $dirs.FileDriven | Should -Be (Join-Path $dirs.ProjectRoot "FileDriven")
    }

    It "应支持指定项目根目录" {
        $dirs = Get-ProjectDirectories -ProjectRoot $script:ProjectRoot
        $dirs.ProjectRoot | Should -Be $script:ProjectRoot
    }
}

# ============================================
#  Get-PathAliases 测试
# ============================================

Describe "Get-PathAliases" {
    It "应返回包含所有必需键的哈希表" {
        $aliases = Get-PathAliases
        $aliases | Should -Not -BeNullOrEmpty
        $aliases.ContainsKey("Configs") | Should -Be $true
        $aliases.ContainsKey("Scripts") | Should -Be $true
        $aliases.ContainsKey("WorkFlow") | Should -Be $true
        $aliases.ContainsKey("Output") | Should -Be $true
        $aliases.ContainsKey("Root") | Should -Be $true
    }

    It "Root 应为项目根目录" {
        $aliases = Get-PathAliases
        $aliases.Root | Should -Be $script:ProjectRoot
    }

    It "Configs 应为 FileDriven\DrivenConfig\JsonConfig" {
        $aliases = Get-PathAliases
        $aliases.Configs | Should -Be (Join-Path $script:ProjectRoot "FileDriven\DrivenConfig\JsonConfig")
    }
}

# ============================================
#  Set-AliasEnvironmentFromPaths 测试
# ============================================

Describe "Set-AliasEnvironmentFromPaths" {
    BeforeEach {
        Get-ChildItem "env:FILEDRIVEN_ALIAS_*" -ErrorAction SilentlyContinue | ForEach-Object {
            Remove-Item "env:$($_.Name)" -ErrorAction SilentlyContinue
        }
    }

    It "应将别名注入到环境变量" {
        Set-AliasEnvironmentFromPaths
        $env:FILEDRIVEN_ALIAS_Root | Should -Be $script:ProjectRoot
        $env:FILEDRIVEN_ALIAS_Configs | Should -Not -BeNullOrEmpty
    }

    It "应支持覆盖别名值" {
        Set-AliasEnvironmentFromPaths -Overrides @{ "Root" = "D:\CustomRoot" }
        $env:FILEDRIVEN_ALIAS_Root | Should -Be "D:\CustomRoot"
    }

    It "应支持自定义别名哈希表" {
        Set-AliasEnvironmentFromPaths -Aliases @{ "Test" = "D:\test" }
        $env:FILEDRIVEN_ALIAS_Test | Should -Be "D:\test"
    }
}
