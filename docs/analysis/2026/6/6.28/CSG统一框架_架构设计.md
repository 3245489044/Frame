# CSG 统一框架 — 架构设计文档

> **Configuration → StateMachine → Graph 三元组统一框架**
> 版本: v1.0 | 最后更新: 2026-06-24

---

## 目录

1. [背景与动机](#1-背景与动机)
2. [核心思想](#2-核心思想)
3. [框架架构](#3-框架架构)
4. [规范形式 (CanonicalForm)](#4-规范形式-canonicalform)
5. [三层投影接口](#5-三层投影接口)
6. [变换引擎 (TransformEngine)](#6-变换引擎-transformengine)
7. [领域映射](#7-领域映射)
8. [适配器集成方案](#8-适配器集成方案)
9. [项目结构](#9-项目结构)
10. [实现路线图](#10-实现路线图)

---

## 1. 背景与动机

### 1.1 当前架构的碎片化现状

WorkFlowPrj 现有三个独立发展的核心领域，它们共享同一个底层模式但缺乏统一抽象：

| 领域 | 现有实现 | 位置 |
|------|---------|------|
| **图 (Graph)** | `CoreGraph<TKey,TNodeData,TEdgeData>`, `IGraph<TKey,TNodeData,TEdgeData>`, 拓扑算法, 事务系统 | `BasicMethod/GraphFoundation/` |
| **状态机 (StateMachine)** | `LifecycleStateMachine<TOperation>`, `SequentialStateMachine<TInput,TContext,TOutput>`, `ConditionalStateMachine<TState>`, `AsyncEventStateMachine`, `ScopeStateManager`, `StateManager` | `BasicMethod/StateMachine/` |
| **配置 (Configuration)** | `UIConfiguration`, `WorkflowDefinition`, `PipelineDefinition`, JSON 渲染管线, 模板预处理 | `Share/UniversalMiddleware/`, `Modules/Pipeline/` |

### 1.2 核心问题

1. **三者各自有自己的 JSON 格式、解析器、执行引擎**，没有统一的配置模型
2. **状态机与工作流引擎分离**，没有集成到执行模型中
3. **图抽象与配置、状态机之间没有统一的理论模型**
4. **缺少从配置到状态机到图的自动推导/生成机制**
5. **跨领域转换（UI→Workflow→Database）需要手动实现**

### 1.3 解决思路

**配置、状态机、图是同一事物的三个投影：**

```
配置 (Configuration) = 静态描述（What）
状态机 (StateMachine) = 动态语义（How）
图 (Graph)           = 拓扑结构（Where）
```

三者之间存在**可逆变换**，通过一个统一的中间表示（CanonicalForm）来沟通。

---

## 2. 核心思想

### 2.1 数学基础

三者统一于**标签转移系统 (Labeled Transition System, LTS)**：

```
LTS = (S, Act, →)
  S  = 状态集
  Act = 动作/标签集
  → ⊆ S × Act × S = 转移关系
```

| 视图 | 对应关系 |
|------|---------|
| **图 (Graph)** | LTS 的静态结构：S = 节点集, → = 边集 |
| **状态机 (StateMachine)** | LTS 的动态语义：S = 状态, Act = 触发操作, → = 转移规则 |
| **配置 (Configuration)** | LTS 的序列化表示：JSON/XML 编码的 S, Act, → |

### 2.2 关键洞察

**任何可计算的过程都可以表示为这三者的统一体：**

- **图**是最底层的数学结构：G = (V, E, L) 其中 V 是顶点集，E 是边集，L 是标签函数
- **状态机**是图的动态解释：状态 = 顶点，转移 = 边，标签 = 转移条件/动作
- **配置**是图的序列化：JSON 表示的邻接表或树结构

三者之间存在**规范形式 (CanonicalForm)**：
- 任何配置都可以规范化到标准图结构
- 任何状态机都可以展开为可达图 (Reachability Graph)
- 任何图都可以序列化为规范配置

---

## 3. 框架架构

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                         CSG.Unified Framework                                │
│                                                                              │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │  Layer 1: CanonicalForm (规范形式 — 统一中间表示)                    │   │
│  │                                                                      │   │
│  │  ┌──────────────────┐  ┌──────────────────┐  ┌──────────────────┐   │   │
│  │  │  CanonicalNode   │  │  CanonicalEdge   │  │  CanonicalForm   │   │   │
│  │  │  - Id            │  │  - Id            │  │  - Nodes         │   │   │
│  │  │  - Label         │  │  - SourceId      │  │  - Edges         │   │   │
│  │  │  - Type          │  │  - TargetId      │  │  - Metadata      │   │   │
│  │  │  - Properties    │  │  - Type          │  │  - ToJson()      │   │   │
│  │  │                  │  │  - Condition     │  │  - FromJson()    │   │   │
│  │  │                  │  │  - Weight        │  │                  │   │   │
│  │  └──────────────────┘  └──────────────────┘  └────────┬─────────┘   │   │
│  └───────────────────────────────────────────────────────┼─────────────┘   │
│                                                            │                │
│  ┌───────────────────────────────────────────────────────┼─────────────┐   │
│  │  Layer 2: Three Projections (三层投影)                 │             │   │
│  │                                                        ▼             │   │
│  │  ┌──────────────────┐  ┌──────────────────┐  ┌──────────────────┐   │   │
│  │  │  IConfiguration  │  │  IStateMachine   │  │     IGraph       │   │   │
│  │  │  (静态描述)      │  │  (动态语义)      │  │  (拓扑结构)      │   │   │
│  │  │                  │  │                  │  │                  │   │   │
│  │  │  ConfigType      │  │  CurrentState    │  │  NodeIds         │   │   │
│  │  │  SchemaVersion   │  │  CanTransitionTo │  │  EdgePairs       │   │   │
│  │  │  ToCanonical()   │  │  TryTransitionTo │  │  ToCanonical()   │   │   │
│  │  │                  │  │  OnStateChanged  │  │                  │   │   │
│  │  └────────┬─────────┘  └────────┬─────────┘  └────────┬─────────┘   │   │
│  └───────────┼─────────────────────┼─────────────────────┼─────────────┘   │
│              │                     │                     │                  │
│  ┌───────────┼─────────────────────┼─────────────────────┼─────────────┐   │
│  │  Layer 3: TransformEngine (变换引擎)                    │             │   │
│  │           │                     │                     │             │   │
│  │  ┌────────┴─────────────────────┴─────────────────────┴────────┐    │   │
│  │  │                    CanonicalForm (IR)                       │    │   │
│  │  └────────┬─────────────────────┬─────────────────────┬────────┘    │   │
│  │           │                     │                     │             │   │
│  │  ┌────────┴────────┐  ┌────────┴────────┐  ┌────────┴────────┐    │   │
│  │  │  C Compiler     │  │  S Expander     │  │  G Serializer   │    │   │
│  │  │  Config → IR    │  │  SM → IR        │  │  Graph → IR     │    │   │
│  │  │  IR → Config    │  │  IR → SM        │  │  IR → Graph     │    │   │
│  │  └─────────────────┘  └─────────────────┘  └─────────────────┘    │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                                                              │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │  Layer 4: Domain Adapters (领域适配器)                               │   │
│  │                                                                      │   │
│  │  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐  ┌────────┐  │   │
│  │  │  UI Adapter  │  │ Workflow     │  │ Database     │  │ File   │  │   │
│  │  │              │  │ Adapter      │  │ Adapter      │  │ Driven │  │   │
│  │  │ UIConfig→CF  │  │ WfDef→CF     │  │ Schema→CF    │  │ Cfg→CF │  │   │
│  │  └──────────────┘  └──────────────┘  └──────────────┘  └────────┘  │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## 4. 规范形式 (CanonicalForm)

规范形式是整个框架的统一中间表示（IR），所有 C/S/G 都映射到它。

### 4.1 规范节点类型

```csharp
public enum CanonicalNodeType
{
    Default,        // 普通节点
    State,          // 状态机状态
    Action,         // 动作/操作
    Condition,      // 条件分支
    Start,          // 起始节点
    End,            // 终止节点
    Parallel,       // 并行网关
    Merge,          // 合并网关
    Data,           // 数据节点
    View,           // UI 视图
    Control,        // UI 控件
    Table,          // 数据库表
    Field,          // 数据库字段
    Entity,         // ECS 实体
    Component       // ECS 组件
}
```

### 4.2 规范边类型

```csharp
public enum CanonicalEdgeType
{
    Default,        // 普通边
    Transition,     // 状态转移
    DataFlow,       // 数据流
    ControlFlow,    // 控制流
    Reference,      // 引用
    Composition,    // 组合（父子关系）
    Dependency,     // 依赖
    Navigation,     // UI 导航
    Association     // 关联
}
```

### 4.3 核心数据结构

```csharp
/// <summary>规范节点</summary>
public record CanonicalNode
{
    public string Id { get; init; }
    public string Label { get; init; }
    public CanonicalNodeType Type { get; init; } = CanonicalNodeType.Default;
    public Dictionary<string, object?> Properties { get; init; } = new();
}

/// <summary>规范边</summary>
public record CanonicalEdge
{
    public string Id { get; init; }
    public string SourceId { get; init; }
    public string TargetId { get; init; }
    public string Label { get; init; }
    public CanonicalEdgeType Type { get; init; } = CanonicalEdgeType.Default;
    public Dictionary<string, object?> Properties { get; init; } = new();
    public string? Condition { get; init; }     // 转移条件（状态机用）
    public double Weight { get; init; } = 1.0;  // 边权重
}

/// <summary>规范形式 — 所有 C/S/G 的统一中间表示</summary>
public class CanonicalForm
{
    public List<CanonicalNode> Nodes { get; init; } = new();
    public List<CanonicalEdge> Edges { get; init; } = new();
    public Dictionary<string, object?> Metadata { get; init; } = new();

    // ========== URI 寻址 ==========
    public object? ResolveUri(string uri);
    public static List<UriTemplate> ListUriTemplates();

    // ========== 序列化 ==========
    public string ToJson(bool indented = true);
    public static CanonicalForm FromJson(string json);
}
```

### 4.4 JSON 序列化格式

```json
{
  "metadata": {
    "source": "UIConfiguration",
    "version": "1.0",
    "created": "2026-06-24T00:00:00Z"
  },
  "nodes": [
    { "id": "form_main", "label": "MainForm", "type": "View", "properties": { "title": "主窗口" } },
    { "id": "btn_submit", "label": "Submit", "type": "Control", "properties": { "text": "提交" } },
    { "id": "state_idle", "label": "Idle", "type": "State", "properties": {} },
    { "id": "state_loading", "label": "Loading", "type": "State", "properties": {} }
  ],
  "edges": [
    { "id": "e1", "sourceId": "form_main", "targetId": "btn_submit", "type": "Composition", "label": "contains" },
    { "id": "e2", "sourceId": "state_idle", "targetId": "state_loading", "type": "Transition", "label": "onClick", "condition": "validate()" }
  ]
}
```

---

## 5. 三层投影接口

### 5.1 配置契约 (IConfiguration)

```csharp
/// <summary>
/// 配置契约 — 任何配置式结构的统一接口
/// </summary>
public interface IConfiguration
{
    /// <summary>配置类型标识 ("UI", "Workflow", "Database", "FileDriven")</summary>
    string ConfigType { get; }

    /// <summary>配置模式版本</summary>
    string SchemaVersion { get; }

    /// <summary>
    /// 将配置转换为规范形式
    /// C → IR
    /// </summary>
    CanonicalForm ToCanonical();
}
```

### 5.2 状态机契约 (IStateMachine)

```csharp
/// <summary>
/// 状态机契约 — 任何状态机的统一接口
/// </summary>
public interface IStateMachine
{
    /// <summary>当前状态标识</summary>
    string CurrentState { get; }

    /// <summary>检查是否可以转换到目标状态</summary>
    bool CanTransitionTo(string state);

    /// <summary>尝试转换到目标状态</summary>
    /// <param name="state">目标状态</param>
    /// <param name="trigger">触发操作（可选）</param>
    /// <returns>是否转换成功</returns>
    bool TryTransitionTo(string state, object? trigger = null);

    /// <summary>状态变更事件 (id, oldState, newState)</summary>
    event Action<string, string, string>? OnStateChanged;

    /// <summary>
    /// 将状态机转换为规范形式
    /// S → IR
    /// </summary>
    CanonicalForm ToCanonical();
}
```

### 5.3 图契约 (IGraph)

```csharp
/// <summary>
/// 图契约 — 任何图结构的统一接口
/// </summary>
public interface IGraph
{
    /// <summary>所有节点 ID</summary>
    IEnumerable<string> NodeIds { get; }

    /// <summary>所有边 (Source, Target) 对</summary>
    IEnumerable<(string Source, string Target)> EdgePairs { get; }

    /// <summary>
    /// 将图转换为规范形式
    /// G → IR
    /// </summary>
    CanonicalForm ToCanonical();
}
```

---

## 6. 变换引擎 (TransformEngine)

### 6.1 核心接口

```csharp
/// <summary>
/// 变换引擎 — C↔S↔G 的双向映射
/// </summary>
public class TransformEngine
{
    // ========== C ↔ IR ==========
    public CanonicalForm ConfigToCanonical(IConfiguration config);
    public TConfig CanonicalToConfig<TConfig>(CanonicalForm form)
        where TConfig : IConfiguration, new();

    // ========== S ↔ IR ==========
    public CanonicalForm StateMachineToCanonical(IStateMachine sm);
    public IStateMachine CanonicalToStateMachine(CanonicalForm form);

    // ========== G ↔ IR ==========
    public CanonicalForm GraphToCanonical(IGraph graph);
    public IGraph CanonicalToGraph(CanonicalForm form);

    // ========== 一致性验证 ==========
    /// <summary>验证三层是否一致</summary>
    public ValidationResult ValidateConsistency(
        CanonicalForm configForm,
        CanonicalForm stateMachineForm,
        CanonicalForm graphForm);
}
```

### 6.2 变换流程

```
C → S (配置编译为状态机):
  UIConfiguration
    → 提取控件树 → 每个控件生成状态节点
    → 提取事件绑定 → 生成转移边
    → 生成 LifecycleStateMachine 定义

S → G (状态机展开为图):
  LifecycleStateMachine
    → 枚举所有 (FromState, Operation, ToState)
    → 每个状态 → 图节点
    → 每个转移 → 图边
    → 生成可达图 (Reachability Graph)

G → C (图序列化为配置):
  CoreGraph
    → 遍历所有节点 → 生成配置元素
    → 遍历所有边 → 生成关系/引用
    → 生成 UIConfiguration / WorkflowDefinition
```

### 6.3 一致性验证规则

| 规则 | 描述 |
|------|------|
| **节点覆盖** | C 的每个元素在 S 和 G 中都有对应节点 |
| **边覆盖** | C 的每个关系在 S 和 G 中都有对应边 |
| **可达性** | S 的所有状态在 G 中都是可达的 |
| **无死锁** | S 的每个非终止状态都有至少一个出转移 |
| **类型一致** | C/S/G 中对应元素的类型标签一致 |

---

## 7. 领域映射

### 7.1 UI 领域

```
UIConfiguration (配置)
  ├── Form, Views, Controls, Navigation, DataBindings
  │
  ├──→ CanonicalForm:
  │     Nodes: Form, View, Button, TextBox, ListView...
  │     Edges: Navigation (View→View), Composition (Form→Panel→Button)
  │     Properties: Position, Size, Style, DataBinding
  │
  ├──→ StateMachine (控件生命周期):
  │     States: Initialized → Loaded → Rendered → Bound → Evented → Disposed
  │     Transitions: Load, Render, Bind, FireEvent, Dispose
  │
  └──→ Graph (控件树):
        Nodes: 所有控件
        Edges: 父子关系 (Composition) + 导航关系 (Navigation)
```

### 7.2 Workflow 领域

```
WorkflowDefinition (配置)
  ├── Steps/Nodes, Transitions, Conditions, ErrorHandling
  │
  ├──→ CanonicalForm:
  │     Nodes: Start, StepA, StepB, Condition, End
  │     Edges: ControlFlow (StepA→StepB), DataFlow (output→input)
  │
  ├──→ StateMachine (执行状态):
  │     States: Pending → Running → Paused → Completed / Failed
  │     Transitions: Start, Pause, Resume, Complete, Fail, Retry
  │
  └──→ Graph (执行依赖图):
        Nodes: 所有步骤
        Edges: 依赖关系 (DAG) + 条件分支
```

### 7.3 Database 领域

```
DatabaseSchema (配置)
  ├── Tables, Columns, Relations, Indexes, Constraints
  │
  ├──→ CanonicalForm:
  │     Nodes: Table, Column, Index, Constraint
  │     Edges: ForeignKey (Table→Table), Composition (Table→Column)
  │     Properties: Type, Nullable, Default, PrimaryKey
  │
  ├──→ StateMachine (数据生命周期):
  │     States: Schema → Migrating → Active → Archived → Dropped
  │     Transitions: Create, Alter, Drop, Archive, Restore
  │
  └──→ Graph (ER 图):
        Nodes: 所有表
        Edges: 外键关系 + 继承关系
```

### 7.4 跨域转换示例

```
UI → Workflow:
  Button.Click → Workflow.Start
  Form.Close → Workflow.Complete
  ProgressBar → Workflow.Step Progress

Workflow → Database:
  Workflow.Step → Database.Table
  Workflow.DataFlow → Database.ForeignKey
  Workflow.State → Database.Row State

UI → Database:
  ListView → Database.Table (数据绑定)
  TextBox → Database.Column (字段映射)
  Form → Database.Schema (整体结构)
```

---

## 8. 适配器集成方案

### 8.1 设计原则

- **零侵入**：不修改现有代码
- **Adapter 模式**：通过适配器将现有实现映射到统一接口
- **逐步迁移**：先适配，再逐步替换

### 8.2 图适配器

```csharp
/// <summary>
/// CoreGraph → IGraph 适配器
/// </summary>
public class GraphAdapter<TKey, TNodeData, TEdgeData> : IGraph
    where TKey : notnull
    where TNodeData : class
{
    private readonly ICoreGraph<TKey, TNodeData, TEdgeData> _graph;
    private readonly Func<TKey, string> _keyToString;
    private readonly Func<TNodeData, Dictionary<string, object?>> _dataToProperties;
    private readonly Func<TEdgeData, Dictionary<string, object?>> _edgeDataToProperties;

    public IEnumerable<string> NodeIds =>
        _graph.GetAllNodeKeys().Select(k => _keyToString(k));

    public IEnumerable<(string Source, string Target)> EdgePairs =>
        _graph.GetAllEdgeIds().Select(id => (
            _keyToString(_graph.GetEdgeSource(id)),
            _keyToString(_graph.GetEdgeTarget(id))
        ));

    public CanonicalForm ToCanonical()
    {
        var form = new CanonicalForm();
        foreach (var key in _graph.GetAllNodeKeys())
        {
            form.Nodes.Add(new CanonicalNode
            {
                Id = _keyToString(key),
                Label = key.ToString() ?? "",
                Properties = _dataToProperties(_graph.GetNodeData(key) ?? throw new InvalidOperationException())
            });
        }
        foreach (var edgeId in _graph.GetAllEdgeIds())
        {
            form.Edges.Add(new CanonicalEdge
            {
                Id = edgeId,
                SourceId = _keyToString(_graph.GetEdgeSource(edgeId)),
                TargetId = _keyToString(_graph.GetEdgeTarget(edgeId)),
                Weight = _graph.GetEdgeWeight(edgeId),
                Properties = _graph.GetEdgeData(edgeId) != null
                    ? _edgeDataToProperties(_graph.GetEdgeData(edgeId)!)
                    : new()
            });
        }
        return form;
    }
}
```

### 8.3 状态机适配器

```csharp
/// <summary>
/// LifecycleStateMachine → IStateMachine 适配器
/// </summary>
public class LifecycleStateMachineAdapter<TOperation> : IStateMachine
    where TOperation : Enum
{
    private readonly LifecycleStateMachine<TOperation> _sm;
    private readonly Func<TOperation, string> _operationToString;

    public string CurrentState => _sm.CurrentState.ToString();

    public event Action<string, string, string>? OnStateChanged;

    public LifecycleStateMachineAdapter(LifecycleStateMachine<TOperation> sm)
    {
        _sm = sm;
        _sm.StateChanged += (_, args) =>
        {
            OnStateChanged?.Invoke(
                "default",
                args.FromState.ToString() ?? "",
                args.ToState.ToString() ?? "");
        };
    }

    public bool CanTransitionTo(string state)
    {
        // 检查是否有任何操作可以到达目标状态
        return Enum.GetValues<TOperation>()
            .Any(op => _sm.CanFire(op) && /* 检查转换后状态 */ true);
    }

    public bool TryTransitionTo(string state, object? trigger = null)
    {
        if (trigger is TOperation op)
        {
            try { _sm.Fire(op); return true; }
            catch { return false; }
        }
        return false;
    }

    public CanonicalForm ToCanonical()
    {
        var form = new CanonicalForm();
        // 枚举所有注册的转移 → CanonicalForm
        // 通过反射或预注册的转移表来构建
        return form;
    }
}
```

### 8.4 配置适配器

```csharp
/// <summary>
/// UIConfiguration → IConfiguration 适配器
/// </summary>
public class UIConfigurationAdapter : IConfiguration
{
    private readonly UIConfiguration _config;

    public string ConfigType => "UI";
    public string SchemaVersion => "1.0";

    public UIConfigurationAdapter(UIConfiguration config)
    {
        _config = config;
    }

    public CanonicalForm ToCanonical()
    {
        var form = new CanonicalForm();
        form.Metadata["source"] = "UIConfiguration";
        form.Metadata["version"] = SchemaVersion;

        // Form → 根节点
        var formNode = new CanonicalNode
        {
            Id = "form",
            Label = _config.Form.Title ?? "Form",
            Type = CanonicalNodeType.View,
            Properties =
            {
                ["width"] = _config.Form.Width,
                ["height"] = _config.Form.Height
            }
        };
        form.Nodes.Add(formNode);

        // Views → 子节点
        foreach (var view in _config.Views)
        {
            var viewNode = new CanonicalNode
            {
                Id = $"view_{view.Key}",
                Label = view.Title ?? view.Key,
                Type = CanonicalNodeType.View
            };
            form.Nodes.Add(viewNode);
            form.Edges.Add(new CanonicalEdge
            {
                Id = $"nav_{view.Key}",
                SourceId = formNode.Id,
                TargetId = viewNode.Id,
                Type = CanonicalEdgeType.Navigation
            });
        }

        // Navigation → 导航边
        // DataBindings → 数据流边
        return form;
    }
}
```

### 8.5 适配器注册

```csharp
/// <summary>
/// 适配器注册中心 — 管理所有领域适配器
/// </summary>
public class AdapterRegistry
{
    private readonly Dictionary<string, Func<object, IConfiguration>> _configAdapters = new();
    private readonly Dictionary<string, Func<object, IStateMachine>> _smAdapters = new();
    private readonly Dictionary<string, Func<object, IGraph>> _graphAdapters = new();

    public void RegisterConfigAdapter<T>(string typeName, Func<T, IConfiguration> adapter)
        where T : class
    {
        _configAdapters[typeName] = obj => adapter((T)obj);
    }

    public void RegisterStateMachineAdapter<T>(string typeName, Func<T, IStateMachine> adapter)
        where T : class
    {
        _smAdapters[typeName] = obj => adapter((T)obj);
    }

    public void RegisterGraphAdapter<T>(string typeName, Func<T, IGraph> adapter)
        where T : class
    {
        _graphAdapters[typeName] = obj => adapter((T)obj);
    }

    public IConfiguration AdaptConfig<T>(T obj) where T : class
    {
        var typeName = typeof(T).FullName ?? typeof(T).Name;
        if (_configAdapters.TryGetValue(typeName, out var adapter))
            return adapter(obj);
        throw new NotSupportedException($"No adapter registered for {typeName}");
    }
}
```

---

## 9. 项目结构

```
CsharpPrj/CSG/
├── CSG.Unified.sln                          # 解决方案文件
│
├── CSG.Unified.Abstractions/                 # 核心抽象层
│   ├── CSG.Unified.Abstractions.csproj
│   ├── CanonicalForm.cs                     # 规范形式
│   ├── CanonicalNode.cs                     # 规范节点
│   ├── CanonicalEdge.cs                     # 规范边
│   ├── CanonicalNodeType.cs                 # 节点类型枚举
│   ├── CanonicalEdgeType.cs                 # 边类型枚举
│   ├── IConfiguration.cs                    # 配置契约
│   ├── IStateMachine.cs                     # 状态机契约
│   ├── IGraph.cs                            # 图契约
│   ├── ITransformEngine.cs                  # 变换引擎接口
│   └── ValidationResult.cs                  # 验证结果
│
├── CSG.Unified.Core/                        # 核心实现
│   ├── CSG.Unified.Core.csproj
│   ├── CanonicalFormBuilder.cs              # 规范形式构建器
│   ├── CanonicalFormJsonConverter.cs        # JSON 序列化/反序列化
│   ├── TransformEngine.cs                   # 变换引擎实现
│   ├── ValidationEngine.cs                  # 一致性验证
│   └── TransformBatch.cs                    # 批量变换
│
├── CSG.Unified.Adapters/                    # 领域适配器
│   ├── CSG.Unified.Adapters.csproj
│   ├── AdapterRegistry.cs                   # 适配器注册中心
│   │
│   ├── Graph/                               # 图适配器
│   │   ├── CoreGraphAdapter.cs              # CoreGraph → IGraph
│   │   └── GraphKeyAdapter.cs               # GraphKey → IGraph
│   │
│   ├── StateMachine/                        # 状态机适配器
│   │   ├── LifecycleSMAdapter.cs            # LifecycleStateMachine → IStateMachine
│   │   ├── SequentialSMAdapter.cs           # SequentialStateMachine → IStateMachine
│   │   ├── ConditionalSMAdapter.cs          # ConditionalStateMachine → IStateMachine
│   │   ├── AsyncEventSMAdapter.cs           # AsyncEventStateMachine → IStateMachine
│   │   └── ScopeStateManagerAdapter.cs      # ScopeStateManager → IStateMachine
│   │
│   └── Configuration/                       # 配置适配器
│       ├── UIConfigurationAdapter.cs        # UIConfiguration → IConfiguration
│       ├── WorkflowDefinitionAdapter.cs     # WorkflowDefinition → IConfiguration
│       └── PipelineDefinitionAdapter.cs     # PipelineDefinition → IConfiguration
│
├── CSG.Unified.Generators/                  # 生成式引擎（可选）
│   ├── CSG.Unified.Generators.csproj
│   ├── GenerativeEngine.cs                  # AI 驱动的生成引擎
│   ├── ConfigGenerator.cs                   # 配置生成器
│   ├── StateMachineGenerator.cs             # 状态机生成器
│   └── GraphGenerator.cs                    # 图生成器
│
└── CSG.Unified.Tests/                       # 单元测试
    ├── CSG.Unified.Tests.csproj
    ├── CanonicalFormTests.cs                # 规范形式测试
    ├── TransformEngineTests.cs              # 变换引擎测试
    ├── TopologyAnalyzerTests.cs             # 拓扑分析测试
    ├── AdapterTests/                        # 适配器测试
    │   ├── CoreGraphAdapterTests.cs
    │   ├── LifecycleSMAdapterTests.cs
    │   └── UIConfigurationAdapterTests.cs
    └── CrossDomainTests.cs                  # 跨域变换测试
```

---

## 10. 实现路线图

### Phase 1: 核心抽象层 (CSG.Unified.Abstractions)

**目标**：定义框架的核心接口和数据结构

| 任务 | 产出 | 预估 |
|------|------|------|
| 定义 `CanonicalNode` / `CanonicalEdge` / `CanonicalForm` | 规范形式数据结构 | 1天 |
| 定义 `IConfiguration` / `IStateMachine` / `IGraph` | 三层投影接口 | 1天 |
| 定义 `ITransformEngine` | 变换引擎接口 | 0.5天 |
| 定义 `ValidationResult` | 验证结果类型 | 0.5天 |
| 单元测试 | 接口契约测试 | 1天 |

### Phase 2: 核心实现 (CSG.Unified.Core)

**目标**：实现变换引擎和验证引擎

| 任务 | 产出 | 预估 |
|------|------|------|
| 实现 `CanonicalFormBuilder` | 规范形式构建器 | 1天 |
| 实现 `CanonicalFormJsonConverter` | JSON 序列化 | 1天 |
| 实现 `TransformEngine` | C↔IR, S↔IR, G↔IR 变换 | 2天 |
| 实现 `ValidationEngine` | 一致性验证 | 1天 |
| 实现 `TransformBatch` | 批量变换 | 1天 |
| 单元测试 | 核心功能测试 | 2天 |

### Phase 3: 领域适配器 (CSG.Unified.Adapters)

**目标**：将现有代码适配到统一框架

| 任务 | 产出 | 预估 |
|------|------|------|
| 实现 `AdapterRegistry` | 适配器注册中心 | 0.5天 |
| 实现 `CoreGraphAdapter` | CoreGraph → IGraph | 1天 |
| 实现 `LifecycleSMAdapter` | LifecycleStateMachine → IStateMachine | 1天 |
| 实现 `SequentialSMAdapter` | SequentialStateMachine → IStateMachine | 0.5天 |
| 实现 `ConditionalSMAdapter` | ConditionalStateMachine → IStateMachine | 0.5天 |
| 实现 `UIConfigurationAdapter` | UIConfiguration → IConfiguration | 1天 |
| 实现 `WorkflowDefinitionAdapter` | WorkflowDefinition → IConfiguration | 1天 |
| 跨域变换测试 | UI↔Workflow 变换测试 | 1天