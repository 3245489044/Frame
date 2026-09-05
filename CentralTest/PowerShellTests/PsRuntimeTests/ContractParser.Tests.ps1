# 纯边界测试驱动：文件不存在/无标签/畸形 JSON/sidecar 缺失等边界
BeforeAll {
    Import-Module (Join-Path $PSScriptRoot "..\Contract\ContractParser.psm1") -Force -ErrorAction Stop
    $script:tmp = Join-Path $env:TEMP "psrt_cp_$(Get-Random)"
    New-Item -Path $script:tmp -ItemType Directory -Force | Out-Null
}
AfterAll { Remove-Item $script:tmp -Recurse -Force -ErrorAction SilentlyContinue }

Describe "ContractParser boundary" {
    Context "Get-PsScriptTags 边界" {
        It "文件不存在返回空表且不抛异常" {
            $t = Get-PsScriptTags -Path (Join-Path $script:tmp "nope.psm1")
            $t.Count | Should -Be 0
        }
        It "无标签的文件返回空表" {
            $f = Join-Path $script:tmp "notags.psm1"
            "function Foo {}" | Set-Content $f -Encoding UTF8
            $t = Get-PsScriptTags -Path $f
            $t.Count | Should -Be 0
        }
        It "畸形标签行（无冒号）被跳过" {
            $f = Join-Path $script:tmp "malformed.psm1"
            "# @ps-action no colon here`n# random comment`nfunction Foo {}" | Set-Content $f -Encoding UTF8
            $t = Get-PsScriptTags -Path $f
            $t.Count | Should -Be 0
        }
    }

    Context "Get-PsContracts 无 sidecar 边界" {
        It "无 sidecar 且无 @ps-action 标签返回空数组" {
            $f = Join-Path $script:tmp "empty.psm1"
            "function Foo {}" | Set-Content $f -Encoding UTF8
            $cs = Get-PsContracts -ScriptPath $f
            $cs.Count | Should -Be 0
        }
        It "无 sidecar 但有 @ps-action 标签时派生单契约（fallback 边界）" {
            $f = Join-Path $script:tmp "tagged.psm1"
            "# @ps-action: resolve.path`n# @verb: resolve`n# @object: path`n# @exports: Resolve-ProjectPath`nfunction Resolve-ProjectPath {}" | Set-Content $f -Encoding UTF8
            $cs = Get-PsContracts -ScriptPath $f
            $cs.Count | Should -Be 1
            $cs[0].identity.action | Should -Be "resolve.path"
        }
        It "派生契约的 verb/object 从 action 拆分" {
            $f = Join-Path $script:tmp "derived.psm1"
            "# @ps-action: read.file`nfunction Read-File {}" | Set-Content $f -Encoding UTF8
            $cs = Get-PsContracts -ScriptPath $f
            $cs[0].identity.verb | Should -Be "read"
            $cs[0].identity.object | Should -Be "file"
        }
    }

    Context "Get-PsContracts sidecar 异常" {
        It "sidecar 畸形 JSON 抛异常" {
            $f = Join-Path $script:tmp "bad.psm1"
            "function Foo {}" | Set-Content $f -Encoding UTF8
            $side = Join-Path $script:tmp "bad.contracts.json"
            "{ not valid json" | Set-Content $side -Encoding UTF8
            { Get-PsContracts -ScriptPath $f } | Should -Throw
        }
        It "sidecar contracts 为空数组返回空数组" {
            $f = Join-Path $script:tmp "emptyc.psm1"
            "function Foo {}" | Set-Content $f -Encoding UTF8
            $side = Join-Path $script:tmp "emptyc.contracts.json"
            '{ "contracts": [] }' | Set-Content $side -Encoding UTF8
            $cs = Get-PsContracts -ScriptPath $f
            $cs.Count | Should -Be 0
        }
        It "sidecar 缺 contracts 键返回空数组" {
            $f = Join-Path $script:tmp "nokey.psm1"
            "function Foo {}" | Set-Content $f -Encoding UTF8
            $side = Join-Path $script:tmp "nokey.contracts.json"
            '{ "other": 1 }' | Set-Content $side -Encoding UTF8
            $cs = Get-PsContracts -ScriptPath $f
            $cs.Count | Should -Be 0
        }
    }
}
