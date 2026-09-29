@{
    # ===== AgentOrchestrationFlow 门禁基线（单源；守卫 = Modules\Node\Check-AgentOrchestrationFlow.ps1）=====
    # 迁移自脚本内联常量（2026-09-29 SR-1b：禁内联数值基线）。每条带 Origin/DueWhen。
    Schema = 'gate-baseline-v1'
    Gate   = 'AgentOrchestrationFlow'

    # 声明面基线（防声明被删而门禁恒绿）
    DeclaredFlowCount = @{ Value = 15; Origin = 'Derived'; DueWhen = '可由声明面流号数复算 ⇒ 后续改由生成器产出' }
    FnConstCount      = @{ Value = 33; Origin = 'Derived'; DueWhen = '可由 WorkflowSpecFiles 常量数复算 ⇒ 后续改由生成器产出' }
    # O-5 静默降级基线（只减不增）：ConversationCentralPipeline 的 `ctx.Spec is null) return;` 现存 0 处
    # （ERR-5 收口：4 处静默 skip 已改「诊断 + 显式 ∅」；新增即违规）
    SilentSkip        = @{ Value = 0; Origin = 'Debt'; DueWhen = '保持归零（拦存在即可）；无需生成' }
}
