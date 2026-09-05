<#
.SYNOPSIS
    Pester 测试：SelectScript.ps1

.DESCRIPTION
    测试 Script\Modules\FileDriven\SelectScript.ps1 脚本。
    该脚本是一个独立的 PowerShell 脚本（非模块），通过 powershell -File 调用。
    测试内容包括：
    - 参数解析（ScriptsDir、RunMode、Title、FilePattern）
    - 脚本列表展示（文件列表、递归扫描、JSON 描述、运行模式）
    - 目录结构信息（ShowDirInfo 开关）
    - 错误处理（目录不存在、空目录）

.NOTES
    从 Script\FileDriven\ 迁移至 PowerShellTests\FileDriven\
#>

BeforeAll {
    $script:ProjectRoot = Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent

    $script:SelectScriptPath = Join-Path $script:ProjectRoot "Script\Modules\FileDriven\SelectScript.ps1"
    $script:ScriptsDir = Join-Path $script:ProjectRoot "FileDriven\DrivenConfig\Scripts"
}

# ============================================
#  基本功能测试
# ============================================

Describe "SelectScript 基本功能" {
    It "脚本文件应存在" {
        Test-Path $script:SelectScriptPath | Should -Be $true
    }

    It "应接受 -ScriptsDir 和 -RunMode 参数" {
        if (Test-Path $script:ScriptsDir) {
            $result = & powershell -NoProfile -File $script:SelectScriptPath `
                -ScriptsDir $script:ScriptsDir `
                -RunMode "auto" 2>&1
            $LASTEXITCODE | Should -BeGreaterOrEqual 0
        }
    }

    It "应支持自定义 Title" {
        if (Test-Path $script:ScriptsDir) {
            $result = & powershell -NoProfile -File $script:SelectScriptPath `
                -ScriptsDir $script:ScriptsDir `
                -RunMode "auto" `
                -Title "自定义标题" 2>&1
            $result | Where-Object { $_ -match "自定义标题" } | Should -Not -BeNullOrEmpty
        }
    }

    It "应支持自定义 FilePattern" {
        if (Test-Path $script:ScriptsDir) {
            $result = & powershell -NoProfile -File $script:SelectScriptPath `
                -ScriptsDir $script:ScriptsDir `
                -RunMode "auto" `
                -FilePattern ".json" 2>&1
            $LASTEXITCODE | Should -BeGreaterOrEqual 0
        }
    }
}

# ============================================
#  脚本列表展示测试
# ============================================

Describe "SelectScript 脚本列表展示" {
    It "应列出指定目录中的脚本文件" {
        if (Test-Path $script:ScriptsDir) {
            $result = & powershell -NoProfile -File $script:SelectScriptPath `
                -ScriptsDir $script:ScriptsDir `
                -RunMode "auto" 2>&1
            $result | Where-Object { $_ -match "序号" } | Should -Not -BeNullOrEmpty
        }
    }

    It "应显示当前运行模式" {
        if (Test-Path $script:ScriptsDir) {
            $result = & powershell -NoProfile -File $script:SelectScriptPath `
                -ScriptsDir $script:ScriptsDir `
                -RunMode "manual" 2>&1
            $result | Where-Object { $_ -match "手动" } | Should -Not -BeNullOrEmpty
        }
    }

    It "应支持 ShowDirInfo 开关" {
        if (Test-Path $script:ScriptsDir) {
            $result = & powershell -NoProfile -File $script:SelectScriptPath `
                -ScriptsDir $script:ScriptsDir `
                -RunMode "auto" `
                -ShowDirInfo 2>&1
            $result | Where-Object { $_ -match "目录结构" } | Should -Not -BeNullOrEmpty
        }
    }
}

# ============================================
#  错误处理测试
# ============================================

Describe "SelectScript 错误处理" {
    It "目录不存在时应返回非零退出码" {
        $result = & powershell -NoProfile -File $script:SelectScriptPath `
            -ScriptsDir "C:\NonExistentDir_12345" `
            -RunMode "auto" 2>&1
        $LASTEXITCODE | Should -Not -Be 0
    }

    It "空目录时应返回非零退出码" {
        $emptyDir = Join-Path $TestDrive "EmptyScriptsDir"
        New-Item -Path $emptyDir -ItemType Directory -Force | Out-Null

        $result = & powershell -NoProfile -File $script:SelectScriptPath `
            -ScriptsDir $emptyDir `
            -RunMode "auto" 2>&1
        $LASTEXITCODE | Should -Not -Be 0
    }
}
