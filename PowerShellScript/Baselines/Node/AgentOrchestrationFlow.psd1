@{
    # ===== AgentOrchestrationFlow 门禁基线（单源；守卫 = Modules\Node\Check-AgentOrchestrationFlow.ps1）=====
    # 迁移自脚本内联常量（2026-09-29 SR-1b：禁内联数值基线）。每条带 Origin/DueWhen。
    Schema = 'gate-baseline-v1'
    Gate   = 'AgentOrchestrationFlow'

    # 声明面基线（防声明被删而门禁恒绿）
    DeclaredFlowCount = @{ Value = 23; Origin = 'Derived'; DueWhen = '可由声明面流号数复算 ⇒ 后续改由生成器产出' }
    # fn 常量数（= WorkflowFunctionNames 的 Names\*.cs 常量值集；P1 分域拆分后由目录聚合取数）
    FnConstCount      = @{ Value = 57; Origin = 'Derived'; DueWhen = '可由 fn 常量文件目录聚合复算；放宽口径只减不增须给理由' }
    # 工具面 wire 常量数（P5 迁出后的 ToolFaceNames 常量值集；防迁出后清空）
    ToolFaceWireConstCount = @{ Value = 18; Origin = 'Derived'; DueWhen = '可由 Agent.Contract\ToolFace\ToolFaceNames.cs 常量数复算' }
    # O-5 静默降级基线（只减不增）：ConversationCentralPipeline 的 `ctx.Spec is null) return;` 现存 0 处
    # （ERR-5 收口：4 处静默 skip 已改「诊断 + 显式 ∅」；新增即违规）
    SilentSkip        = @{ Value = 0; Origin = 'Debt'; DueWhen = '保持归零（拦存在即可）；无需生成' }
}
