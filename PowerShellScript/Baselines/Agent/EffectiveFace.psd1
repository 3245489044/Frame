@{
    # ===== EffectiveFace 门禁基线（单源；守卫 = Operation\DSL\Agent\Check-EffectiveFace.ps1）=====
    # 语义（B′ · 2026-10-14）：**生效面**（运行时装载面）须与**构建产物**同源（内容指纹）。
    #   对偶：ANTI-STALE-ARTIFACT-VERIFY 管「源码 ↔ 产物」；本件管「跑的面 == 刚构建的面」。
    # 每条事实必带 Origin/DueWhen（四分事实见 Baselines\README.md）。
    Schema = 'gate-baseline-v1'
    Gate   = 'EffectiveFace'

    # 正对照下限：代表件数须 ≥ 此值（防判据无标的假绿）。
    MinReps = @{ Value = 6; Origin = 'Derived'; DueWhen = '' }

    # 生效面代表件（Agent 面 + 共享核心件；与 Build-AgentGeneration.ps1 / Check-GenerationLayout.ps1 判据同名单）。
    # Agent.Modules.dll 单列：模块层承载轮末沉淀/物化写门，缺它则「已改码未生效」无法被机械判出（R-3）。
    Members = @{ Origin = 'Derived'; DueWhen = ''; Value = @(
        'Agent.Launcher.dll',
        'Agent.Modules.dll',
        'Share.HostAssembly.dll',
        'Share.Conversation.dll',
        'Plate.HtmlViewer.dll',
        'WorkFlowMethod.dll'
    ) }
}
