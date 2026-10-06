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
    Manifest = @{ Value = 'Docs\analysis\2026\10\10.6\2026-10-06-节点域接入聚拢点.batches.json'; Origin = 'Derived'; DueWhen = '' }

    # 正对照下限：活动批的允许面模式数须 ≥ 此值（防授权单被掏空 ⇒ 判据无标的假绿）。
    MinAllowPatterns = @{ Value = 3; Origin = 'Derived'; DueWhen = '' }
}
