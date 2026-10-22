@{
    # ===== NodeCollectPort 门禁基线（单源；守卫 = Modules\Node\Check-NodeCollectPort.ps1）=====
    Schema = 'gate-baseline-v1'
    Gate   = 'NodeCollectPort'

    # 正对照基线：声明面收集端口声明数 ≥ 基线（声明被删而门禁恒绿 = 失效）
    CollectDeclaration = @{ Value = 1; Origin = 'Derived'; DueWhen = '可由声明面 inports 收集端口数复算 ⇒ 后续改由生成器产出' }
}
