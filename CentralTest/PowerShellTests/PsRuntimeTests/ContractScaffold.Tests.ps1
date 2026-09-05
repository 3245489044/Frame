# 纯边界测试驱动：验证脚手架生成器在边缘条件下守住不变量
BeforeAll {
    Import-Module (Join-Path $PSScriptRoot "..\Runtime\OutputSchema.psm1") -Force -ErrorAction Stop
    Import-Module (Join-Path $PSScriptRoot "..\Extension\ContractScaffold.psm1") -Force -ErrorAction Stop
}

Describe "ContractScaffold 边界" {
    Context "正向生成" {
        It "生成含动词优先标签、函数定义、New-PsResult 的脚手架" {
            $spec = @{
                action = "count.file.lines"; verb = "count"; object = "file"
                purpose = "统计行数"; function = "Get-FileLineCount"
                params = @(@{ name = "Path"; type = "string"; required = $true })
            }
            $out = Join-Path $env:TEMP "sc_$(Get-Random).psm1"
            try {
                $r = New-PsContractScript -Spec $spec -OutputPath $out
                $r.status | Should -Be "success"
                $c = Get-Content $out -Raw -Encoding UTF8
                $c | Should -Match "@ps-action: count.file.lines"
                $c | Should -Match "function Get-FileLineCount"
                $c | Should -Match "New-PsResult"
                $side = $out -replace '\.psm1$', '.contracts.json'
                $sideObj = Get-Content $side -Raw -Encoding UTF8 | ConvertFrom-Json
                $sideObj.contracts[0].identity.action | Should -Be "count.file.lines"
                $sideObj.contracts[0].identity.function | Should -Be "Get-FileLineCount"
            } finally {
                Remove-Item $out, ($out -replace '\.psm1$', '.contracts.json') -Force -ErrorAction SilentlyContinue
            }
        }
    }

    Context "边界: spec 异常" {
        It "spec 为 null 时返回 failure" {
            $out = Join-Path $env:TEMP "sc_null_$(Get-Random).psm1"
            try {
                $r = New-PsContractScript -Spec $null -OutputPath $out
                $r.status | Should -Be "failure"
                $r.errors | Should -Not -BeNullOrEmpty
                Test-Path $out | Should -Be $false
            } finally {
                Remove-Item $out -Force -ErrorAction SilentlyContinue
            }
        }

        It "spec 缺失 action 时返回 failure" {
            $spec = @{ verb = "count"; object = "file"; function = "Get-X" }
            $out = Join-Path $env:TEMP "sc_noaction_$(Get-Random).psm1"
            try {
                $r = New-PsContractScript -Spec $spec -OutputPath $out
                $r.status | Should -Be "failure"
                Test-Path $out | Should -Be $false
            } finally {
                Remove-Item $out -Force -ErrorAction SilentlyContinue
            }
        }

        It "spec 缺失 function 时返回 failure" {
            $spec = @{ action = "count.file"; verb = "count"; object = "file" }
            $out = Join-Path $env:TEMP "sc_nofn_$(Get-Random).psm1"
            try {
                $r = New-PsContractScript -Spec $spec -OutputPath $out
                $r.status | Should -Be "failure"
                Test-Path $out | Should -Be $false
            } finally {
                Remove-Item $out -Force -ErrorAction SilentlyContinue
            }
        }

        It "params 为空时仍生成成功（无参数契约）" {
            $spec = @{
                action = "ping.system"; verb = "ping"; object = "system"
                purpose = "存活探测"; function = "Test-SystemAlive"; params = @()
            }
            $out = Join-Path $env:TEMP "sc_noparam_$(Get-Random).psm1"
            try {
                $r = New-PsContractScript -Spec $spec -OutputPath $out
                $r.status | Should -Be "success"
                $side = $out -replace '\.psm1$', '.contracts.json'
                $sideObj = Get-Content $side -Raw -Encoding UTF8 | ConvertFrom-Json
                # PowerShell ConvertTo-Json 把空数组序列化为 null/""，故用 BeNullOrEmpty 表达"无参数契约"
                $sideObj.contracts[0].params | Should -BeNullOrEmpty
            } finally {
                Remove-Item $out, ($out -replace '\.psm1$', '.contracts.json') -Force -ErrorAction SilentlyContinue
            }
        }
    }

    Context "边界: 输出路径异常" {
        It "OutputPath 所在目录不存在时返回 failure" {
            $spec = @{
                action = "x.y"; verb = "x"; object = "y"
                purpose = "p"; function = "Do-X"; params = @()
            }
            $out = Join-Path $env:TEMP "nonexist_dir_$(Get-Random)\sc.psm1"
            $r = New-PsContractScript -Spec $spec -OutputPath $out
            $r.status | Should -Be "failure"
        }
    }
}
