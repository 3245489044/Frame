@{
    # ===== UsharpJoint 门禁基线（单源；守卫 = Operation\DSL\Check-UsharpJoint.ps1）=====
    # 语义（J1–J4）：Usharp ↔ C# 交汇面聚拢 —— 消费侧经唯一门面 UsharpDataPorts / 唯一 JSON 出口 UsharpValueJson / 门面四语义覆盖。
    Schema = 'gate-baseline-v1'
    Gate   = 'UsharpJoint'

    # J1 消费侧 'using Plate.DslMethod.Usharp;' 文件数（M2 收口后：仅剩合规经门面的组合根不计入本判据 ⇒ 只减不增）
    ConsumerUsharpUsing = @{ Value = 0; Origin = 'Derived'; DueWhen = '' }
    # J2 消费侧 'Plate.DslMethod.Usharp.UsharpValueJson' 计数（唯一 JSON 出口须经门面）
    ConsumerValueJsonCalls = @{ Value = 0; Origin = 'Derived'; DueWhen = '' }
    # J3 消费侧 'new Usharp*' 内联构造（棘轮：只减不增。7 处为既有语言/适配器构造
    #    〔UsharpSourceFile / UsharpNodeSpecSource / UsharpDeclarationEditor / UsharpDecisionControlSource /
    #     UsharpFirstRuleSetLoader〕，非值模型散点；后续门面化归零）
    ConsumerNewUsharp = @{ Value = 7; Origin = 'Derived'; DueWhen = '门面化 Usharp.Adapters.*/Usharp.Syntax 内联构造后归零' }
    # J4 门面四语义覆盖类数（① 对象↔值 / ② 值↔文本 / ③ SQL / ④ 声明）
    FacadeSemanticPorts = @{ Value = 4; Origin = 'Derived'; DueWhen = '' }
}
