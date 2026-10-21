@{
    # ===== BatchScope 门禁基线（单源；守卫 = Operation\DSL\Process\Check-BatchScope.ps1）=====
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
    # v24（2026-10-11）：单指针切换至「函数流动图-编译期接入（H1–H6）」授权单（active=[H1]）。
    #   切换理由 = 用户转入实施《函数流动图-编译期接入施工单.md》：改动落在 Projects/Plug/Agent/src
    #   （Agent.Launcher/Composition 图谱工具 FlowGraphRegistry + 各域 *FlowCatalog）· Agent.Core/Engine/Nodes/Circuit
    #   （接入契约）· Frame/PowerShellScript（电路门禁扩项）· Environment/CentralTest/AgentTest/Agent.Launcher.Tests
    #   （锁定测试）· Docs/analysis/2026/10/10.11（授权单），均在新单 allowSet 内；上一系列（DB-VIEW）交付面保留不变。
    #   在途改动（CircuitFramework 测试件 5 改 1 新 · events.jsonl）按新单 preexisting 登记（不属本系列活动面）。
    #   恢复 = 回填 v23 的 Value（上方注释保留 DB-VIEW 单原值）。
    # v25（2026-10-11）：沿用同一授权单，把 F1–F3（后续修复批：电路门禁路径漂移 / Agent.Core 装配相位归位 /
    #   Cline payload 测试路径漂移）加入 active（并保留 H2–H6 在途面，避免误判其未跟踪交付物为越界）。
    #   切换理由 = 用户转入实施《2026-10-11-函数流动图-后续修复清单-施工单.md》；改动落在
    #   Frame/PowerShellScript/Operation/DSL/Agent/Check-CircuitSemantics.ps1 · Projects/Plug/Agent/src/Agent.Core/{Nodes/Loading,Properties}
    #   · Projects/Plug/Agent/src/Agent.Launcher/Composition/Root/AgentComposition.cs
    #   · Environment/CentralTest/AgentTest/Agent.Core.Tests/ClinePayloadKeysTests.cs，均在 F1–F3 allowSet 内。
    #   恢复 = 回填 v24 的 active 值并移除 F1–F3 批条目。
    # v26（2026-10-11）：单指针切换至「Agent 运行时未激活-框架级修复（B0–B6）」授权单（active=[B0..B6]）。
    #   切换理由 = 用户转入实施《2026-10-11-Agent运行时未激活-框架级修复施工单.md》：改动落在
    #   Projects/FileDriven/FileDriven.Execution（日志注册/反射单缝/钩子/启动段）· Projects/Plug/Agent/src/Agent.Launcher
    #   （ConversationViewService/AgentUiHost(Access)/AgentFrameworkHosting/AgentPhaseGate/AgentInstanceHost）·
    #   Projects/Information/DrivenConfig（AgentRuntimeUI.json）· Frame/CsharpPrj（TConfig UIConfiguration ·
    #   FrontEndDriven InitCommandGate/Executor/ScopeLifecycleAdapter/fde_log_messages）· Frame/PowerShellScript/Baselines，
    #   均在新单 allowSet 内；上一系列（函数流动图 H/F）已收口，其授权单文件内容保留不变。在途改动（History 运行时
    #   上下文系列）按新单 preexisting 登记（不属本系列活动面）。
    #   恢复 = 回填 v25 的 Value（上方注释保留函数流动图单原值）。
    # v27（2026-10-12）：单指针切换至「数据库页节点读写删重构（DB-RW）」授权单（active=[DB-RW]）。
    #   切换理由 = 用户转入实施《2026-10-12-数据库页节点读写删重构-剩余未完成逻辑清单.md》：改动落在
    #   Frame/PowerShellScript/Modules/Node/Check-DbPageGatherWiring.ps1（C11 门禁）·
    #   Frame/CsharpPrj/Operation/DataStoreDriven/Sqlite/SqliteNodeStore.cs（C10 存储批接线）·
    #   Projects/Plug/Agent/src/Agent.Launcher/**（C8 渲染器端口收口）· Frame/PowerShellScript/Baselines/BatchScope.psd1，
    #   均在新单 allowSet 内；上一系列（Agent 运行时未激活 B0–B6）已交付，其授权单文件内容保留不变。
    #   恢复 = 回填 v26 的 Value（上方注释保留运行时未激活单原值）。
    # v28（2026-10-12）：单指针切换至「会话页读写单一真源缺口（CONV-SS）」授权单（active=[CONV-SS]）。
    #   切换理由 = 用户转入实施《2026-10-12-会话页读写单一真源缺口-填补与逻辑整理.md》：改动落在
    #   Projects/Plug/Agent/src/Agent.Launcher/**（读模型端口 IConversationTreeModel + ConversationViewService 去合成 +
    #   历史/任务删除级联）· Projects/Plug/Agent/src/Agent.Orchestration/SessionWorkflow/ConversationPipeline.cs（会话枚举单源）·
    #   Frame/PowerShellScript/Operation/DSL/**（新门禁 Check-ConversationReadSingleSource + GateManifests）·
    #   Frame/PowerShellScript/Modules/Node/**，均在新单 allowSet 内；上一系列（DB-RW 数据库页节点读写删）已交付，
    #   其授权单文件内容保留不变。恢复 = 回填 v27 的 Value（上方注释保留 DB-RW 单原值）。
    # v29（2026-10-12）：单指针切换至「框架层缺陷与 Usharp 集成（G1–G6）」授权单（active=[G1]）。
    #   切换理由 = 用户转入实施《2026-10-12-框架层缺陷与Usharp集成-根因与彻底优化方案.md》：G1 改动落在
    #   Frame/Usharp/src/Usharp.Adapters.Declarations（DeclaredBundle/BundleBuilder 新增）·
    #   Frame/Usharp/src/Usharp.Semantics（编译计数）· Frame/PowerShellScript/Baselines，均在新单 allowSet 内；
    #   上一系列（CONV-SS 会话页读写单一真源缺口）已交付，其授权单文件内容保留不变。
    #   恢复 = 回填 v28 的 Value（上方注释保留 CONV-SS 单原值）。
    # v30（2026-10-13）：单指针切换至「Agent 现存红清单收口（N3 可见性 / N4 漂移门 / N5 技术债）」
    #   授权单（active=[N3,N4,N5]）。切换理由 = 用户转入实施《Agent 现存红清单与修复建议-修复进展与遗留.md》；
    #   本系列改动落在 Environment/CentralTest/**（矩阵/别名/AgentTest 用例）· Frame/PowerShellScript/**（门禁/基线）·
    #   Frame/CsharpPrj/Strategy/Observability/**（SpecPortHost）· Projects/Plug/Agent/**（doc/skills/Config/src），
    #   均在新单 allowSet 内。上一系列（G1：框架层缺陷与 Usharp 集成）已交付，其授权单文件内容保留不变。
    #   恢复 = 回填 G1 单原值（见下方注释）。
    # Manifest(G1) = @{ Value = 'Docs\analysis\2026\10\10.12\2026-10-12-框架层缺陷与Usharp集成.batches.json'; Origin = 'Derived'; DueWhen = '' }
    # v31（2026-10-13）：单指针切换至「刷新/写回调用链结构缺陷与模块化重构（RB）」授权单（active=[RB]）。
    #   切换理由 = 用户转入实施《2026-10-13-刷新写回调用链-结构缺陷与模块化重构方案.md》：改动落在
    #   Frame/CsharpPrj/Plate/HtmlViewer/**（DomOps/HtmlBackendRuntime/HtmlKeyGuard/Assets 内嵌 JS）·
    #   Frame/CsharpPrj/Operation/FrontEndDriven/**（HtmlBridgeAssembly）· Frame/CsharpPrj/Share/Composition/**
    #   （RefreshPublisher）· Projects/Plug/Agent/src/**（ConversationViewService/AgentUiLibraryConversation）·
    #   Projects/Information/DrivenConfig/**（AgentRuntimeUI.json）· Environment/CentralTest/**，均在新单 allowSet 内。
    #   上一系列（N3/N4/N5/GL 红清单收口）在途改动按新单 preexisting 登记，切换不误判。
    #   恢复 = 回填 v30 的 Value。
    # v32（2026-10-13）：现行授权单内**增批**（不改 Manifest 指针）—— 新增 GEN-SSOT 批
    #   （Agent 产物加载面 SSOT 与换代链闭合：V1 生效代指针 / V2 装载面新鲜度可见化 /
    #   V3 根换代接线 / V4c 桥接根换代）。切换理由 = 用户转入实施《Agent 产物总是加载旧的 —
    #   框架层彻底修复》：改动落在 Frame/CsharpPrj/Modules/Registry/**（ExtensionGenerationLayout 生效代指针）·
    #   Frame/PowerShellScript/Operation/DSL/Agent/Check-GenerationLayout.ps1（L5 指针判据）·
    #   Projects/BuildFramework/**（Build-AgentGeneration 写指针）· Projects/FileDriven/**（装载面/桥/端口）·
    #   Projects/PrjBridge/**（插件根换代入口）· Projects/Plug/Agent/src/**（根引导接线），
    #   均在 GEN-SSOT allowSet 内；RB 批条目与其 preexisting 保留不变。
    # Manifest(v30) = @{ Value = 'Docs\analysis\2026\10\10.13\2026-10-13-Agent红清单收口.batches.json'; Origin = 'Derived'; DueWhen = '' }
    # Manifest(v31..v32) = @{ Value = 'Docs\analysis\Agent\refactor\2026-10-13-刷新写回调用链重构.batches.json'; Origin = 'Derived'; DueWhen = '' }
    # v33（2026-10-15）：单指针切换至「Usharp ↔ C# 交汇面聚拢」授权单（active=[M1,M2,M3]）。
    #   切换理由 = 用户转入实施《2026-10-15-Usharp交汇面聚拢-施工单.md》：M1 改动落在
    #   Frame/Usharp/src（UsharpDataPorts 门面升格 + 适配层纪律）· Frame/CsharpPrj/Plate/DslMethod/Usharp（裁定注释）·
    #   Frame/CsharpPrj/Operation/DataStoreDriven/Abstractions（IDocumentCodec 注释）；M2 落在 Projects/Plug/Agent/src（15 处散点改写）；
    #   M3 落在 Frame/PowerShellScript/{Operation/DSL,Baselines}，均在新单 allowSet 内。
    #   RB/GEN-SSOT/UI-SPLIT/IC 等在途系列的非目标改动按新单 preexisting 登记，切换不误判。
    #   恢复 = 回填 RB 单原值（上方 v31..v32 行）。
    # v34（2026-10-15）：单指针切换至「Share 通用刷新管理框架 —— 收拢与实施」授权单（active=[P0,TOOL]）。
    #   切换理由 = 用户转入实施《2026-10-15-Share通用刷新管理框架-收拢与实施施工单.md》：P0 改动落在
    #   Frame/CsharpPrj/Share/Composition/AssemblyFramework/UiRefresh/** 与
    #   Environment/CentralTest/CSharpTest/Share.Tsets/AssemblyFramework.Tests/**；工具链批（Gap 2）改动落在
    #   Frame/PowerShellScript/Modules/TestRunner/**（New-DotnetTestArgs 透传 BuildGuard_GlobalSession，
    #   使 invoke-verify 内的 dotnet test 不自冲突 BG001）与 Frame/PowerShellScript/Baselines/**（本指针），
    #   均在新单 allowSet 内。Usharp 交汇面聚拢单（v33）已收口，其授权单文件内容保留不变。
    #   恢复 = 回填 v33 的 Value（下方注释行）。
    # v35（2026-10-15）：单指针不变（v34），现行授权单内**增批**（FRAME）——「通用刷新框架立项」：
    #   把 Share/Composition/AssemblyFramework/UiRefresh/** 迁入新工程 Share/Frameworks/RefreshFramework/**，
    #   UI 原语通用化（去 Ui 前缀），登记 Frame/CsharpPrj/modules.json（新增工程）+ Share.csproj 伞引用，
    #   测试从 Share.Tsets/AssemblyFramework.Tests 迁入 Share.Tsets/RefreshFramework.Tests。切换理由 = 用户转入
    #   实施《Share通用刷新管理框架-收拢与实施施工单》并显式要求新建工程（推翻原 §2.2"不新建项目"）。
    #   derived 追加 Frame/CsharpPrj/generated/**（Generate-ModulePaths 重生成派生面）。P0/TOOL 批条目不变。
    # v36（2026-10-15）：单指针不变（v34），现行授权单内**激活 P1 批**（调度收编）——「刷新框架接入 Agent」：
    #   框架侧新增 Share/Frameworks/RefreshFramework/RefreshScheduler.cs（吸收 AssemblyFramework.RefreshPublisher）
    #   + IRefreshSource.cs（统一刷新源登记）；Agent 侧 Agent.Ui/Ui/Hosting/AgentUiRefreshPublisher.cs 改为
    #   RefreshScheduler 纯装配 + RegisterSource（UI 只声明变化源，节拍/去重/重试由框架统一管理）；退役
    #   Frame/CsharpPrj/Share/Composition/AssemblyFramework/RefreshPublisher.cs。改动面 = Frame/CsharpPrj/Share/**（P1 allowSet）
    #   + Projects/Plug/Agent/src/**（P1 allowSet）+ Environment/CentralTest/CSharpTest/Share.Tsets/**（P1 allowSet）。
    #   切换理由 = 用户转入实施「把通用刷新框架接入 Agent，刷新逻辑由框架统一管理（不再每 UI 单独注册维护）」。
    # Manifest(v35)... P0/TOOL/FRAME 批条目不变。
    # v37（2026-10-15）：单指针不变（v34），现行授权单内**激活 P2 批**（写仲裁接入）——「写落地决策上移」：
    #   Plate 新增写仲裁端口 UiBackend.Abstractions.IWriteArbiter/WriteVerdict（DIP：plate 只依赖抽象）；
    #   HtmlBackendRuntime 增可选 IWriteArbiter，region/host/tree 三路经 TryAdmit（Stale/Skip 不触 DOM）；
    #   宿主侧 FrontEndDriven.HtmlHosting.RefreshWriteArbiter 适配 Strategy.Refresh.Ports.IRefreshStore，
    #   在 HtmlBridgeAssembly.Attach 注入（单一状态源 InMemoryRefreshStore）。改动面 = Frame/CsharpPrj/Plate/HtmlViewer/** ·
    #   Frame/CsharpPrj/Operation/FrontEndDriven/** · Environment/CentralTest/CSharpTest/Operation.Tests/**（均 P2 allowSet）。
    #   切换理由 = 用户转入实施 P2（把刷新框架写仲裁接入宿主 DOM 写落地）。
    # Manifest(v36)... P0/TOOL/FRAME/P1 批条目不变。
    # v38（2026-10-15）：单指针不变（v34），现行授权单内**激活 P3+P4 批**（单状态源+兜底收编 / 收尾观测）：
    #   P3 —— RefreshFramework 增 RefreshPriority + RefreshScheduler 双车道（Low 不抢占 High）+ EnableIdlePolling +
    #   PollingRefreshSource；IRefreshSource 增 Priority；HtmlBackendRuntime 增指纹注入（Func<string,string> fingerprint）；
    #   领域 ConversationViewService.Hash 与写落地共用框架 IFingerprint（Fnv1aFingerprint 单源）；Agent 兜底轮询收编为低优先级源。
    #   P4 —— HtmlUpdatePusher/HtmlBackendRuntime 增 traceFactory 注入（RefreshTraceId 单源）；Agent push 注 RefreshTraceId.New；
    #   agent_log_messages.json 补 ConversationView_RefreshTrace 定义（消除未定义 WARN 刷屏）。
    #   改动面 = Frame/CsharpPrj/Share/** · Frame/CsharpPrj/Plate/HtmlViewer/** · Frame/CsharpPrj/Operation/FrontEndDriven/** ·
    #   Projects/Plug/Agent/src/** · Environment/CentralTest/CSharpTest/Share.Tsets/**（均 P3/P4 allowSet）。
    #   切换理由 = 用户转入实施 P3+P4（单状态源/兜底收编 + 追踪与日志收尾）。
    # Manifest(v37)... P0/TOOL/FRAME/P1/P2 批条目不变。
    # Manifest(v33) = @{ Value = 'Docs\analysis\2026\10\10.15\2026-10-15-Usharp交汇面聚拢.batches.json'; Origin = 'Derived'; DueWhen = '' }
    # v39（2026-10-17）：单指针切换至「未知智能体与 Modules 分层——剩余未完项」计划授权单（active=[A1..A5,V1]）。
    #   切换理由 = 用户转入实施该规划；本系列改动落在 Projects/Plug/Agent/src（U02/U03/U05/U06/U07/U11–U13）·
    #   Projects/Vm（U01）· Projects/FileDriven（U01）· Frame/PowerShellScript（U08/U09 门禁与基线）·
    #   Environment/CentralTest（回归/验收），均在新单 allowSet 内。当前子模块在途改动（F1/E8–E12 已完成面 +
    #   nodes.json + agent 数据目录）按新单 preexisting 登记，不误判为本系列新增。
    #   恢复 IEF 系列 = 回填下一行 v38 的 Value（上方注释保留 IEF 单原值）。
    # Manifest(v38) = @{ Value = 'Docs\analysis\2026\10\10.17\2026-10-17-IEF通用框架与wait-until解锁.batches.json'; Origin = 'Derived'; DueWhen = '' }
    # v40（2026-10-17）：单指针切换至「Agent SDK 升级与零 SQLite 通用存储适配」计划授权单（active=[B0..B5]）。
    #   切换理由 = 用户转入实施该规划；本系列改动落在 Frame/TypeScriptPrj（AgentStorage/FsSink/ClineApi）·
    #   Frame/PowerShellScript（隔离门禁/基线）· Projects/Plug/Agent/src（ClineDaemonHost）·
    #   Environment/CentralTest（回归/验收）· Docs/analysis/2026/10/10.17（授权单/施工单），均在新单 allowSet 内。
    #   当前子模块在途改动（agent 数据目录残留）按新单 preexisting 登记。
    #   恢复 10.17 未知智能体系列 = 回填下一行 v39 的 Value（上方注释保留）。
    # Manifest(v39) = @{ Value = 'Docs\analysis\2026\10\10.17\2026-10-17-未知智能体剩余项.batches.json'; Origin = 'Derived'; DueWhen = '' }
    # v41（2026-10-19）：单指针切换至「History 分发运行失败实施步骤（A1/B1/C1/D1）」授权单。
    #   切换理由 = 用户转入实施《2026-10-19-History分发运行失败-实施步骤.md》，一次改完全部四批；本系列改动落在
    #   Projects/Plug/Agent/src（契约表/注册表/handler/UI 转发/装配）· Projects/FileDriven（执行结果契约/校验器）·
    #   Frame/PowerShellScript（IntentContract 门禁+基线）· Frame/CsharpPrj（FrontEndDriven 栅栏 / BackEndDriven 命令面 /
    #   TConfig 调度抽象）· Projects/Information/DrivenConfig/Scripts/Agent（脚本迁移）· Environment/CentralTest（用例），
    #   均在本单四批 allowSet 并集内。上一系列（分发构建单次化 D1）已交付，其授权单文件内容保留不变。
    #   恢复 = 回填 v40 的 Value（下一行注释保留 分发构建单次化 单原值）。
    # v42（2026-10-19）：单指针切换至「框架重构后续未完成项（R-01..R-12）」授权单。切换理由 = 用户转入实施
    #   2026-10-19-框架重构-后续未完成项-施工清单.md；本系列改动落在 Docs/analysis/2026/10/10.19/** ·
    #   Projects/Plug/Agent/{src,Config,skills,PowerShellScript}/** · Frame/PowerShellScript/** ·
    #   Frame/CsharpPrj/Operation/**（INodeOutputStore 有界查询/条件写面）· Environment/CentralTest/**，
    #   均在新单 allowSet 内；F1–F7 已完成面按新单 preexisting 登记，不误判为本系列新增。
    #   恢复 History 分发系列 = 回填下一行 v41 的 Value（上方注释保留）。
    # Manifest(v41) = @{ Value = 'Docs\analysis\2026\10\10.19\2026-10-19-History分发运行失败-实施步骤.batches.json'; Origin = 'Derived'; DueWhen = '' }
    # v43（2026-10-19）：单指针切换至「信号声明式框架（Usharp 唯一声明面 + 薄执行）」授权单（active=[B1..B7]）。
    #   切换理由 = 用户转入实施《2026-10-19-信号声明式框架-实施步骤.md》；本系列改动落在
    #   Projects/Plug/Agent/{Config,src}/** · Frame/PowerShellScript/** · Environment/CentralTest/** ·
    #   Docs/analysis/2026/10/10.19/**，均在新单 allowSet 内。上一系列（框架重构后续未完成项）授权单内容保留不变。
    #   恢复 = 回填下一行 v42 的 Value（上方注释保留 框架重构后续未完成项 单原值）。
    # Manifest(v42) = @{ Value = 'Docs\analysis\2026\10\10.19\2026-10-19-框架重构-后续未完成项.batches.json'; Origin = 'Derived'; DueWhen = '' }
    # v44（2026-10-20）：单指针切换至「多智能体缓存几何 · 生效面缺口（F0–F7）」授权单。
    #   切换理由 = 用户转入实施该施工单（framework-first：内核 → 适配 → 生效面）；本系列改动落在
    #   Frame/CsharpPrj/Share/Frameworks/ContextGeometry（新内核）· Frame/CsharpPrj/{modules.json, Share/Share.csproj}
    #   · Frame/CsharpPrj/Plate/HttpMethod（出站 usage 解析）· Projects/BuildFramework（烘焙清单）
    #   · Projects/Plug/Agent/{src,Config}（适配/装配/契约）· Data/Agent/Config（声明镜像）
    #   · Frame/PowerShellScript（门禁+基线）· Environment/CentralTest（内核单测）
    #   · Docs/analysis/2026/10/10.20（授权单/施工单），均在新单 allowSet 内。
    #   上一系列（信号声明式框架）授权单内容保留不变。
    #   恢复 = 回填下方注释 v43 的 Value。
    # Manifest(v43) = @{ Value = 'Docs\analysis\2026\10\10.19\2026-10-19-信号声明式框架.batches.json'; Origin = 'Derived'; DueWhen = '' }
    # v45（2026-10-20）：单指针切换至「多智能体缓存几何 · 剩余缺口（H0–H7，framework-first）」授权单。
    #   切换理由 = 用户转入实施《2026-10-20-多智能体缓存几何-剩余缺口-通用框架施工单.md》；本系列改动落在
    #   Frame/CsharpPrj/Share/Frameworks/ContextGeometry（内核扩展）· Projects/Plug/Agent/src
    #   （契约几何端口 / 适配器 / 主循环工具面 / 委托入口 / 遥测 / 投影）· Projects/Plug/Agent/Config 与
    #   Data/Agent/Config（context-geometry.ushar 双写）· Environment/CentralTest（内核单测）·
    #   Docs/analysis/2026/10/10.20（授权单/施工单），均在新单 allowSet 内。
    #   F 系列在途改动按新单 preexisting 登记，不误判为本系列新增。
    #   恢复 = 回填下方注释 v44 的 Value。
    # Manifest(v44) = @{ Value = 'Docs\analysis\2026\10\10.20\2026-10-20-多智能体缓存几何-生效面缺口.batches.json'; Origin = 'Derived'; DueWhen = '' }
    # v46（2026-10-20）：单指针切换至「多智能体缓存几何 · 剩余逻辑收敛（K0/K0.5/K1–K7）」授权单。
    #   切换理由 = 用户转入实施《2026-10-20-多智能体缓存几何-剩余逻辑-收敛施工单.md》；全序列一次登记、active 逐批推进。
    #   本系列改动落在 Frame/CsharpPrj/Share/Frameworks/ContextGeometry（内核扩展）·
    #   Projects/Plug/Agent/src（Host 聚拢点 / 契约端口 / 委托投递 / 子智能体前缀 / 遥测 / 投影）·
    #   Projects/Plug/Agent/{Config,PowerShellScript} · Data/Agent/Config（声明双写）·
    #   Frame/PowerShellScript（边界/聚拢点门禁 + 基线）· Environment/CentralTest（用例），均在新单 allowSet 内。
    #   H0–H7 已完成面与其它系列在途改动按新单 preexisting 登记，不误判为本系列新增。
    #   恢复 = 回填下方注释 v45 的 Value。
    # Manifest(v45) = @{ Value = 'Docs\analysis\2026\10\10.20\2026-10-20-多智能体缓存几何-剩余缺口-H.batches.json'; Origin = 'Derived'; DueWhen = '' }
    # v47（2026-10-20）：单指针切换至「Data 数据根逻辑副本消除（D1/D2/D3/D5）」授权单。
    #   切换理由 = 用户转入实施《2026-10-20-Data数据根不应承载逻辑副本-设计与消除方案.md》；本系列改动落在
    #   Projects/Plug/Agent/src/Agent.Axioms/Infrastructure（候选链/解析器）· Agent.Ui/.../Configuration（回写落点）·
    #   Projects/BuildFramework/DataRoot（停播种）· Agent.Core/Diagnostics（可见化键）· Environment/CentralTest（单测）·
    #   Frame/PowerShellScript/Baselines（本指针）· Docs/**，均在新单 allowSet 内。
    #   K 系列与 HierarchyMethod 系列在途改动按新单 preexisting 登记，不误判为本系列新增。
    #   恢复 = 回填下方注释 v46 的 Value。
    # Manifest(v46) = @{ Value = 'Docs\analysis\2026\10\10.20\2026-10-20-多智能体缓存几何-剩余逻辑-K.batches.json'; Origin = 'Derived'; DueWhen = '' }
    # v46（2026-10-20）：单指针切换至「多智能体缓存几何 · 剩余逻辑收敛（K0/K0.5/K1–K7）」授权单。
    #   切换理由 = 用户转入实施该施工单；全序列一次登记、一次性施工。
    #   本系列改动落在 Frame/CsharpPrj/Share/Frameworks/ContextGeometry（内核扩展）·
    #   Projects/Plug/Agent/src（Host 聚拢点 / 契约端口 / 委托投递 / 子智能体前缀 / 遥测 / 投影）·
    #   Projects/Plug/Agent/{Config,PowerShellScript} · Data/Agent/Config（声明双写）·
    #   Frame/PowerShellScript（边界/聚拢点门禁 + 基线）· Environment/CentralTest（用例）。
    #   并发在途（Data 数据根逻辑副本消除系列）按本单 preexisting 登记，不误判为本系列新增。
    #   恢复 = 回填下方注释 v45 的 Value。
    # Manifest(v45) = @{ Value = 'Docs\analysis\2026\10\10.20\2026-10-20-多智能体缓存几何-剩余缺口-H.batches.json'; Origin = 'Derived'; DueWhen = '' }
    # v48（2026-10-20）：单指针切换至「多智能体缓存几何 · 设计闭环剩余步骤（R0–R8）」授权单。
    #   切换理由 = 用户转入实施该施工单（大步快改：R0–R8 代码一次落地）；本系列改动落在
    #   Projects/Plug/Agent/src（内核/契约/子智能体/委托/适配/装配）· Projects/Plug/Agent/Config
    #   · Frame/CsharpPrj/Share/Frameworks/ContextGeometry · Frame/PowerShellScript · Environment/CentralTest
    #   · Docs/analysis/2026/10/10.20，均在新单 allowSet 内。v47 实况：注释已写但指针未切换（Data 系列在途），
    #   其改动按新单 preexisting 登记，不误判为本系列新增。
    #   恢复 = 回填下方注释 v47/v46 的 Value。
    # Manifest(v47) = @{ Value = 'Docs\analysis\2026\10\10.20\2026-10-20-Data数据根逻辑副本消除.batches.json'; Origin = 'Derived'; DueWhen = '' }
    # Manifest(v46) = @{ Value = 'Docs\analysis\2026\10\10.20\2026-10-20-多智能体缓存几何-剩余逻辑-K.batches.json'; Origin = 'Derived'; DueWhen = '' }
    # v49（2026-10-20）：单指针切换至「LLM 创建任务 → 子智能体路由（T0–T5）」授权单。
    #   切换理由 = 用户转入实施《2026-10-20-LLM创建任务与子智能体路由-设计补齐方案.md》；本系列改动落在
    #   Frame/CsharpPrj/Operation/HierarchyMethod/Nodes/TaskGraph（路由端口/写图契约）· Projects/Plug/Agent/src
    #   （Agent.Core 路由实现/适配器/执行器解析 · Agent.Modules.Response 决策载荷 · Agent.Launcher 组合根 ·
    #   Agent.McpRegistryHost 工具面 · Agent.Ui 入队派生）· Projects/Plug/Agent/{Config,skills} ·
    #   Environment/CentralTest（单测）· Docs/analysis/2026/10/10.20（授权单/方案），均在新单 allowSet 内。
    #   R0–R8 已完成面与其它系列在途改动按新单 preexisting 登记，不误判为本系列新增。
    #   恢复 = 回填下方注释 v48 的 Value。
    # Manifest(v48) = @{ Value = 'Docs\analysis\2026\10\10.20\2026-10-20-多智能体缓存几何-设计闭环-R.batches.json'; Origin = 'Derived'; DueWhen = '' }
    # v50（2026-10-20）：单指针切换至「通用框架提取 · 调用链收敛（U1–U4）」授权单。
    #   切换理由 = 用户转入实施《2026-10-20-通用框架提取-调用链收敛分析.md》；本系列改动落在
    #   Frame/CsharpPrj/Share（JsonPayload / TaskStatusPool / AssemblyFramework）· Projects/BuildFramework/Graph/props ·
    #   Projects/Plug/Agent/src（Contract/Core/Ui/Launcher）· Environment/CentralTest（单测）· Docs/analysis/2026/10/10.20，均在新单 allowSet 内。
    #   上一系列（LLM 创建任务/子智能体路由）在途改动按新单 preexisting 登记，不误判为本系列新增。
    #   恢复 = 回填下方注释 v49 的 Value。
    # Manifest(v49) = @{ Value = 'Docs\analysis\2026\10\10.20\2026-10-20-LLM创建任务与子智能体路由.batches.json'; Origin = 'Derived'; DueWhen = '' }
    # v51（2026-10-20）：单指针切换至「子智能体工作流派发 · 遗留与链路缺陷（P0-1/P0-2/P1/P2）」授权单。
    #   切换理由 = 用户转入实施该缺陷清单（大步快改：P0 静默清零 + 委派闭环 + 双真源/校验 + 清理一次落地）；
    #   本系列改动落在 Projects/Plug/Agent/{src,Config} · Frame/PowerShellScript/{BasicMethod,Modules/PsRuntime}
    #   · Environment/CentralTest/AgentTest · Docs/analysis/2026/10/10.20，均在新单 allowSet 内。
    #   Usharp DeclaredBundle 在途改动（Frame 4 件）按新单 preexisting 登记，不误判为本系列新增。
    #   恢复 = 回填下方注释 v50 的 Value。
    # Manifest(v50) = @{ Value = 'Docs\analysis\2026\10\10.20\2026-10-20-通用框架提取-调用链收敛.batches.json'; Origin = 'Derived'; DueWhen = '' }
    # Manifest(v51) = @{ Value = 'Docs\analysis\2026\10\10.20\2026-10-20-子智能体工作流派发.batches.json'; Origin = 'Derived'; DueWhen = '' }
    # v52（2026-10-20）：单指针切换至「子智能体工作流派发 · 遗留收敛（R1–R6）」授权单。
    #   切换理由 = 用户要求「大步快改，一次改完」遗留项（E13 清零 / D10 / D13 / A8 / A11 / T1·T3·T4·T7）；
    #   本系列改动落在 Frame/Usharp/src · Frame/PowerShellScript（Agent 门禁 + BasicMethod + Baselines）
    #   · Projects/Plug/Agent/{src,Config} · Environment/CentralTest · Docs/analysis/2026/10/10.20，均在新单 allowSet 内。
    #   上一系列（子智能体工作流派发）改动按新单 allowSet 覆盖，不误判为越界。
    #   恢复 = 回填下方注释 v51 的 Value。
    # Manifest(v52) = @{ Value = 'Docs\analysis\2026\10\10.20\2026-10-20-子智能体工作流派发-遗留收敛.batches.json'; Origin = 'Derived'; DueWhen = '' }
    # v53（2026-10-20）：单指针切换至「子智能体工作流派发 · 调用链重复/冗余/错误收敛（X1–X4）」授权单。
    #   切换理由 = 用户要求大步快改、一次改完盘点项（正确性/单源/结构/契约）；本系列改动落在
    #   Projects/Plug/Agent/{src,Config} · Frame/PowerShellScript/{BasicMethod,Modules/PsRuntime,Baselines}
    #   · Environment/CentralTest/AgentTest · Docs/analysis/2026/10/10.20，均在新单 allowSet 内。
    #   上一系列在途改动（TaskAwaitAdapter 回写诚实性、两个未跟踪测试文件）按新单 preexisting 登记，不误判为越界。
    #   恢复 = 回填下方注释 v52 的 Value。
    # v54（2026-10-21）：单指针切换至「执行体即单次调用（Actor-as-Invocation）· 节点化施工（A0–A5）」授权单。
    #   切换理由 = 用户要求大步快改一次改完（调用实例驱动 / 行=调用 / 声明只作目录 / 结果通道事实 / 执行体面零控制）；
    #   本系列改动落在 Projects/Plug/Agent/src（Agent.Session/Agent.Ui/Agent.Modules/Agent.Launcher）
    #   · Frame/PowerShellScript/Operation/DSL/{Agent,Registry} · Environment/CentralTest/AgentTest · Docs/analysis/2026/10/10.21，
    #   均在新单 allowSet 内。上一系列（X1–X5）在途 K-1~K-4 改动按新单 preexisting 登记，不误判为本系列新增。
    #   恢复 = 回填下方注释 v53 的 Value。
    # Manifest(v53) = @{ Value = 'Docs\analysis\2026\10\10.20\2026-10-20-子智能体工作流派发-调用链收敛.batches.json'; Origin = 'Derived'; DueWhen = '' }
    # v55（2026-10-21）：单指针切换至「执行体观测 · 分散/重复/冗余/错误逻辑收敛（B1–B6）」授权单。
    #   切换理由 = 用户要求大步快改、一次改完分析文档全部项（登记单点 / 提示词键 / 门禁真实性 /
    #   调用与结果语义 / 账本与冗余）；本系列改动落在 Projects/Plug/Agent/src（Session/Engine/Modules/Ui）
    #   · Frame/PowerShellScript/Operation/DSL/Agent · Environment/CentralTest/AgentTest/Agent.Session.Tests
    #   · Docs/analysis/2026/10/10.21，均在新单 allowSet 内。上一系列（执行体即单次调用）在途改动按新单 preexisting 登记。
    #   恢复 = 回填下方注释 v54 的 Value。
    # Manifest(v54) = @{ Value = 'Docs\analysis\2026\10\10.21\2026-10-21-执行体即单次调用.batches.json'; Origin = 'Derived'; DueWhen = '' }
    Manifest = @{ Value = 'Docs\analysis\2026\10\10.21\2026-10-21-框架层设计缺陷-治理.batches.json'; Origin = 'Derived'; DueWhen = '' }

    # 正对照下限：活动批的允许面模式数须 ≥ 此值（防授权单被掏空 ⇒ 判据无标的假绿）。
    MinAllowPatterns = @{ Value = 3; Origin = 'Derived'; DueWhen = '' }
}

