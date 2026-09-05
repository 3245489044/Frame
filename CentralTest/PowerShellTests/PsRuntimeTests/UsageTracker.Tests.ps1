# 纯边界测试驱动：验证用量累计与升级阈值的边缘兜底
BeforeAll {
    Import-Module (Join-Path $PSScriptRoot "..\Extension\UsageTracker.psm1") -Force -ErrorAction Stop
}

Describe "UsageTracker 边界" {
    Context "正向用量累计" {
        It "累计调用次数" {
            $s = Join-Path $env:TEMP "u_$(Get-Random).json"
            try {
                Update-PsContractUsage -Action "x.y" -StorePath $s | Out-Null
                @(Get-PsContractUsage -Action "x.y" -StorePath $s).Count | Should -Be 1
            } finally { Remove-Item $s -Force -ErrorAction SilentlyContinue }
        }

        It "多次累计正确递增" {
            $s = Join-Path $env:TEMP "u_inc_$(Get-Random).json"
            try {
                1..3 | ForEach-Object { Update-PsContractUsage -Action "a.b" -StorePath $s | Out-Null }
                @(Get-PsContractUsage -Action "a.b" -StorePath $s).Count | Should -Be 3
            } finally { Remove-Item $s -Force -ErrorAction SilentlyContinue }
        }
    }

    Context "升级阈值" {
        It "达阈值返回应升级" {
            $s = Join-Path $env:TEMP "u_promo_$(Get-Random).json"
            try {
                1..3 | ForEach-Object { Update-PsContractUsage -Action "p.q" -StorePath $s | Out-Null }
                $p = Get-PsContractPromotion -Action "p.q" -StorePath $s -Threshold 3 -WindowDays 30
                $p.ShouldPromote | Should -Be $true
                $p.RecentCount | Should -Be 3
            } finally { Remove-Item $s -Force -ErrorAction SilentlyContinue }
        }

        It "未达阈值不升级" {
            $s = Join-Path $env:TEMP "u_nopromo_$(Get-Random).json"
            try {
                Update-PsContractUsage -Action "p.q" -StorePath $s | Out-Null
                $p = Get-PsContractPromotion -Action "p.q" -StorePath $s -Threshold 3 -WindowDays 30
                $p.ShouldPromote | Should -Be $false
                $p.RecentCount | Should -Be 1
            } finally { Remove-Item $s -Force -ErrorAction SilentlyContinue }
        }

        It "窗口外记录不计入近期" {
            $s = Join-Path $env:TEMP "u_window_$(Get-Random).json"
            try {
                # 手动构造带 60 天前旧记录 + 1 条新记录
                $old = (Get-Date).AddDays(-60).ToString("o")
                $now = (Get-Date).ToString("o")
                @{ records = @{ "p.q" = @($old, $now) } } | ConvertTo-Json -Depth 5 | Set-Content $s -Encoding UTF8
                $p = Get-PsContractPromotion -Action "p.q" -StorePath $s -Threshold 2 -WindowDays 30
                $p.ShouldPromote | Should -Be $false
                $p.RecentCount | Should -Be 1
            } finally { Remove-Item $s -Force -ErrorAction SilentlyContinue }
        }
    }

    Context "边界: 输入与文件" {
        It "StorePath 不存在时自动创建并记录" {
            $s = Join-Path $env:TEMP "u_absent_$(Get-Random).json"
            try {
                $cnt = Update-PsContractUsage -Action "a.b" -StorePath $s
                $cnt | Should -Be 1
                Test-Path $s | Should -Be $true
            } finally { Remove-Item $s -Force -ErrorAction SilentlyContinue }
        }

        It "Action 为空时返回 0 不记录" {
            $s = Join-Path $env:TEMP "u_emptyact_$(Get-Random).json"
            try {
                $cnt = Update-PsContractUsage -Action "" -StorePath $s
                $cnt | Should -Be 0
            } finally { Remove-Item $s -Force -ErrorAction SilentlyContinue }
        }

        It "未记录的 Action 查询返回空" {
            $s = Join-Path $env:TEMP "u_noact_$(Get-Random).json"
            try {
                @{ records = @{} } | ConvertTo-Json -Depth 5 | Set-Content $s -Encoding UTF8
                @(Get-PsContractUsage -Action "never.seen" -StorePath $s).Count | Should -Be 0
            } finally { Remove-Item $s -Force -ErrorAction SilentlyContinue }
        }
    }
}
