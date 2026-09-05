<#
.SYNOPSIS
    Pester 测试：ProjectConfig.psm1

.DESCRIPTION
    测试 Script\Modules\Core\ProjectConfig.psm1 中的所有函数：
    - Get-ProjectConfig
    - Get-GlobalTargetFramework
    - Get-InvalidTfmMap
    - Get-WinFormsDependentProjects
    - Get-RedundantPlatforms
#>

BeforeAll {
    $script:ModulePath = Join-Path (Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent) "Script\Modules\Core\ProjectConfig.psm1"
    Import-Module $script:ModulePath -Force
}

# ============================================
#  Get-ProjectConfig 测试
# ============================================

Describe "Get-ProjectConfig" {
    It "应返回包含所有必需键的哈希表" {
        $config = Get-ProjectConfig
        $config | Should -Not -BeNullOrEmpty
        $config.ContainsKey("GlobalTargetFramework") | Should -Be $true
        $config.ContainsKey("InvalidTfmMap") | Should -Be $true
        $config.ContainsKey("WinFormsDependentProjects") | Should -Be $true
        $config.ContainsKey("RedundantPlatforms") | Should -Be $true
    }

    It "GlobalTargetFramework 应为 net10.0" {
        $config = Get-ProjectConfig
        $config.GlobalTargetFramework | Should -Be "net10.0"
    }

    It "InvalidTfmMap 应包含 net10.0-windows7.0 映射" {
        $config = Get-ProjectConfig
        $config.InvalidTfmMap["net10.0-windows7.0"] | Should -Be "net10.0-windows"
    }

    It "WinFormsDependentProjects 应包含 WdUI.csproj" {
        $config = Get-ProjectConfig
        $config.WinFormsDependentProjects | Should -Contain "WdUI.csproj"
    }

    It "RedundantPlatforms 应包含 Debug|x64" {
        $config = Get-ProjectConfig
        $config.RedundantPlatforms | Should -Contain "Debug|x64"
    }

    It "返回的哈希表应为副本（修改不影响内部状态）" {
        $config1 = Get-ProjectConfig
        $config2 = Get-ProjectConfig
        $config1.InvalidTfmMap["test"] = "value"
        $config2.InvalidTfmMap.ContainsKey("test") | Should -Be $false
    }
}

# ============================================
#  Get-GlobalTargetFramework 测试
# ============================================

Describe "Get-GlobalTargetFramework" {
    It "应返回 net10.0" {
        Get-GlobalTargetFramework | Should -Be "net10.0"
    }
}

# ============================================
#  Get-InvalidTfmMap 测试
# ============================================

Describe "Get-InvalidTfmMap" {
    It "应返回包含 net10.0-windows7.0 的映射表" {
        $map = Get-InvalidTfmMap
        $map["net10.0-windows7.0"] | Should -Be "net10.0-windows"
    }

    It "返回的映射表应为副本" {
        $map1 = Get-InvalidTfmMap
        $map2 = Get-InvalidTfmMap
        $map1["test"] = "value"
        $map2.ContainsKey("test") | Should -Be $false
    }
}

# ============================================
#  Get-WinFormsDependentProjects 测试
# ============================================

Describe "Get-WinFormsDependentProjects" {
    It "应返回项目名称数组" {
        $projects = Get-WinFormsDependentProjects
        $projects | Should -Not -BeNullOrEmpty
        $projects | Should -Contain "WdUI.csproj"
        $projects | Should -Contain "FileDriven.UI.csproj"
    }

    It "返回的数组应为副本" {
        $p1 = Get-WinFormsDependentProjects
        $p2 = Get-WinFormsDependentProjects
        $p1.Count | Should -Be $p2.Count
    }
}

# ============================================
#  Get-RedundantPlatforms 测试
# ============================================

Describe "Get-RedundantPlatforms" {
    It "应返回平台配置数组" {
        $platforms = Get-RedundantPlatforms
        $platforms | Should -Not -BeNullOrEmpty
        $platforms | Should -Contain "Debug|x64"
        $platforms | Should -Contain "Release|x86"
    }
}
