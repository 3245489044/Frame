@{
    # ===== ToolFaceDeclaration 门禁基线（单源；守卫 = Operation\DSL\Check-ToolFaceDeclaration.ps1）=====
    Schema = 'gate-baseline-v1'
    Gate   = 'ToolFaceDeclaration'

    # 正对照基线：声明面 faces 数 ≥ 基线（声明被删而门禁恒绿 = 失效）
    FaceCount = @{ Value = 2; Origin = 'Derived'; DueWhen = '可由 Config\tool-face.json 的 faces 数复算 ⇒ 后续改由生成器产出' }

    # 基线：不外泄工具集（执行端 + 内部面）
    NotExposed = @{ Value = @('cline', 'answer'); Origin = 'Derived'; DueWhen = '' }
}
