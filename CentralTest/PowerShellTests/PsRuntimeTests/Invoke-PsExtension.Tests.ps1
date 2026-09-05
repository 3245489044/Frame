# 纯边界测试驱动：验证拓展协议编排的脚手架→注册→用量链路与边缘兜底
BeforeAll {
    Import-Module (Join-Path $PSScriptRoot "..\Runtime\OutputSchema.psm1") -Force -ErrorAction Stop
    Import-Module (Join-Path $PSScriptRoot "..\Extension\ContractScaffold.psm1") -Force -ErrorAction Stop
    Import-Module (Join-Path $PSScriptRoot "..\Extension\ContractRegistrar.psm1") -Force -ErrorAction Stop
    Import-Module (Join-Path $PSScriptRoot "..\Extension\UsageTracker.psm1") -Force -ErrorAction Stop
    . (Join-Path $PSScriptRoot "..\Extension\Invoke-PsExtension.ps1")
}

Describe "Invoke-PsExtension 边界" {
    Context "正向编排" {
        It "产出脚手架并注册，phase=scaffolded" {
            $tmp = Join-Path $env:TEMP "ext_$(Get-Random)"
            New-Item -Path $tmp -ItemType Directory -Force | Out-Null
            try {
                $spec = @{ action = "demo.ops.run"; verb = "demo"; object = "ops"; purpose = "demo"; function = "Invoke-Demo"; params = @() }
                $reg = Join-Path $tmp "reg.json"
                $r = Invoke-PsExtension -Spec $spec -OutDir $tmp -RegistryPath $reg -UsagePath (Join-Path $tmp "u.json") -Arguments @{}
                $r.status | Should -Be "success"
                $r.data.phase | Should -Be "scaffolded"
                $r.data.action | Should -Be "demo.ops.run"
                $r.data.function | Should -Be "Invoke-Demo"
                Test-Path $r.data.path | Should -Be $true
                # 注册表写入
                $regObj = Get-Content $reg -Raw -Encoding UTF8 | ConvertFrom-Json
                $regObj.Functions.'Invoke-Demo'.Alias | Should -Be "demo.ops.run"
            } finally { Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue }
        }

        It "记录用量并返回 promotion 信息" {
            $tmp = Join-Path $env:TEMP "ext_promo_$(Get-Random)"
            New-Item -Path $tmp -ItemType Directory -Force | Out-Null
            try {
                $spec = @{ action = "p.q"; verb = "p"; object = "q"; purpose = "p"; function = "Do-P"; params = @() }
                $reg = Join-Path $tmp "reg.json"
                $usage = Join-Path $tmp "u.json"
                $r = Invoke-PsExtension -Spec $spec -OutDir $tmp -RegistryPath $reg -UsagePath $usage -Arguments @{}
                $r.data.promotion.RecentCount | Should -Be 1
                $r.data.promotion.ShouldPromote | Should -Be $false
            } finally { Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue }
        }
    }

    Context "边界: 输入异常" {
        It "Spec 为 null 时返回 failure" {
            $tmp = Join-Path $env:TEMP "ext_null_$(Get-Random)"
            New-Item -Path $tmp -ItemType Directory -Force | Out-Null
            try {
                $r = Invoke-PsExtension -Spec $null -OutDir $tmp -RegistryPath (Join-Path $tmp "reg.json") -UsagePath (Join-Path $tmp "u.json") -Arguments @{}
                $r.status | Should -Be "failure"
            } finally { Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue }
        }

        It "Spec 缺 action 时脚手架生成失败返回 failure" {
            $tmp = Join-Path $env:TEMP "ext_noact_$(Get-Random)"
            New-Item -Path $tmp -ItemType Directory -Force | Out-Null
            try {
                $spec = @{ verb = "x"; object = "y"; function = "Do-X" }
                $r = Invoke-PsExtension -Spec $spec -OutDir $tmp -RegistryPath (Join-Path $tmp "reg.json") -UsagePath (Join-Path $tmp "u.json") -Arguments @{}
                $r.status | Should -Be "failure"
            } finally { Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue }
        }

        It "OutDir 不存在时脚手架生成失败返回 failure" {
            $tmp = Join-Path $env:TEMP "ext_nodir_$(Get-Random)"
            $spec = @{ action = "a.b"; verb = "a"; object = "b"; purpose = "p"; function = "Do-A"; params = @() }
            $r = Invoke-PsExtension -Spec $spec -OutDir $tmp -RegistryPath (Join-Path $env:TEMP "reg_$(Get-Random).json") -UsagePath (Join-Path $env:TEMP "u_$(Get-Random).json") -Arguments @{}
            $r.status | Should -Be "failure"
        }
    }
}
