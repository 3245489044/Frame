# 纯边界测试驱动：验证 Agent 薄绑定入口的加载与边缘兜底
BeforeAll {
    . (Join-Path $PSScriptRoot "Invoke-AgentStep.ps1")
}

Describe "AgentStep binding" {
    It "Invoke-AgentStep 加载 PsRuntime + Agent 配置并暴露入口" {
        (Get-Command Invoke-AgentStep -ErrorAction SilentlyContinue) | Should -Not -BeNullOrEmpty
    }

    It "Invoke-AgentStep read.file 命中返回 success" {
        $r = Invoke-AgentStep -Action "read.file" -Params @{ Path = "$PSScriptRoot\Invoke-AgentStep.Tests.ps1" }
        $r.status | Should -Be "success"
    }

    It "Invoke-AgentStep resolve.path 命中返回 success" {
        $r = Invoke-AgentStep -Action "resolve.path" -Params @{ RelativePath = "PowerShellScript\BasicMethod" }
        $r.status | Should -Be "success"
    }
}

Describe "AgentStep 边界" {
    It "未命中动作返回 failure 标 no_capability" {
        $r = Invoke-AgentStep -Action "nope.nope" -Params @{}
        $r.status | Should -Be "failure"
        $r.errors[0] | Should -Match "no_capability"
    }

    It "read.file 缺必填参数 Path 返回 failure" {
        $r = Invoke-AgentStep -Action "read.file" -Params @{}
        $r.status | Should -Be "failure"
    }

    It "Action 为空返回 failure" {
        $r = Invoke-AgentStep -Action "" -Params @{}
        $r.status | Should -Be "failure"
    }
}
