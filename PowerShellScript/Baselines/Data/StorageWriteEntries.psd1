@{
    # ===== 存储写入入口守卫基线（纯数据单源；守卫 = Operation\DSL\Data\Check-StorageWriteEntries.ps1）=====
    # 判据载体 = 调用点计数（SEP.JudgmentCarrierStructural）；只减不增。
    Schema = 'gate-baseline-v1'
    Gate   = 'StorageWriteEntries'
    Source = 'Frame\PowerShellScript\Operation\DSL\Data\Check-StorageWriteEntries.ps1'

    # SW1 图库 owner 创建点：唯一 = StorageCoreCompositionRoot（Frame）。
    GraphCoordinatorCreate = @{
        Value   = 1
        Origin  = 'Derived'
        DueWhen = '新增第二图库（如独立日志图库）须先改为多 owner 注册，再上调'
    }

    # SW2 SqliteConnectionFactory 构造点（现状含任务池/路径解析/工厂内部；只减不增）。
    ConnectionFactoryNew = @{
        Value   = 9
        Origin  = 'Derived'
        DueWhen = 'TaskPoolHosting / DatabaseBootstrapper 路径解析并入组合根后下调'
    }

    # SW3 直构 SqliteNodeStore：允许 1（DataStoreProviderFactory 内），其余必须经 bundle.Nodes。
    DirectNodeStoreNew = @{
        Value   = 1
        Origin  = 'Derived'
        DueWhen = '节点库构造进一步下沉后下调至 1（保持）'
    }

    # SW4 生产侧 UpdatePayloadAsync 调用点（Agent 侧；轮边界唯一写点 + 适配器/查询转发）。
    TaskPayloadWrite = @{
        Value   = 6
        Origin  = 'Derived'
        DueWhen = 'AgentRunner.Persistence / NodeWritePipeline 收口后下调'
    }
}
