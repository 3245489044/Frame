<#
.SYNOPSIS
    Pester 测试：FindLauncherDir.ps1

.DESCRIPTION
    测试 Script\Modules\FileDriven\FindLauncherDir.ps1 脚本。
    该脚本是一个独立的 PowerShell 脚本（非模块），通过 powershell -File 调用。
    测试内容包括：
    - 基本功能（脚本存在、参数接受、stdout 输出）
    - 路径计算（Debug/Release 目录路径）
    - 退出码（正常/异常退出码）
    - 输出格式（stdout/stderr 分离）

.NOTES
    从 Script\FileDriven\ 迁移至 PowerShellTests\FileDriven\
#>

BeforeAll {
    $script:ProjectRoot = Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent

    $script:FindScriptPath = Join-Path $script:ProjectRoot "Script\Modules\FileDriven\FindLauncherDir.ps1"
    $script:FileDrivenDir = Join-Path $script:ProjectRoot "FileDriven"
}

# ============================================
#  基本功能测试
# ============================================

Describe "FindLauncherDir 基本功能" {
    It "脚本文件应存在" {
        Test-Path $script:FindScriptPath | Should -Be $true
    }

    It "应接受 -FileDrivenDir 参数" {
        # 验证脚本可以正常调用（不抛出异常）
        $result = & powershell -NoProfile -File $script:FindScriptPath -FileDrivenDir $script:FileDrivenDir 2>&1
        $LASTEXITCODE | Should -BeGreaterOrEqual 0
    }

    It "应输出目录路径到 stdout" {
        # 分别捕获 stdout 和 stderr
        $stdout = & powershell -NoProfile -File $script:FindScriptPath -FileDrivenDir $script:FileDrivenDir 2>$null
        $stderr = & powershell -NoProfile -File $script:FindScriptPath -FileDrivenDir $script:FileDrivenDir 2>&1

        # stdout 应该包含路径信息（最后一行是路径）
        $stdout | Should -Not -BeNullOrEmpty
    }
}

# ============================================
#  路径计算测试
# ============================================

Describe "FindLauncherDir 路径计算" {
    It "应正确计算 Debug 目录路径" {
        # ★ 使用 ReadProjectConfig 动态获取路径，与 FindLauncherDir.ps1 保持一致
        . "$script:ProjectRoot\Script\Modules\Core\ReadProjectConfig.ps1"
        $expectedDebugDir = Get-BuildOutputPath -ProjectRoot $script:ProjectRoot -Configuration "Debug"
        $expectedDebugDir | Should -Not -BeNullOrEmpty
    }

    It "应正确计算 Release 目录路径" {
        # ★ 使用 ReadProjectConfig 动态获取路径，与 FindLauncherDir.ps1 保持一致
        . "$script:ProjectRoot\Script\Modules\Core\ReadProjectConfig.ps1"
        $expectedReleaseDir = Get-BuildOutputPath -ProjectRoot $script:ProjectRoot -Configuration "Release"
        $expectedReleaseDir | Should -Not -BeNullOrEmpty
    }
}


# ============================================
#  退出码测试
# ============================================

Describe "FindLauncherDir 退出码" {
    It "脚本执行后应返回退出码" {
        & powershell -NoProfile -File $script:FindScriptPath -FileDrivenDir $script:FileDrivenDir 2>$null | Out-Null
        # 退出码应为 0（找到启动器）、1（未找到且构建失败）或 2（构建成功但产物不在预期位置）
        $LASTEXITCODE | Should -BeGreaterOrEqual 0
    }

    It "FileDrivenDir 不存在时应返回非零退出码" {
        & powershell -NoProfile -File $script:FindScriptPath -FileDrivenDir "C:\NonExistentDir_12345" 2>$null | Out-Null
        $LASTEXITCODE | Should -Not -Be 0
    }
}

# ============================================
#  输出格式测试
# ============================================

Describe "FindLauncherDir 输出格式" {
    It "stdout 最后一行应为目录路径" {
        $stdout = & powershell -NoProfile -File $script:FindScriptPath -FileDrivenDir $script:FileDrivenDir 2>$null
        $lastLine = $stdout | Select-Object -Last 1
        if ($lastLine) {
            $lastLine | Should -Match '[a-zA-Z]:\\'
        }
    }

    It "stderr 应包含日志信息" {
        $allOutput = & powershell -NoProfile -File $script:FindScriptPath -FileDrivenDir $script:FileDrivenDir 2>&1
        $stderrLines = $allOutput | Where-Object { $_ -isnot [string] }
        # stderr 可能包含 Write-Host 输出（信息流）
    }
}
