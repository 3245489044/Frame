@{
    # ===== NodeDeclSingleSource 门禁基线（单源；守卫 = Modules\Node\Check-NodeDeclSingleSource.ps1）=====
    Schema = 'gate-baseline-v1'
    Gate   = 'NodeDeclSingleSource'

    # 正对照基线：FrameBinaryRefs.props 的 UsharpBinaryRefs 桥注册行数 ≥ 基线（注册被删而门禁恒绿 = 失效）
    AdapterRegistration = @{ Value = 1; Origin = 'Derived'; DueWhen = '可由 FrameBinaryRefs.props 注册行数复算 ⇒ 后续改由生成器产出' }
}
