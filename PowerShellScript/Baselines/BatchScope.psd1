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
    # v11（2026-10-08）：单指针切换至「Agent 动态编译就绪度与聚拢点框架重构（B0–B6 =
    #   P0 基线固化 → P1 D1 数据入实例 → P2 RF-1/RF-2 相位纯化 → P3 槽口径单源+取根收口 →
    #   P4 D3/D4 重建 → P5 D6/D7 换代端到端）」计划授权单。切换理由 = 用户转入实施该规划；
    #   本系列改动落在 Projects/Plug/Agent/** · Frame/DataCenter/** · Frame/PowerShellScript/**
    #   · Projects/PrjBridge/** · Environment/CentralTest/**，均在新单 allowSet 内。既有在途改动
    #   （10.6 观测帧/CSS 系列产品+UI+Information 共 4 件）不在任何系列活动面内，按新单 preexisting
    #   登记；10.7 端口层消除系列的 Agent/CentralTest 在途改动落在新单 B2–B6 allowSet 内，切换不误判。
    #   恢复 = 回填 v10 的 Value（上方注释保留 10.7 单原值）。
    # v12（2026-10-08）：单指针切换至「VM 框架（面向数据运行时内核）后续步骤（VM-D0 → VM-P0b →
    #   VM-P1…VM-P5）」计划授权单。切换理由 = 用户转入实施该规划（P0 已交付，后续 P0b–P5 待施工）；
    #   本系列改动落在 Projects/Vm/** · Projects/FileDriven/** · Projects/Plug/Agent/**
    #   · Frame/PowerShellScript/** · Environment/CentralTest/** · Docs/analysis/2026/10/10.8/**，
    #   均在新单 allowSet 内。既有在途改动（Agent B 系列 P0/B2-a/B2-b 产品与测试，共 17 项）
    #   不在任何 VM 批次 allowSet 内者（仅 Projects/Directory.Build.props 一项为硬缺口）连同其余
    #   在途项按新单 preexisting 登记；P0 已交付物（Vm 工程/ABI/Gate + 使用侧接线）同样经 preexisting
    #   表达（新单 $comment：P0 不单列批）。B 系列 10.8 授权单文件内容保留不变。
    #   恢复 = 回填 v11 的 Value（上方注释保留 Agent B 系列单原值）。
    # v13（2026-10-08）：VM 授权单内扩面（VM-P3 / VM-P4 增 Frame/CsharpPrj/** 与 Projects/BuildFramework/**），
    #   不动 Manifest 指针。切换理由 = VM-P4 接线方案 §5 的授权面缺口（共享边界登记 Vm / 根上移的
    #   SharedBoundaryCatalog 与 SharedBoundaryRefs.props 同步落在 Frame/CsharpPrj 与 Projects/BuildFramework，
    #   两处均不在原 allowSet 内 ⇒ BatchScope 必红）；本步只扩面不改判据口径，既有批文件内容保留不变。
    #   恢复 = 从 VM-P3 / VM-P4 的 allowSet 移除该两行。
    # v14（2026-10-08）：VM 授权单内登记 VM-P6 工作线（运行期编译就绪：VM-P6-0..d），
    #   不动 Manifest 指针。切换理由 = 用户裁决「以换代为核心语义，在 Vm 内设核心框架」；
    #   本步只增批次面（allowSet 覆盖 Projects/Vm/** · Projects/Plug/Agent/** · Projects/FileDriven/**
    #   · Environment/CentralTest/** · Frame/PowerShellScript/** ± Docs/analysis/2026/10/10.8/**），
    #   既有批文件内容保留不变。恢复 = 从 active/batches 移除 VM-P6-* 条目。
    # v15（2026-10-08）：VM 授权单内登记「热更新贯通」施工单批次（VM-P4-面①/面③ · VM-P6-d/e/f），
    #   不动 Manifest 指针。切换理由 = 施工单《2026-10-08-Vm框架-热更新贯通-剩余步骤施工单.md》§2 新增批；
    #   各批 allowSet 均落在既有 VM-P4/VM-P6-d 面内（无需扩面），既有批文件内容保留不变。
    #   恢复 = 从 active/batches 移除 VM-P4-面①/面③ 与 VM-P6-e/f、并把 VM-P6-d 状态回退 pending。
    # v16（2026-10-09）：VM 授权单内登记「换代路线收敛：构建到新目录 → 换 ALC → 重装配 → 数据全在根」
    #   施工单批次（VM-R-D0 · VM-R0 · VM-R1 · VM-R2 · VM-R2b · VM-R3 · VM-R4 · VM-R5），不动 Manifest 指针。
    #   切换理由 = 施工单《2026-10-09-Vm框架-换代路线收敛-构建到新目录-换ALC-重装配.md》§7 批次；
    #   路线 A 核心语义为「代码可换 + 数据不动」（Roslyn 运行期编译线降为可选面）。
    #   扩面（VM-R2b「重装配桥」）：档 A 需在新 ALC 内经 PrjBridge 重绑 AgentPluginPorts.Plugin + 重跑
    #   AgentComposition 四相位，落点含 Projects/PrjBridge/** 与 Projects/FileDriven/**（原 §7 allowSet 未覆盖）。
    #   恢复 = 从 active/batches 移除 VM-R* 条目。

    # v17（2026-10-09）：单指针切换至「统一节点管理框架 后续实施步骤（U1–U9）」授权单。切换理由 =
    #   用户转入实施《统一节点管理框架-后续实施步骤-施工单.md》U1（关键路径）；该系列改动落在
    #   Frame/Usharp/**（语言扩展/闸A/发射器/适配）· Frame/CsharpPrj/Plate/DslMethod/**（枚举 +Domain）
    #   · Projects/Plug/Agent/Config/nodes/**（10 域 .ushar）· Frame/PowerShellScript/**（门禁 C3）·
    #   Environment/CentralTest/UsharpTests/**（等价门夹具/用例），均在新单 allowSet 内。
    #   既有在途改动（VM 系列 Agent/DataCenter/PrjBridge/CentralTest + U0/U5/U6/U7 交付面 + 文档，共 29 项）
    #   按新单 preexisting 登记（不属本系列活动面）；git 干净度：VM 系列无在途产品改动者已提交。
    #   恢复 = 回填 v16 的 Value（上方注释保留 VM 单原值）。
    # v18（2026-10-09）：单指针切换至「引用关系 USharp 化重构（F0–F5）」授权单。切换理由 =
    #   用户转入实施《引用关系USharp化重构-已交付与后续实施.md》§3 施工单；本系列 F1 落点含
    #   Environment/CentralTest/UsharpTests/**（MSB4067 修复）· Frame/DataCenter/**（call/composedField 读取器）
    #   · Frame/PowerShellScript/**，F4 落点含 Frame/CsharpPrj/Operation/DataStoreDriven/**，均不在上一系列
    #   （U 系列）活动面 allowSet 内 ⇒ 切换不误判其改动。既有在途改动（10.9 文档 + U 系列单文件）落在
    #   本单 Docs/analysis/2026/10/10.9/** 覆盖面内。恢复 = 回填 v17 的 Value（上方注释保留 U 单原值）。
    # v19（2026-10-09）：单指针回切「统一节点管理框架 后续实施步骤（U1–U9）」授权单（回到 v17 的 Value），
    #   并把该单扩为含 U3–U9 批（active=[U5]，接线收口关键路径）。切换理由 = 用户转入实施
    #   《统一节点管理框架-U3-U9-接线收口-实施步骤.md》：U5/U3/U6/U7/U8/U9 落点落在
    #   Projects/Plug/Agent/src/**（Agent.Modules/Agent.Core/Agent.Launcher/Agent.Session）· Config/{nodes,workflows}
    #   · Frame/{Usharp,PowerShellScript} 与 Frame/CsharpPrj/Modules/Node/**；F 系列在途改动（Frame/DataCenter/**）
    #   经 preexisting 登记（不在 U 单活动面内）。既有 F1–F3 交付物保留不变。
    #   恢复 = 回填 v18 的 Value（上方注释保留 F 单原值）。
    # v20（2026-10-10）：单指针切换至「USharp 数据化 · 全表 USharp 化（B0/L2/L4/L5/L6/L7）」授权单。
    #   切换理由 = 用户转入实施《USharp数据化-全表USharp化-已交付与后续施工单.md》；本系列改动落在
    #   Frame/Usharp/**（数据层语言/适配）· Frame/CsharpPrj/Operation/DataStoreDriven/**（表模板 + 各
    #   Sqlite Store 迁移 + 组合根装配）· Frame/CsharpPrj/Plate/DslMethod/**（[Table]/[Record] 枚举）
    #   · Projects/Plug/Agent/src/**（组合根注入 codec）· Projects/Plug/Agent/Config/**（配置 JSON→.ushar）
    #   · Frame/PowerShellScript/**（门禁/基线）· Environment/CentralTest/**（codec/round-trip 用例），
    #   均在新单 allowSet 内；在途改动（L0/L1/L3 交付面 + U 系列剩余件）按新单 preexisting 登记。
    #   恢复 = 回填 v19 的 Value（上方注释保留 U 单原值）。
    # v21（2026-10-10）：沿用同一授权单，新增批 U（USharp 数据化后续收口 U-1…U-11）；active 由 L4a…L7
    #   收敛为 [U]（宽 allowSet 覆盖 Frame/** · Projects/Plug/Agent/** · Environment/CentralTest/** ·
    #   Docs/analysis/2026/10/10.10/**）。切换理由 = 用户转入实施《USharp数据化-后续未实施清单.md》；
    #   已交付批（B0/L2/L4*/L6/L7）登记 done，L5（配置 JSON→.ushar）随 U-1 推进。
    #   恢复 = 回填 v20 的 active 值与各批 status。
    # v22（2026-10-11）：单指针切换至「数据库页批量操作 UI 闭环（U1–U5）」授权单（active=[DB-UI]）。
    #   切换理由 = 用户转入实施《2026-10-11-数据库页批量操作UI闭环-施工单.md》：改动落在
    #   Projects/Plug/Agent/src（Agent.Contract 槽键 · Agent.Launcher 渲染/批量库）· Projects/Information/DrivenConfig
    #   （AgentRuntimeUI.json + Scripts/Agent）· Frame/PowerShellScript（门禁+基线），均在新单 allowSet 内；
    #   上一系列（USharp 数据化 U-1…U-11）已交付批内容保留不变（其授权单文件仍在）。
    #   恢复 = 回填 v21 的 Value（上方注释保留 USharp 单原值）。
    # v23（2026-10-11）：单指针切换至「数据库页节点显示优化与全库可修改（D1–D5）」授权单（active=[DB-VIEW]）。
    #   切换理由 = 用户转入实施《2026-10-11-数据库页节点显示优化与全库可修改-方案.md》：改动落在
    #   Projects/Plug/Agent/src（Launcher 渲染/批量/记录编辑器 · Contract 槽键）· Projects/Information/DrivenConfig
    #   （AgentRuntimeUI.json + Scripts/Agent）· Frame/PowerShellScript（门禁 G5+基线），均在新单 allowSet 内；
    #   上一系列（DB-UI 批量操作闭环）已交付，其授权单文件内容保留不变。
    #   恢复 = 回填 v22 的 Value（上方注释保留 DB-UI 单原值）。
    Manifest = @{ Value = 'Docs\analysis\2026\10\10.11\2026-10-11-数据库页节点显示优化与全库可修改.batches.json'; Origin = 'Derived'; DueWhen = '' }

    # 正对照下限：活动批的允许面模式数须 ≥ 此值（防授权单被掏空 ⇒ 判据无标的假绿）。
    MinAllowPatterns = @{ Value = 3; Origin = 'Derived'; DueWhen = '' }
}
