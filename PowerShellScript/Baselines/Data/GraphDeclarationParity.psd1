@{
    # ===== 图边声明完整性基线（纯数据单源；守卫 = Operation\DSL\Data\Check-GraphDeclarationParity.ps1）=====
    Schema = 'gate-baseline-v1'
    Gate   = 'GraphDeclarationParity'
    Source = 'Frame\PowerShellScript\Operation\DSL\Data\Check-GraphDeclarationParity.ps1'

    # 参与图写、必须五字段齐备的边集（声明面 = Config\nodes\reference.ushar）。
    RequiredEdges = @{
        Value   = @('node_to_node', 'Owns', 'cross_db_link', 'session_owns', 'task_owns_task', 'history_owns_task')
        Origin  = 'Derived'
        DueWhen = '新增图写边类型时同步登记；删除边类型须先迁移调用点'
    }

    # 缺项容差（0 = 任一缺项即红）。
    MissingFieldTolerance = @{
        Value   = 0
        Origin  = 'Derived'
        DueWhen = '保持 0（禁默认值兜底）'
    }
}
