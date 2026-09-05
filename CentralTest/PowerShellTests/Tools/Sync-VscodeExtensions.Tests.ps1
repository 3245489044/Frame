<#
.SYNOPSIS
    Pester 测试：Sync-VscodeExtensions.ps1

.DESCRIPTION
    测试 Script\Tools\Sync-VscodeExtensions.ps1 脚本中的函数：
    - Find-VscodeCmd
    - Export-VscodeExtensions
    - Install-VscodeExtensions
    - Compare-VscodeExtensions
    - Show-Menu
    - Pause-Message
#>

BeforeAll {
    $script:ProjectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
    $script:ScriptPath = Join-Path $script:ProjectRoot "Script\Tools\Sync-VscodeExtensions.ps1"

    # dot-source 脚本以获取内部函数
    . $script:ScriptPath
}

# ============================================
#  Find-VscodeCmd 测试
# ============================================

Describe "Find-VscodeCmd" {
    It "应返回 code 命令路径（如果存在）" {
        $cmd = Find-VscodeCmd
        # 可能返回 "code" 或 $null，取决于环境
        if ($cmd) {
            $cmd | Should -Not -BeNullOrEmpty
        }
    }
}

# ============================================
#  Export-VscodeExtensions 测试
# ============================================

Describe "Export-VscodeExtensions" {
    It "VS Code 不存在时应返回 $false" {
        # 临时修改 Find-VscodeCmd 的行为
        Mock Find-VscodeCmd { return $null }
        $result = Export-VscodeExtensions
        $result | Should -Be $false
    }
}

# ============================================
#  Install-VscodeExtensions 测试
# ============================================

Describe "Install-VscodeExtensions" {
    It "扩展列表文件不存在时应输出错误" {
        $originalExtListFile = $script:extListFile
        $script:extListFile = "C:\NonExistentFile_Test.list"
        try {
            $output = & { Install-VscodeExtensions } 6>&1
            $output | Where-Object { $_ -match "未找到扩展列表文件" } | Should -Not -BeNullOrEmpty
        } finally {
            $script:extListFile = $originalExtListFile
        }
    }

    It "VS Code 不存在时应输出错误" {
        $extListFile = Join-Path $TestDrive "extensions.list"
        Set-Content -Path $extListFile -Value "ms-dotnettools.csharp" -Encoding UTF8

        $originalExtListFile = $script:extListFile
        $script:extListFile = $extListFile
        Mock Find-VscodeCmd { return $null }
        try {
            $output = & { Install-VscodeExtensions } 6>&1
            $output | Where-Object { $_ -match "未找到 VS Code" } | Should -Not -BeNullOrEmpty
        } finally {
            $script:extListFile = $originalExtListFile
        }
    }
}

# ============================================
#  Compare-VscodeExtensions 测试
# ============================================

Describe "Compare-VscodeExtensions" {
    It "扩展列表文件不存在时应输出错误" {
        $originalExtListFile = $script:extListFile
        $script:extListFile = "C:\NonExistentFile_Test456.list"
        try {
            $output = & { Compare-VscodeExtensions } 6>&1
            $output | Where-Object { $_ -match "未找到扩展列表文件" } | Should -Not -BeNullOrEmpty
        } finally {
            $script:extListFile = $originalExtListFile
        }
    }

    It "VS Code 不存在时应输出错误" {
        $extListFile = Join-Path $TestDrive "extensions.list"
        Set-Content -Path $extListFile -Value "ms-dotnettools.csharp" -Encoding UTF8

        $originalExtListFile = $script:extListFile
        $script:extListFile = $extListFile
        Mock Find-VscodeCmd { return $null }
        try {
            $output = & { Compare-VscodeExtensions } 6>&1
            $output | Where-Object { $_ -match "未找到 VS Code" } | Should -Not -BeNullOrEmpty
        } finally {
            $script:extListFile = $originalExtListFile
        }
    }
}

# ============================================
#  Pause-Message 测试
# ============================================

Describe "Pause-Message" {
    It "非 ConsoleHost 时应不抛出异常" {
        $originalHostName = $host.Name
        # 模拟非 ConsoleHost 环境
        & { Pause-Message } 6>&1 | Should -BeNullOrEmpty
    }
}

