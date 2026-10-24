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

    # RI2 旧标签兼容表定义点（S3：已迁入 AgentStoreCatalog.Aliases，此处归零）。
    LegacyAliasDefinition = @{
        Value   = 0
        Origin  = 'Derived'
        DueWhen = '保持 0；新增兼容别名 = 目录增一行（禁新表）'
    }

    # RI3 域注册表构造点（S3：读/写/删三面统一经 AgentStoreCatalog 单一注册表）。
    DomainRegistryConstruction = @{
        Value   = 1
        Origin  = 'Derived'
        DueWhen = '保持 1；新增注册面即判红'
    }

    # RI4 节点域单点读调用点（S3：详情刷新改经 IRecordReadPort，旧单点读调用归零）。
    NodeReadCallSites = @{
        Value   = 0
        Origin  = 'Derived'
        DueWhen = '保持 0；新增直接节点读调用即判红'
    }
}
