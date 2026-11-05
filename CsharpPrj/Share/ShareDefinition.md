**Share 层不能反向引用 Operation 层、Strategy 层、以及 Frame 之外的产品工程（Projects\*）**

**Share 层只允许引用 BasicMethod 层、Modules 层、Plate 层（TConfig/JsonMethod/XmlMethod/WinFormsValueContract 等）、Protocol 层**

**唯一已登记例外：CommandFramework -> Projects\Route\Route.csproj**（命令配置资源路径经 Route.InformationPaths 解析，2026-09-06 路径单一真源收敛 决策 A）；登记处 CsharpPrj\modules.json 的 exceptions.references，待收敛为 Frame 侧抽象 + 组合根注入后移除。

## 强制方式

| 层 | 手段 | 位置 |
|----|------|------|
| 构建期（硬规则） | Share 出口规则守卫 | CsharpPrj\LayerBoundary.targets（LayerBarrier_Enforce=error|warn|off） |
| 离线全量（四类检查） | 悬空 / 未属性化 / 层序 / 循环依赖 + 反向引用报告 | PowerShellScript\Share\Solution\Test-ProjectReferences.ps1 -Check |

## 引用书写规范

ProjectReference 一律使用模块路径属性，禁止 ..\ 相对路径字面量：

    <ProjectReference Include="$(Proj_StateManagement)" />

模块路径单一真源：CsharpPrj\modules.json -> Generate-ModulePaths.ps1 -> generated\Directory.Build.props.modules.generated.props。
目录调整只需改 modules.json 一处并重新生成，全部 csproj 零改动。

## 目录分组

> 分组 = 职责分类，不是严格依赖分层；成员以磁盘实际目录与 `modules.json` 登记为准。
> 共 30 个模块工程（不含 `Share.csproj` 聚合工程）。

### Foundations（基座：抽象 / 模型 / 状态 / 配置）

| 模块（工程名） | 目录 | 职责 |
|---|---|---|
| `FrameworkAbstractions` | `Share\Foundations\FrameworkAbstractions` | 组件注册表、容器抽象、引导、配置、数据绑定、布局、线程、错误处理等通用契约 |
| `StateManagement` | `Share\Foundations\StateManagement` | 状态块与快照/恢复，线程安全的通用状态承载与变更设施 |
| `Share.ConfigurationModels` | `Share\Foundations\ConfigurationModels` | 纯 POCO 配置模型，无 UI 框架依赖 |
| `ScopeFramework` | `Share\Foundations\ScopeFramework` | 多层作用域到命令、步骤链的纯逻辑翻译器 |
| `TreeTraceFramework` | `Share\Foundations\TreeTraceFramework` | 通用树形追踪、查询与序列化 |
| `Share.JsonPayload` | `Share\Foundations\JsonPayload` | JSON 载荷取值守卫（仅 BCL，零 ProjectReference） |

### Frameworks（领域无关的通用框架 / 内核）

| 模块（工程名） | 目录 | 职责 |
|---|---|---|
| `UniversalWorkflowFramework` | `Share\Frameworks\UniversalReference` | 通用工作流与引用框架（目录名 `UniversalReference`，工程名 `UniversalWorkflowFramework`） |
| `GraphFramework` | `Share\Frameworks\GraphFramework` | 图模型/快照、ECS 图、分析、指令、执行与桥接 |
| `LayoutAnalysis` | `Share\Frameworks\LayoutAnalysis` | 基于图基座与通用布局数学计算层次关系与布局坐标 |
| `Diagnostics` | `Share\Frameworks\Diagnostics` | 日志、指标与诊断上下文的通用实现 |
| `Share.Conversation` | `Share\Frameworks\Conversation` | 对话历史纯逻辑模型（消息列表、追加、窗口渲染、序列化） |
| `CircuitFramework` | `Share\Frameworks\CircuitFramework` | 声明式电路描述/编译/静态分析/调度/执行 |
| `AiScheduling` | `Share\Frameworks\AiScheduling` | AI-in-the-loop 调度：状态导演 + 转移守卫 + 状态机 |
| `Share.AgentLoop` | `Share\Frameworks\AgentLoop` | LLM 工具多轮循环内核，工具执行经委托注入 |
| `Share.Messaging` | `Share\Frameworks\Messaging` | 消息信封/地址/生命周期/路由策略/流控判据/闭包报告（仅 BCL） |
| `Share.ContextGeometry` | `Share\Frameworks\ContextGeometry` | 前缀几何内核：缓存带/段/快照/组合/内容寻址/命中核算/策略判据 |
| `Share.PortContext` | `Share\Frameworks\PortContext` | 容器-成员上下文唯一装配器（UPC）：规格/装配/空因码/键轴/三出口同形 |
| `Share.Signal` | `Share\Frameworks\Signal` | 信号账本内核：契约/边集状态机/幂等接纳/租约/TTL/终态观测 |
| `Share.NodeIo` | `Share\Frameworks\NodeIo` | 节点文档读写内核：loc 反解、读写同源、载荷契约 |
| `Share.ObjectiveBoundary` | `Share\Frameworks\ObjectiveBoundary` | 目标边界评估：确定性压力 + 迟滞 + 反应式/主动式双通道 + 仲裁合并 |
| `RoslynJsonFramework` | `Share\Frameworks\RoslynJson` | Roslyn 语法树到转换 IR 的映射、规则表与发射器 |

### Composition（组合 / 装配 / 流程机制）

| 模块（工程名） | 目录 | 职责 |
|---|---|---|
| `AssemblyFramework` | `Share\Composition\AssemblyFramework` | 组合根装配：段机 + Boot→Bind→Assemble→Publish 相位序 + 串行门 |
| `CommandFramework` | `Share\Composition\CommandFramework` | 命令字符串解析、注册、管线执行到结果返回的完整生命周期 |
| `ControlLookup` | `Share\Composition\ControlLookup` | 将按名/类型/祖先/后代查找统一为“查找源 + 有序聚合器” |
| `PlaceholderFramework` | `Share\Composition\PlaceholderFramework` | 数据引用解析与追踪，组合为可管线化求值流程 |
| `UniversalMiddleware` | `Share\Composition\UniversalMiddleware` | 统一 JSON 处理为可组合管线（链/容器/转换/序列化/渲染/事件管线） |

### Orchestration（运行期编排 / 宿主 / 池 / 适配）

| 模块（工程名） | 目录 | 职责 |
|---|---|---|
| `Share.Adapters` | `Share\Orchestration\Adapters` | 命名管道客户端与连接管理、日志查询适配器 |
| `EventBindingOrchestrator` | `Share\Orchestration\EventBindingOrchestrator` | 声明式事件/命令绑定编译并编排到通用中间件与命令框架 |
| `HostAssembly` | `Share\Orchestration\HostAssembly` | 组合根装配管线 + 可观测降级 + 线程安全槽位 + 单实例工厂 + 统一错误边界 |
| `TaskStatusPool` | `Share\Orchestration\TaskStatusPool` | 池（Pool）/态射（Morphism）/上下文（Context）三大核心抽象 |

### 分组判据

| 分组 | 放这里 | 不放这里 |
|---|---|---|
| `Foundations` | 纯抽象、纯模型、状态承载、配置、作用域、取值守卫；被其他 Share 模块复用 | 含完整执行流程或运行期编排 |
| `Frameworks` | 领域无关、可独立复用的“通用 X 框架 / 内核”，有清晰能力边界 | 仅做组装、宿主或流程编排 |
| `Composition` | 把离散能力组装/装配成可执行流程的机制：命令、中间件、占位符、控件查找、装配段 | 具体业务领域实现 |
| `Orchestration` | 运行期宿主、事件绑定编排、任务状态池、外部适配器 | 纯数据模型或基础契约 |

### 现状备注（2026-11-05）

- 旧分组表仅列 18 个模块，本次按磁盘实际补全至 30 个（不含 `Share.csproj`）。
- 4 个分组是职责分类，不是严格依赖分层。实测存在跨组反向边：
  - `Frameworks\ObjectiveBoundary` → `Orchestration\TaskStatusPool`
  - `Frameworks\Signal` → `Orchestration\TaskStatusPool`
  - `Composition\AssemblyFramework` → `Orchestration\HostAssembly`
- 目录名 ≠ 工程名的模块：`UniversalReference` → `UniversalWorkflowFramework`、`RoslynJson` → `RoslynJsonFramework`、`ConfigurationModels` → `Share.ConfigurationModels`。
- `RoslynJsonFramework`、`UniversalWorkflowFramework` 已于 2026-11-05 补齐 `ModuleRule` 元数据，Share 下 30 个模块工程全部具备分类元数据。
- 两套层号并存，勿混用：`modules.json` 根 `Share.layer=2`；`Share.csproj` 的 `ModuleRule_Layer=3`。