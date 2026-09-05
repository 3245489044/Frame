# 纯边界测试驱动：校验失败短路/异常/null 契约/hook 边界
BeforeAll {
    Import-Module (Join-Path $PSScriptRoot "..\Runtime\OutputSchema.psm1") -Force -ErrorAction Stop
    Import-Module (Join-Path $PSScriptRoot "..\Contract\ContractValidator.psm1") -Force -ErrorAction Stop
    . (Join-Path $PSScriptRoot "..\Runtime\Invoke-PsContract.ps1")
    $script:tmp = Join-Path $env:TEMP "psrt_exec_$(Get-Random)"
    New-Item -Path $script:tmp -ItemType Directory -Force | Out-Null
    $script:mod = Join-Path $script:tmp "Echo.psm1"
    @"
# @ps-action: echo.test
function Invoke-Echo { param([string]`$Msg) @{ ok = `$Msg } }
"@ | Set-Content $script:mod -Encoding UTF8
}
AfterAll { Remove-Item $script:tmp -Recurse -Force -ErrorAction SilentlyContinue }

Describe "Invoke-PsContract boundary" {
    It "参数校验失败短路，不执行后续 stage" {
        $c = @{ identity = @{ action = "echo.test"; function = "Invoke-Echo"; path = $script:mod }; params = @(@{ name = "Msg"; type = "string"; required = $true }); env_tags = @{ ps_version = "5.1+"; os = "windows"; modules = @() } }
        $r = Invoke-PsContract -Contract $c -Arguments @{}
        $r.status | Should -Be "failure"
        $r.data._stages | Should -Not -Contain "ExecuteFunction"
    }
    It "执行抛异常返回 failure 带异常消息" {
        $bad = Join-Path $script:tmp "Bad.psm1"
        "function Invoke-Bad { throw 'boom' }" | Set-Content $bad -Encoding UTF8
        $c = @{ identity = @{ action = "x.bad"; function = "Invoke-Bad"; path = $bad }; params = @(); env_tags = @{ ps_version = "5.1+"; os = "windows"; modules = @() } }
        $r = Invoke-PsContract -Contract $c -Arguments @{}
        $r.status | Should -Be "failure"
        $r.errors | Should -Match "boom"
    }
    It "env 校验失败短路（缺模块）" {
        $c = @{ identity = @{ action = "echo.test"; function = "Invoke-Echo"; path = $script:mod }; params = @(); env_tags = @{ ps_version = "5.1+"; os = "windows"; modules = @("NoSuchMod_$(Get-Random)") } }
        $r = Invoke-PsContract -Contract $c -Arguments @{}
        $r.status | Should -Be "failure"
        $r.data._stages | Should -Contain "ValidateEnv"
        $r.data._stages | Should -Not -Contain "ExecuteFunction"
    }
    It "null 契约不抛异常返回 failure" {
        $r = Invoke-PsContract -Contract $null -Arguments @{}
        $r.status | Should -Be "failure"
    }
    It "路径不存在执行失败返回 failure" {
        $c = @{ identity = @{ action = "x.y"; function = "Nope"; path = (Join-Path $script:tmp "nonexist.psm1") }; params = @(); env_tags = @{ ps_version = "5.1+"; os = "windows"; modules = @() } }
        $r = Invoke-PsContract -Contract $c -Arguments @{}
        $r.status | Should -Be "failure"
    }
    It "AfterStageHook 每个 stage 后被调用" {
        $c = @{ identity = @{ action = "echo.test"; function = "Invoke-Echo"; path = $script:mod }; params = @(@{ name = "Msg"; type = "string"; required = $true }); env_tags = @{ ps_version = "5.1+"; os = "windows"; modules = @() } }
        $log = [System.Collections.ArrayList]@()
        $hook = { param($ctx) $null = $log.Add($ctx.stage) }
        $r = Invoke-PsContract -Contract $c -Arguments @{ Msg = "hi" } -AfterStageHook $hook
        $log.Count | Should -BeGreaterThan 0
    }
}
