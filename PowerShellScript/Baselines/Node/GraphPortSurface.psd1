@{
    # ===== GraphPortSurface 门禁基线（单源；守卫 = Operation\DSL\Node\Check-GraphPortSurface.ps1）=====
    Schema = 'gate-baseline-v1'
    Gate   = 'GraphPortSurface'

    # 变更分类（PS4）：端口面为二进制契约面，命名空间迁移 = Breaking。
    ChangeClass = @{ Value = 'Breaking'; Origin = 'Derived' }

    # 迁移说明（PS4：Breaking 必填；指向可复算的迁移映射）。
    MigrationNote = @{
        Value = '类型仅迁移命名空间（UniversalWorkflowFramework.Relations → .Kernel/.Derivation/.Gather/.Gather.Adapters）；类型名与签名不变；端口命名空间只保留契约；DTO 辅助成员迁至 Relations.Kernel 语义助手（IsRemove()/Probes()/Targets()/ToGuard()/Rejected()/Empty()/RelationPlans.Empty）'
        Origin = 'Derived'
    }

    # 端口子命名空间白名单（PS6）；新增实现层须先在此登记。
    AllowedNamespaces = @('Kernel', 'Derivation', 'Gather', 'Gather.Adapters', 'Mounting')
}
