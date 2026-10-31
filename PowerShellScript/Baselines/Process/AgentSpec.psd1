@{
    # ===== AgentSpec 门禁基线（单源；守卫 = Operation\DSL\Agent\Declaration\Check-AgentSpec.ps1）=====
    # 正对照（防「声明被删而门禁恒绿」）：agent 目录数下限；新增 agent 只增不减。
    Schema = 'gate-baseline-v1'
    Gate   = 'AgentSpec'

    MinAgents = @{ Value = 1; Origin = 'Derived'; DueWhen = 'agent 数由 Config\agents\* 机械复算；接入第二个智能体时上调' }
}
