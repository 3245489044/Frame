# store.resource 动作单测：经薄绑定命中 store.resource（薄垫片委托 C# 存储桥写标准资源节点；不再落独立 SQL ur_resources 表）
BeforeAll {
    . (Join-Path $PSScriptRoot "..\..\..\PowerShellScript\PsRuntime\Binding\Invoke-AgentStep.ps1")
    $script:testId = "Storage\psr_test_$(Get-Random)"
}

Describe "store.resource" {
    It "缺必填 Id 返回 failure" {
        $r = Invoke-AgentStep -Action "store.resource" -Params @{ ResourceType = "analysis"; Symbol = "analysis:x" }
        $r.status | Should -Be "failure"
    }

    It "存储桥写入资源节点返回 success 且 data.Upserted 为布尔" {
        $r = Invoke-AgentStep -Action "store.resource" -Params @{
            ResourceType = "analysis"
            Id = $script:testId
            Symbol = "analysis:$script:testId"
            Metadata = @{ dep = "A"; src = "B" }
        }
        $r.status | Should -Be "success"
        $r.data.Upserted | Should -BeOfType [bool]
        $r.data.Id | Should -Be $script:testId
    }
}