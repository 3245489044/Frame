# WorkFlowMethod CSG 协议集成方案

> **文档目标**：综合阐述 WorkFlowMethod 如何应用 CSG 协议（Configuration-StateMachine-Graph 统一中间表示），包括架构设计、适配器模式、集成服务、管线集成点以及使用示例。

---

## 1. 概述

### 1.1 什么是 CSG 协议

CSG 协议是 **C**onfiguration-**S**tateMachine-**G**raph 三元组统一中间表示，用于将任意领域对象统一描述为三种正交投影：

| 投影 | 名称 | 含义 | 问题 |
|------|------|------|------|
| **C** | Configuration | 静态描述 | **What** — 有什么配置/参数 |
| **S** | StateMachine | 动态语义 | **How** — 如何执行/流转 |
| **G** | Graph | 拓扑结构 | **Where** — 节点间如何连接 |

### 1.2 为什么在 WorkFlowMethod 中集成 CSG

WorkFlowMethod 是工作流执行引擎，天然具有三投影特征：

- **C 投影**：`PipelineContext.SharedData`、`ChainInfo<NodeConfig>` 等配置数据
- **S 投影**：`PipelineContext.CurrentNodeKey/NextNodeKey` 等执行状态
- **G 投影**：`GraphSnapshot` 的节点和边拓扑结构

通过 CSG 协议，WorkFlowMethod 获得以下能力：

1. **统一中间表示**：将工作流配置、状态、图结构统一为 `CanonicalForm`
2. **三投影一致性验证**：自动检查 C/S/G 三个视图是否一致
3. **标准 JSON 导出**：将执行结果导出为标准化 CSG JSON 格式
4. **URI 寻址**：通过 `csg://` 协议直接寻址工作流中的元素
5. **适配器注册**：通过 `CsgFacade` 统一管理类型适配

---

## 2. 架构设计

### 2.1 整体架构

```
┌─────────────────────────────────────────────────────────────┐
│                    WorkFlowMethod                            │
│                                                              │
│  ┌──────────────────────────────────────────────────────┐   │
│  │              CsgIntegrationService                    │   │
│  │  ┌──────────┐  ┌──────────┐  ┌──────────────────┐   │   │
│  │  │ C 适配器  │  │ S 适配器  │  │   G 适配器       │   │   │
│  │  │ Config    │  │ State    │  │   Graph          │   │   │
│  │  │ Adapter   │  │ Machine  │  │   Snapshot       │   │   │
│  │  │           │  │ Adapter  │  │   Adapter        │   │   │
│  │  └─────┬─────┘  └────┬─────┘  └────────┬─────────┘   │   │
│  │        │              │                 │              │   │
│  │        ▼              ▼                 ▼              │   │
│  │  ┌────────────────────────────────────────────────┐   │   │
│  │  │              CsgFacade                          │   │   │
│  │  │  ┌──────────────┐  ┌──────────────────────┐    │   │   │
│  │  │  │ Transform    │  │  ValidationEngine     │    │   │   │
│  │  │  │ Engine       │  │  ConsistencyValidator │    │   │   │
│  │  │  └──────────────┘  └──────────────────────┘    │   │   │
│  │  └────────────────────────────────────────────────┘   │   │
│  └──────────────────────────────────────────────────────┘   │
│                                                              │
│  ┌──────────────────────────────────────────────────────┐   │
│  │              OrchestrationBridge                      │   │
│  │  ┌────────────────────────────────────────────────┐   │   │
│  │  │  执行前：ChainInfo → CanonicalForm (C 投影)    │   │   │
│  │  │  执行后：三投影一致性验证 + JSON 导出           │   │   │
│  │  └────────────────────────────────────────────────┘   │   │
│  └──────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────┘
```

### 2.2 项目依赖关系

```
WorkFlowMethod.csproj
  └── ProjectReference → CSG.Unified.csproj
       ├── Contracts/IConfiguration.cs      ← C 投影接口
       ├── Contracts/IStateMachine.cs       ← S 投影接口
       ├── Contracts/IGraph.cs              ← G 投影接口
       ├── CsgFacade.cs                     ← 统一门面
       ├── CanonicalForm.cs                 ← 规范形式
       └── ...
```

---

## 3. 适配器层（Adapter Layer）

适配器层是 CSG 集成的核心，负责将 WorkFlowMethod 的领域类型适配为 CSG 协议接口。

### 3.1 C 投影适配器：`PipelineContextConfigAdapter`

**文件**：`CSG/Adapters/PipelineContextConfigAdapter.cs`

**职责**：将 `PipelineContext` 适配为 `IConfiguration` 接口，提供工作流的静态配置视图。

**映射规则**：

| PipelineContext 属性 | CanonicalForm 元素 | CanonicalNodeType |
|---------------------|-------------------|-------------------|
| `SharedData` 中的键值对 | `CanonicalNode` | `Data` |
| `CurrentNodeKey` | `CanonicalNode` | `Action` |
| `NextNodeKey` | `CanonicalNode` | `Action` |
| `GraphSnapshot` 引用 | `CanonicalNode` | `Data` |
| 当前→下一节点关系 | `CanonicalEdge` | `ControlFlow` |

**核心代码**：

```csharp
public class PipelineContextConfigAdapter : IConfiguration
{
    public string ConfigType => "WorkflowPipeline";
    public string SchemaVersion => "1.0";

    public CanonicalForm ToCanonical()
    {
        var form = new CanonicalForm();
        // 1. SharedData → Data 节点
        foreach (var kvp in _context.SharedData)
            form.Nodes.Add(new CanonicalNode { Id = $"param_{kvp.Key}", Type = CanonicalNodeType.Data, ... });
        // 2. CurrentNodeKey/NextNodeKey → Action 节点
        // 3. 控制流边
        // 4. 元数据
        return form;
    }
}
```

### 3.2 S 投影适配器：`PipelineContextStateMachineAdapter`

**文件**：`CSG/Adapters/PipelineContextStateMachineAdapter.cs`

**职责**：将 `PipelineContext` 适配为 `IStateMachine` 接口，提供工作流的动态执行状态视图。

**状态映射**：

| PipelineContext 属性 | IStateMachine 成员 | 说明 |
|---------------------|-------------------|------|
| `CurrentNodeKey` | `CurrentState` | 当前执行节点 |
| `NextNodeKey` | `CanTransitionTo()` | 可转换的目标状态 |
| `CurrentInput` | `TryTransitionTo()` 的 trigger | 触发转换的输入 |

**核心代码**：

```csharp
public class PipelineContextStateMachineAdapter : IStateMachine
{
    public string CurrentState => _context.CurrentNodeKey ?? "idle";

    public bool CanTransitionTo(string state)
    {
        if (string.Equals(_context.NextNodeKey, state, StringComparison.OrdinalIgnoreCase))
            return true;
        if (_context.GraphSnapshot != null)
            return _context.GraphSnapshot.Nodes.ContainsKey(state);
        return false;
    }

    public bool TryTransitionTo(string state, object? trigger = null)
    {
        if (!CanTransitionTo(state)) return false;
        _context.CurrentNodeKey = state;
        _context.NextNodeKey = null;
        if (trigger != null) _context.CurrentInput = trigger;
        return true;
    }
}
```

### 3.3 G 投影适配器：`GraphSnapshotGraphAdapter`

**文件**：`CSG/Adapters/GraphSnapshotGraphAdapter.cs`

**职责**：将 `GraphSnapshot` 适配为 `IGraph` 接口，提供工作流的图拓扑视图。

**映射规则**：

| GraphSnapshot 属性 | IGraph 成员 | CanonicalForm 元素 |
|-------------------|-------------|-------------------|
| `NodeKeys` | `NodeIds` | `CanonicalNode` (Default) |
| `Edges` | `EdgePairs` | `CanonicalEdge` (Default) |
| 节点出入度 | — | `Properties["isEntry"/"isExit"]` |

**核心代码**：

```csharp
public class GraphSnapshotGraphAdapter : IGraph
{
    public IEnumerable<string> NodeIds => _snapshot.NodeKeys;
    public IEnumerable<(string Source, string Target)> EdgePairs =>
        _snapshot.Edges.Select(e => (e.SourceNodeKey, e.TargetNodeKey));

    public CanonicalForm ToCanonical()
    {
        var form = new CanonicalForm();
        // 1. 映射所有节点（标记入口/出口）
        foreach (var nodeKey in _snapshot.NodeKeys) { ... }
        // 2. 映射所有边
        foreach (var edge in _snapshot.Edges) { ... }
        // 3. 元数据
        return form;
    }
}
```

---

## 4. 集成服务层（Service Layer）

### 4.1 `CsgIntegrationService`

**文件**：`CSG/CsgIntegrationService.cs`

**职责**：CSG 协议集成服务，提供以下功能：

| 方法 | 功能 | 使用场景 |
|------|------|----------|
| `RegisterAdapters()` | 注册三个适配器到 CsgFacade | 构造函数自动调用 |
| `ChainToCanonical()` | ChainInfo → CanonicalForm | 管线初始化阶段 |
| `ValidateWorkflow()` | 三投影一致性验证 | 执行后验证 |
| `ExportToJson()` | 合并三投影导出为标准 JSON | 执行结果导出 |
| `ResolveUri()` | csg:// 协议 URI 寻址 | 运行时元素查询 |

**适配器注册**：

```csharp
private void RegisterAdapters()
{
    // C 投影：PipelineContext → IConfiguration
    _csg.RegisterAdapter<PipelineContext, IConfiguration>(
        ctx => new PipelineContextConfigAdapter(ctx));

    // S 投影：PipelineContext → IStateMachine
    _csg.RegisterAdapter<PipelineContext, IStateMachine>(
        ctx => new PipelineContextStateMachineAdapter(ctx));

    // G 投影：GraphSnapshot → IGraph
    _csg.RegisterAdapter<GraphSnapshot, IGraph>(
        snap => new GraphSnapshotGraphAdapter(snap));
}
```

**三投影一致性验证规则**：

| 验证项 | 说明 | 严重级别 |
|--------|------|----------|
| C 投影的 Action 节点应在 S 投影中存在 | 配置中定义的执行节点必须在状态机中有对应状态 | Warning |
| S 投影的当前状态应在 G 投影中存在 | 当前执行节点必须在图拓扑中有对应节点 | Warning |
| G 投影的边应在 S 投影中有对应 Transition | 图中的连接关系必须在状态机中有对应转移 | Warning |
| C 投影和 S 投影的 ContextId 一致 | 两个投影来自同一个上下文 | Error |

**URI 寻址格式**：

```
csg://node/{nodeId}       → 获取节点（当前/下一/图中）
csg://edge/{edgeId}       → 获取边
csg://context/{contextId} → 获取上下文
csg://snapshot/{snapshotId} → 获取快照
```

---

## 5. 管线集成点（Pipeline Integration）

### 5.1 `OrchestrationBridge` 中的集成

**文件**：`Execution/OrchestrationBridge.cs`

CSG 集成发生在 `ExecuteGraphAsync` 方法的执行后阶段：

```
ExecuteGraphAsync:
  1. 创建 GraphSnapshot
  2. 创建 ExecutionSession
  3. 创建 PipelineContext
  4. 配置并执行管道
  5. 处理执行结果
  6. ★ CSG 集成：
     a. 三投影一致性验证
     b. 导出为标准 CSG JSON
     c. 记录 CSG 导出信息
```

**集成代码**：

```csharp
// 7. ★ CSG 集成：执行后验证和导出
if (result != null && result.IsSuccess && context != null)
{
    try
    {
        // 7a. 三投影一致性验证
        var validation = _csgService.ValidateWorkflow(context);
        if (!validation.IsValid)
        {
            session.Log?.Warn("OrchestrationBridge.CSG",
                $"CSG 一致性验证发现 {validation.Errors.Count} 个错误");
        }

        // 7b. 将执行结果导出为标准 CSG JSON
        var csgJson = _csgService.ExportToJson(context);
        session.Data.Set("CsgExport", csgJson);

        // 7c. 记录 CSG 导出信息
        session.Log?.Info("OrchestrationBridge.CSG",
            $"CSG 导出完成，节点数: {context.GraphSnapshot?.NodeKeys.Count ?? 0}");
    }
    catch (Exception csgEx)
    {
        session.Log?.Warn("OrchestrationBridge.CSG",
            $"CSG 集成处理异常（非致命）: {csgEx.Message}");
    }
}
```

> **设计原则**：CSG 集成是**非致命**的 — 即使 CSG 处理失败，也不会影响工作流的正常执行结果。

---

## 6. 数据流图

### 6.1 完整执行流程

```
ChainInfo<NodeConfig>
       │
       ▼
OrchestrationBridge.ExecuteWorkflowAsync()
       │
       ├── GraphFoundationBridge.ChainToGraph() → IGraph
       │
       ▼
OrchestrationBridge.ExecuteGraphAsync()
       │
       ├── GraphSnapshot.CreateFrom() → GraphSnapshot
       ├── ExecutionSession.Create()
       ├── PipelineContext.Create()
       │
       ├── ConfigurePipeline() → StagePipeline
       │
       ├── ExecutePipelineAsync()
       │   ├── InitStage
       │   ├── PauseStage
       │   ├── ConfigStage
       │   ├── OrchestrationAdapterStage
       │   ├── OrchestrationExecutionStage
       │   ├── OrchestrationLoopStage
       │   ├── RecordStage
       │   ├── MetricsStage
       │   ├── OrchestrationErrorHandlingStage
       │   └── FinalizeStage
       │
       ├── ★ CSG 验证
       │   ├── PipelineContextConfigAdapter.ToCanonical()    → C 投影
       │   ├── PipelineContextStateMachineAdapter.ToCanonical() → S 投影
       │   ├── GraphSnapshotGraphAdapter.ToCanonical()       → G 投影
       │   └── 一致性检查
       │
       ├── ★ CSG 导出
       │   └── ExportToJson() → CSG JSON
       │
       └── PipelineExecutionResult
```

### 6.2 三投影数据流

```
                    PipelineContext
                    ┌──────────────┐
                    │ SharedData   │──→ C 投影 (What)
                    │ CurrentNode  │──→ S 投影 (How)
                    │ NextNode     │
                    └──────┬───────┘
                           │
                    GraphSnapshot
                    ┌──────────────┐
                    │ Nodes        │──→ G 投影 (Where)
                    │ Edges        │
                    └──────────────┘

    三投影合并 → CanonicalForm → JSON 导出
```

---

## 7. 使用示例

### 7.1 基本使用

```csharp
// 1. 创建 OrchestrationBridge（自动创建 CsgIntegrationService）
var bridge = new OrchestrationBridge();

// 2. 执行工作流
var result = await bridge.ExecuteWorkflowAsync(chain);

// 3. 获取 CSG 导出结果
if (result.IsSuccess && result.Context != null)
{
    var session = result.Context.GetSession();
    var csgJson = session.Data.Get<string>("CsgExport");
    Console.WriteLine(csgJson);
}
```

### 7.2 直接使用 CsgIntegrationService

```csharp
// 1. 创建集成服务
var csgService = new CsgIntegrationService();

// 2. ChainInfo → CanonicalForm
var form = csgService.ChainToCanonical(chain);

// 3. 三投影验证
var validation = csgService.ValidateWorkflow(context);
if (!validation.IsValid)
{
    foreach (var error in validation.Errors)
        Console.WriteLine($"Error: {error}");
}

// 4. JSON 导出
var json = csgService.ExportToJson(context);

// 5. URI 寻址
var node = csgService.ResolveUri("csg://node/MyNode", context);
```

### 7.3 直接使用适配器

```csharp
// C 投影
var configAdapter = new PipelineContextConfigAdapter(context);
var configForm = configAdapter.ToCanonical();

// S 投影
var stateAdapter = new PipelineContextStateMachineAdapter(context);
var stateForm = stateAdapter.ToCanonical();
Console.WriteLine($"当前状态: {stateAdapter.CurrentState}");

// G 投影
var graphAdapter = new GraphSnapshotGraphAdapter(snapshot);
var graphForm = graphAdapter.ToCanonical();
```

---

## 8. 文件清单

| 文件 | 路径 | 说明 |
|------|------|------|
| `CsgIntegrationService.cs` | `CSG/CsgIntegrationService.cs` | CSG 集成服务 |
| `PipelineContextConfigAdapter.cs` | `CSG/Adapters/PipelineContextConfigAdapter.cs` | C 投影适配器 |
| `PipelineContextStateMachineAdapter.cs` | `CSG/Adapters/PipelineContextStateMachineAdapter.cs` | S 投影适配器 |
| `GraphSnapshotGraphAdapter.cs` | `CSG/Adapters/GraphSnapshotGraphAdapter.cs` | G 投影适配器 |
| `OrchestrationBridge.cs` | `Execution/OrchestrationBridge.cs` | 管线集成点 |
| `WorkFlowMethod.csproj` | `WorkFlowMethod.csproj` | 项目引用配置 |

---

## 9. 设计原则与约束

### 9.1 核心原则

1. **非侵入性**：CSG 集成是可选增强，不影响工作流核心执行逻辑
2. **非致命性**：CSG 处理异常不会中断工作流执行
3. **适配器模式**：通过适配器解耦领域类型和协议接口
4. **单一职责**：每个适配器只负责一个投影方向

### 9.2 约束

| 约束 | 说明 |
|------|------|
| 适配器是只读的 | 适配器不修改源对象，只读取数据生成 CanonicalForm |
| S 投影可写 | `IStateMachine.TryTransitionTo()` 会修改 PipelineContext 状态 |
| CSG 异常不传播 | CSG 集成异常被 try-catch 捕获并记录日志 |
| 适配器注册一次 | 在 CsgIntegrationService 构造函数中自动完成 |

---

## 10. 扩展指南

### 10.1 添加新的适配器

如果需要将新的 WorkFlowMethod 类型适配到 CSG 协议：

```csharp
// 1. 创建适配器类
public class MyNewAdapter : IConfiguration
{
    public string ConfigType => "MyType";
    public string SchemaVersion => "1.0";
    public CanonicalForm ToCanonical() { ... }
}

// 2. 在 CsgIntegrationService.RegisterAdapters() 中注册
_csg.RegisterAdapter<MyType, IConfiguration>(
    obj => new MyNewAdapter(obj));
```

### 10.2 扩展验证规则

在 `CsgIntegrationService.ValidateWorkflow()` 中添加新的验证项：

```csharp
// 示例：验证节点命名规范
foreach (var node in configForm.Nodes)
{
    if (!node.Id.All(c => char.IsLetterOrDigit(c) || c == '_'))
    {
        warnings.Add($"节点 ID '{node.Id}' 包含非法字符");
    }
}
```

---

> **文档版本**：1.0  
> **最后更新**：2026-06-25  
> **关联协议**：[CSG 协议](../Protocol/CSG/CSG.Unified/)  
> **关联模块**：[WorkFlowMethod](../../CsharpPrj/Operation/WorkFlowMethod/)
