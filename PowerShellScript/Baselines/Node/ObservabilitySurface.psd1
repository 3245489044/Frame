@{
    # ===== ObservabilitySurface 门禁基线（单源；守卫 = Modules\Node\Check-ObservabilitySurface.ps1）=====
    # F-P0-1d 后基线 12 → 46（增补 Agent.Modules 的 ModuleLog 面 10 个调用点 msgId：23 键）
    Schema = 'gate-baseline-v1'
    Gate   = 'ObservabilitySurface'

    # 名册最小事件数（只增不减的棘轮；上游 F-6 判据点名的关键键见脚本内 $KeyVisible）
    EventCount = @{ Value = 46; Origin = 'Debt'; DueWhen = 'SR-3b：改由事件名册（visibility_semantics.json）生成后转 Derived' }
}
