# 纯边界测试驱动：验证 init 加载器与通用扫描器的边缘兜底
BeforeAll {
    . (Join-Path $PSScriptRoot "..\_init.ps1")
    $script:root = Resolve-Path "$PSScriptRoot\..\..\.." -ErrorAction Stop
}

Describe "PsRuntime init" {
    It "init 后核心函数可用" {
        (Get-Command Invoke-PsAction -ErrorAction SilentlyContinue) | Should -Not -BeNullOrEmpty
        (Get-Command New-PsResult -ErrorAction SilentlyContinue) | Should -Not -BeNullOrEmpty
        (Get-Command Register-PsContracts -ErrorAction SilentlyContinue) | Should -Not -BeNullOrEmpty
        (Get-Command Invoke-PsContract -ErrorAction SilentlyContinue) | Should -Not -BeNullOrEmpty
        (Get-Command Normalize-PsAction -ErrorAction SilentlyContinue) | Should -Not -BeNullOrEmpty
    }

    It "Register-PsContracts 扫描 BasicMethod 返回契约数 >0" {
        $r = Register-PsContracts -ProjectRoot $script:root -ContractDirs @("PowerShellScript\BasicMethod")
        $r.status | Should -Be "success"
        $r.data.count | Should -BeGreaterThan 0
    }

    It "扫描结果含动词优先动作 read.file" {
        $r = Register-PsContracts -ProjectRoot $script:root -ContractDirs @("PowerShellScript\BasicMethod")
        ($r.data.contracts | Where-Object { $_.identity.action -eq "read.file" }) | Should -Not -BeNullOrEmpty
    }
}

Describe "Register-PsContracts 边界" {
    It "ContractDirs 为空数组返回 success 且 count=0" {
        $r = Register-PsContracts -ProjectRoot $script:root -ContractDirs @()
        $r.status | Should -Be "success"
        $r.data.count | Should -Be 0
    }

    It "ContractDirs 含不存在的目录时跳过不报错" {
        $r = Register-PsContracts -ProjectRoot $script:root -ContractDirs @("Nonexistent\Dir")
        $r.status | Should -Be "success"
        $r.data.count | Should -Be 0
    }

    It "ProjectRoot 为空时不抛异常返回 failure" {
        $r = Register-PsContracts -ProjectRoot "" -ContractDirs @("PowerShellScript\BasicMethod")
        $r.status | Should -Be "failure"
    }
}
