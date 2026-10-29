@{
    # ===== 兜底清册（FallbackInventoryRate）=====
    # 语义：静默兜底一律登记；未登记即红（守卫 = Operation\DSL\Agent\Check-FallbackInventoryRate.ps1）。
    # 事实行形态 = @{ Value = '<File>|<Pattern>'; Origin='Debt'; DueWhen='…' }；
    #   File = Projects\Plug\Agent\src 相对路径（正斜杠）；Pattern ∈ {template-empty, empty-catch, tryload-null}。
    # 只减不增：删除兜底时同批删除本表条目（P3 无静默兜底）。
    Schema = 'fallback-inventory-v1'
    Measured = '2026-10-29'

    Entries = @(
        @{ Value = 'Agent.Core/DataStream/ReactiveTrigger.cs|empty-catch'
           Origin = 'Debt'; DueWhen = '响应式触发器取消/回填失败显式化' }
        @{ Value = 'Agent.Core/DataStream/ReactiveZoneBridge.cs|empty-catch'
           Origin = 'Debt'; DueWhen = '写端释放失败显式化' }
        @{ Value = 'Agent.Modules/Framework/Invoke/PsRuntime/PsRuntimeHost.cs|empty-catch'
           Origin = 'Debt'; DueWhen = 'PS 运行时清理失败显式化' }
        @{ Value = 'Agent.Modules/Response/Collaboration/Declarations/CollaborationCatalog.cs|tryload-null'
           Origin = 'Debt'; DueWhen = '声明缺失 ⇒ 空因可见（禁 Default 兜底）' }
    )
}
