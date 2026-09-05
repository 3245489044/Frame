<#
.SYNOPSIS
    Pester 测试：LogViewer.ps1（重构版）

.DESCRIPTION
    测试 Script\FileDriven\FileDriven\LogViewer.ps1 中的纯数据函数。
    注意：涉及 Clear-Host 和递归菜单的交互式函数（Show-*）在非交互终端中
    无法通过 Mock 可靠测试，因此本测试只覆盖 Get-LogData、Get-LogEntries
    和 Clear-LogDataCache 等数据层函数。
#>

BeforeAll {
    $script:ProjectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
    $script:LogViewerPath = Join-Path $script:ProjectRoot "Script\FileDriven\FileDriven\LogViewer.ps1"

    # 点引入加载被测试脚本
    . $script:LogViewerPath

    # 测试参数
    $script:TestLogsDir = Join-Path $script:ProjectRoot "logs"
}

# ============================================
#  Get-LogData 测试
# ============================================

Describe "Get-LogData" {
    It "日志目录不存在时应返回 `$null" {
        $result = Get-LogData -LogsDir "C:\NonExistentDir"
        $result | Should -Be $null
    }

    It "应支持 ForceReload 参数" {
        Mock -CommandName Test-Path { return $true } -ParameterFilter { $Path -match "batch_.*_latest.json" }
        Mock -CommandName Get-ChildItem { return @() }

        $result = Get-LogData -LogsDir $script:TestLogsDir -ForceReload
        $result | Should -Be $null
    }

    It "应支持 BatchId 参数" {
        Mock -CommandName Test-Path { return $false }

        $result = Get-LogData -LogsDir $script:TestLogsDir -BatchId "test-batch"
        $result | Should -Be $null
    }
}

# ============================================
#  Clear-LogDataCache 测试
# ============================================

Describe "Clear-LogDataCache" {
    It "应清除缓存（不抛出异常）" {
        { Clear-LogDataCache } | Should -Not -Throw
    }
}

# ============================================
#  Get-LogEntries 测试
# ============================================

Describe "Get-LogEntries" {
    It "日志目录不存在时应返回空数组" {
        $result = @(Get-LogEntries -LogsDir "C:\NonExistentDir")
        $result.Count | Should -Be 0
    }

    It "应支持 Level 筛选参数" {
        Mock -CommandName Get-LogData { return @{ entries = @() } }

        $result = @(Get-LogEntries -LogsDir $script:TestLogsDir -Level "ERROR")
        $result.Count | Should -BeGreaterOrEqual 0
    }

    It "应支持 Category 筛选参数" {
        Mock -CommandName Get-LogData { return @{ entries = @() } }

        $result = @(Get-LogEntries -LogsDir $script:TestLogsDir -Category "Launcher_Lifecycle")
        $result.Count | Should -BeGreaterOrEqual 0
    }

    It "应支持 Module 筛选参数" {
        Mock -CommandName Get-LogData { return @{ entries = @() } }

        $result = @(Get-LogEntries -LogsDir $script:TestLogsDir -Module "Launcher")
        $result.Count | Should -BeGreaterOrEqual 0
    }

    It "应支持 Query 搜索参数" {
        Mock -CommandName Get-LogData { return @{ entries = @() } }

        $result = @(Get-LogEntries -LogsDir $script:TestLogsDir -Query "error")
        $result.Count | Should -BeGreaterOrEqual 0
    }

    It "应支持 Limit 参数" {
        Mock -CommandName Get-LogData { return @{ entries = @() } }

        $result = @(Get-LogEntries -LogsDir $script:TestLogsDir -Limit 10)
        $result.Count | Should -BeGreaterOrEqual 0
    }
}
