# HierarchyMethod 优化设计：参考 C# 语句-函数-类设计，引用关系视为代码关联

> 模块路径：`CsharpPrj/Operation/HierarchyMethod`
> 关联文档：[HierarchyMethod 功能分析](./HierarchyMethod功能分析.md)
> 复用框架：`Share/UniversalReference`（UniversalWorkflowFramework）、`Plate/DslMethod`
> 分析日期：2026-08-25

---

## 1. 核心洞察

当前 HierarchyMethod 的引用关系是 **「运行时数据流记录」**（由映射+溯源推导、随执行落盘、用于对账与检索），
而 C# 的引用关系是 **「编译期可静态分析的类型关系」**（调用图 / 继承 / 接口 / 依赖在编译时就确定且可校验）。

> **把引用关系视为 C# 关联，本质是把图模型从「数据流记录」升级为「类型化契约」。**

**关键实现策略（v2 修订）**：类型化契约**不新增基础设施**，而是构建在已有框架之上——

- **引用关系构建在 `UniversalReference` 中**：引用图存储（`IResourceReferenceStore`）本就定义于
  `UniversalReference/StorageFramework/Abstractions`（Operation.DataStoreDriven 依赖其底层基础设施），
  复用其 Core 校验器（`GraphValidator` / `CircularDependencyDetector` / `ReferenceDiagnostics`）、
  Schema 注册（`ReferenceConfig`）、关系路由（`RelationshipManagement`）、分支图（`BranchManagement`）。
- **复用 `DslMethod` 框架**：DSL 声明式定义引用 Schema（`[Node]` / `[Edge]` / `[Constraint]` →
  `ReferenceSchemaRegistry` 装载校验）、契约绑定治理（`[BindAxiom]` / `ContractRegistry` + 缺口矩阵分析）。

这正与框架「零 LLM + 确定性核心」的定位完美契合——**编译器本身就是最典型的确定性组件**，
静态分析层完全 LLM-free，且全部基于既有框架能力（零新增基础设施、零新增依赖）。

---

## 2. 概念映射：C# 三层设计 ↔ HierarchyMethod

| C# 层级 | 关键设计机制 | HierarchyMethod 对应 | 现状差距 |
|---------|-------------|---------------------|---------|
| **语句 Statement** | 顺序执行、块级作用域、局部变量 | `RoundStep`（步骤节点）+ `NodeOutput`（端口值） | 端口全局可见，**无局部作用域语义** |
| **函数 Method** | 签名（参数+返回类型）、实参绑定形参、可调用 | `RoundSpec`（工作流）+ `FuncName`（调用键）+ `NodeToNode`（实参绑定） | 端口**无类型契约**，调用点**无编译期校验** |
| **类 Class** | 封装、可见性（public/private）、继承、接口实现 | `WorkflowId` 容器 + `History`（命名空间）+ `Owns`（成员组合） | **无继承 / 接口 / 可见性**三类类型关系 |
| 命名空间/程序集 | 组织类型、隔离命名 | `History` 容器 / 模块 | 已有，可强化 |

**映射主线**：语句→步骤、函数→工作流、类→容器。
当前模型恰好实现了 C# 的「语法」部分（步骤顺序、数据流），但缺失了「类型系统」部分——而这正是 C# 引用关系真正强大之处。

---

## 3. 引用关系 = C# 关联（逐边映射）

### 3.1 现有引用边 → C# 关联语义

| 现有引用边 | 视为 C# 关联 | 优化动作 |
|-----------|-------------|---------|
| `NodeToNode`（上游端口→下游端口） | **方法调用实参绑定**（`y = Foo(x)`） | 绑定携带类型（DSL 声明），调用点静态校验 |
| `Calls`（步骤 i → i+1） | **语句顺序 / 方法调用** | 分支/合并复用 `BranchManagement.BranchGraph`，环检测复用 `CircularDependencyDetector` |
| `Owns`（工作流→步骤） | **类包含成员（has-a）** | 成员可见性（`exports` 边）+ 生命周期随容器 |
| `KeyMappingToNode` | **using / 全局常量引用** | 上下文键类型化（DSL `[Node]` 端口类型） |
| `cross_db_link` / `history_owns_task` | **对象引用 / 命名空间归属** | 保持，语义对齐 |
| `session_owns` / `round_sequence` | **会话作用域 / 事件时序** | 保持 |

### 3.2 新增引用边（注册进 UniversalReference Schema）

全部新增边经 `ReferenceConfig.Schema.EdgeTypes` 注册（或扩展 `ReferenceType` 枚举），
并以 DSL `[Edge]` 声明 → `ReferenceSchemaRegistry` 校验（src/tgt 悬空、重复、dep 目标）。

| 新增边 | 对齐 C# 概念 | 语义 | 复用承载 |
|--------|-------------|------|---------|
| **`Inherits`** | 类继承（is-a） | 工作流模板继承 + 步骤覆写 | EdgeType 注册 + 成员查找链解析 |
| **`Implements`** | 接口实现 | 工作流声明实现某端口契约，导演按契约分派 | EdgeType 注册 + `ContractRegistry` 绑定治理 |
| **`DependsOn`** | 编译期依赖（using） | 显式声明上下文依赖 | EdgeType 注册 + `GetDependencies` 查询 |
| **`Exports`** | public 成员 | 跨工作流可见性边界 | EdgeType 注册 + `[Constraint]` 可见性约束 |

---

## 4. 复用映射总表（上轮方案 → 既有框架组件）

| 上轮方案（新增） | 复用组件 | 所在框架 | 新增工作量 |
|----------------|---------|---------|-----------|
| ① 端口类型签名 | DSL `[Node]` 端口类型声明 → `ReferenceSchemaRegistry` → `ReferenceSchema` | DslMethod | 仅编排层 |
| ② 静态编译检查器 | `GraphValidator.ValidateIntegrity`（悬空边/重复/起止节点）+ `CircularDependencyDetector`（环）+ `ReferenceDiagnostics`（孤立/未使用/缺失）+ `ReferenceSchemaRegistry.Validate`（DSL 悬空端） | UniversalReference.Core + DslMethod | 仅编排层 |
| ③ 作用域可见性 | `exports` 边注册 `ReferenceConfig.Schema.EdgeTypes` + DSL `[Constraint]` | UniversalReference.Reference + DslMethod | Schema 注册 |
| ④ 工作流契约与分派 | `IAxiom` 契约公理 + `[BindAxiom]` 标注 + `ContractRegistry` 治理 + `RelationshipManagement`（`IRelationshipRule` / `RelationshipDispatcher`）规则分派 | DslMethod + UniversalReference | 契约声明 |
| ⑤ 模板继承 + 分支 | `inherits` 边注册 + `BranchManagement.BranchGraph`（Start/Action/Decision/End/Subflow + Conditional 边） | UniversalReference | Schema 注册 |
| 引用图存储 | `IResourceReferenceStore`（已有）+ `IUniversalReferenceManager` 上层查询（`GetDependencies` / `FindPaths` / `GetResourcesByType` / `CleanupNonExistentResources`） | UniversalReference | 零新增 |

---

## 5. 五个优化方向（复用改造后）

### 5.1 ① 端口类型签名（对齐方法签名）—— DSL 声明式

- 用 DslMethod DSL 声明工作流节点与端口类型：`[Node] collect(goal:string, ctx:any) -> (plan:string[])`。
- `ReferenceSchemaRegistry.Load` 装载文档 → 提取 `[Node]` → `Validate`（重复/悬空）→ `ToSchema()` 编译为运行时 `ReferenceSchema`。
- 端口类型签名 = 引用 Schema 的节点属性；`NodeToNode` 实参绑定携带类型，调用点经 Schema 静态校验。

### 5.2 ② 静态编译检查器（对齐 C# 编译器）—— 编排层，算法全复用

新增 `WorkflowCompiler`（仅编排）：把工作流引用关系投影为 UR 图 → 依次执行：

| 检查项 | 对齐 C# | 复用组件 | 处置 |
|--------|---------|---------|------|
| 未定义引用（undeclared node/port） | CS0103 | `GraphValidator.ValidateIntegrity` | 编译错误 |
| 悬空边（端点节点已删） | 悬空引用 | `GraphValidator.ValidateIntegrity` + `CleanupNonExistentResources` | 编译错误 / 清理 |
| 类型不匹配（实参端口类型 ≠ 形参类型） | CS0029 | `ReferenceSchemaRegistry` 端口类型对照 | 编译错误 |
| 调用环检测（Calls 边成环） | 死循环风险 | `CircularDependencyDetector.Analyze` | 编译告警 |
| 孤立 / 未使用 / 缺失引用 | CS0168 等 | `ReferenceDiagnostics.RunFullDiagnostics` | 编译警告 |

与现有 `WorkflowReferenceStaticizer` 衔接：**静态化产出引用关系 → WorkflowCompiler 消费校验**。

### 5.3 ③ 作用域与可见性（对齐 C# 块级作用域）

- 节点输出端口 = **局部变量**：默认仅工作流内下游可见。
- 跨工作流访问须显式 `exports` 边（对齐 public）——注册进 `ReferenceConfig.Schema.EdgeTypes`，
  并以 DSL `[Constraint] exports: (workflow, node)` 声明约束，`ReferenceSchemaRegistry.Validate` 静态检查。
- 全局上下文（KeyMapping）= 静态字段，须显式声明。
- 落地：`NodeOutput` 增加可见性元数据，`NodeOutputStore` 检索时做作用域过滤（此部分为框架内小改）。

### 5.4 ④ 工作流契约与多态分派（对齐接口 + 虚方法）

- **契约 = 公理**：以 DslMethod `IAxiom` 定义契约公理（如 `IAnswerWorkflow`），工作流实现类标注
  `[BindAxiom(typeof(IAnswerWorkflow))]` → `AxiomScanner` 扫描索引 → `ContractRegistry` 校验（target 悬空 / impl 缺失）。
- **实现绑定 = Implements 边**：注册进 Schema，写引用图。
- **运行时分派 = 关系路由**：`RelationshipManagement` 的 `IRelationshipRule`（含 `ExpressionRule` 条件解析）
  + `RelationshipDispatcher`（单播/广播）按契约选择实现（对齐虚方法分派），运行时替换实现不影响调用方。
- 与现有 `IExecutorTrigger` / `ITaskExecutorResolver` 端口注入模式天然一致。

### 5.5 ⑤ 工作流模板继承 + 分支（对齐类继承 + 控制流）

- `inherits` 边：派生工作流 → 基类工作流（注册进 Schema）。
- 派生复用基类步骤 + 覆写同名 `NodeId` 步骤（对齐 `override`）；步骤解析顺序：派生自有 → 基类（成员查找链）。
- 分支/合并控制流：复用 `BranchManagement.BranchGraph`（`BranchNode.Type` 已含 Start / Action / Decision / End / Subflow，
  `BranchEdge.Type` 已含 Default / Normal / Conditional / Error），`WorkflowResultDeriver` 的链尾推导升级为分支图出度 0 推导。
- 结合 `RoundSpecSource`（Declared / BuiltinDefault）已有机制。

---

## 6. 架构边界与兼容性

1. **写边原语唯一收敛不变**：写边仍收敛于 `DataStoreDriven.ReferenceGraphCoordinator`（其底层即
   UR StorageFramework）——**复用的是 UR 的 Schema 注册 / 校验 / 诊断 / 关系路由 与 DslMethod 的 DSL 声明 / 契约治理**，
   不另起写边通道（SingleImplementation / TH-12）。
2. **框架零 LLM 不变**：`WorkflowCompiler` / Schema 校验 / 关系路由全部为确定性组件。
3. **可空守卫不变**：未装配 UR 管理端口 / Schema 注册表时返回诊断（`UnavailableDiagnostics`），不阻断 DB-only 路径。
4. **依赖方向不变**：HierarchyMethod → DataStoreDriven → UniversalReference（底层）单向；DslMethod 为 Plate 层，仅消费其 Schema/契约输出，不反向依赖。

---

## 7. 落地建议

```text
Phase 1（框架内，LLM-free）: ① DSL 端口类型签名 + ② WorkflowCompiler 编排复用
  → 引用关系构建进 UniversalReference Schema；编译器算法全复用，仅新增编排层
Phase 2（框架内）: ③ 作用域可见性（NodeOutput 元数据 + exports 边注册）
Phase 3（Agent 应用层）: ④ 契约分派（[BindAxiom] + RelationshipManagement）+ ⑤ 模板继承/分支
  → Implements / Inherits 边定义注册进 Schema，执行分派放 Agent 层（保持框架零 LLM）
```

各 Phase 边界清晰，可独立交付、独立验证，不破坏现有执行路径，且 Phase 1-2 的绝大部分能力来自既有框架复用。

---

## 8. 结论

参考 C# 后，HierarchyMethod 的引用关系应从「运行时数据流记录」升级为「编译期类型化契约」：

- `NodeToNode` 视为**实参绑定**、`Owns` 视为**成员组合**；
- 新增 `Inherits` / `Implements` / `DependsOn` / `Exports` 对齐**继承 / 接口 / 依赖 / 可见性**；
- **实现上零新增基础设施**：引用关系构建在 `UniversalReference`（存储 + 校验器 + Schema + 关系路由 + 分支图，
  引用图存储本就在其 StorageFramework 内），类型契约与治理复用 `DslMethod`（DSL Schema 声明 + `[BindAxiom]` 契约治理）；
- 配套的**静态编译器 `WorkflowCompiler` 仅是编排层**，算法全部复用既有组件。

该优化方向与框架「零 LLM + 确定性核心」的定位完全契合：全部检查与契约逻辑均为确定性组件，
以复用方式落地，最大程度降低新增维护面。

---

## 9. 实施状态（2026-08-25 落地回填）

> 本节回填本设计已落地代码的实际形态。**关键调整**：落地时严格「只复用 DslMethod 的校验逻辑，
> 框架不新增/不依赖生成产物」——编译校验层全部内存执行，未改动 `reference-schema.dsl.md`、
> 未再生成 `reference-schema.generated.json`；端口类型与新增边为「机制」，按需在具体节点类型声明，由内联 DSL 测试证明。

### 9.1 Phase 1 —— 端口类型签名 + WorkflowCompiler（框架内，LLM-free）

| 项 | 落地 | 位置 |
|---|---|---|
| ① 端口类型签名 | `ResourceNodeType` 新增 `InputPorts/OutputPorts`（解析 `[Node] inports/outports` 的 `port:type` 逗号列表，`ParsePorts`）；`IReferenceTypeRegistry.TryGetPortType(nodeType, port, isInput, out type)` | `CsharpPrj/Plate/DslMethod/Schema/ReferenceSchemaModel.cs`、`CsharpPrj/Operation/DataStoreDriven/ReferenceSchema/{IReferenceTypeRegistry,ReferenceTypeRegistry}.cs` |
| ② 静态编译检查器 | `WorkflowCompiler.Compile(spec, registry?, dslSchema?)`（仅编排）：DSL 校验(复用 `ReferenceSchemaRegistry.Validate`) + 图完整性(`GraphValidator.ValidateIntegrity`) + 调用环(`CircularDependencyDetector.Analyze`) + 孤立/未使用(`ReferenceDiagnostics.RunFullDiagnostics`) + 端口类型不匹配(CS0029)。可空守卫，内存执行，不产生编译产物 | `CsharpPrj/Operation/HierarchyMethod/Nodes/Relations/WorkflowCompiler.cs` |

### 9.2 Phase 2 —— 作用域可见性（框架内）

| 项 | 落地 | 位置 |
|---|---|---|
| 可见性元数据 | `OutputVisibility`（WorkflowLocal/Exported）+ `NodeOutput.Visibility`，经 `NodeOutputDoc` 持久化往返 | `CsharpPrj/Operation/HierarchyMethod/Nodes/{NodeOutput,NodeOutputCodec}.cs` |
| 作用域判定 | `OutputScopeResolver.IsVisibleTo`：同工作流恒可见；跨工作流仅 Exported | `CsharpPrj/Operation/HierarchyMethod/Nodes/OutputScopeResolver.cs` |
| 检索过滤 | `NodeOutputStore` 新增 `WriteOutputAsync(…, visibility)` + `TryGetNodeOutputScopedAsync`/`EnumerateOutputsScopedAsync` | `CsharpPrj/Operation/HierarchyMethod/Nodes/NodeOutputStore.cs` |
| exports 边 | `WorkflowReferenceTypes.Exports` + `NodeRelationDeriver.DeriveExportsEdge` | `CsharpPrj/Operation/DataStoreDriven/Abstractions/NodeRelationStore.cs`、`Core/NodeRelationDeriver.cs` |

### 9.3 Phase 3（框架侧）—— 类型关系词汇表 + 继承解析

| 项 | 落地 | 位置 |
|---|---|---|
| 类型关系边词汇 | `WorkflowReferenceTypes.Inherits/Implements/DependsOn` + `DeriveInheritsEdge/DeriveImplementsEdge/DeriveDependsOnEdge` | `NodeRelationStore.cs`、`NodeRelationDeriver.cs` |
| 模板继承声明 | `RoundSpec.BaseWorkflow`（对齐 is-a） | `CsharpPrj/Operation/HierarchyMethod/Nodes/Workflows/RoundSpec.cs` |
| 成员查找链 | `WorkflowTemplateResolver.MergeSteps(derived, base)`：派生覆写同名 `EffectiveNodeId` + 基类补足（对齐 override） | `CsharpPrj/Operation/HierarchyMethod/Nodes/Relations/WorkflowTemplateResolver.cs` |

### 9.4 Agent 执行链接线

`WorkflowCompiler` 已接入 `WorkflowRunner.EnsureWorkflowCompiledAsync`（每进程每工作流一次，独立于静态化守卫）：编译 → 日志（`WorkflowRunner_compile_issues`/`compile_failed`）→ 再静态化写边；best-effort 不阻断本轮、不翻转业务 ok。注册表可经 `AgentRunnerAssemblyInput.ReferenceRegistry → AgentRunner → WorkflowRunner` 注入（未注入则仅结构校验，跳过 CS0029）。

- `Projects/Agent/src/Agent.Core/Nodes/WorkflowRunner.cs`
- `Projects/Agent/src/Agent.Core/Loops/AgentRunner.cs`、`Loops/AgentRunnerFactory.cs`
- `Projects/Agent/src/Agent.Core/Diagnostics/agent_log_messages.json`

### 9.5 验证与未落地

- **验证**：`ReferenceSchema.Tests` 新增 `WorkflowCompilerTests`（7 条）+ `FrameworkScopeTests`（7 条），共 **14/14 通过**；`HierarchyMethod`/`Agent.Core`/`Agent.Orchestration` 编译 0 警告 0 错误。
- **未落地（Agent 应用层，保持框架零 LLM）**：④ 契约运行时分派（`[BindAxiom]` + `RelationshipDispatcher` 按契约选实现）、⑤ 分支控制流执行（`BranchGraph` + 导演按 Decision/Conditional 选路）；`WorkflowResultDeriver` 双源（`Operation.HierarchyMethod` 的 `ResolveFinalStepIndex` 与 `Agent.Core/Nodes` 的 `ResolveFinalAnswer`，后者依赖 Agent.Contract 类型，待收敛）。
