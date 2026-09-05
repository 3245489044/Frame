<#
.SYNOPSIS
    Pester 测试：OutputHelpers.psm1

.DESCRIPTION
    测试 Script\Modules\Core\OutputHelpers.psm1 中的所有函数：
    - Write-Header
    - Write-SubHeader
    - Write-Info
    - Write-Success
    - Write-Warning
    - Write-Error
    - Write-Diff
    - Show-FixSummary
#>

BeforeAll {
    $script:ModulePath = Join-Path (Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent) "Script\Modules\Core\OutputHelpers.psm1"
    Import-Module $script:ModulePath -Force
}

# ============================================
#  Write-Header 测试
# ============================================

Describe "Write-Header" {
    It "应输出带等号线框的标题" {
        $output = & { Write-Header "测试标题" } 6>&1
        $output.Count | Should -BeGreaterThan 0
        $output[0] | Should -Match "===="
        $output[1] | Should -Match "测试标题"
    }

    It "应支持自定义颜色" {
        $output = & { Write-Header "标题" -Color "Green" } 6>&1
        $output.Count | Should -BeGreaterThan 0
    }

    It "应支持自定义宽度" {
        $output = & { Write-Header "标题" -Width 30 } 6>&1
        $output[0].Length | Should -Be 30
    }
}

# ============================================
#  Write-SubHeader 测试
# ============================================

Describe "Write-SubHeader" {
    It "应输出带框的子标题" {
        $output = & { Write-SubHeader "子标题" } 6>&1
        $output.Count | Should -BeGreaterThan 0
        $output[0] | Should -Match "╔══"
        $output[1] | Should -Match "子标题"
        $output[2] | Should -Match "╚══"
    }
}

# ============================================
#  Write-Info 测试
# ============================================

Describe "Write-Info" {
    It "应输出标签-值格式的信息" {
        $output = & { Write-Info -Label "名称" -Value "测试值" } 6>&1
        $output.Count | Should -BeGreaterThan 0
        $output[0] | Should -Match "名称"
        $output[0] | Should -Match "测试值"
    }

    It "应处理空标签和空值" {
        $output = & { Write-Info -Label "" -Value "" } 6>&1
        $output.Count | Should -BeGreaterThan 0
    }
}

# ============================================
#  Write-Success / Write-Warning / Write-Error 测试
# ============================================

Describe "Write-Success" {
    It "应输出成功消息" {
        $output = & { Write-Success "操作成功" } 6>&1
        $output[0] | Should -Match "成功"
        $output[0] | Should -Match "操作成功"
    }
}

Describe "Write-Warning" {
    It "应输出警告消息" {
        $output = & { Write-Warning "注意" } 6>&1
        $output[0] | Should -Match "警告"
        $output[0] | Should -Match "注意"
    }
}

Describe "Write-Error" {
    It "应输出错误消息" {
        $output = & { Write-Error "出错了" } 6>&1
        $output[0] | Should -Match "错误"
        $output[0] | Should -Match "出错了"
    }
}

# ============================================
#  Write-Diff 测试
# ============================================

Describe "Write-Diff" {
    It "应输出差异内容" {
        $output = & { Write-Diff "差异内容" } 6>&1
        $output[0] | Should -Match "差异内容"
    }
}

# ============================================
#  Show-FixSummary 测试
# ============================================

Describe "Show-FixSummary" {
    It "应显示修复摘要" {
        $output = & { Show-FixSummary -summary @("修复了 3 个问题") } 6>&1
        $output | Where-Object { $_ -match "修复了 3 个问题" } | Should -Not -BeNullOrEmpty
    }

    It "空摘要应不输出" {
        $output = & { Show-FixSummary -summary @() } 6>&1
        $output | Should -BeNullOrEmpty
    }
}
