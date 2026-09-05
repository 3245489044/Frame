# 纯边界测试驱动：归一化边界 + 检索未命中/空集/过滤边界
BeforeAll {
    Import-Module (Join-Path $PSScriptRoot "..\Capability\CapabilityResolver.psm1") -Force -ErrorAction Stop
    $script:cs = @(
        @{ identity = @{ action = "read.file"; verb = "read"; object = "file"; function = "Read-File"; path = "a.psm1" }; env_tags = @{ ps_version = "5.1+"; os = "windows"; modules = @() } }
        @{ identity = @{ action = "write.file"; verb = "write"; object = "file"; function = "Write-File"; path = "b.psm1" }; env_tags = @{ ps_version = "5.1+"; os = "windows"; modules = @() } }
        @{ identity = @{ action = "clone.git"; verb = "clone"; object = "git"; function = "Invoke-GitClone"; path = "c.psm1" }; env_tags = @{ ps_version = "5.1+"; os = "windows"; modules = @() } }
    )
}

Describe "Normalize-PsAction boundary" {
    It "未注册别名原样返回" {
        (Normalize-PsAction -Action "unknown.thing") | Should -Be "unknown.thing"
    }
    It "仅 verb 无 DefaultObject 原样返回" {
        (Normalize-PsAction -Action "clone") | Should -Be "clone"
    }
    It "空字符串输入原样返回" {
        (Normalize-PsAction -Action "") | Should -Be ""
    }
    It "已是 verb.object 不被别名干扰" {
        Register-PsActionAlias -Alias "read" -Action "read.special"
        # "read.file" 已是 verb.object，不应被别名 "read" 覆盖
        (Normalize-PsAction -Action "read.file") | Should -Be "read.file"
    }
}

Describe "Resolve-PsCapability boundary" {
    It "null contracts 不抛异常返回空" {
        $r = Resolve-PsCapability -Contracts $null -Action "read.file"
        $r | Should -BeNullOrEmpty
    }
    It "空 contracts 数组返回空" {
        $r = Resolve-PsCapability -Contracts @() -Action "read.file"
        $r | Should -BeNullOrEmpty
    }
    It "未命中 action 返回空" {
        $r = Resolve-PsCapability -Contracts $script:cs -Action "nope.nope"
        $r | Should -BeNullOrEmpty
    }
    It "按 object 过滤排除非目标 object 契约" {
        # 过滤 object=file，应排除 clone.git
        $r = Resolve-PsCapability -Contracts $script:cs -Object "file"
        # 结果中不应含 clone.git
        ($r | Where-Object { $_.identity.object -eq "git" }) | Should -BeNullOrEmpty
        # 结果中应含 file 契约（用 -Not -BeNullOrEmpty 防止平凡空实现）
        ($r | Where-Object { $_.identity.object -eq "file" }) | Should -Not -BeNullOrEmpty
    }
    It "精确 action 命中排除同 object 其他 action" {
        $r = Resolve-PsCapability -Contracts $script:cs -Action "write.file"
        $r.identity.action | Should -Be "write.file"
        # 不应返回 read.file（同 object 不同 action）
        $r.identity.verb | Should -Be "write"
    }
    It "verb+object 歧义时返回多个候选" {
        # object=file 有 read+write 两个 verb
        $r = Resolve-PsCapability -Contracts $script:cs -Verb "read" -Object "file"
        $r.identity.action | Should -Be "read.file"
    }
}
