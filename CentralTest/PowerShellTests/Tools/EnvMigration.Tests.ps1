<#
.SYNOPSIS
    Pester 测试：EnvMigration 模块

.DESCRIPTION
    测试 PowerShellScript\Tools\EnvMigration 目录中的脚本：
    - EnvMigration-Common.ps1（公共函数）
    - Migrate-DevEnv.ps1（迁移逻辑）
    - Cleanup-DevEnv.ps1（清理逻辑）

    测试策略：
    - 使用 Mock 模拟文件系统操作和外部命令
    - 使用 TestDrive 作为临时测试目录
    - 不修改真实文件系统
#>

BeforeAll {
    # 加载测试辅助模块
    $script:TestHelperPath = Join-Path $PSScriptRoot "..\TestHelper\TestHelper.psm1"
    Import-Module $script:TestHelperPath -Force -WarningAction SilentlyContinue

    # 定位项目根目录
    $script:ProjectRoot = Get-ProjectRoot

    # 被测试脚本路径
    $script:CommonScriptPath = Join-Path $script:ProjectRoot "PowerShellScript\Tools\EnvMigration\EnvMigration-Common.ps1"
    $script:ConfigPath = Join-Path $script:ProjectRoot "PowerShellScript\Tools\EnvMigration\config.json"

    # 读取配置文件供测试使用
    $script:Config = Get-Content $script:ConfigPath -Raw | ConvertFrom-Json
}

# ============================================
#  EnvMigration-Common.ps1 测试
# ============================================

Describe "EnvMigration-Common.ps1" {
    BeforeAll {
        . $script:CommonScriptPath
    }

    # ============================================
    #  Resolve-EnvPath
    # ============================================

    Describe "Resolve-EnvPath" {
        It "空路径应返回空字符串" {
            Resolve-EnvPath "" | Should -Be ""
            Resolve-EnvPath $null | Should -Be ""
        }

        It "不含环境变量的路径应原样返回" {
            Resolve-EnvPath "D:\DevEnv\test" | Should -Be "D:\DevEnv\test"
        }

        It "应解析 %USERPROFILE% 环境变量" {
            $expected = [Environment]::GetEnvironmentVariable("USERPROFILE", "Process")
            $result = Resolve-EnvPath "%USERPROFILE%\test"
            $result | Should -BeExactly "$expected\test"
        }

        It "应解析 %APPDATA% 环境变量" {
            $expected = [Environment]::GetEnvironmentVariable("APPDATA", "Process")
            $result = Resolve-EnvPath "%APPDATA%\sub\dir"
            $result | Should -BeExactly "$expected\sub\dir"
        }

        It "不存在的环境变量应保留原样" {
            $result = Resolve-EnvPath "%NONEXISTENT_VAR_12345%\test"
            $result | Should -BeExactly "%NONEXISTENT_VAR_12345%\test"
        }
    }

    # ============================================
    #  Get-HumanSize
    # ============================================

    Describe "Get-HumanSize" {
        It "null 应返回 '0 B'" {
            Get-HumanSize $null | Should -Be "0 B"
        }

        It "0 应返回 '0 B'" {
            Get-HumanSize 0 | Should -Be "0 B"
        }

        It "小于 1KB 的值应返回字节数" {
            Get-HumanSize 500 | Should -Be "500 B"
        }

        It "应正确格式化 KB" {
            $result = Get-HumanSize 2048
            $result | Should -Match "2\.00\s+KB"
        }

        It "应正确格式化 MB" {
            $result = Get-HumanSize (10 * 1024 * 1024)
            $result | Should -Match "10\.00\s+MB"
        }

        It "应正确格式化 GB" {
            $result = Get-HumanSize (2 * 1024 * 1024 * 1024)
            $result | Should -Match "2\.00\s+GB"
        }
    }

    # ============================================
    #  Invoke-Robocopy
    # ============================================

    Describe "Invoke-Robocopy" {
        BeforeEach {
            Mock Start-Process { return @{ ExitCode = 0 } }
        }

        It "退出码 0 应视为成功" {
            Invoke-Robocopy -Source "C:\Source" -Target "C:\Target" -Label "Test" | Should -Be $true
        }

        It "退出码 8 应视为失败（位掩码检查）" {
            Mock Start-Process { return @{ ExitCode = 8 } }
            Invoke-Robocopy -Source "C:\Source" -Target "C:\Target" -Label "Test" | Should -Be $false
        }

        It "退出码 16 应视为成功（16 的二进制 10000，第 4 位未设置）" {
            Mock Start-Process { return @{ ExitCode = 16 } }
            Invoke-Robocopy -Source "C:\Source" -Target "C:\Target" -Label "Test" | Should -Be $true
        }

        It "退出码 4 应视为成功（无错误位）" {
            Mock Start-Process { return @{ ExitCode = 4 } }
            Invoke-Robocopy -Source "C:\Source" -Target "C:\Target" -Label "Test" | Should -Be $true
        }
    }

    # ============================================
    #  Backup-And-CopyToTarget
    # ============================================

    Describe "Backup-And-CopyToTarget" {
        BeforeEach {
            Mock Invoke-Robocopy { return $true }
        }

        It "源路径不存在时应返回 $false" {
            Mock Test-Path { return $false } -ParameterFilter { $Path -eq "C:\NonExistent" }
            Backup-And-CopyToTarget -Source "C:\NonExistent" -TargetPath "C:\Target" -BackupPath "C:\Backup" -Label "Test" | Should -Be $false
        }

        It "备份失败时应返回 $false" {
            Mock Test-Path { return $true } -ParameterFilter { $Path -eq "C:\Source" }
            Mock Invoke-Robocopy { return $false } -ParameterFilter { $Label -eq "Test/backup" }
            Backup-And-CopyToTarget -Source "C:\Source" -TargetPath "C:\Target" -BackupPath "C:\Backup" -Label "Test" | Should -Be $false
        }

        It "复制失败时应返回 $false" {
            Mock Test-Path { return $true } -ParameterFilter { $Path -eq "C:\Source" }
            Mock Invoke-Robocopy { return $true } -ParameterFilter { $Label -eq "Test/backup" }
            Mock Invoke-Robocopy { return $false } -ParameterFilter { $Label -eq "Test" }
            Backup-And-CopyToTarget -Source "C:\Source" -TargetPath "C:\Target" -BackupPath "C:\Backup" -Label "Test" | Should -Be $false
        }

        It "备份和复制都成功时应返回 $true" {
            Mock Test-Path { return $true } -ParameterFilter { $Path -eq "C:\Source" }
            Backup-And-CopyToTarget -Source "C:\Source" -TargetPath "C:\Target" -BackupPath "C:\Backup" -Label "Test" | Should -Be $true
        }
    }

    # ============================================
    #  Get-EnabledItems
    # ============================================

    Describe "Get-EnabledItems" {
        It "应返回配置中启用的项目名称" {
            $testConfig = [PSCustomObject]@{
                migrationItems = [PSCustomObject]@{
                    NuGet  = [PSCustomObject]@{ enabled = $true }
                    npm    = [PSCustomObject]@{ enabled = $true }
                    Docker = [PSCustomObject]@{ enabled = $false }
                }
            }
            $result = Get-EnabledItems $testConfig
            $result.Count | Should -Be 2
            $result | Should -Contain "NuGet"
            $result | Should -Contain "npm"
        }

        It "所有项都禁用时应返回空数组" {
            $testConfig = [PSCustomObject]@{
                migrationItems = [PSCustomObject]@{
                    NuGet = [PSCustomObject]@{ enabled = $false }
                    npm   = [PSCustomObject]@{ enabled = $false }
                }
            }
            Get-EnabledItems $testConfig | Should -BeNullOrEmpty
        }

        It "配置为空时应返回空数组" {
            $testConfig = [PSCustomObject]@{ migrationItems = [PSCustomObject]@{} }
            Get-EnabledItems $testConfig | Should -BeNullOrEmpty
        }
    }

    # ============================================
    #  Wait-PathDeleted
    # ============================================

    Describe "Wait-PathDeleted" {
        It "路径不存在时应立即返回 $true" {
            Mock Test-Path { return $false }
            Wait-PathDeleted -Path "C:\NonExistent" -MaxSeconds 5 | Should -Be $true
        }

        It "路径在超时内删除时应返回 $true" {
            # 循环: while ((Test-Path $Path) -and $waitCount -lt $maxTries)
            # 第1次 Test-Path → true → 进入循环 → Start-Sleep → $waitCount=1
            # 第2次 Test-Path → false → 退出循环
            # 第3次 Test-Path → return (-not (Test-Path $Path)) → false → -not false = true
            $callCount = 0
            Mock Test-Path {
                $callCount++
                return ($callCount -ne 1)  # 第1次 true，后续 false
            }
            Mock Start-Sleep { }
            Wait-PathDeleted -Path "C:\ToDelete" -MaxSeconds 5 | Should -Be $true
        }

        It "路径在超时后仍存在时应返回 $false" {
            Mock Test-Path { return $true }
            Mock Start-Sleep { }
            Wait-PathDeleted -Path "C:\Persistent" -MaxSeconds 1 | Should -Be $false
        }
    }

    # ============================================
    #  Get-ItemSourcePaths
    # ============================================

    Describe "Get-ItemSourcePaths" {
        It "字符串源应返回解析后的路径" {
            Get-ItemSourcePaths -Source "D:\DevEnv\test" | Should -Be "D:\DevEnv\test"
        }

        It "PSCustomObject 源应提取路径属性" {
            $source = [PSCustomObject]@{
                path = "D:\DevEnv\test"
                name = "test"
            }
            $result = Get-ItemSourcePaths -Source $source
            $result | Should -Contain "D:\DevEnv\test"
        }
    }

    # ============================================
    #  Write-Banner
    # ============================================

    Describe "Write-Banner" {
        It "应输出横幅内容（6 行：5 行边框 + 1 空行）" {
            Mock Write-Host { }
            Write-Banner
            Should -Invoke Write-Host -Times 6 -Exactly
        }
    }

    Describe "Write-Step" {
        It "应输出步骤标题（3 行）" {
            Mock Write-Host { }
            Write-Step "测试步骤"
            Should -Invoke Write-Host -Times 3 -Exactly
        }
    }

    # ============================================
    #  Confirm-Prompt
    # ============================================

    Describe "Confirm-Prompt" {
        It "输入 y 应返回 $true" {
            Mock Read-Host { return "y" }
            Confirm-Prompt "继续?" | Should -Be $true
        }

        It "输入 n 应返回 $false" {
            Mock Read-Host { return "n" }
            Confirm-Prompt "继续?" | Should -Be $false
        }

        It "默认值为 y 时空输入应返回 $true" {
            Mock Read-Host { return "" }
            Confirm-Prompt "继续?" -Default "y" | Should -Be $true
        }

        It "默认值为 n 时空输入应返回 $false" {
            Mock Read-Host { return "" }
            Confirm-Prompt "继续?" -Default "n" | Should -Be $false
        }
    }

    # ============================================
    #  Stop-ProcessSafely
    # ============================================

    Describe "Stop-ProcessSafely" {
        It "进程不存在时应返回 $false" {
            # 实际代码使用 -ErrorAction SilentlyContinue，不会抛出异常
            Mock Get-Process { return $null }
            Stop-ProcessSafely -ProcessName "NonExistentProcess12345" | Should -Be $false
        }

        It "进程存在时应停止并返回 $true" {
            Mock Get-Process { return @( [PSCustomObject]@{ ProcessName = "testproc"; Id = 1234 } ) }
            Mock Stop-Process { }
            Mock Start-Sleep { }
            Stop-ProcessSafely -ProcessName "testproc" | Should -Be $true
            Should -Invoke Stop-Process -Times 1 -Exactly
        }
    }

    # ============================================
    #  Move-ToRecycleBin
    # ============================================

    Describe "Move-ToRecycleBin" {
        It "路径不存在时应静默返回" {
            Mock Test-Path { return $false }
            { Move-ToRecycleBin -Path "C:\NonExistent" } | Should -Not -Throw
        }

        It "路径存在时应尝试移到回收站" {
            Mock Test-Path { return $true }
            Mock Write-Host { }
            { Move-ToRecycleBin -Path "C:\TestDir" } | Should -Not -Throw
        }
    }
}

# ============================================
#  Migrate-DevEnv.ps1 测试
# ============================================

Describe "Migrate-DevEnv.ps1" {
    BeforeAll {
        . $script:CommonScriptPath
    }

    Describe "Add-ItemRollbackLines" {
        It "env 策略应生成环境变量回滚行" {
            $rollbackLines = @()
            $item = [PSCustomObject]@{
                strategy    = "env"
                source      = "%OLD_TEST_VAR%"
                envVar      = "OLD_TEST_VAR"
                displayName = "测试环境变量"
                target      = "TestTarget"
            }
            Add-ItemRollbackLines -Item $item -Key "TestEnv" -DevEnvRoot "D:\DevEnv" -RollbackLines ([ref]$rollbackLines)
            $rollbackLines.Count | Should -BeGreaterThan 0
            $rollbackLines[0] | Should -Match "回滚"
        }

        It "copy 策略应生成复制回滚行" {
            $rollbackLines = @()
            $item = [PSCustomObject]@{
                strategy    = "copy"
                source      = "C:\Source"
                target      = "TestTarget"
                displayName = "测试复制"
            }
            Mock Test-Path { return $true } -ParameterFilter { $Path -like "*.backup" }
            Add-ItemRollbackLines -Item $item -Key "TestCopy" -DevEnvRoot "D:\DevEnv" -RollbackLines ([ref]$rollbackLines)
            $rollbackLines.Count | Should -BeGreaterThan 0
        }
    }

    Describe "New-RollbackHeader" {
        It "应生成回滚脚本头部" {
            $header = New-RollbackHeader -DevEnvRoot "D:\DevEnv"
            $header | Should -Not -BeNullOrEmpty
            $header[0] | Should -Be "<#"
        }
    }

    Describe "New-RollbackFooter" {
        It "应生成回滚脚本尾部" {
            $footer = New-RollbackFooter
            $footer | Should -Not -BeNullOrEmpty
            # New-RollbackFooter 返回 @("Write-Host '...'")
            # 在 PowerShell 中 @() 只有一个元素时会被展开为字符串
            # 所以 $footer 是字符串，$footer[0] 是第一个字符 'W'
            # 直接匹配整个字符串
            $footer | Should -Match "Write-Host"
        }
    }

    Describe "Get-RollbackRestoreLine" {
        It "应生成恢复命令" {
            $line = Get-RollbackRestoreLine -BackupPath "D:\Backup" -SourcePath "C:\Original"
            $line | Should -Match "Copy-Item"
            $line | Should -Match "D:\\Backup"
            $line | Should -Match "C:\\Original"
        }
    }
}

# ============================================
#  Cleanup-DevEnv.ps1 测试
# ============================================

Describe "Cleanup-DevEnv.ps1" {
    BeforeAll {
        . $script:CommonScriptPath
    }

    Describe "Scan-SourceData" {
        It "应返回每个项目的扫描结果" {
            # 使用 PSCustomObject，因为实际代码用 $Config.migrationItems.$key
            # 在 PSCustomObject 上，$obj.$key 可以动态访问属性
            $testConfig = [PSCustomObject]@{
                devEnvRoot = "D:\DevEnv"
                migrationItems = [PSCustomObject]@{
                    NuGet = [PSCustomObject]@{
                        enabled     = $true
                        strategy    = "env"
                        source      = "C:\TestDir"
                        envVar      = "NUGET_PACKAGES"
                        displayName = "NuGet"
                        target      = "NuGet"
                    }
                }
            }
            Mock Test-Path { return $true }
            Mock Get-ChildItem { return @() }

            $results = Scan-SourceData -Config $testConfig -ItemsToScan @("NuGet")
            $results | Should -Not -BeNullOrEmpty
            # PowerShell 自动展开单元素数组，所以 $results 是单个对象
            # 使用 @() 包裹以确保数组访问
            @($results)[0].Key | Should -Be "NuGet"
        }

        It "项目不存在时应标记为不存在" {
            $testConfig = [PSCustomObject]@{
                devEnvRoot = "D:\DevEnv"
                migrationItems = [PSCustomObject]@{
                    NonExistent = [PSCustomObject]@{
                        enabled     = $true
                        strategy    = "copy"
                        source      = "C:\NonExistent"
                        displayName = "不存在"
                        target      = "NonExistent"
                    }
                }
            }
            Mock Test-Path { return $false }

            $results = Scan-SourceData -Config $testConfig -ItemsToScan @("NonExistent")
            $results | Should -Not -BeNullOrEmpty
            @($results)[0].Exists | Should -Be $false
        }
    }

    Describe "Copy-DockerConfigDir" {
        It "源目录不存在时应静默返回" {
            Mock Test-Path { return $false }
            Mock Write-Host { }
            Copy-DockerConfigDir -SourceDir "C:\NonExistent" -TargetParent "D:\Target" -DirName "config" -Label "Test"
            Should -Invoke Write-Host -Times 0
        }

        It "源目录存在时应复制" {
            Mock Test-Path { return $true }
            Mock New-Item { }
            Mock Copy-Item { }
            Mock Write-Host { }
            Copy-DockerConfigDir -SourceDir "C:\Source" -TargetParent "D:\Target" -DirName "config" -Label "Test"
            Should -Invoke Copy-Item -Times 1 -Exactly
        }
    }
}

# ============================================
#  集成测试：配置加载
# ============================================

Describe "配置加载（集成）" {
    It "config.json 应包含 devEnvRoot" {
        $script:Config.devEnvRoot | Should -Not -BeNullOrEmpty
    }

    It "config.json 应包含 migrationItems" {
        $script:Config.migrationItems | Should -Not -BeNullOrEmpty
    }

    It "config.json 中每个项目都应包含 strategy 和 enabled" {
        $script:Config.migrationItems.PSObject.Properties | ForEach-Object {
            $_.Value.strategy | Should -Not -BeNullOrEmpty
            $_.Value.enabled | Should -Not -BeNullOrEmpty
        }
    }

    It "config.json 中应包含所有支持的策略类型" {
        $strategies = @()
        $script:Config.migrationItems.PSObject.Properties | ForEach-Object {
            $strategies += $_.Value.strategy
        }
        $uniqueStrategies = $strategies | Sort-Object -Unique
        $uniqueStrategies | Should -Contain "env"
        $uniqueStrategies | Should -Contain "config"
        $uniqueStrategies | Should -Contain "copy"
        $uniqueStrategies | Should -Contain "symlink"
        $uniqueStrategies | Should -Contain "wsl"
        $uniqueStrategies | Should -Contain "docker"
    }
}
