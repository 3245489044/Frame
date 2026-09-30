@{
    # ===== 框架激活门禁基线（纯数据单源；守卫 = Operation\DSL\Check-FrameworkActivation.ps1）=====
    # 防止 ReferenceGraphEngine 退回死代码（全仓零装配点 ⇒ 图引擎空转）。
    # 2026-09-30 B0 激活：工厂 CreateGraphEngine 为首个装配入口。
    Schema = 'gate-baseline-v1'
    Gate   = 'FrameworkActivation'
    Source = 'Frame\PowerShellScript\Operation\DSL\Check-FrameworkActivation.ps1'

    # FA1 图引擎装配点下限：`new ReferenceGraphEngine` 命中数（全仓 .cs，排除 obj/bin/BuildOutput）。
    # 装配点退役（工厂方法删除）⇒ 下调；未装配（命中 0）即红（图引擎空转）。
    GraphEngineAssemblies = @{
        Value  = 1
        Origin = 'Derived'
        DueWhen = '图引擎装配点退役后下调（须同步改判据为「经新入口装配」，不直接归零）'
    }
}
