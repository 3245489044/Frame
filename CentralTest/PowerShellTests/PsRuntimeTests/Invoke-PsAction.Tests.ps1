# 纯边界测试驱动：验证 LLM 单一入口的归一化/命中/拓展/兜底链路与边缘容错
BeforeAll {
    Import-Module (Join-Path $PSScriptRoot "..\Runtime\OutputSchema.psm1") -Force -ErrorAction Stop
    Import-Module (Join-Path $PSScriptRoot "..\Contract\ContractValidator.psm1") -Force -ErrorAction Stop
    Import-Module (Join-Path $PSScriptRoot "..\Capability\CapabilityResolver.psm1") -Force -ErrorAction Stop
    Import-Module (Join-Path $PSScriptRoot "..\Gate\CanHandler.psm1") -Force -ErrorAction Stop
    Import-Module (Join-Path $PSScriptRoot "..\Extension\ContractScaffold.psm1") -Force -ErrorAction Stop
    Import-Module (Join-Path $PSScriptRoot "..\Extension\ContractRegistrar.psm1") -Force -ErrorAction Stop
    Import-Module (Join-Path $PSScriptRoot "..\Extension\UsageTracker.psm1") -Force -ErrorAction Stop
    . (Join-Path $PSScriptRoot "..\Runtime\Invoke-PsContract.ps1")
    . (Join-Path $PSScriptRoot "..\Extension\Invoke-PsExtension.ps1")
    . (Join-Path $PSScriptRoot "..\Runtime\Invoke-PsAction.ps1")

    $script:tmp = Join-Path $env:TEMP "psa_$(Get-Random)"
    New-Item -Path $script:tmp -ItemType Directory -Force | Out-Null
    $script:mod = Join-Path $script:tmp "Echo.psm1"
    @"
# @ps-action: echo.test
function Invoke-Echo { param([string]`$Msg) @{ ok = `$Msg } }
"@ | Set-Content $script:mod -Encoding UTF8
    $script:cs = @(@{
        identity = @{ action = "echo.test"; verb = "echo"; object = "test"; function = "Invoke-Echo"; path = $script:mod }
        params = @(@{ name = "Msg"; type = "string"; required = $true })
        env_tags = @{ ps_version = "5.1+"; os = "windows"; modules = @() }
    })
}
AfterAll { Remove-Item $script:tmp -Recurse -Force -ErrorAction SilentlyContinue }

Describe "Invoke-PsAction 命中与拓展" {
    It "命中动作→执行→success（LLM 单一调用）" {
        $r = Invoke-PsAction -Action "echo.test" -Params @{ Msg = "hi" } -Contracts $script:cs
        $r.status | Should -Be "success"
        $r.data.ok | Should -Be "hi"
    }

    It "未命中且无 Spec→返回 failure 标 no_capability" {
        $r = Invoke-PsAction -Action "nope.nope" -Params @{} -Contracts $script:cs
        $r.status | Should -Be "failure"
        $r.errors[0] | Should -Match "no_capability"
    }

    It "未命中但有 Spec→走拓展协议 scaffolded" {
        $spec = @{ action = "demo.run"; verb = "demo"; object = "run"; purpose = "d"; function = "Invoke-Demo"; params = @() }
        $reg = Join-Path $script:tmp "reg.json"
        @{ Functions = @{} } | ConvertTo-Json -Depth 5 | Set-Content $reg -Encoding UTF8
        $r = Invoke-PsAction -Action "demo.run" -Params @{} -Contracts $script:cs -ExtensionSpec $spec -OutDir $script:tmp -RegistryPath $reg -UsagePath (Join-Path $script:tmp "u.json")
        $r.data.phase | Should -Be "scaffolded"
    }
}

Describe "Invoke-PsAction 归一化" {
    It "LLM 简写 verb + DefaultObject→归一化后命中" {
        $r = Invoke-PsAction -Action "echo" -Params @{ Msg = "hi" } -Contracts $script:cs -DefaultObject "test"
        $r.status | Should -Be "success"
        $r.data.ok | Should -Be "hi"
    }

    It "别名经 Register-PsActionAlias→归一化后命中" {
        Register-PsActionAlias -Alias "回显" -Action "echo.test"
        $r = Invoke-PsAction -Action "回显" -Params @{ Msg = "hi" } -Contracts $script:cs
        $r.status | Should -Be "success"
        $r.data.ok | Should -Be "hi"
    }

    It "未归一化且无 DefaultObject→原样落 no_capability" {
        $r = Invoke-PsAction -Action "echo" -Params @{} -Contracts $script:cs
        $r.status | Should -Be "failure"
        $r.errors[0] | Should -Match "no_capability"
    }
}

Describe "Invoke-PsAction 边界" {
    It "Contracts 为 null→不抛异常返回 no_capability" {
        $r = Invoke-PsAction -Action "echo.test" -Params @{ Msg = "hi" } -Contracts $null
        $r.status | Should -Be "failure"
        $r.errors[0] | Should -Match "no_capability"
    }

    It "Action 为空→返回 no_capability" {
        $r = Invoke-PsAction -Action "" -Params @{} -Contracts $script:cs
        $r.status | Should -Be "failure"
        $r.errors[0] | Should -Match "no_capability"
    }

    It "Params 为 null→命中动作时按空参数执行（参数校验失败返回 failure）" {
        $r = Invoke-PsAction -Action "echo.test" -Params $null -Contracts $script:cs
        $r.status | Should -Be "failure"
        # Msg 必填，Params 为 null 时 param 校验失败
    }
}
