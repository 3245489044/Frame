@{
    # ===== WorkflowRegistration 门禁基线（单源；守卫 = Operation\DSL\Agent\Check-WorkflowRegistration.ps1）=====
    Schema = 'gate-baseline-v1'
    Gate   = 'WorkflowRegistration'

    # 正对照下限（防取数面漂移 → 三集合全空即假绿）
    MinRegistrations = @{ Value = 30; Origin = 'Derived'; DueWhen = '随登记表增长只增不减；下调须给理由' }

    # 未登记声明面豁免（有声明文件、无 [Flow] 或无登记消费者；新增须显式登记于此）
    UnregisteredExempt = @{
        Value = @('RuntimeGovernance', 'history-ops', 'transfer-key')
        Origin = 'Debt'
        DueWhen = '各自补登记或删除死声明后移除豁免'
    }
}
