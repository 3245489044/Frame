# list.dir 动作单测：经 Invoke-AgentStep 薄绑定命中 list.dir（枚举文件/子目录）
BeforeAll {
    . (Join-Path $PSScriptRoot "..\..\..\PowerShellScript\PsRuntime\Binding\Invoke-AgentStep.ps1")
}

Describe "list.dir" {
    It "命中返回 success 且列出文件与子目录" {
        $r = Invoke-AgentStep -Action "list.dir" -Params @{ Path = "PowerShellScript\BasicMethod\FileOps" }
        $r.status | Should -Be "success"
        $r.data.TotalFiles | Should -BeGreaterThan 0
        $r.data.Files | Should -Not -BeNullOrEmpty
        $r.data.TotalDirs | Should -BeGreaterOrEqual 0
        $r.data.Path | Should -Not -BeNullOrEmpty
    }

    It "递归 + 通配 Pattern 命中" {
        $r = Invoke-AgentStep -Action "list.dir" -Params @{ Path = "PowerShellScript\BasicMethod"; Recurse = $true; Pattern = "*.psm1" }
        $r.status | Should -Be "success"
        $r.data.TotalFiles | Should -BeGreaterThan 0
    }

    It "缺必填 Path 返回 failure" {
        $r = Invoke-AgentStep -Action "list.dir" -Params @{}
        $r.status | Should -Be "failure"
    }

    It "不存在的目录返回 failure 标 dir_not_found" {
        $r = Invoke-AgentStep -Action "list.dir" -Params @{ Path = "NoSuchDir_XYZ_NotFound" }
        $r.status | Should -Be "failure"
        $r.errors[0] | Should -Match "dir_not_found"
    }
}