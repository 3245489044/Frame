<#
.SYNOPSIS
    Pester 测试：Sync-SlnToDisk.ps1

.DESCRIPTION
    测试 Script\Solution\Sync-SlnToDisk.ps1 脚本中的函数：
    - Parse-SolutionFile
    - Scan-DiskProjects
    - Build-DirectoryTree
    - Generate-SolutionContent
    - New-GuidString
    - Show-Help
#>

BeforeAll {
    $script:ProjectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
    $script:ScriptPath = Join-Path $script:ProjectRoot "Script\Solution\Sync-SlnToDisk.ps1"

    # dot-source 脚本以获取内部函数
    . $script:ScriptPath
}

# ============================================
#  New-GuidString 测试
# ============================================

Describe "New-GuidString" {
    It "应返回有效的 GUID 字符串" {
        $guid = New-GuidString
        $guid | Should -Match '^[A-F0-9]{8}-[A-F0-9]{4}-[A-F0-9]{4}-[A-F0-9]{4}-[A-F0-9]{12}$'
    }

    It "每次调用应返回不同的 GUID" {
        $guid1 = New-GuidString
        $guid2 = New-GuidString
        $guid1 | Should -Not -Be $guid2
    }
}

# ============================================
#  Parse-SolutionFile 测试
# ============================================

Describe "Parse-SolutionFile" {
    It "文件不存在时应返回空结果" {
        $result = Parse-SolutionFile -Path "C:\NonExistent.sln"
        $result.Projects.Count | Should -Be 0
        $result.Folders.Count | Should -Be 0
    }

    It "应正确解析 .sln 文件" {
        $slnFile = Join-Path $TestDrive "test.sln"
        $slnContent = @'
Microsoft Visual Studio Solution File, Format Version 12.00
# Visual Studio Version 17
VisualStudioVersion = 17.0.31903.59
MinimumVisualStudioVersion = 10.0.40219.1
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "TestProject", "TestProject\TestProject.csproj", "{ABC12345-1234-1234-1234-123456789ABC}"
EndProject
Global
	GlobalSection(SolutionConfigurationPlatforms) = preSolution
		Debug|Any CPU = Debug|Any CPU
	EndGlobalSection
	GlobalSection(ProjectConfigurationPlatforms) = postSolution
		{ABC12345-1234-1234-1234-123456789ABC}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
		{ABC12345-1234-1234-1234-123456789ABC}.Debug|Any CPU.Build.0 = Debug|Any CPU
	EndGlobalSection
EndGlobal
'@
        Set-Content -Path $slnFile -Value $slnContent -Encoding UTF8

        $result = Parse-SolutionFile -Path $slnFile
        $result.Projects.Count | Should -Be 1
        $result.Projects[0].Name | Should -Be "TestProject"
        $result.Projects[0].Path | Should -Be "TestProject\TestProject.csproj"
        $result.Projects[0].Guid | Should -Be "{ABC12345-1234-1234-1234-123456789ABC}"
    }

    It "应正确解析 Solution Folder" {
        $slnFile = Join-Path $TestDrive "test2.sln"
        $slnContent = @'
Microsoft Visual Studio Solution File, Format Version 12.00
Project("{2150E333-8FDC-42A3-9474-1A3956D46DE8}") = "SolutionFolder", "SolutionFolder", "{DEF12345-1234-1234-1234-123456789DEF}"
EndProject
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "TestProject", "TestProject.csproj", "{ABC12345-1234-1234-1234-123456789ABC}"
EndProject
Global
	GlobalSection(NestedProjects) = preSolution
		{ABC12345-1234-1234-1234-123456789ABC} = {DEF12345-1234-1234-1234-123456789DEF}
	EndGlobalSection
EndGlobal
'@
        Set-Content -Path $slnFile -Value $slnContent -Encoding UTF8

        $result = Parse-SolutionFile -Path $slnFile
        $result.Projects.Count | Should -Be 1
        $result.Folders.Count | Should -Be 1
        $result.HasNestedProjects | Should -Be $true
        $result.NestedProjects.Count | Should -Be 1
    }
}

# ============================================
#  Scan-DiskProjects 测试
# ============================================

Describe "Scan-DiskProjects" {
    It "应扫描目录中的 .csproj 文件" {
        $testDir = Join-Path $TestDrive "ProjectRoot"
        New-Item -Path (Join-Path $testDir "SubDir") -ItemType Directory -Force | Out-Null
        New-Item -Path (Join-Path $testDir "SubDir\TestProject.csproj") -ItemType File -Force | Out-Null
        New-Item -Path (Join-Path $testDir "RootProject.csproj") -ItemType File -Force | Out-Null

        $projects = Scan-DiskProjects -RootDir $testDir
        $projects.Count | Should -Be 2
    }

    It "应排除 BuildOutput 目录" {
        $testDir = Join-Path $TestDrive "ProjectRoot2"
        New-Item -Path (Join-Path $testDir "BuildOutput") -ItemType Directory -Force | Out-Null
        New-Item -Path (Join-Path $testDir "BuildOutput\Output.csproj") -ItemType File -Force | Out-Null
        New-Item -Path (Join-Path $testDir "Valid.csproj") -ItemType File -Force | Out-Null

        $projects = Scan-DiskProjects -RootDir $testDir
        $projects.Count | Should -Be 1
        $projects[0].Name | Should -Be "Valid"
    }
}

# ============================================
#  Build-DirectoryTree 测试
# ============================================

Describe "Build-DirectoryTree" {
    It "应构建正确的目录树" {
        $projects = @(
            [PSCustomObject]@{ Name = "Proj1"; Path = "Dir1\Proj1.csproj"; DirPath = "Dir1" }
            [PSCustomObject]@{ Name = "Proj2"; Path = "Dir2\Proj2.csproj"; DirPath = "Dir2" }
            [PSCustomObject]@{ Name = "RootProj"; Path = "RootProj.csproj"; DirPath = "" }
        )

        $result = Build-DirectoryTree -Projects $projects
        $result.Tree.Count | Should -Be 2
        $result.GuidMap.Count | Should -Be 2
    }

    It "空项目列表应返回空树" {
        $result = Build-DirectoryTree -Projects @()
        $result.Tree.Count | Should -Be 0
        $result.GuidMap.Count | Should -Be 0
    }
}

# ============================================
#  Show-Help 测试
# ============================================

Describe "Show-Help" {
    It "应显示帮助信息" {
        $output = & { Show-Help } 6>&1
        $output.Count | Should -BeGreaterThan 0
        $output | Where-Object { $_ -match "Sync-SlnToDisk" } | Should -Not -BeNullOrEmpty
    }
}

