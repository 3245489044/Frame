@{
    # ===== NodeFlowDomainMatch 门禁基线（单源；守卫 = Modules\Node\Check-NodeFlowDomainMatch.ps1）=====
    Schema = 'gate-baseline-v1'
    Gate   = 'NodeFlowDomainMatch'

    # 正对照基线：U4 域对应声明面 [Node] uses=/produces= 域 token 总数 ≥ 基线（声明被删而门禁恒绿 = 失效）
    DomainTokenFloor = @{ Value = 12; Origin = 'Derived'; DueWhen = '可由 [Node] uses=/produces= token 数复算 ⇒ 后续改由生成器产出' }
}
