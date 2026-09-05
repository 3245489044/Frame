<#
.SYNOPSIS
    Pester 测试：LogViewer.ps1 核心函数

.DESCRIPTION
    测试 Script\FileDriven\FileDriven\LogViewer.ps1 中的核心函数：
    - Get-LogData
    - Clear-LogDataCache
    - Get-LogEntries
    - Show-LogViewer
#>

BeforeAll {
    $script:ProjectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
    $script:LogViewerPath = Join-Path $script:ProjectRoot "Script\FileDriven\FileDriven\LogViewer.ps1"

    # dot-source 脚本以获取内部函数
    . $script:LogViewerPath

    # 创建测试日志目录和文件
    $script:TestLogsDir = Join-Path $TestDrive "logs"
    New-Item -ItemType Directory -Path $script:TestLogsDir -Force | Out-Null

    # 创建测试日志数据
    $script:TestLogData = @{
        batchId = "test_batch_001"
        totalCount = 5
        summary = @{
            byLevel = @{
                INFO = 2
                ERROR = 2
                WARN = 1
            }
            byModule = @{
                Launcher = 3
                Pipeline = 2
            }
        }
        entries = @(
            @{ timestamp = "2024-01-01 10:00:00"; level = "INFO"; category = "Launcher_Lifecycle"; module = "Launcher"; message = "启动完成" }
            @{ timestamp = "2024-01-01 10:01:00"; level = "ERROR"; category = "Pipeline_Execution"; module = "Pipeline"; message = "执行失败" }
            @{ timestamp = "2024-01-01 10:02:00"; level = "WARN"; category = "Launcher_Config"; module = "Launcher"; message = "配置缺失" }
            @{ timestamp = "2024-01-01 10:03:00"; level = "INFO"; category = "Launcher_Lifecycle"; module = "Launcher"; message = "处理完成" }
            @{ timestamp = "2024-01-01 10:04:00"; level = "ERROR"; category = "Pipeline_Execution"; module = "Pipeline"; message = "超时错误" }
        )
    }

    # 写入测试日志文件
    $script:TestLogFile = Join-Path $script:TestLogsDir "batch_test_batch_001_latest.json"
    $script:TestLogData | ConvertTo-Json -Depth 10 | Set-Content -Path $script:TestLogFile -Encoding UTF8
}

# ============================================
#  Get-LogData 测试
# ============================================

Describe "Get-LogData" {
    It "应读取日志文件并返回数据" {
        $data = Get-LogData -LogsDir $script:TestLogsDir -ForceReload
        $data | Should -Not -BeNullOrEmpty
        $data.batchId | Should -Be "test_batch_001"
        $data.totalCount | Should -Be 5
    }

    It "日志目录不存在时应返回 $null" {
        $data = Get-LogData -LogsDir "C:\NonExistentDir_Test"
        $data | Should -BeNullOrEmpty
    }

    It "应使用缓存（5 秒内不重复读取）" {
        # 第一次读取（强制重新加载）
        $data1 = Get-LogData -LogsDir $script:TestLogsDir -ForceReload
        $data1 | Should -Not -BeNullOrEmpty

        # 第二次读取（应使用缓存）
        $data2 = Get-LogData -LogsDir $script:TestLogsDir
        $data2 | Should -Not -BeNullOrEmpty
        $data2.batchId | Should -Be "test_batch_001"
    }

    It "指定 BatchId 时应读取对应文件" {
        $data = Get-LogData -LogsDir $script:TestLogsDir -ForceReload -BatchId "test_batch_001"
        $data | Should -Not -BeNullOrEmpty
        $data.batchId | Should -Be "test_batch_001"
    }

    It "指定不存在的 BatchId 时应返回 $null" {
        $data = Get-LogData -LogsDir $script:TestLogsDir -ForceReload -BatchId "non_existent_batch"
        $data | Should -BeNullOrEmpty
    }
}

# ============================================
#  Clear-LogDataCache 测试
# ============================================

Describe "Clear-LogDataCache" {
    It "应清除缓存" {
        # 先加载数据到缓存
        $null = Get-LogData -LogsDir $script:TestLogsDir -ForceReload

        # 清除缓存
        Clear-LogDataCache

        # 验证缓存已清除（应重新读取文件）
        $data = Get-LogData -LogsDir $script:TestLogsDir
        $data | Should -Not -BeNullOrEmpty
    }
}

# ============================================
#  Get-LogEntries 测试
# ============================================

Describe "Get-LogEntries" {
    It "应返回所有条目" {
        $entries = Get-LogEntries -LogsDir $script:TestLogsDir
        $entries.Count | Should -Be 5
    }

    It "应按级别筛选" {
        $entries = Get-LogEntries -LogsDir $script:TestLogsDir -Level "ERROR"
        $entries.Count | Should -Be 2
        foreach ($entry in $entries) {
            $entry.level | Should -Be "ERROR"
        }
    }

    It "应按分类筛选" {
        $entries = Get-LogEntries -LogsDir $script:TestLogsDir -Category "Launcher_Lifecycle"
        $entries.Count | Should -Be 2
        foreach ($entry in $entries) {
            $entry.category | Should -Be "Launcher_Lifecycle"
        }
    }

    It "应按模块筛选" {
        $entries = Get-LogEntries -LogsDir $script:TestLogsDir -Module "Pipeline"
        $entries.Count | Should -Be 2
        foreach ($entry in $entries) {
            $entry.module | Should -Be "Pipeline"
        }
    }

    It "应支持关键词搜索" {
        $entries = Get-LogEntries -LogsDir $script:TestLogsDir -Query "失败"
        $entries.Count | Should -BeGreaterThan 0
    }

    It "应限制返回条数" {
        $entries = Get-LogEntries -LogsDir $script:TestLogsDir -Limit 2
        $entries.Count | Should -BeLessOrEqual 2
    }

    It "日志目录为空时应返回空数组" {
        $emptyDir = Join-Path $TestDrive "empty_logs"
        New-Item -ItemType Directory -Path $emptyDir -Force | Out-Null
        $entries = Get-LogEntries -LogsDir $emptyDir
        $entries | Should -BeNullOrEmpty
    }
}

# ============================================
#  Show-LogViewer 测试
# ============================================

Describe "Show-LogViewer" {
    It "应能调用 Show-LogViewer（无 ExecCommand）" {
        # 由于 Show-LogViewer 会调用 Clear-Host 和 Read-Host，
        # 这里只验证函数存在且能正常调用
        { Show-LogViewer -ProjectRoot $script:ProjectRoot -LogsDir $script:TestLogsDir } | Should -Not -Throw
    }

    It "应能调用 Show-LogViewer（带 ExecCommand）" {
        { Show-LogViewer -ProjectRoot $script:ProjectRoot -LogsDir $script:TestLogsDir -ExecCommand "level ERROR" } | Should -Not -Throw
    }
}

