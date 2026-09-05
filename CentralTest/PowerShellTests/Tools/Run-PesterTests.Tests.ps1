<#
.SYNOPSIS
    Pester 测试：Run-PesterTests.ps1

.DESCRIPTION
    测试 Script\Tools\Run-PesterTests.ps1 脚本中的函数：
    - Get-TestCategories
    - Show-TestCategories
    - Invoke-AllTests
    - Invoke-CategoryTests
#>

BeforeAll {
    $script:ProjectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
    $script:ScriptPath = Join-Path $script:ProjectRoot "Script\Tools\Run-PesterTests.ps1"

    # dot-source 脚本以获取内部函数
    . $script:ScriptPath
}

# ============================================
#  Get-TestCategories 测试
# ============================================

Describe "Get-TestCategories" {
    It "应返回测试分类列表" {
        $categories = Get-TestCategories
        $categories | Should -Not -BeNullOrEmpty
        $categories | Should -Contain "BatTool"
        $categories | Should -Contain "LogViewer"
    }

    It "测试目录不存在时应返回空数组" {
        $originalTestDir = $script:testDir
        $script:testDir = "C:\NonExistentDir_Test123"
        try {
            $categories = Get-TestCategories
            $categories | Should -BeNullOrEmpty
        } finally {
            $script:testDir = $originalTestDir
        }
    }
}

# ============================================
#  Show-TestCategories 测试
# ============================================

Describe "Show-TestCategories" {
    It "应显示测试分类" {
        $output = & { Show-TestCategories } 6>&1
        $output.Count | Should -BeGreaterThan 0
        $output | Where-Object { $_ -match "BatTool" } | Should -Not -BeNullOrEmpty
    }
}

# ============================================
#  Invoke-AllTests 测试
# ============================================

Describe "Invoke-AllTests" {
    It "测试目录不存在时应输出错误" {
        $originalTestDir = $script:testDir
        $script:testDir = "C:\NonExistentDir_Test456"
        try {
            $output = & { Invoke-AllTests } 6>&1
            $output | Where-Object { $_ -match "未找到测试目录" } | Should -Not -BeNullOrEmpty
        } finally {
            $script:testDir = $originalTestDir
        }
    }
}

# ============================================
#  Invoke-CategoryTests 测试
# ============================================

Describe "Invoke-CategoryTests" {
    It "分类不存在时应输出错误" {
        $output = & { Invoke-CategoryTests -Category "NonExistentCategory" } 6>&1
        $output | Where-Object { $_ -match "未找到测试分类" } | Should -Not -BeNullOrEmpty
    }
}

