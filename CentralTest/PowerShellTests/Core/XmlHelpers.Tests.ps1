<#
.SYNOPSIS
    Pester 测试：XmlHelpers.psm1

.DESCRIPTION
    测试 Script\Modules\Core\XmlHelpers.psm1 中的所有函数：
    - Read-XmlFile
    - Test-XmlElementExists
    - Test-ContentHasElement
    - Remove-XmlElementLines
    - Get-MsbuildNamespaceManager
    - Select-MsbuildNodes
    - Update-XmlElementValue
    - Add-XmlElementAfterSibling
    - Get-CsprojPropertyValues
#>

BeforeAll {
    $script:ModulePath = Join-Path (Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent) "Script\Modules\Core\XmlHelpers.psm1"
    Import-Module $script:ModulePath -Force
}

# ============================================
#  Read-XmlFile 测试
# ============================================

Describe "Read-XmlFile" {
    It "应正确解析有效的 XML 文件" {
        $xmlFile = Join-Path $TestDrive "test.xml"
        Set-Content -Path $xmlFile -Value '<?xml version="1.0"?><Project><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>' -Encoding UTF8

        $xml, $content = Read-XmlFile -filePath $xmlFile
        $xml | Should -Not -BeNullOrEmpty
        $content | Should -Not -BeNullOrEmpty
    }

    It "无效 XML 应返回 $null 和原始内容" {
        $xmlFile = Join-Path $TestDrive "invalid.xml"
        Set-Content -Path $xmlFile -Value "这不是 XML 内容" -Encoding UTF8

        $xml, $content = Read-XmlFile -filePath $xmlFile
        $xml | Should -Be $null
        $content | Should -Be "这不是 XML 内容"
    }
}

# ============================================
#  Test-XmlElementExists 测试
# ============================================

Describe "Test-XmlElementExists" {
    It "元素存在时应返回 $true" {
        $content = '<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>'
        Test-XmlElementExists -content $content -elementName "TargetFramework" | Should -Be $true
    }

    It "元素不存在时应返回 $false" {
        $content = '<Project><PropertyGroup></PropertyGroup></Project>'
        Test-XmlElementExists -content $content -elementName "NonExistent" | Should -Be $false
    }

    It "注释中的元素不应被检测到" {
        $content = '<!-- <TargetFramework>net10.0</TargetFramework> --><Project><PropertyGroup></PropertyGroup></Project>'
        Test-XmlElementExists -content $content -elementName "TargetFramework" | Should -Be $false
    }
}

# ============================================
#  Test-ContentHasElement 测试
# ============================================

Describe "Test-ContentHasElement" {
    It "元素存在时应返回 $true" {
        Test-ContentHasElement -content "<TargetFramework>net10.0</TargetFramework>" -elementName "TargetFramework" | Should -Be $true
    }

    It "元素不存在时应返回 $false" {
        Test-ContentHasElement -content "<Project></Project>" -elementName "TargetFramework" | Should -Be $false
    }

    It "注释中的元素不应被检测到" {
        $content = "<!-- <TargetFramework>net10.0</TargetFramework> -->`r`n<Project></Project>"
        Test-ContentHasElement -content $content -elementName "TargetFramework" | Should -Be $false
    }
}

# ============================================
#  Remove-XmlElementLines 测试
# ============================================

Describe "Remove-XmlElementLines" {
    It "应移除单行 XML 元素" {
        $content = "<Project>`r`n  <PropertyGroup>`r`n    <TargetFramework>net10.0</TargetFramework>`r`n  </PropertyGroup>`r`n</Project>"
        $result = Remove-XmlElementLines -content $content -elementName "TargetFramework"
        $result | Should -Not -Match '<TargetFramework>'
    }

    It "应保留注释中的元素" {
        $content = "<!-- <TargetFramework>net10.0</TargetFramework> -->`r`n<Project></Project>"
        $result = Remove-XmlElementLines -content $content -elementName "TargetFramework"
        $result | Should -Match '<!--'
    }

    It "应处理多行 XML 元素" {
        $content = "<Project>`r`n  <PropertyGroup>`r`n    <TargetFramework>`r`n      net10.0`r`n    </TargetFramework>`r`n  </PropertyGroup>`r`n</Project>"
        $result = Remove-XmlElementLines -content $content -elementName "TargetFramework"
        $result | Should -Not -Match '<TargetFramework>'
    }
}

# ============================================
#  Get-MsbuildNamespaceManager 测试
# ============================================

Describe "Get-MsbuildNamespaceManager" {
    It "应返回命名空间管理器" {
        $xml = New-Object System.Xml.XmlDocument
        $nsmgr = Get-MsbuildNamespaceManager -xml $xml
        $nsmgr | Should -Not -BeNullOrEmpty
        $nsmgr.LookupNamespace("default") | Should -Be "http://schemas.microsoft.com/developer/msbuild/2003"
    }
}

# ============================================
#  Select-MsbuildNodes 测试
# ============================================

Describe "Select-MsbuildNodes" {
    It "应选择 MSBuild 命名空间下的节点" {
        $xmlContent = '<?xml version="1.0"?><Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>'
        $xml = New-Object System.Xml.XmlDocument
        $xml.LoadXml($xmlContent)

        $nodes = Select-MsbuildNodes -xml $xml -xpath "TargetFramework"
        $nodes.Count | Should -BeGreaterThan 0
        $nodes[0].InnerText | Should -Be "net10.0"
    }
}

# ============================================
#  Update-XmlElementValue 测试
# ============================================

Describe "Update-XmlElementValue" {
    It "应更新 XML 元素的值" {
        $content = "<Project><PropertyGroup><TargetFramework>net10.0-windows7.0</TargetFramework></PropertyGroup></Project>"
        $result = Update-XmlElementValue -content $content -elementName "TargetFramework" -oldValue "net10.0-windows7.0" -newValue "net10.0-windows"
        $result | Should -Match '<TargetFramework>net10.0-windows</TargetFramework>'
        $result | Should -Not -Match 'net10.0-windows7.0'
    }

    It "旧值不存在时应返回原内容" {
        $content = "<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>"
        $result = Update-XmlElementValue -content $content -elementName "TargetFramework" -oldValue "net9.0" -newValue "net10.0"
        $result | Should -Be $content
    }
}

# ============================================
#  Add-XmlElementAfterSibling 测试
# ============================================

Describe "Add-XmlElementAfterSibling" {
    It "应在指定元素后插入新元素" {
        $xmlContent = '<?xml version="1.0"?><Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>'
        $xml = New-Object System.Xml.XmlDocument
        $xml.LoadXml($xmlContent)

        $result = Add-XmlElementAfterSibling -xml $xml -afterElementName "TargetFramework" -newElementName "UseWindowsForms" -newElementValue "true"
        $result | Should -Be $true

        $nsmgr = Get-MsbuildNamespaceManager -xml $xml
        $node = $xml.SelectSingleNode("//default:UseWindowsForms", $nsmgr)
        $node | Should -Not -BeNullOrEmpty
        $node.InnerText | Should -Be "true"
    }

    It "参考元素不存在时应返回 $false" {
        $xmlContent = '<?xml version="1.0"?><Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003"><PropertyGroup></PropertyGroup></Project>'
        $xml = New-Object System.Xml.XmlDocument
        $xml.LoadXml($xmlContent)

        $result = Add-XmlElementAfterSibling -xml $xml -afterElementName "NonExistent" -newElementName "Test" -newElementValue "value"
        $result | Should -Be $false
    }
}

# ============================================
#  Get-CsprojPropertyValues 测试
# ============================================

Describe "Get-CsprojPropertyValues" {
    It "单文件模式应返回属性值哈希表" {
        $csprojFile = Join-Path $TestDrive "test.csproj"
        Set-Content -Path $csprojFile -Value '<?xml version="1.0"?><Project><PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType></PropertyGroup></Project>' -Encoding UTF8

        $result = Get-CsprojPropertyValues -csprojPath $csprojFile
        $result | Should -Not -BeNullOrEmpty
        $result.TargetFramework | Should -Be "net10.0"
        $result.OutputType | Should -Be "Exe"
    }

    It "单文件模式文件不存在时应返回 $null" {
        $result = Get-CsprojPropertyValues -csprojPath "C:\NonExistent.csproj"
        $result | Should -Be $null
    }

    It "批量模式应返回分类统计" {
        $projectRoot = Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent
        $csprojFiles = Get-ChildItem -Path $projectRoot -Filter "*.csproj" -Recurse -ErrorAction SilentlyContinue | Select-Object -First 5
        if ($csprojFiles) {
            $result = Get-CsprojPropertyValues -csprojFiles $csprojFiles
            $result.ContainsKey("missing") | Should -Be $true
            $result.ContainsKey("hasValue") | Should -Be $true
            $result.ContainsKey("values") | Should -Be $true
        }
    }
}
