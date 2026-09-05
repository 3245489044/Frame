# 纯边界测试驱动：只测 null/默认/幂等/非法输入边界
BeforeAll {
    Import-Module (Join-Path $PSScriptRoot "..\Runtime\OutputSchema.psm1") -Force -ErrorAction Stop
}

Describe "OutputSchema boundary" {
    Context "New-PsResult 默认值与非法输入" {
        It "不传 message 时默认为空字符串" {
            $r = New-PsResult -Status success
            $r.message | Should -Be ""
        }
        It "不传 errors 时默认为空数组（非 null）" {
            $r = New-PsResult -Status success
            # 空数组在 Pester BeNullOrEmpty 判定为 empty，故用类型+计数校验
            $r.errors -is [array] | Should -Be $true
            $r.errors.Count | Should -Be 0
        }
        It "不传 data 时 data 为 null" {
            $r = New-PsResult -Status success
            $r.data | Should -BeNullOrEmpty
        }
        It "非法 status 抛异常（枚举边界）" {
            { New-PsResult -Status "invalid" } | Should -Throw
        }
        It "failure 状态带 errors 数组" {
            $r = New-PsResult -Status failure -Message "boom" -Errors @("e1", "e2")
            $r.status | Should -Be "failure"
            $r.errors.Count | Should -Be 2
            $r.errors | Should -Contain "e1"
        }
    }

    Context "ConvertTo-PsResult 边界输入" {
        It "null 输入包装为 success 且 data 为 null" {
            $r = ConvertTo-PsResult -InputObject $null
            $r.status | Should -Be "success"
            $r.data | Should -BeNullOrEmpty
        }
        It "已结构化结果不双重包装（幂等边界）" {
            $raw = New-PsResult -Status failure -Message "orig" -Errors @("e")
            $r = ConvertTo-PsResult -InputObject $raw
            # 应原样返回，不包装
            $r.status | Should -Be "failure"
            $r.message | Should -Be "orig"
            $r.errors | Should -Contain "e"
            $r.data | Should -BeNullOrEmpty
        }
        It "基本类型输入（字符串）包装到 data" {
            $r = ConvertTo-PsResult -InputObject "raw string"
            $r.status | Should -Be "success"
            $r.data | Should -Be "raw string"
        }
        It "数组输入包装到 data" {
            $r = ConvertTo-PsResult -InputObject @(1, 2, 3)
            $r.data.Count | Should -Be 3
        }
        It "hashtable 输入包装到 data" {
            $r = ConvertTo-PsResult -InputObject @{ x = 42 }
            $r.data.x | Should -Be 42
        }
    }
}
