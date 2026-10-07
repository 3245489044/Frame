@{
    # ===== BatchScope 门禁基线（单源；守卫 = Operation\DSL\Check-BatchScope.ps1）=====
    # 语义（过程层缺陷 P1/P2/P6）：批次授权单是「本批允许改哪些文件」的唯一生产者；
    #   过程判据载体 = 实际改动文件集（git status，含子模块 Frame/Projects），与代码门禁同一公理
    #   （SEP.JudgmentCarrierStructural：判据载体 = 结构事实，非执行者叙述）。
    # 每条事实必带 Origin/DueWhen（四分事实见 Baselines\README.md）。
    Schema = 'gate-baseline-v1'
    Gate   = 'BatchScope'

    # 授权单路径（仓库相对）——唯一来源，禁在脚本内硬编码；缺即回退 warn（灰度）。
    # v6（2026-10-06）：单指针切换至「process 多节点 → 单节点存储重构（Form=Stream 通用件 + 聚合单行）」
    #   计划授权单。切换理由 = 该系列改动落在 Frame/CsharpPrj/Modules/Node/**（PlacementForm.Stream /
    #   NodeFormCodec / NodeSpecValidator / NodeFrameworkSpecLoader）与 Environment/CentralTest/CSharpTest/Modules.Tests/**，
    #   均不在上一系列（观测帧单行化与折叠渲染）的活动面内；上一系列 W1–W4 已提交、无在途改动，
    #   W5/W6 未开工 ⇒ 切换不误判其改动。保留既往授权单文件内容不变。
    # v7（2026-10-06）：单指针切换至「节点域接入聚拢点（B0–R5：描述行自包含 / 宿主访问器泛型化 /
    #   依赖束字典化 / 派生面自动生成 / 门禁收口）」计划授权单。切换理由 = 该系列改动落在
    #   Projects/Plug/Agent/src（Agent.Modules/Agent.Launcher/Agent.Contract 节点面）与 Config 默认值面，
    #   均不在 process 系列活动面内；process 系列 P4/P5 按方案 §9（本系列 D-3=①）让位于 R1–R3。
    #   恢复 = 回填上一行 Value（上方注释保留 process 单原值）。
    # v8（2026-10-06）：单指针回切「process 多节点 → 单节点存储重构」计划授权单（D-3=B：沿用既有
    #   process 单，`active=["P2","P4","P5"]`）。切换理由 = 聚拢点系列 B0–R5 已收口并提交
    #   （git 干净，无在途改动 ⇒ 不会误判其改动）；本系列剩余面（读面收口 / 归档单节点 / 门禁新口径）
    #   落在 Projects/Plug/Agent/src（Gather/Context/Launcher/Modules）· Config/{nodes,instances} ·
    #   Frame/PowerShellScript（门禁+基线）· Environment/CentralTest/AgentTest，均在本单 allowSet 内。
    #   恢复聚拢点系列 = 回填 v7 的 Value（上方注释保留）。
    # v9（2026-10-06）：单指针切换至「会话域单节点化（session-view / session-message / segment-*）」
    #   计划授权单。切换理由 = 用户裁决 D-S1=必须单节点化 + 一次改完；该系列改动落在
    #   Projects/Plug/Agent/src（Contract/NodeDomain·Runtime、Modules/{Signal,Aggregate,Gather,NodeSpec,
    #   NodeCarriers,History,Productions}、Launcher）· Config/{nodes,instances} · Frame/PowerShellScript/**
    #   · Environment/CentralTest/AgentTest/**，均在新单 allowSet 内；git 干净（无在途改动）⇒ 切换不误判。
    #   恢复 = 回填 v8 的 Value（上方注释保留 process 单原值）。
    # v10（2026-10-07）：单指针切换至「AgentTest 端口层消除（S2 完成 → E2 框架能力 → S4 删面）」
    #   计划授权单（active=[E0,CHAIN,E2,MIG,S4]）。切换理由 = 本系列改动落在
    #   Environment/CentralTest/**（SpecSourceGen / Tools / AgentTest / Get-VerifyPlan）·
    #   Frame/DataCenter/** · Frame/CsharpPrj/Strategy/Observability/** · Frame/PowerShellScript/**
    #   · Docs/analysis/2026/10/10.7|10.4/**，均在新单 allowSet 内；git 干净（Frame/Projects 子模块无在途
    #   改动）⇒ 切换不误判。会话域单节点化 S-0..S-4/K 已收口，其授权单文件内容保留不变。
    #   恢复 = 回填 v9 的 Value（上方注释保留会话域单原值）。
    Manifest = @{ Value = 'Docs\analysis\2026\10\10.7\2026-10-07-AgentTest-端口层消除-E2与S4.batches.json'; Origin = 'Derived'; DueWhen = '' }

    # 正对照下限：活动批的允许面模式数须 ≥ 此值（防授权单被掏空 ⇒ 判据无标的假绿）。
    MinAllowPatterns = @{ Value = 3; Origin = 'Derived'; DueWhen = '' }
}
