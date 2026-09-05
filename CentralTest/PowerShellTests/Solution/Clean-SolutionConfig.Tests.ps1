<#
.SYNOPSIS
    Pester 测试：Clean-SolutionConfig.ps1

.DESCRIPTION
    测试 Script\Solution\Clean-SolutionConfig.ps1 脚本中的函数：
    - Test-HasRedundantConfig
    - Get-RedundantLineCount
    - Optimize-SolutionFile
    - Test-SlnFile
    - Show-Help
#>

BeforeAll {
    $script:ProjectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
    $script:ScriptPath = Join-Path $script:ProjectRoot "Script\Solution\Clean-SolutionConfig.ps1"

    # dot-source 脚本以获取内部函数
    . $script:ScriptPath
}

# ============================================
#  Test-SlnFile 测试
# ============================================

Describe "Test-SlnFile" {
    It "文件不存在时应返回 $false" {
        $result = Test-SlnFile
        $result | Should -Be $false
    }
}

# ============================================
#  Test-HasRedundantConfig 测试
# ============================================

Describe "Test-HasRedundantConfig" {
    It "只有 Debug|Any CPU 时应返回 $false" {
        $lines = @(
            'GlobalSection(SolutionConfigurationPlatforms) = preSolution',
            '	Debug|Any CPU = Debug|Any CPU',
            'EndGlobalSection',
            'GlobalSection(ProjectConfigurationPlatforms) = postSolution',
            '	{ABC}.Debug|Any CPU.ActiveCfg = Debug|Any CPU',
            '	{ABC}.Debug|Any CPU.Build.0 = Debug|Any CPU',
            'EndGlobalSection'
        )
        Test-HasRedundantConfig -Lines $lines | Should -Be $false
    }

    It "包含 Release 配置时应返回 $true" {
        $lines = @(
            'GlobalSection(SolutionConfigurationPlatforms) = preSolution',
            '	Debug|Any CPU = Debug|Any CPU',
            '	Release|Any CPU = Release|Any CPU',
            'EndGlobalSection',
            'GlobalSection(ProjectConfigurationPlatforms) = postSolution',
            '	{ABC}.Debug|Any CPU.ActiveCfg = Debug|Any CPU',
            '	{ABC}.Release|Any CPU.ActiveCfg = Release|Any CPU',
            'EndGlobalSection'
        )
        Test-HasRedundantConfig -Lines $lines | Should -Be $true
    }

    It "包含 x64 配置时应返回 $true" {
        $lines = @(
            'GlobalSection(SolutionConfigurationPlatforms) = preSolution',
            '	Debug|Any CPU = Debug|Any CPU',
            '	Debug|x64 = Debug|x64',
            'EndGlobalSection',
            'GlobalSection(ProjectConfigurationPlatforms) = postSolution',
            '	{ABC}.Debug|Any CPU.ActiveCfg = Debug|Any CPU',
            '	{ABC}.Debug|x64.ActiveCfg = Debug|x64',
            'EndGlobalSection'
        )
        Test-HasRedundantConfig -Lines $lines | Should -Be $true
    }

    It "空行应被忽略" {
        $lines = @(
            '',
            'GlobalSection(SolutionConfigurationPlatforms) = preSolution',
            '',
            '	Debug|Any CPU = Debug|Any CPU',
            '',
            'EndGlobalSection',
            ''
        )
        Test-HasRedundantConfig -Lines $lines | Should -Be $false
    }
}

# ============================================
#  Get-RedundantLineCount 测试
# ============================================

Describe "Get-RedundantLineCount" {
    It "无冗余时应返回 0" {
        $lines = @(
            'GlobalSection(SolutionConfigurationPlatforms) = preSolution',
            '	Debug|Any CPU = Debug|Any CPU',
            'EndGlobalSection',
            'GlobalSection(ProjectConfigurationPlatforms) = postSolution',
            '	{ABC}.Debug|Any CPU.ActiveCfg = Debug|Any CPU',
            '	{ABC}.Debug|Any CPU.Build.0 = Debug|Any CPU',
            'EndGlobalSection'
        )
        Get-RedundantLineCount -Lines $lines | Should -Be 0
    }

    It "应正确统计冗余行数" {
        $lines = @(
            'GlobalSection(SolutionConfigurationPlatforms) = preSolution',
            '	Debug|Any CPU = Debug|Any CPU',
            '	Release|Any CPU = Release|Any CPU',
            'EndGlobalSection',
            'GlobalSection(ProjectConfigurationPlatforms) = postSolution',
            '	{ABC}.Debug|Any CPU.ActiveCfg = Debug|Any CPU',
            '	{ABC}.Debug|Any CPU.Build.0 = Debug|Any CPU',
            '	{ABC}.Release|Any CPU.ActiveCfg = Release|Any CPU',
            '	{ABC}.Release|Any CPU.Build.0 = Release|Any CPU',
            'EndGlobalSection'
        )
        Get-RedundantLineCount -Lines $lines | Should -Be 3
    }
}

# ============================================
#  Show-Help 测试
# ============================================

Describe "Show-Help" {
    It "应显示帮助信息" {
        $output = & { Show-Help } 6>&1
        $output.Count | Should -BeGreaterThan 0
        $output | Where-Object { $_ -match "Clean-SolutionConfig" } | Should -Not -BeNullOrEmpty
    }
}

