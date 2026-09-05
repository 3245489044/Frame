# read.file -Persist 单测：命中后附带文件哈希，经薄绑定组合自动转 ur_resources 资源节点（file:<path>）
BeforeAll {
    . (Join-Path $PSScriptRoot "..\..\..\PowerShellScript\PsRuntime\Binding\Invoke-AgentStep.ps1")
    $script:sample = Join-Path $PSScriptRoot "sample_persist.txt"
    [System.IO.File]::WriteAllText($script:sample, "line1`nline2`nline3")
    $script:bridge = Join-Path $PSScriptRoot "..\..\..\Agent\BuildOutput\Debug\Agent.StorageBridge.exe"
}
AfterAll {
    Remove-Item -Path $script:sample -ErrorAction SilentlyContinue
}

Describe "read.file -Persist" {
    It "Persist=on 附带 Hash 且 经薄绑定组合自动转 file:<path> 节点成功" {
        $r = Invoke-AgentStep -Action "read.file" -Params @{ Path = $script:sample; Persist = "on" }
        $r.status | Should -Be "success"
        $r.data.Hash | Should -Not -BeNullOrEmpty
        $r.data._persistStored.status | Should -Be "success"
        # 经存储桥确认 file 节点已写入 ur_resources（无悬空）
        $nodeId = "file:$($r.data.Path)"
        $g = (& $script:bridge get-resource --resourceType file --id $nodeId | Out-String).Trim() | ConvertFrom-Json
        $g.data.Found | Should -Be $true
    }

    It "Persist 缺省（off）不带 Hash 与 _persistStored" {
        $r = Invoke-AgentStep -Action "read.file" -Params @{ Path = $script:sample }
        $r.status | Should -Be "success"
        $r.data.ContainsKey("Hash") | Should -Be $false
        $r.data.ContainsKey("_persistStored") | Should -Be $false
    }
}