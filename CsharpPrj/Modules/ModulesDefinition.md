# Modules 目录分组

> 与 `Share\ShareDefinition.md` 同构：先按职责分组，再按模块落工程，模块内按架构角色切子目录。
> 分组 = 职责分类，不是严格依赖分层；成员以磁盘目录与 `modules.json` 登记为准。
> 共 27 个一级模块 + 2 个嵌套子模块（`Parsing\Keys`、`LogicComposition\Conversion`），合计 29 个模块工程（不含 `Modules.csproj` 聚合工程）。

## 分组总览

| 分组 | 模块数 | 定位 |
|---|---:|---|
| `Foundations` | 7 | 基础内核：计算、并发、契约、治理、存储、线程、诊断基础设施 |
| `Data` | 6 | 数据与键空间：组件数据、数据流、图、键、解析、节点 |
| `Frameworks` | 6 | 通用框架：事件、执行、逻辑组合、观测、同步、校验 |
| `Composition` | 5 | 组合与接线：元命令、参数解析、管线、注册表、端口接盒 |
| `Orchestration` | 3 | 运行期编排：采集、维护、插件宿主 |

## Foundations（基础内核）

| 模块（工程名） | 目录 | 职责 |
|---|---|---|
| `Compute` | `Modules\Foundations\Compute` | 扁平/SoA 布局、Arena/Scratch、SIMD 核、tile 调度、算子图编译 |
| `Concurrency` | `Modules\Foundations\Concurrency` | 并发复杂度/拆分/成本门禁/组键/成本记账（BCL-only） |
| `ContractFramework` | `Modules\Foundations\ContractFramework` | 契约身份/注册/对账/执行/取证内核 |
| `DiagnosticContext` | `Modules\Foundations\DiagnosticContext` | 插桩上下文、作用域存储与快照 |
| `Governance` | `Modules\Foundations\Governance` | 资源计量、作用域链、两相预留、统一裁决 |
| `InternalStorage` | `Modules\Foundations\InternalStorage` | 内部存储抽象与实现 |
| `Threading` | `Modules\Foundations\Threading` | 异步执行、调度、写入、超时/挂起/终止 |

## Data（数据与键空间）

| 模块（工程名） | 目录 | 职责 |
|---|---|---|
| `ComponentData` | `Modules\Data\ComponentData` | ECS 组件数据、变更通知、优先级反应器链 |
| `DeclarativeDataflow` | `Modules\Data\DeclarativeDataflow` | 声明式数据流、预算/租约/拓扑/序列化 |
| `GraphProcessing` | `Modules\Data\GraphProcessing` | 图算法、归一化、快照、诊断与操作 |
| `KeySpace` | `Modules\Data\KeySpace` | 键族/身份/三态值/集合策略/端口契约/读写同源 |
| `Parsing` | `Modules\Data\Parsing` | 解析抽象/适配/标记/模型；含 `Keys` 键文法内核子模块 |
| `Node` | `Modules\Data\Node` | 与位置无关的节点与帧框架（Key + JSON + Placement） |

## Frameworks（通用框架）

| 模块（工程名） | 目录 | 职责 |
|---|---|---|
| `EventFramework` | `Modules\Frameworks\EventFramework` | 事件源/总线/服务/适配器 |
| `ExecutionFramework` | `Modules\Frameworks\ExecutionFramework` | 执行引擎、执行中心、会话与系统 |
| `LogicComposition` | `Modules\Frameworks\LogicComposition` | 合并/组合/插入/连接/验证抽象；含 `Conversion` 转换内核子模块 |
| `Observe` | `Modules\Frameworks\Observe` | 观测协议、指标、传输 |
| `Sync` | `Modules\Frameworks\Sync` | 同步协议 v1 契约与三端环路（Authority/Replica/Transport） |
| `ValidationFramework` | `Modules\Frameworks\ValidationFramework` | 声明式校验引擎、提供器、反馈与验证 |

## Composition（组合与接线）

| 模块（工程名） | 目录 | 职责 |
|---|---|---|
| `MetaCommand` | `Modules\Composition\MetaCommand` | 元命令执行器 |
| `ParameterResolver` | `Modules\Composition\ParameterResolver` | 参数解析/绑定/匹配/来源/策略 |
| `Pipeline` | `Modules\Composition\Pipeline` | 中央管线、静态管线、步骤执行 |
| `Registry` | `Modules\Composition\Registry` | 注册表、自动注册、部署 |
| `Wiring` | `Modules\Composition\Wiring` | 端口接盒：声明式接线/供给闭合/单投递点 |

## Orchestration（运行期编排）

| 模块（工程名） | 目录 | 职责 |
|---|---|---|
| `Gather` | `Modules\Orchestration\Gather` | 采集 Hub/规范/视图 |
| `Maintain` | `Modules\Orchestration\Maintain` | 数据维护与迁移、声明式管线 |
| `PluginHost` | `Modules\Orchestration\PluginHost` | 插件发现、collectible ALC、代目录、激活/换代/回滚 |

## 分组判据

| 分组 | 放这里 | 不放这里 |
|---|---|---|
| `Foundations` | 无业务语义的基础内核、跨模块复用的底层设施 | 具体数据模型、完整执行流程 |
| `Data` | 数据模型、数据流、键/解析、图/节点等数据面能力 | 运行期宿主或流程编排 |
| `Frameworks` | 领域无关、可独立复用的“通用 X 框架” | 仅做组装、接线或宿主 |
| `Composition` | 把离散能力组装/接线成可执行流程的机制 | 具体业务领域实现 |
| `Orchestration` | 运行期宿主、采集、维护、插件生命周期 | 纯数据模型或基础契约 |

## 路径与构建

- 路径单一真源：`Frame\CsharpPrj\modules.json`
- 生成链：`modules.json` → `Generate-ModulePaths.ps1` → `generated\Directory.Build.props.modules.generated.props`
- csproj 引用一律使用 `$(Proj_<项目名>)`，禁止相对路径字面量
- 聚合工程：`Modules.csproj` 引用全部模块，并用 `Compile Remove="<分组>\<模块>\**\*.cs"` 防止源码重复编译
- `MainProj.sln` 只登记 6 个根伞工程，模块不直接挂 sln
- 构建期层边界：`LayerBoundary.targets`；引用校验：`Test-ProjectReferences.ps1 -Check`

## 新增模块

1. 先归组：从 `Foundations` / `Data` / `Frameworks` / `Composition` / `Orchestration` 中选职责最贴近的一组。
2. 建目录：`Modules\<分组>\<模块>\<工程>.csproj`，补齐 `ModuleRule`。
3. 登记：`modules.json` 增加 `"<项目名>": "Modules\\<分组>\\<模块>\\<工程>.csproj"`。
4. 重跑：`Frame\PowerShellScript\Share\Solution\Generate-ModulePaths.ps1`。
5. 接入：`Modules.csproj` 增加 `$(Proj_<项目名>)` 与对应 `Compile Remove`。
6. 校验：`Test-ProjectReferences.ps1 -Check`。

## 备注

- 目录名 ≠ 工程名的模块：
  - `Data\Node` → `Modules.Node.csproj`
  - `Data\KeySpace` → `Modules.KeySpace.csproj`
  - `Composition\Wiring` → `Modules.Wiring.csproj`
  - `Data\Parsing\Keys` → `Modules.Parsing.Keys.csproj`
  - `Frameworks\LogicComposition\Conversion` → `LogicComposition.Conversion.csproj`
- `PluginHost` 已登记 `modules.json`；`Maintain` 已纳入 `Modules.csproj` 聚合。
- 分组是职责分类，不保证严格 DAG；依赖方向以 `ProjectReference` 与 `Test-ProjectReferences.ps1 -Check` 为准。
