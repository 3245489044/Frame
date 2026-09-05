# 纯边界测试驱动：null 契约/缺参数/缺模块/os 不匹配边界
BeforeAll {
    Import-Module (Join-Path $PSScriptRoot "..\Contract\ContractValidator.psm1") -Force -ErrorAction Stop
    Import-Module (Join-Path $PSScriptRoot "..\Gate\CanHandler.psm1") -Force -ErrorAction Stop
}

Describe "CanHandler boundary" {
    It "null 契约返回 no_contract" {
        $r = Test-PsCanHandle -Contract $null -Arguments @{}
        $r.CanHandle | Should -Be $false
        $r.Reason | Should -Be "no_contract"
    }
    It "缺必填参数返回 param_missing" {
        $c = @{ params = @(@{ name = "Path"; type = "string"; required = $true }); env_tags = @{ ps_version = "5.1+"; os = "windows"; modules = @() } }
        $r = Test-PsCanHandle -Contract $c -Arguments @{}
        $r.CanHandle | Should -Be $false
        $r.Reason | Should -Match "param"
        $r.Reason | Should -Match "Path"
    }
    It "缺模块返回 env_unmet 含模块名" {
        $c = @{ params = @(); env_tags = @{ ps_version = "5.1+"; os = "windows"; modules = @("NoSuchMod_$(Get-Random)") } }
        $r = Test-PsCanHandle -Contract $c -Arguments @{}
        $r.CanHandle | Should -Be $false
        $r.Reason | Should -Match "env"
    }
    It "os 不匹配返回 env_unmet 含 os" {
        $c = @{ params = @(); env_tags = @{ ps_version = "5.1+"; os = "linux"; modules = @() } }
        $r = Test-PsCanHandle -Contract $c -Arguments @{}
        $r.CanHandle | Should -Be $false
        $r.Reason | Should -Match "env"
    }
    It "null 参数不抛异常" {
        $c = @{ params = @(); env_tags = @{ ps_version = "5.1+"; os = "windows"; modules = @() } }
        $r = Test-PsCanHandle -Contract $c -Arguments $null
        $r | Should -Not -BeNullOrEmpty
    }
    It "参数优先于环境校验（短路）" {
        # 缺参数且缺模块：应先报 param 而非 env
        $c = @{ params = @(@{ name = "Path"; type = "string"; required = $true }); env_tags = @{ ps_version = "5.1+"; os = "windows"; modules = @("NoSuchMod") } }
        $r = Test-PsCanHandle -Contract $c -Arguments @{}
        $r.Reason | Should -Match "param"
        $r.Reason | Should -Not -Match "env"
    }
}
