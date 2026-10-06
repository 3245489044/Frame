@{
    # ===== BatchScope 门禁基线（单源；守卫 = Operation\DSL\Check-BatchScope.ps1）=====
    # 语义（过程层缺陷 P1/P2/P6）：批次授权单是「本批允许改哪些文件」的唯一生产者；
    #   过程判据载体 = 实际改动文件集（git status，含子模块 Frame/Projects），与代码门禁同一公理
    #   （SEP.JudgmentCarrierStructural：判据载体 = 结构事实，非执行者叙述）。
    # 每条事实必带 Origin/DueWhen（四分事实见 Baselines\README.md）。
    Schema = 'gate-baseline-v1'
    Gate   = 'BatchScope'

    # 授权单路径（仓库相对）——唯一来源，禁在脚本内硬编码；缺即回退 warn（灰度）。
    # v5（2026-10-06）：本系列「观测帧单行化与折叠渲染（D1=C 新建 observability 域 + 双写过渡 / D3=A 档渲染 /
    #   D4=载荷 seq）」改动落在 Projects/Plug/Agent/**、Projects/Information/DrivenConfig/**、
    #   Frame/PowerShellScript/** 与 Environment/CentralTest/AgentTest/**，⇒ 单指针切换至本计划授权单
    #   （保留既往授权单文件内容不变；上一系列 S1/S2 在途改动在本授权单 preexisting 登记）。
    Manifest = @{ Value = 'Docs\analysis\2026\10\10.6\2026-10-06-观测帧单行化与折叠渲染.batches.json'; Origin = 'Derived'; DueWhen = '' }

    # 正对照下限：活动批的允许面模式数须 ≥ 此值（防授权单被掏空 ⇒ 判据无标的假绿）。
    MinAllowPatterns = @{ Value = 3; Origin = 'Derived'; DueWhen = '' }
}
