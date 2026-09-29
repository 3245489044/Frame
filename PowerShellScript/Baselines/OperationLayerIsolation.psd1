@{
    # ===== OperationLayerIsolation 门禁基线（单源；守卫 = Operation\DSL\Check-OperationLayerIsolation.ps1）=====
    Schema = 'gate-baseline-v1'
    Gate   = 'OperationLayerIsolation'

    # 正对照/债务基线（防解析面失效 ⇒ 棘轮恒真）
    OperationProject = @{ Value = 11;  Origin = 'Derived'; DueWhen = '可由 modules.json 的 Operation 工程数复算 ⇒ 后续改由生成器产出' }
    ProjMap          = @{ Value = 100; Origin = 'Derived'; DueWhen = '可由 ProjectReference 解析数复算 ⇒ 后续改由生成器产出' }
    # 悬空同层引用债务基线（只减不增）：4 条 = 孤儿工程 FunctionExecutionFramework.csproj 的 4 条失效相对路径
    # （其源码已被 WorkFlowMethod 默认 glob 收编；仓内 Node 文档已登记该既有噪声）
    DanglingDebt     = @{ Value = 4;   Origin = 'Debt'; DueWhen = '孤儿工程清理后 → 0（销账后本行删除）' }
}
