<#
.SYNOPSIS
    Pester 测试：InteractiveHelpers.psm1

.DESCRIPTION
    测试 Script\Modules\Core\InteractiveHelpers.psm1 中的所有函数：
    - Confirm-And-DeleteDirectories
    - Confirm-And-FixProjects
    - Show-FixSummary
    - Group-ByFilePath
#>

BeforeAll {
    $script:ModulePath = Join-Path (Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent) "Script\Modules\Core\InteractiveHelpers.psm1"
    Import-Module $script:ModulePath -Force
}

# ============================================
#  Group-ByFilePath 测试
# ============================================

Describe "Group-ByFilePath" {
    It "应按文件路径对项目进行分组" {
        $items = @(
            @{ File = [System.IO.FileInfo]::new("C:\test\project1.csproj"); Issue = "A" }
            @{ File = [System.IO.FileInfo]::new("C:\test\project1.csproj"); Issue = "B" }
            @{ File = [System.IO.FileInfo]::new("C:\test\project2.csproj"); Issue = "C" }
        )
        $grouped = Group-ByFilePath -items $items
        $grouped.Keys.Count | Should -Be 2
        $grouped["C:\test\project1.csproj"].Nodes.Count | Should -Be 2
        $grouped["C:\test\project2.csproj"].Nodes.Count | Should -Be 1
    }

    It "应支持字符串路径" {
        $items = @(
            @{ File = "C:\test\project1.csproj"; Issue = "A" }
            @{ File = "C:\test\project2.csproj"; Issue = "B" }
        )
        $grouped = Group-ByFilePath -items $items
        $grouped.Keys.Count | Should -Be 2
    }

    It "空数组应返回空哈希表" {
        $grouped = Group-ByFilePath -items @()
        $grouped.Keys.Count | Should -Be 0
    }
}

# ============================================
#  Show-FixSummary 测试
# ============================================

Describe "Show-FixSummary" {
    It "应显示修复摘要" {
        $output = & { Show-FixSummary -summary @("修复了 3 个问题", "更新了 2 个文件") } 6>&1
        $output.Count | Should -BeGreaterThan 0
        $output | Where-Object { $_ -match "修复了 3 个问题" } | Should -Not -BeNullOrEmpty
    }

    It "空摘要应不输出" {
        $output = & { Show-FixSummary -summary @() } 6>&1
        $output | Should -BeNullOrEmpty
    }

    It "应显示备份目录" {
        $backupDir = Join-Path $TestDrive "backups"
        New-Item -Path $backupDir -ItemType Directory -Force | Out-Null
        $output = & { Show-FixSummary -summary @("修复完成") -backupDir $backupDir } 6>&1
        $output | Where-Object { $_ -match "备份目录" } | Should -Not -BeNullOrEmpty
    }
}

# ============================================
#  Confirm-And-DeleteDirectories 测试（非交互部分）
# ============================================

Describe "Confirm-And-DeleteDirectories" {
    It "空目录列表应返回零计数" {
        $result = Confirm-And-DeleteDirectories -dirList @() -showSize $false
        $result.deletedCount | Should -Be 0
        $result.failedCount | Should -Be 0
        $result.skippedCount | Should -Be 0
    }
}

# ============================================
#  Confirm-And-FixProjects 测试（非交互部分）
# ============================================

Describe "Confirm-And-FixProjects" {
    It "空项目列表应返回 $false" {
        $result = Confirm-And-FixProjects -projectList @() -displayLabel "测试项目" -confirmMessage "确认修复？" -fixAction {}
        $result | Should -Be $false
    }
}
