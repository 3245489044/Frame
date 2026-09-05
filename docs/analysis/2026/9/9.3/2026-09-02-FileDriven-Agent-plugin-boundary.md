# FileDriven × Agent 插件化分离 — 装配内聚到 Agent、宿主无关化分析

> **日期**：2026-09-02
> **触发**：FileDriven GUI 复跑 `agent_mcpinvoke_create_task_test.json`，呈现四核心（任务池/会话/LLM 网关/数据库）全部"未装配"却 `success=true`——即"静默半装配态"。定位为宿主边界缺陷：**MainForm 编排并门控 Agent 装配**，与"FileDriven 是通用工作流宿主、不应知晓 Agent 内部"的边界相悖。
> **目标**：FileDriven 收敛为**纯插件宿主**（不引用 Agent、不编排 Agent 装配、不门控 Agent 内部）；Agent 作为**自装配插件**，装配/可用性/生命周期在 **Agent 内部自持自报**，与 MainForm 无关。
> **方法**：源码核实当前耦合 → 界定职责边界 → 目标插件模型 → 分阶段改造（每阶段可回退、逐 sln 编译）。

---

## 一、当前耦合（源码核实）

### 1.1 既有"隔离"手段（红线下已存在，是正确的插件宿主雏形）
- **红线**：FileDriven 不得引用 `Projects\*`。
- **PrjBridge**（唯一直接引用 Agent+Engine 的中间桥）：实现 `FileDriven.Abstractions` 端口；整体搬入 FileDriven 输出目录；经 scoped ALC（`ExtensionAssemblyHost`）**部署即发现**（`PrjBridgeWiringHook → PrjBridgeRegistrar.RegisterPorts` 反射装配 ambient 定位器）。
- **函数库发现**：`FunctionLibraryLoader` 依 `function-libraries.json` 注册 AgentUiLibrary 等 → 工作流节点可调 `AgentUi_*`。
- **ambient 端口**：`ClinePorts.Runner` / `AgentUiPorts.Bridge`（静态定位器）。

### 1.2 破坏"宿主无关"的耦合点
| 耦合 | 位置 | 问题 |
|------|------|------|
| **宿主编排 Agent 装配** | `FileDriven.Execution/Initial/MainForm.cs:115-162`：`TryBootstrapGuiAsync` → 记录（**不按 Succeeded 门控**）→ `ComposeHtmlHostAsync` → `AttachAgentUiConfigPath` | Agent 能否用 = MainForm 是否记得装配/门控；S1 的 fail-fast 只到 Agent.Launcher 侧，宿主侧吞掉 |
| **宿主门控 Agent 可用性** | MainForm 装配结果不阻断，OnLoad 照常"成功"；`AgentUi_*` 各自读静态槽返回"未装配" | "DB-only 但 OnLoad 成功、Agent 静默不可用"宿主侧残余 |
| **全局静态组合根** | `AgentUiHost` 15+ 静态槽（Database/TaskPool/Conversation/ReferenceGrounding/ConversationTemplates…）被所有 `AgentUi_*` 直接读 | 装配产物是全局可读的散槽，非某插件实例持有；任何宿主都可"看得到但没装配" |
| **HTML 宿主依赖时序** | HTML 会话视图（HtmlViewer 控件/ECS sink）是 FileDriven 侧真实控件，Agent 侧只能晚绑定/自愈 | 这是**真正的宿主服务依赖**，应作为"服务注入端口"，而非"装配编排"混入 MainForm |

### 1.3 关键事实
`AgentFramework.StartAsync`（`Agent.Launcher/Composition/AgentFramework.cs`，README §3 明示"唯一组合根（常驻，推荐）"）**已是宿主无关的自足装配入口**——四核心在 Agent 内部可一次装配，`AgentFrameworkHosting.ThrowIfCoreIncomplete` 也已 fail-fast。**Agent 自装配能力已存在，缺的是"把它作为自持插件暴露、由宿主只发现不编排"。**

---

## 二、目标边界模型

```
                    ┌──────────────── FileDriven (纯插件宿主) ────────────────┐
 FileDriven.Abstractions(契约，无 Agent 类型)
   IAgentSelfHostPlugin      // 能力插件契约
   AgentCapabilityState      // 可用性状态(插件自报，宿主不裁决)
   IHostUiServices           // 宿主服务注入(HTML viewer/ECS)——仅"服务"，非"装配编排"
                    └──────────────┬───────────────────────────────────────┘
                                   │ 发现/枚举/路由能力/渲染插件状态；不调 Agent.StartAsync
                  ┌────────────────▼────────────────┐
                  │  PrjBridge.Agent (唯一 Agent 引用载体)          │
                  │  AgentSelfHostPlugin : IAgentSelfHostPlugin      │
                  │    ├─ 自持生命周期：激活时内部调 AgentFramework.   │
                  │    │    StartAsync + ThrowIfCoreIncomplete        │
                  │    ├─ Readiness：Available + 摘要 / Unavailable   │
                  │    │    + failedModules + reason（自报，宿主只展示）│
                  │    └─ 持有 Agent 组合实例(收敛 AgentUiHost 静态槽) │
                  └────────────────┬────────────────┘
                                   │ 内部
                  ┌────────────────▼────────────────┐
                  │  Agent.* (任务池/会话/LLM/DB/HTML 装配)          │
                  │  —— 一切装配在 Agent 内部，宿主无关              │
                  └─────────────────────────────────┘
```

**原则**
- **装配归 Agent**：`AgentFramework.StartAsync` + 四核心对账只在 Agent 插件内部被触发；FileDriven 不调用、不传 `AgentConfig` 编排、不 gate。
- **可用性自报**：插件暴露 `Readiness`（Available / Unavailable + failedModules + reason）；宿主只**渲染**，不裁决成功。
- **宿主只发现 + 路由**：FileDriven 枚举已装载插件 → 展示各自状态 → 把能力调用路由给插件（经契约 `InvokeCapabilityAsync`）；`AgentUi_*` 函数读**所属插件实例的状态**，而非全局静态槽。
- **HTML 是服务注入，非装配编排**：插件需要 host 的 HtmlViewer/ECS 时经 `IHostUiServices` **晚绑定、幂等**请求（现有自愈语义保留），不再是 MainForm 引导装配的一环。


---

## 三、为什么结构性消除"静默半装配"

- FileDriven 不再声明/门控 Agent 成败 → "OnLoad 成功但 Agent 静默不可用"在宿主侧**不存在**（宿主不承担 Agent 可用性判定）。
- Agent 插件要么 `Ready`（自装配成功），要么 `Unavailable(failedModules+reason)`——二值，无降级中间态。
- 任何 `AgentUi_*` 在插件未 Ready 时，返回**插件自报的 Unavailable**（统一、显式），取代散落的"未装配"字符串。
- 测试/脚本以**插件 Readiness**为闸，而非 MainForm boot 结果。

---

## 四、分阶段改造（每阶段独立、可回退、逐 sln 编译）

### 阶段 A — 契约（纯加性，FileDriven.Abstractions）
- 新增 `IAgentSelfHostPlugin` + `AgentCapabilityState` + `IHostUiServices`（仅契约类型，不引 Agent）。
- 不接任何宿主/插件行为（死契约先立、编译 0 错误）。

### 阶段 B — Agent 自宿主 + 宿主去编排（本阶段消灭"MainForm 编排 Agent 装配"）
- `PrjBridge.Agent` 新增 `AgentSelfHostPlugin : IAgentSelfHostPlugin`：把 `AgentFrameworkHosting.TryBootstrapGuiAsync` 的自装配 + `ThrowIfCoreIncomplete` **内聚成插件自持方法**（幂等 EnsureReadyAsync，自报 Readiness）。
- `MainForm` 删除/收敛其 Agent 引导块（`TryBootstrapGuiAsync`+门控+`ComposeHtmlHostAsync`）为通用"激活已发现插件 + 渲染 Readiness"；`ComposeHtmlHostAsync` 归入 `IHostUiServices` 晚绑定路径。
- `AgentUi_*` 从读全局 `AgentUiHost` 静态槽改为读**当前插件实例**（经契约暴露的 composition 快照）。
- 判据：FileDriven GUI 中 Agent 四核心状态 = 插件自报；无宿主侧 gate 代码；红线不变（仍只经 PrjBridge 引 Agent）。

### 阶段 C — AgentUiHost 静态槽 → 插件实例组合（承接 8.30 已立项的去静态化）
- 15+ 静态槽收敛为 `AgentUiComposition` 实例，由 `AgentSelfHostPlugin` 持有并注入工作流函数。
- 消除"任何宿主都看得到但没装配的全局槽"这一半装配的结构根源。
- 波及 9 库注册机制 + 测试（8.30 已评估"独立大工程"），单独立项。

### 阶段 D — 宿主/测试闸收敛
- 测试 harness 与通用宿主把"插件 Readiness"作为唯一 Agent 可用性闸（替代 boot/OnLoad 隐性假设）。

---

## 五、风险与边界

| 项 | 说明 |
|----|------|
| 静态槽波及面大 | 阶段 C 是独立大工程（8.30 已评估）；阶段 A/B 先动契约与宿主去编排，不一次动 15+ 槽 |
| HTML 是宿主真实控件 | 属"宿主服务注入"，经 `IHostUiServices` 保留晚绑定/自愈，不并入 Agent 装配 |
| 红线 | FileDriven 仍只经 PrjBridge 引 Agent；契约全在 FileDriven.Abstractions |
| 兼容 | `TryBootstrapGuiAsync` 在过渡期保留为插件 EnsureReady 内部实现（非宿主编排入口），存量宿主迁移后退役 |
| 运行验证 | 阶段 B 落地后需 GUI 复跑 `agent_mcpinvoke_create_task_test.json` 确认四核心自报 Ready/Unavailable 显式化 |

---

## 六、一句话结论

> 当前"静默半装配"的结构根源是**宿主（MainForm）编排并门控 Agent 装配**，把 Agent 自有的装配能力 `AgentFramework.StartAsync` + `ThrowIfCoreIncomplete` 的激活权交给了宿主。插件化分离 = **反转激活权**：Agent 以 `AgentSelfHostPlugin` 自持装配与可用性（宿主无关、自报 Readiness），FileDriven 收敛为"发现 + 渲染状态 + 路由能力 + 注入 HTML 宿主服务"的纯宿主；宿主对 Agent 的成败零编排、零裁决。装配与 MainForm 解耦后，"OnLoad 成功但 Agent 静默不可用"在结构上不再可能。

---

*本文档基于 2026-09-02 会话对 FileDriven/PrjBridge/Agent.Launcher 装配接线（MainForm.OnLoadAsync、AgentUiBridge、AgentFrameworkHosting/AgentFramework.StartAsync、AgentUiHost 静态槽、ExtensionAssemblyHost 发现、function-libraries 注册）的源码核实整理。*

