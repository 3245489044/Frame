# 纯边界测试驱动：缺参数/缺模块/os 不匹配/null 契约边界
BeforeAll {
    Import-Module (Join-Path $PSScriptRoot "..\Contract\ContractValidator.psm1") -Force -ErrorAction Stop
}

Describe "ContractValidator boundary" {
    Context "Test-PsContractParams 缺参数边界" {
        It "缺单个必填参数标记该参数名" {
            $c = @{ params = @(@{ name = "Path"; type = "string"; required = $true }) }
            $r = Test-PsContractParams -Contract $c -Arguments @{}
            $r.IsValid | Should -Be $false
            $r.Errors | Should -Contain "Path"
        }
        It "缺多个必填参数全部被标记" {
            $c = @{ params = @(
                @{ name = "Path"; type = "string"; required = $true }
                @{ name = "Content"; type = "string"; required = $true }
            ) }
            $r = Test-PsContractParams -Contract $c -Arguments @{}
            $r.Errors | Should -Contain "Path"
            $r.Errors | Should -Contain "Content"
            $r.Errors.Count | Should -Be 2
        }
        It "部分必填参数缺失仅标记缺失的" {
            $c = @{ params = @(
                @{ name = "Path"; type = "string"; required = $true }
                @{ name = "Content"; type = "string"; required = $true }
            ) }
            $r = Test-PsContractParams -Contract $c -Arguments @{ Path = "x" }
            $r.Errors | Should -Contain "Content"
            $r.Errors | Should -Not -Contain "Path"
        }
        It "非必填参数缺失不标记" {
            $c = @{ params = @(@{ name = "Opt"; type = "string"; required = $false }) }
            $r = Test-PsContractParams -Contract $c -Arguments @{}
            $r.IsValid | Should -Be $true
        }
        It "null 契约不抛异常且返回失败" {
            $r = Test-PsContractParams -Contract $null -Arguments @{}
            $r.IsValid | Should -Be $false
        }
        It "空 params 数组无必填参数通过" {
            $r = Test-PsContractParams -Contract @{ params = @() } -Arguments @{}
            $r.IsValid | Should -Be $true
        }
    }

    Context "Test-PsContractEnv 环境边界" {
        It "缺模块标记 module:名称" {
            $c = @{ env_tags = @{ ps_version = "5.1+"; os = "windows"; modules = @("NoSuchModule_$(Get-Random)") } }
            $r = Test-PsContractEnv -Contract $c
            $r.IsValid | Should -Be $false
            ($r.Errors | Where-Object { $_ -match "module:" }) | Should -Not -BeNullOrEmpty
        }
        It "os 不匹配标记 os" {
            $c = @{ env_tags = @{ ps_version = "5.1+"; os = "linux"; modules = @() } }
            $r = Test-PsContractEnv -Contract $c
            $r.IsValid | Should -Be $false
            $r.Errors | Should -Contain "os"
        }
        It "null 契约不抛异常" {
            $r = Test-PsContractEnv -Contract $null
            $r.IsValid | Should -Be $false
        }
        It "无 env_tags 的契约不抛异常" {
            $r = Test-PsContractEnv -Contract @{}
            # 无 env_tags 视为无环境要求，不应崩溃
            $r | Should -Not -BeNullOrEmpty
        }
        It "空 modules 数组不标记模块错误" {
            $c = @{ env_tags = @{ ps_version = "5.1+"; os = "windows"; modules = @() } }
            $r = Test-PsContractEnv -Contract $c
            ($r.Errors | Where-Object { $_ -match "module:" }) | Should -BeNullOrEmpty
        }
    }
}
