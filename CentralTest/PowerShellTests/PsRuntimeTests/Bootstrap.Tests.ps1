# 纯边界测试驱动：验证 BasicMethod sidecar 存在/可解析/含期望 action
BeforeAll {
    Import-Module (Join-Path $PSScriptRoot "..\Contract\ContractParser.psm1") -Force -ErrorAction Stop
    $root = Resolve-Path "$PSScriptRoot\..\..\.." -ErrorAction Stop
    $script:faCs = Get-PsContracts -ScriptPath (Join-Path $root "PowerShellScript\BasicMethod\FileOps\FileAccess.psm1")
    $script:fbCs = Get-PsContracts -ScriptPath (Join-Path $root "PowerShellScript\BasicMethod\FileOps\FileBatch.psm1")
    $script:paCs = Get-PsContracts -ScriptPath (Join-Path $root "PowerShellScript\BasicMethod\Core\Path.psm1")
}

Describe "BasicMethod 动词优先契约边界" {
    Context "FileAccess sidecar" {
        It "sidecar 存在且可解析（非空）" {
            $script:faCs | Should -Not -BeNullOrEmpty
        }
        It "含 read.file 契约" {
            ($script:faCs | Where-Object { $_.identity.action -eq "read.file" }) | Should -Not -BeNullOrEmpty
        }
        It "含 write.file 契约" {
            ($script:faCs | Where-Object { $_.identity.action -eq "write.file" }) | Should -Not -BeNullOrEmpty
        }
        It "read.file 契约 function 指向 Read-File" {
            $c = $script:faCs | Where-Object { $_.identity.action -eq "read.file" }
            $c.identity.function | Should -Be "Read-File"
        }
    }

    Context "FileBatch sidecar" {
        It "含 replace.file.batch 契约" {
            ($script:fbCs | Where-Object { $_.identity.action -eq "replace.file.batch" }) | Should -Not -BeNullOrEmpty
        }
    }

    Context "Path sidecar" {
        It "含 resolve.path 契约" {
            ($script:paCs | Where-Object { $_.identity.action -eq "resolve.path" }) | Should -Not -BeNullOrEmpty
        }
    }
}
