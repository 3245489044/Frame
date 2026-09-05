<#
.SYNOPSIS
    Pester 测试：Analyze-CsprojReferences.ps1

.DESCRIPTION
    测试 Script\Analysis\Analyze-CsprojReferences.ps1 脚本中的核心函数：
    - Get-NormalizedPath
    - Read-CsprojContent
    - Get-ProjectProperty
    - Get-ProjectReferences
    - Get-PackageReferences
    - Get-CompileIncludes
    - Find-CsprojFiles
    - Show-Help
#>

BeforeAll {
    $script:ProjectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
    $script:ScriptPath = Join-Path $script:ProjectRoot "Script\Analysis\Analyze-CsprojReferences.ps1"

    # dot-source 脚本以获取内部函数
    . $script:ScriptPath

    # 创建测试用的 csproj 文件
    $script:TestCsprojDir = Join-Path $TestDrive "TestProject"
    New-Item -ItemType Directory -Path $script:TestCsprojDir -Force | Out-Null

    $script:TestCsprojPath = Join-Path $script:TestCsprojDir "TestProject.csproj"
    @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <OutputType>Library</OutputType>
    <AssemblyName>TestProject</AssemblyName>
    <RootNamespace>TestProject</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
    <PackageReference Include="Microsoft.Extensions.Logging" Version="8.0.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\AnotherProject\AnotherProject.csproj" />
  </ItemGroup>
  <ItemGroup>
    <Compile Include="Class1.cs" />
    <Compile Include="Class2.cs" />
  </ItemGroup>
</Project>
"@ | Set-Content -Path $script:TestCsprojPath -Encoding UTF8
}

# ============================================
#  Get-NormalizedPath 测试
# ============================================

Describe "Get-NormalizedPath" {
    It "空路径应返回空字符串" {
        Get-NormalizedPath -Path "" | Should -Be ""
        Get-NormalizedPath -Path "   " | Should -Be ""
    }

    It "绝对路径应直接标准化" {
        $result = Get-NormalizedPath -Path "C:\Test\Project.csproj"
        $result | Should -Be "C:\Test\Project.csproj"
    }

    It "相对路径应基于 BaseDir 解析" {
        $result = Get-NormalizedPath -Path "..\AnotherProject\AnotherProject.csproj" -BaseDir $script:TestCsprojDir
        $result | Should -Not -BeNullOrEmpty
        $result | Should -Match "AnotherProject.csproj"
    }
}

# ============================================
#  Read-CsprojContent 测试
# ============================================

Describe "Read-CsprojContent" {
    It "文件不存在时应返回 $null" {
        $result = Read-CsprojContent -FilePath "C:\NonExistentFile.csproj"
        $result | Should -BeNullOrEmpty
    }

    It "应正确读取 csproj 文件" {
        $result = Read-CsprojContent -FilePath $script:TestCsprojPath
        $result | Should -Not -BeNullOrEmpty
        $result.Xml | Should -Not -BeNullOrEmpty
        $result.Content | Should -Not -BeNullOrEmpty
        $result.Lines | Should -Not -BeNullOrEmpty
        $result.FilePath | Should -Be $script:TestCsprojPath
    }
}

# ============================================
#  Get-ProjectProperty 测试
# ============================================

Describe "Get-ProjectProperty" {
    It "应正确获取 TargetFramework" {
        $csprojData = Read-CsprojContent -FilePath $script:TestCsprojPath
        $tf = Get-ProjectProperty -Xml $csprojData.Xml -PropertyName "TargetFramework"
        $tf | Should -Be "net10.0"
    }

    It "应正确获取 AssemblyName" {
        $csprojData = Read-CsprojContent -FilePath $script:TestCsprojPath
        $name = Get-ProjectProperty -Xml $csprojData.Xml -PropertyName "AssemblyName"
        $name | Should -Be "TestProject"
    }

    It "不存在的属性应返回空字符串" {
        $csprojData = Read-CsprojContent -FilePath $script:TestCsprojPath
        $prop = Get-ProjectProperty -Xml $csprojData.Xml -PropertyName "NonExistentProperty"
        $prop | Should -Be ""
    }
}

# ============================================
#  Get-ProjectReferences 测试
# ============================================

Describe "Get-ProjectReferences" {
    It "应正确解析 ProjectReference" {
        $csprojData = Read-CsprojContent -FilePath $script:TestCsprojPath
        $refs = Get-ProjectReferences -Xml $csprojData.Xml -CsprojDir $script:TestCsprojDir
        $refs.Count | Should -BeGreaterThan 0
        $refs[0].Name | Should -Be "AnotherProject.csproj"
    }
}

# ============================================
#  Get-PackageReferences 测试
# ============================================

Describe "Get-PackageReferences" {
    It "应正确解析 PackageReference" {
        $csprojData = Read-CsprojContent -FilePath $script:TestCsprojPath
        $pkgs = Get-PackageReferences -Xml $csprojData.Xml
        $pkgs.Count | Should -Be 2
        $pkgs[0].Name | Should -Be "Newtonsoft.Json"
        $pkgs[1].Name | Should -Be "Microsoft.Extensions.Logging"
    }

    It "应正确解析包版本" {
        $csprojData = Read-CsprojContent -FilePath $script:TestCsprojPath
        $pkgs = Get-PackageReferences -Xml $csprojData.Xml
        $pkgs[0].Version | Should -Be "13.0.3"
    }
}

# ============================================
#  Get-CompileIncludes 测试
# ============================================

Describe "Get-CompileIncludes" {
    It "应正确解析 Compile Include" {
        $csprojData = Read-CsprojContent -FilePath $script:TestCsprojPath
        $files = Get-CompileIncludes -Xml $csprojData.Xml -CsprojDir $script:TestCsprojDir
        $files.Count | Should -Be 2
        $files[0].Include | Should -Be "Class1.cs"
        $files[1].Include | Should -Be "Class2.cs"
    }
}

# ============================================
#  Find-CsprojFiles 测试
# ============================================

Describe "Find-CsprojFiles" {
    It "路径不存在时应返回空数组" {
        $files = Find-CsprojFiles -InputPath "C:\NonExistentDir"
        $files | Should -BeNullOrEmpty
    }

    It "应能查找目录中的 csproj 文件" {
        $files = Find-CsprojFiles -InputPath $script:TestCsprojDir
        $files.Count | Should -Be 1
        $files[0] | Should -Match "TestProject.csproj"
    }
}

# ============================================
#  Show-Help 测试
# ============================================

Describe "Show-Help" {
    It "应显示帮助信息" {
        # Show-Help 内部使用 $MyInvocation.MyCommand.Path，dot-source 后为空
        # 直接调用时使用 Write-Header 输出，捕获所有输出流
        $output = & { Show-Help } 2>&1
        $output | Should -Not -BeNullOrEmpty
    }
}

