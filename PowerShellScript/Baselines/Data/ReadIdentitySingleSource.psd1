@{
    # ===== 行身份单源守卫基线（纯数据单源；守卫 = Operation\DSL\Data\Check-ReadIdentitySingleSource.ps1）=====
    Schema = 'gate-baseline-v1'
    Gate   = 'ReadIdentitySingleSource'
    Source = 'Frame\PowerShellScript\Operation\DSL\Data\Check-ReadIdentitySingleSource.ps1'

    # RI1 存储键→物理表映射定义点（单源 = Agent.Contract\NodeDomain\DbStoreKeys）。
    TableMapDefinition = @{
        Value   = 1
        Origin  = 'Derived'
        DueWhen = '映射迁入 AgentStoreCatalog 后改判据指向（仍必须为 1）'
    }

    # RI2 旧标签兼容表定义点（单源 = DomainRoute.LegacyLabels；迁入 catalog 后此处下调为 0）。
    LegacyAliasDefinition = @{
        Value   = 1
        Origin  = 'Derived'
        DueWhen = 'LegacyAliases 迁入 AgentStoreCatalog 后下调'
    }

    # RI3 域注册表构造点（读注册 / 写删除注册 / 写行注册）。
    DomainRegistryConstruction = @{
        Value   = 3
        Origin  = 'Derived'
        DueWhen = 'AgentStoreCatalog 合并两注册面后下调至 1'
    }

    # RI4 节点域单点读调用点（详情/编辑须走行身份端口）。
    NodeReadCallSites = @{
        Value   = 1
        Origin  = 'Derived'
        DueWhen = '保存后刷新改走行端口后下调至 0'
    }
}
