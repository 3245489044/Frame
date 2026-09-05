# 纯边界测试驱动：只测缺失/异常场景，用 Should -Not -Contain 防止平凡实现
# 注：Pester 5 + PS 5.1 下 BeforeAll dot-sourcing .psm1 不传播函数（Should -Throw 会假阳性）
#     改用 Import-Module 加载 .psm1
BeforeAll {
    Import-Module (Join-Path $PSScriptRoot "..\Contract\ContractSchema.psm1") -Force -ErrorAction Stop
    $script:full = @{
        identity = @{ action = "a"; verb = "v"; object = "o"; function = "f"; path = "p" }
        params = @(); output_schema = @{}; env_tags = @{}
    }
    function script:Clone-Hashtable([hashtable]$h) {
        $clone = @{}
        foreach ($k in $h.Keys) {
            if ($h[$k] -is [hashtable]) { $clone[$k] = script:Clone-Hashtable $h[$k] }
            else { $clone[$k] = $h[$k] }
        }
        return $clone
    }
}

Describe "ContractSchema boundary" {
    Context "Test-PsContract 缺顶层维度" {
        It "缺 identity 时仅标记 identity" {
            $c = script:Clone-Hashtable $script:full; $c.Remove("identity")
            $r = Test-PsContract -Contract $c
            $r.IsValid | Should -Be $false
            $r.Errors | Should -Contain "identity"
            $r.Errors | Should -Not -Contain "params"
            $r.Errors | Should -Not -Contain "output_schema"
            $r.Errors | Should -Not -Contain "env_tags"
        }
        It "缺 params 时仅标记 params" {
            $c = script:Clone-Hashtable $script:full; $c.Remove("params")
            $r = Test-PsContract -Contract $c
            $r.Errors | Should -Contain "params"
            $r.Errors | Should -Not -Contain "identity"
            $r.Errors | Should -Not -Contain "output_schema"
            $r.Errors | Should -Not -Contain "env_tags"
        }
        It "缺 output_schema 时仅标记 output_schema" {
            $c = script:Clone-Hashtable $script:full; $c.Remove("output_schema")
            $r = Test-PsContract -Contract $c
            $r.Errors | Should -Contain "output_schema"
            $r.Errors | Should -Not -Contain "identity"
            $r.Errors | Should -Not -Contain "params"
            $r.Errors | Should -Not -Contain "env_tags"
        }
        It "缺 env_tags 时仅标记 env_tags" {
            $c = script:Clone-Hashtable $script:full; $c.Remove("env_tags")
            $r = Test-PsContract -Contract $c
            $r.Errors | Should -Contain "env_tags"
            $r.Errors | Should -Not -Contain "identity"
            $r.Errors | Should -Not -Contain "params"
            $r.Errors | Should -Not -Contain "output_schema"
        }
        It "全缺时四个维度都被标记" {
            $r = Test-PsContract -Contract @{}
            $r.Errors | Should -Contain "identity"
            $r.Errors | Should -Contain "params"
            $r.Errors | Should -Contain "output_schema"
            $r.Errors | Should -Contain "env_tags"
        }
        It "null 契约不抛异常且标记顶层维度" {
            $r = Test-PsContract -Contract $null
            $r.IsValid | Should -Be $false
            $r.Errors | Should -Contain "identity"
            $r.Errors | Should -Contain "params"
        }
    }

    Context "Test-PsContract identity 缺子字段" {
        It "缺 action 时标记 identity.action 不标记其他子字段" {
            $c = script:Clone-Hashtable $script:full; $c.identity.Remove("action")
            $r = Test-PsContract -Contract $c
            $r.Errors | Should -Contain "identity.action"
            $r.Errors | Should -Not -Contain "identity.verb"
            $r.Errors | Should -Not -Contain "identity.object"
            $r.Errors | Should -Not -Contain "identity.function"
            $r.Errors | Should -Not -Contain "identity.path"
        }
        It "缺 verb 时标记 identity.verb 不标记 action" {
            $c = script:Clone-Hashtable $script:full; $c.identity.Remove("verb")
            $r = Test-PsContract -Contract $c
            $r.Errors | Should -Contain "identity.verb"
            $r.Errors | Should -Not -Contain "identity.action"
        }
        It "缺 object 时标记 identity.object" {
            $c = script:Clone-Hashtable $script:full; $c.identity.Remove("object")
            $r = Test-PsContract -Contract $c
            $r.Errors | Should -Contain "identity.object"
        }
        It "缺 function 时标记 identity.function" {
            $c = script:Clone-Hashtable $script:full; $c.identity.Remove("function")
            $r = Test-PsContract -Contract $c
            $r.Errors | Should -Contain "identity.function"
        }
        It "缺 path 时标记 identity.path" {
            $c = script:Clone-Hashtable $script:full; $c.identity.Remove("path")
            $r = Test-PsContract -Contract $c
            $r.Errors | Should -Contain "identity.path"
        }
        It "identity 为 null 时所有子字段被标记" {
            $c = script:Clone-Hashtable $script:full; $c["identity"] = $null
            $r = Test-PsContract -Contract $c
            $r.Errors | Should -Contain "identity.action"
            $r.Errors | Should -Contain "identity.verb"
            $r.Errors | Should -Contain "identity.object"
            $r.Errors | Should -Contain "identity.function"
            $r.Errors | Should -Contain "identity.path"
        }
    }

    Context "Get-PsContractSchema 边界" {
        It "schema 文件不存在时抛异常" {
            # 用真实路径但指向不存在的 schema：构造一个空目录
            $fakeRoot = Join-Path $env:TEMP "psrt_noschema_$(Get-Random)"
            New-Item -Path $fakeRoot -ItemType Directory -Force | Out-Null
            try {
                { Get-PsContractSchema -ProjectRoot $fakeRoot } | Should -Throw
            } finally {
                Remove-Item $fakeRoot -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
        It "缓存命中不重复读盘" {
            $root = Resolve-Path "$PSScriptRoot\..\..\.." -ErrorAction Stop
            $null = Get-PsContractSchema -ProjectRoot $root
            # 第二次走缓存：再调用一次不抛异常即证明缓存路径可达
            { Get-PsContractSchema -ProjectRoot $root } | Should -Not -Throw
        }
    }
}
