@{
    # ===== BatchScope 门禁基线（单源；守卫 = Operation\DSL\Check-BatchScope.ps1）=====
    # 语义（过程层缺陷 P1/P2/P6）：批次授权单是「本批允许改哪些文件」的唯一生产者；
    #   过程判据载体 = 实际改动文件集（git status，含子模块 Frame/Projects），与代码门禁同一公理
    #   （SEP.JudgmentCarrierStructural：判据载体 = 结构事实，非执行者叙述）。
    # 每条事实必带 Origin/DueWhen（四分事实见 Baselines\README.md）。
    Schema = 'gate-baseline-v1'
    Gate   = 'BatchScope'

    # 授权单路径（仓库相对）——唯一来源，禁在脚本内硬编码；缺即回退 warn（灰度）。
    # v3（2026-10-05）：本系列「Agent.Session 模块化整理（储存/转换/接口三层分离）」改动落在
    #   Projects/Plug/Agent/src/**（5 消费方）与 Environment/CentralTest/AgentTest/**，越出「气泡合并」授权面
    #   ⇒ 另立本计划授权单（方案 b：单指针切换，保留既往授权单文件内容不变）。
    Manifest = @{ Value = 'Docs\analysis\2026\10\10.5\2026-10-05-Agent.Session三层分离.batches.json'; Origin = 'Derived'; DueWhen = '' }

    # 正对照下限：活动批的允许面模式数须 ≥ 此值（防授权单被掏空 ⇒ 判据无标的假绿）。
    MinAllowPatterns = @{ Value = 3; Origin = 'Derived'; DueWhen = '' }
}
