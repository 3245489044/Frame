<#
.SYNOPSIS
    Pester 测试：Fix-CrossProjectAnalysis.ps1

.DESCRIPTION
    测试 Script\Solution\Fix-CrossProjectAnalysis.ps1 脚本中的函数：
    - Get-TargetFrameworkIssues
    - Fix-TargetFramework
    - Get-MissingBuildConfigIssues
    - Get-ProjectNameFromGuid
    - Fix-MissingBuildConfig
    - Test-SlnFile
    - Show-Help
#>

BeforeAll {
    $script:ProjectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
    $script:ScriptPath = Join-Path $script:ProjectRoot "Script\Solution\Fix-CrossProjectAnalysis.ps1"

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
#  Get-ProjectNameFromGuid 测试
# ============================================

Describe "Get-ProjectNameFromGuid" {
    It "应返回匹配 GUID 的项目名称" {
        $lines = @(
            'Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "TestProject", "TestProject.csproj", "{ABC12345-1234-1234-1234-123456789ABC}"'
        )
        $name = Get-ProjectNameFromGuid -Guid "{ABC12345-1234-1234-1234-123456789ABC}" -SlnLines $lines
        $name | Should -Be "TestProject"
    }

    It "GUID 不匹配时应返回 GUID 本身" {
        $lines = @(
            'Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "TestProject", "TestProject.csproj", "{ABC12345-1234-1234-1234-123456789ABC}"'
        )
        $name = Get-ProjectNameFromGuid -Guid "{NONEXISTENT-GUID}" -SlnLines $lines
        $name | Should -Be "{NONEXISTENT-GUID}"
    }
}

# ============================================
#  Get-MissingBuildConfigIssues 测试
# ============================================

Describe "Get-MissingBuildConfigIssues" {
    It "所有项目都有 Build.0 时应返回空列表" {
        $slnFile = Join-Path $TestDrive "complete.sln"
        $content = @'
Microsoft Visual Studio Solution File, Format Version 12.00
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "TestProject", "TestProject.csproj", "{ABC12345-1234-1234-1234-123456789ABC}"
EndProject
Global
	GlobalSection(ProjectConfigurationPlatforms) = postSolution
		{ABC12345-1234-1234-1234-123456789ABC}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
		{ABC12345-1234-1234-1234-123456789ABC}.Debug|Any CPU.Build.0 = Debug|Any CPU
	EndGlobalSection
EndGlobal
'@
        Set-Content -Path $slnFile -Value $content -Encoding UTF8

        # 临时替换 slnFile 变量
        $originalSlnFile = $script:slnFile
        $script:slnFile = $slnFile
        try {
            $issues = Get-MissingBuildConfigIssues
            $issues.Count | Should -Be 0
        } finally {
            $script:slnFile = $originalSlnFile
        }
    }

    It "缺少 Build.0 时应返回问题列表" {
        $slnFile = Join-Path $TestDrive "incomplete.sln"
        $content = @'
Microsoft Visual Studio Solution File, Format Version 12.00
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "TestProject", "TestProject.csproj", "{ABC12345-1234-1234-1234-123456789ABC}"
EndProject
Global
	GlobalSection(ProjectConfigurationPlatforms) = postSolution
		{ABC12345-1234-1234-1234-123456789ABC}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
	EndGlobalSection
EndGlobal
'@
        Set-Content -Path $slnFile -Value $content -Encoding UTF8

        $originalSlnFile = $script:slnFile
        $script:slnFile = $slnFile
        try {
            $issues = Get-MissingBuildConfigIssues
            $issues.Count | Should -Be 1
            $issues[0].Guid | Should -Be "{ABC12345-1234-1234-1234-123456789ABC}"
        } finally {
            $script:slnFile = $originalSlnFile
        }
    }
}

# ============================================
#  Fix-MissingBuildConfig 测试
# ============================================

Describe "Fix-MissingBuildConfig" {
    It "应补全缺失的 Build.0 配置" {
        $slnFile = Join-Path $TestDrive "fixable.sln"
        $content = @'
Microsoft Visual Studio Solution File, Format Version 12.00
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "TestProject", "TestProject.csproj", "{ABC12345-1234-1234-1234-123456789ABC}"
EndProject
Global
	GlobalSection(ProjectConfigurationPlatforms) = postSolution
		{ABC12345-1234-1234-1234-123456789ABC}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
	EndGlobalSection
EndGlobal
'@
        Set-Content -Path $slnFile -Value $content -Encoding UTF8

        $originalSlnFile = $script:slnFile
        $script:slnFile = $slnFile
        try {
            $issues = Get-MissingBuildConfigIssues
            $fixedCount = Fix-MissingBuildConfig -Issues $issues
            $fixedCount | Should -Be 1

            # 验证文件已更新
            $updatedContent = Get-Content $slnFile -Raw
            $updatedContent | Should -Match 'Build\.0'
        } finally {
            $script:slnFile = $originalSlnFile
        }
    }

    It "空问题列表应返回 0" {
        $fixedCount = Fix-MissingBuildConfig -Issues @()
        $fixedCount | Should -Be 0
    }
}

# ============================================
#  Fix-TargetFramework 测试
# ============================================

Describe "Fix-TargetFramework" {
    It "应修复 TargetFramework" {
        $csprojFile = Join-Path $TestDrive "test.csproj"
        Set-Content -Path $csprojFile -Value '<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>' -Encoding UTF8

        $issues = @(
            [PSCustomObject]@{
                FilePath = $csprojFile
                RelativePath = "test.csproj"
                CurrentTF = "net10.0"
                NewTF = "net10.0-windows"
            }
        )

        $fixedCount = Fix-TargetFramework -Issues $issues
        $fixedCount | Should -Be 1

        $updatedContent = Get-Content $csprojFile -Raw
        $updatedContent | Should -Match 'net10.0-windows'
    }

    It "空问题列表应返回 0" {
        $fixedCount = Fix-TargetFramework -Issues @()
        $fixedCount | Should -Be 0
    }
}

# ============================================
#  Show-Help 测试
# ============================================

Describe "Show-Help" {
    It "应显示帮助信息" {
        $output = & { Show-Help } 6>&1
        $output.Count | Should -BeGreaterThan 0
        $output | Where-Object { $_ -match "Fix-CrossProjectAnalysis" } | Should -Not -BeNullOrEmpty
    }
}

