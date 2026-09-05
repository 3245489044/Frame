# FileDriven × Agent 插件化分离 — 实施看板（Stage A–D）

> **方案文档**：`2026-09-02-FileDriven-Agent-plugin-boundary.md`（§四 阶段 / §五 风险与边界）
> **决策（已拍板）**：FileDriven 收敛为纯插件宿主（不引用/编排/门控 Agent 装配）；Agent 以 `AgentSelfHostPlugin` 自持装配与可用性（宿主无关、自报 Readiness）；结构上消灭「MainForm 编排并门控 Agent 装配 → OnLoad 成功但 Agent 静默不可用」。
> **范围（本看板）**：A（契约）→ B（自宿主+宿主去编排）→ C（静态槽去静态化，大工程）→ D（宿主/测试闸收敛）；C 按子步独立推进。
> **红线**：FileDriven 仍只经 PrjBridge 引 Agent；契约全在 FileDriven.Abstractions。
> **执行顺序**：A → B（结构性消灭主缺陷）→ D（随 B 收口测试闸）→ C（独立大工程）。逐 sln 编译（Agent.sln / PrjBridge.csproj / FileDriven.sln）、独立 commit、每阶段可回退。
> **跨会话性质**：每阶段为独立可回退落地单元；本文档记录进度、已改动文件、每步验证结论。

---

## 阶段状态

| 阶段 | 面 | 状态 | 落地单元（子步） | 备注 |
|------|----|------|------------------|------|
| A | 契约（纯加性） | ✅ 完成 | A1-A3 契约类型 | FileDriven.Abstractions/Ports/Agent |
| B | 自宿主 + 宿主去编排 | ◐ 进行中 | B1-B5 | B1/B2/B3 落地（编译）；B4/C 合并；B5 待；B3 运行级验证待 GUI |
| C | 静态槽去静态化 | ▢ 未开始 | C1-C5 | 大工程，独立子步；依赖 B3 运行验证通过后铺开 |
| D | 宿主/测试闸收敛 | ◐ 进行中 | D1-D2 | D1 随 B3 运行验证；D2 契约测试待 CentralTest 测试上下文接入 |

> 图例：▢ 未开始 · ◐ 进行中 · ✅ 完成（含编译+测试验证） · ✋ 阻塞

---

## 阶段 A · 契约（纯加性，FileDriven.Abstractions/Ports/Agent）

> 目标判据：新增三契约类型（仅契约，不引 Agent、不接宿主/插件行为），三端编译 0 错误。

- [x] A1：`IAgentSelfHostPlugin`（Name / Readiness / 幂等 EnsureReadyAsync / InvokeCapabilityAsync）
- [x] A2：`AgentCapabilityState`（Available/Unavailable 二值 + FailedModules + Reason）
- [x] A3：`IHostUiServices`（HTML viewer / ECS sink / config path / templates-theme 宿主服务注入，晚绑定幂等）
- [x] A4：FileDriven.sln（Abstractions）编译 0 错误校验

---

## 会话日志

| 日期 | 阶段 | 动作 | 验证结论 |
|------|------|------|----------|
| 2026-09-02 | 规划 | 勘察 boundary 线与代码现状（MainForm:115-162 仍编排；IAgentUiBridge/AgentUiPorts ambient；AgentUiHost ~18 静态槽 + AgentUiLibrary*.cs 33 处直读；AgentUiComposition 现 8 字段仅 HTML/渲染） | 目标文档 A-D 未落地，无独立看板 |
| 2026-09-02 | A | 新增三契约类型（IAgentSelfHostPlugin / AgentCapabilityState / IHostUiServices）于 FileDriven.Abstractions/Ports/Agent | ✅ FileDriven.sln 0 错误 0 警告（Abstractions 与整 sln 均过） |
| 2026-09-02 | B1/B2 | AgentSelfHostPlugin（幂等 EnsureReadyAsync 自持装配自报 Readiness）+ AgentPluginPorts 发现定位器 + PrjBridgeRegistrar 注入插件 | ✅ PrjBridge.csproj 0 错误 0 警告 |
| 2026-09-02 | B3 | MainForm 去编排：宿主触发插件 EnsureReadyAsync（自装配自报）取代桥 TryBootstrapGuiAsync；零 gate；HTML 保留宿主服务注入 | ✅ FileDriven.sln 0 错误 0 警告；宿主侧无 TryBootstrapGuiAsync 编排调用者（运行验证待 GUI） |
| 2026-09-02 | B 收尾 | `AgentAvailability` 枚举缺省序修正（Unavailable=0 缺省，防 `new()` 缺省态误判 Available） | ✅ FileDriven.sln 0 错误 0 警告 |
| 2026-09-02 | B5/D2 | B5：单自宿主插件发现已由 `AgentPluginPorts.Plugin` 覆盖（多插件枚举待出现第二个自宿主插件类型再泛化）。D2：尝试在 FileDriven.UI.Testes 加 AgentCapabilityState 单测，但该测试工程 ProjectReference 相对路径在当前布局解析到不存在的 `CentralTest\FileDriven\*`（未接入任一可构建 sln），standalone 无法 restore → 回退测试文件，避免提交无法验证的产物 | D2 契约测试需在 CentralTest 测试上下文接入后补 |
| 2026-09-02 | B3 运行排障 | GUI 复跑 `MainForm_AgentNotDiscovered`（Plugin null）、无 `MainForm_HtmlHostCompose`（Bridge 亦 null），而 PrjBridge=ports_wired 且部署 PrjBridge.dll 含 AgentPluginPorts 接线 → **根因：scoped ALC 装载序类型分裂**——PrjBridge(Priority=0) 先于 FileDriven.Abstractions 入默认 ALC 被装载，ExtensionLoadContext 按 deps.json 自加载 Abstractions 副本，RegisterPorts 写入副本定位器、MainForm 读本体恒 null（亦为历史「静默半装配」ALC 层根因）。修复：PrjBridgeWiringHook 装载前强制 `Assembly.Load("FileDriven.Abstractions")`（复用默认副本）+ 重建重部署 PrjBridge | FileDriven.sln 0 错误；PrjBridge 重部署（BuildOutput\Debug 四件齐全 21:56）——运行验证待 GUI 复跑 |
| 2026-09-02 | B3 运行验证 ✅ | GUI 复跑：`MainForm_AgentReadiness`（插件被发现，取代 AgentNotDiscovered）；`AgentUi_LocalTestSet`=本地联调后端=local-http（LLM 网关已装配）；任务池入队/DB 读写/资源增删正常 → **Agent 四核心自装配达成，边界目标（宿主去编排、插件自报、无静默半装配）实现** | 判定：A-D 线的「未装配」已消除 |
| 2026-09-02 | （越界观察）| 会话发送暴露新引擎层错误 `Method not found: AxiomContext..ctor(...)`；复跑又见 `会话页渲染失败 Entry point was not found`。核实 run-dir Agent 闭包全部一致(21:59:5x)、AxiomContext.cs 自 8/12 含该构造 → 非版本过期，确认为**共享引擎/UI 程序集(WorkFlowMethod/Share.*/Plate.*/HtmlViewer) 跨 default ALC(FileDriven) 与 scoped ALC(Agent) 的类型身份分裂**（执行越过 FileDriven↔Agent 服务边界时签名不相容） | 属引擎/scoped-ALC 集成范畴，非 plugin-boundary 线 |
| 2026-09-02 | 引擎 ALC 统一 | 修复 `ExtensionLoadContext.Load`：凡默认 ALC 可解析的程序集一律复用默认副本（单一身份），仅 Agent-only/扩展专属落 scoped 自装 → 共享引擎/UI 栈不再双副本分裂 | FileDriven.sln 0 错误；run-dir 宿主依赖刷新 22:07——GUI 复跑待验 |

---

## 续推进路径（下次会话续推进依据）

> 方案权威见 `2026-09-02-FileDriven-Agent-plugin-boundary.md`；进度单一记录=本看板。

### Stage B · Agent 自宿主 + 宿主去编排（消灭「MainForm 编排 Agent 装配」）

- [x] B1：自宿主插件自持装配 —— `PrjBridge/Agent/AgentSelfHostPlugin : IAgentSelfHostPlugin`：幂等 `EnsureReadyAsync`（内部触达 `AgentFrameworkHosting.TryBootstrapGuiAsync` + 四核心对账 ThrowIfCoreIncomplete；结果缓存，仅首装配）；成败折叠为插件自报 `AgentCapabilityState`（Available/Unavailable + failedModules + reason），宿主只渲染。`EnsureReadyAsync` 返回自报 Readiness（契约自报语义）。
- [x] B2：宿主可发现注入 —— `FileDriven.Abstractions/Ports/Agent/AgentPluginPorts.cs`（发现定位器）；`PrjBridgeRegistrar.RegisterPorts` 注入 `AgentPluginPorts.Plugin`；旧 `IAgentUiBridge`/`AgentUiPorts.Bridge` 过渡期保留（存量宿主迁移后退役）。
- [x] B3：`FileDriven.Execution/Initial/MainForm.cs:115-162` 去编排 —— 宿主不再经桥调 `TryBootstrapGuiAsync`，改为触发已发现插件 `AgentPluginPorts.Plugin.EnsureReadyAsync()`（自装配自报 Readiness）+ 渲染，零 gate；HTML compose/配置/模板保留为宿主服务注入（晚绑定/幂等/自愈不变）。【编译通过；运行级验证待 GUI 复跑 `agent_mcpinvoke_create_task_test.json`】
- [ ] B4：`AgentUiLibrary*.cs` 的 `AgentUi_*` 读所属插件实例 composition 快照（契约暴露入口）【与 Stage C 合并落地】。
- [ ] B5：宿主发现/枚举衔接（PrjBridgeWiringHook / PrjBridgeRegistrar 增 GetPlugins()）。
- 判据：GUI 四核心状态=插件自报；无宿主侧 gate；红线不变；运行复跑 `agent_mcpinvoke_create_task_test.json`。

### Stage C · AgentUiHost 静态槽 → 插件实例组合（独立大工程）
- C1 扩展 `AgentUiComposition` 为全组合快照（并入 AgentFramework/Database/ReferenceGrounding/TaskPool/Conversation/History*/NodeTaskHistory/ExecutionFlag/Html/渲染/视图类型等全部产品）。
- C2 组合根写入（AgentFrameworkHosting/ConversationHosting/DataStoreHosting/各 Attach*）迁移到单例插件实例；EnsureSingleGraphOwner/单连接守卫保留实例内。
- C3 `AgentUiLibrary*.cs`（33 处 `AgentUiHost.`）改读插件快照；静态门面降级/退役（Require()/GetHtmlAssemblySnapshot 等价实例化）。
- C4 `AgentWorkflowFunctionRegistrar`/9 库注册机制以 composition 注入替代「字符串注册表 + 全局槽」双通道。
- C5 释放路径收敛插件实例（对齐 StateCenter 唯一释放），单写者约束维持。

### Stage D · 宿主/测试闸收敛
- D1 测试 harness/通用宿主以插件 Readiness 为唯一 Agent 可用性闸。
- D2 FileDriven.UI.Testes / Agent.Core.Tests 补 AgentSelfHostPlugin / Readiness / IHostUiServices / 未 Ready 路由契约测试。

### 建议下次会话起点
1. **GUI 复跑 `agent_mcpinvoke_create_task_test.json` 验证 B3**（`MainForm_AgentReadiness` 日志四核心自报 Ready/Unavailable+failedModules+reason，无回归）→ 同时落地 D1（测试 harness/宿主以插件 Readiness 为唯一闸）。
2. **Stage C**（大工程）：C1 扩展 `AgentUiComposition` 为全组合快照 → C2 组合根写入迁移 → C3 `AgentUiLibrary*.cs` 改读插件快照 → C4 注册注入 → C5 释放收敛；每子步独立 commit/Agent.sln 编译/测试回归。⚠ 需先确认 Agent.sln 无在途中间件栈脏改动搅混，且可运行 GUI 回归。
3. **D2**：待 CentralTest 测试工程接线到可构建 sln 后补 AgentCapabilityState/插件契约单测。
