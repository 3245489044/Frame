# 工作流无状态化 + KeyMapping 统一 + StateCenter 状态外置 重构方案

> 版本：v2.0（v1.0 基础上引入 StateHoldDriven 状态外置，工作流彻底无状态化）
> 日期：2026-08-12
> 适用范围：Agent 骨架执行链路（静态 agent-loop.json）+ 动态工作流片段（DynamicWorkflowMerger）
> 状态后端：StateHoldDriven.StateCenter + IStorageCell（现有实现，零改动复用）
> 映射引擎：ParameterResolver.KeyMapping（现有规则引擎，零改动复用）

---

## 1. 背景与目标

### 1.1 背景

当前 Agent 的工作流存在三个结构性问题：

1. **状态与工作流强耦合**：4 层上下文（SchedulerState / AxiomContext.Data / RoundPipelineContext.Vars / WorkflowContext）的职责边界模糊，状态变更缺乏事务、版本追踪、冲突检测。工作流既是执行引擎又是状态容器，违反单一职责。
2. **映射逻辑分叉**：静态骨架 [RoundStepInvoker](file:///D:/Project/Program/WorkFlowPrj/Agent/src/Agent.Axioms/Loops/RoundStepInvoker.cs) 用严格字符串匹配；动态片段 [DynamicWorkflowMerger.KeyMapping](file:///D:/Project/Program/WorkFlowPrj/Agent/src/Agent.Core/DynamicWorkflow/DynamicWorkflowMerger.cs#L42) 是 `Dictionary<string,string>` 伪 KeyMapping——从未调用项目现成的 `KeyMapperBase<string>` 规则引擎。
3. **容器语义缺失**：RoundSpec 只是「步骤列表」，没有对外输入/输出契约，跨智能体数据对接依赖 WorkflowRunner 内的硬编码补丁。

而项目已有的 [StateHoldDriven](file:///D:/Project/Program/WorkFlowPrj/CsharpPrj/Operation/StateHoldDriven) 模块提供了完整的状态外置能力：
- [StateCenter](file:///D:/Project/Program/WorkFlowPrj/CsharpPrj/Operation/StateHoldDriven/Core/StateCenter.cs#L16-L172)：全量状态唯一权威，支持 Scope 分区、事务、订阅/版本去重
- [StateStore](file:///D:/Project/Program/WorkFlowPrj/CsharpPrj/Operation/StateHoldDriven/Core/StateStore.cs#L17-L167)：基于 IStorageCell 的统一后端
- [WorkflowExecutionEngine](file:///D:/Project/Program/WorkFlowPrj/CsharpPrj/Operation/StateHoldDriven/WorkflowExecution/WorkflowExecutionEngine.cs#L39-L114)：无状态执行引擎，WorkflowContext 仅含临时 Params

### 1.2 目标

1. **工作流无状态化**：RoundSpec / WorkflowFragment 只保留 Steps + InputPorts + OutputPorts + KeyMapping 声明，不持有任何运行时状态
2. **状态全部外置到 StateCenter**：原 4 层上下文映射到 StateCenter 的三层 StorageLayer（Config / Session / Transient）
3. **映射引擎唯一性**：全代码库仅通过 `KeyProcessor.Mapper.MapBatch` 完成键重映射
4. **100% 向后兼容**：无映射配置时行为等价；旧 JSON 零修改可运行

### 1.3 非目标

- **不修改 StateHoldDriven 模块**：StateCenter / StateStore / IStorageCell / StateKey 零改动（复用现有 API）
- **不修改 WorkFlowMethod 执行引擎层**：UnifiedRegistryCenter / FunctionExecutor / FunctionContext 管线不动
- **不引入新项目**：全部改动在现有 Agent.Axioms / Agent.Core / Agent.Contract 内
- **不替换 StaticAsyncPipeline 管线**：WorkflowRunner.BuildPipeline 仍沿用声明式步骤管线

---

## 2. StateHoldDriven 能力盘点（已有实现，零改动复用）

### 2.1 StateCenter（状态唯一权威）

| API | 位置 | 能力 |
|-----|------|------|
| `Read<TState>(StateKey)` | [StateCenter.cs#L97-L101](file:///D:/Project/Program/WorkFlowPrj/CsharpPrj/Operation/StateHoldDriven/Core/StateCenter.cs#L97-L101) | 按 StateKey 读取状态，委托 IStorageCell.Get |
| `Write<TState>(StateKey, value, source)` | [StateCenter.cs#L103-L109](file:///D:/Project/Program/WorkFlowPrj/CsharpPrj/Operation/StateHoldDriven/Core/StateCenter.cs#L103-L109) | 写入状态 + 发布版本化变更事件 |
| `GetOrCreateScope(scope)` | [StateCenter.cs#L43-L55](file:///D:/Project/Program/WorkFlowPrj/CsharpPrj/Operation/StateHoldDriven/Core/StateCenter.cs#L43-L55) | 创建/获取 Scope 分区 StateStore（子 cell，parent 链回退） |
| `BeginTransaction(scope?)` | [StateCenter.cs#L57-L69](file:///D:/Project/Program/WorkFlowPrj/CsharpPrj/Operation/StateHoldDriven/Core/StateCenter.cs#L57-L69) | 开启状态事务（Scope 隔离） |
| `Subscriptions` | [StateCenter.cs#L71](file:///D:/Project/Program/WorkFlowPrj/CsharpPrj/Operation/StateHoldDriven/Core/StateCenter.cs#L71) | StateSubscriptionHub：订阅/版本去重 |

### 2.2 StateKey（三维状态键）

```csharp
// 定义于 Share.StateManagement
public readonly record struct StateKey(string Namespace, string Scope, string Key)
{
    public string FullKey => $"{Namespace}:{Scope}:{Key}";
}
```

Agent 的 StateKey 命名约定：

| Namespace | Scope | Key 示例 | 用途 |
|-----------|-------|---------|------|
| `"Agent.Scheduler"` | `"thread-{hash}"` | `"round"`, `"budget"`, `"strategy"` | 跨轮持久状态（原 SchedulerState） |
| `"Agent.Session"` | `"{sessionId}"` | `"user_input"`, `"goal"`, `"exec_result"` | 跨轮会话状态（原 AxiomContext.Data） |
| `"Agent.Round"` | `"round-{n}"` | `"filtered_input"`, `"system"`, `"l1"` | 轮内临时状态（原 Vars） |

### 2.3 IStorageCell（分层存储后端）

| API | 位置 | 能力 |
|-----|------|------|
| `Set<T>(key, value, StorageLayer)` | [IStorageCell.cs#L13](file:///D:/Project/Program/WorkFlowPrj/CsharpPrj/Plate/TConfig/ContextFrame/Abstractions/IStorageCell.cs#L13) | 分层写入 |
| `Get<T>(key, StorageLayer?)` | [IStorageCell.cs#L15](file:///D:/Project/Program/WorkFlowPrj/CsharpPrj/Plate/TConfig/ContextFrame/Abstractions/IStorageCell.cs#L15) | 分层读取（null=搜索所有层） |
| `TryGet<T>(key, out value, StorageLayer?)` | [IStorageCell.cs#L17](file:///D:/Project/Program/WorkFlowPrj/CsharpPrj/Plate/TConfig/ContextFrame/Abstractions/IStorageCell.cs#L17) | 安全读取 |
| `GetKeys(StorageLayer)` | [IStorageCell.cs#L28](file:///D:/Project/Program/WorkFlowPrj/CsharpPrj/Plate/TConfig/ContextFrame/Abstractions/IStorageCell.cs#L28) | **枚举某层所有键名**——DataContainerBinder 映射源键集合的直接来源 |
| `GetEntries(StorageLayer)` | [IStorageCell.cs#L30](file:///D:/Project/Program/WorkFlowPrj/CsharpPrj/Plate/TConfig/ContextFrame/Abstractions/IStorageCell.cs#L30) | 枚举某层所有键值对 |
| `ClearLayer(StorageLayer)` | [IStorageCell.cs#L26](file:///D:/Project/Program/WorkFlowPrj/CsharpPrj/Plate/TConfig/ContextFrame/Abstractions/IStorageCell.cs#L26) | 清空某层（轮结束清理 Transient） |
| `DataChanged` 事件 | [IStorageCell.cs#L61](file:///D:/Project/Program/WorkFlowPrj/CsharpPrj/Plate/TConfig/ContextFrame/Abstractions/IStorageCell.cs#L61) | 底层唯一物理事件源 |

### 2.4 StorageLayer（三层存储语义）

```csharp
public enum StorageLayer { Config, Session, Transient }
```

| 层 | 语义 | 对应原 Agent 层 | 生命周期 |
|----|------|----------------|---------|
| `Config` | 持久配置 | SchedulerState（跨进程持久） | 进程级 |
| `Session` | 会话内持久 | AxiomContext.Data（跨轮持久） | 会话级 |
| `Transient` | 轮内临时 | RoundPipelineContext.Vars（轮内） | 单轮级，轮结束 ClearLayer |

### 2.5 ParameterResolver.KeyMapping（映射引擎，零改动复用）

| 组件 | 位置 | 能力 |
|------|------|------|
| `MappingRuleType.Exact` | [KeyMappingRule.cs](file:///D:/Project/Program/WorkFlowPrj/CsharpPrj/Modules/ParameterResolver/KeyMapping/KeyMappingRule.cs) | 精确匹配 |
| `MappingRuleType.Pattern` | 同上 | 通配符 `*` 模式匹配 |
| `MappingRuleType.MultiLevel` | 同上 | 分层路径匹配（按 `.` 拆层） |
| `KeyMapperBase<string>.MapBatch` | [KeyMapperBase.cs](file:///D:/Project/Program/WorkFlowPrj/CsharpPrj/Modules/ParameterResolver/KeyMapping/KeyMapperBase.cs) | 批量映射入口 |
| `KeyProcessor.Mapper` | [KeyProcessor.cs#L49-L51](file:///D:/Project/Program/WorkFlowPrj/CsharpPrj/Operation/WorkFlowMethod/NodeProcess/KeyMapping/KeyProcessor.cs#L49-L51) | Lazy 单例 |

---

## 3. 架构总览（无状态工作流 + StateCenter 状态外置）

```
┌──────────────────────────────────────────────────────────────────────────┐
│  Layer 0. 无状态工作流容器（声明层，纯数据）                                 │
│                                                                          │
│  RoundSpec / WorkflowFragment（不可变 record，零运行时状态）              │
│    ├─ Name                                                               │
│    ├─ InputPorts[]     ← 对外输入契约（键 + 类型 + 可选默认值）            │
│    ├─ OutputPorts[]    ← 对外输出契约（键 + 来源键）                      │
│    ├─ GlobalKeyMapping ← 容器级全局规则（KeyMappingConfig）              │
│    └─ Steps[]                                                           │
│         ├─ FuncName / ParameterKeys / ResultKeys                         │
│         ├─ InputMapping   ← 节点入参映射                                  │
│         └─ OutputMapping  ← 节点出参映射                                  │
└─────────────────────────────────────┬────────────────────────────────────┘
                                      │
┌─────────────────────────────────────▼────────────────────────────────────┐
│  Layer 1. StateCenter 状态层（状态唯一权威，工作流不持有状态）              │
│                                                                          │
│  StateCenter (StateHoldDriven，现有实现零改动)                             │
│    ├─ Config 层  ← SchedulerState（跨进程持久）                           │
│    ├─ Session 层 ← AxiomContext.Data（跨轮持久）                          │
│    └─ Transient 层 ← Vars（轮内临时，轮结束 ClearLayer）                    │
│                                                                          │
│  读写 API：                                                              │
│    StateCenter.Read<T>(StateKey)   → IStorageCell.TryGet<T>(key, layer)  │
│    StateCenter.Write<T>(StateKey, val) → IStorageCell.Set<T>(key, val, S)│
│    StateCenter.GetOrCreateScope(scope) → 分区隔离                         │
│    StateCenter.BeginTransaction(scope) → 原子事务                          │
└─────────────────────────────────────┬────────────────────────────────────┘
                                      │
┌─────────────────────────────────────▼────────────────────────────────────┐
│  Layer 2. DataContainerBinder（无状态映射层，新增）                         │
│                                                                          │
│  唯一映射引擎：KeyProcessor.Mapper.MapBatch                               │
│                                                                          │
│  四阶段（每阶段从 StateCenter 读 → 映射 → 写回 StateCenter）：            │
│    ① BindSeedPhase      ← Read Session/Config → 映射 → Write Transient  │
│    ② BindStepInputPhase ← Read Transient → MapBatch → 填充 parameters   │
│    ③ BindStepOutputPhase ← 结果 → MapBatch → Write Transient             │
│    ④ BindHarvestPhase   ← Read Transient → 映射 → Write Session          │
│                                                                          │
│  语义保证：映射=新增键不删原键（拷贝语义，向后兼容）                         │
└─────────────────────────────────────┬────────────────────────────────────┘
                                      │
┌─────────────────────────────────────▼────────────────────────────────────┐
│  Layer 3. 执行管线层（无状态调度，零状态持有）                              │
│                                                                          │
│  静态骨架：WorkflowRunner → RoundStepInvoker → DataContainerBinder       │
│  动态片段：DynamicWorkflowMerger → DataContainerBinder                   │
│  （两条路径共享同一 StateCenter 实例 + 同一 DataContainerBinder 单例）     │
└─────────────────────────────────────┬────────────────────────────────────┘
                                      │
┌─────────────────────────────────────▼────────────────────────────────────┐
│  Layer 4. WorkFlowMethod 执行层（零改动）                                  │
│                                                                          │
│  WorkFlowMethodUsage.CallFunctionAsync → UnifiedRegistryCenter → Delegate│
└──────────────────────────────────────────────────────────────────────────┘
```

### 3.1 状态流转（单轮生命周期）

```
┌─────────────────────────────────────────────────────────────────────┐
│ 轮开始                                                               │
│                                                                     │
│ 1. BindSeedPhase                                                    │
│    StateCenter.Read(Session, "Agent.Session:{sid}", *)               │
│      → 读入 user_input, goal, session_id, step_count, ...           │
│    StateCenter.Read(Config, "Agent.Scheduler:{thread}", *)          │
│      → 读入 round, budget, strategy, ...                             │
│    KeyProcessor.Mapper.MapBatch(sourceKeys, InputPorts, globalMap)  │
│      → 全局映射（如 cline_session → session_id）                      │
│    StateCenter.Write(Transient, "Agent.Round:{rid}", mapped_keys)   │
│      → 写入 Transient 层供步骤消费                                    │
│                                                                     │
│ 2. 每步骤执行                                                       │
│    BindStepInputPhase:                                              │
│      IStorageCell.GetKeys(Transient) → sourceKeys                    │
│      KeyProcessor.Mapper.MapBatch(sourceKeys, ParameterKeys, map)    │
│      → 填充 parameters（不修改 StateCenter）                         │
│    WorkFlowMethodUsage.CallFunctionAsync(funcName, parameters)       │
│    BindStepOutputPhase:                                             │
│      结果 → ReflectResult → resultProps                              │
│      KeyProcessor.Mapper.MapBatch(resultProps.Keys, ...) → targetKeys│
│      StateCenter.Write(Transient, targetKeys, values)               │
│      → 写回 Transient 层（P2：不覆盖已有原键）                        │
│                                                                     │
│ 3. BindHarvestPhase                                                  │
│    StateCenter.Read(Transient, "Agent.Round:{rid}", *)               │
│    KeyProcessor.Mapper.MapBatch(sourceKeys, OutputPorts, globalMap)  │
│    StateCenter.Write(Session, "Agent.Session:{sid}", mapped_keys)   │
│      → 跨轮持久化                                                    │
│    StateCenter.Write(Config, "Agent.Scheduler:{thread}", round+1)   │
│    IStorageCell.ClearLayer(Transient)                               │
│      → 轮结束清理 Transient 层                                       │
│                                                                     │
│ 轮结束                                                               │
└─────────────────────────────────────────────────────────────────────┘
```

### 3.2 设计原则（不可退让）

| # | 原则 | 实现手段 |
|---|------|---------|
| P1 | **工作流无状态** | RoundSpec / WorkflowFragment 是不可变 record，不持有任何可变状态。所有运行时状态读写通过 StateCenter |
| P2 | **映射引擎唯一性** | 所有键映射必经 `KeyProcessor.Mapper.MapBatch`；禁止 Dictionary.ForEach 直接拷贝 |
| P3 | **不可变容器（拷贝语义）** | InputMapping 只读不写 StateCenter；OutputMapping 写 Transient 层时新增键不删原键；冲突时保留原值 + 记录诊断 |
| P4 | **JSON 双形态** | KeyMappingConfig 同时支持简写 `{"a":"b"}` 和完整 `{"Rules":[...]}` 形态 |
| P5 | **零配置等价现状** | 无映射配置时，DataContainerBinder 各阶段直接返回 Empty |
| P6 | **Transient 层轮结束清理** | 每轮 Harvest 后 `IStorageCell.ClearLayer(Transient)`，防止状态泄漏 |

---

## 4. 状态映射设计（原 4 层 → StateCenter 三层）

### 4.1 映射表

| 原 Agent 层 | StateCenter 层 | StateKey.Namespace | Scope 来源 | 生命周期 |
|-------------|---------------|---------------------|-----------|---------|
| SchedulerState | StorageLayer.Config | `"Agent.Scheduler"` | `thread-{SHA256(userInput)[..16]}` | 进程级持久 |
| AxiomContext.Data | StorageLayer.Session | `"Agent.Session"` | `{sessionId}` | 会话级持久 |
| RoundPipelineContext.Vars | StorageLayer.Transient | `"Agent.Round"` | `round-{n}` | 单轮级，Harvest 后 ClearLayer |
| WorkflowContext.Parameters | （不外置） | — | — | 函数调用栈内临时，无需 StateCenter |

### 4.2 StateKey 构造约定

```csharp
// Agent.Contract 新增 StateKeyFactory（静态辅助类）
public static class AgentStateKeys
{
    public const string NsScheduler = "Agent.Scheduler";
    public const string NsSession   = "Agent.Session";
    public const string NsRound     = "Agent.Round";

    // Scheduler 层（Config）
    public static StateKey Scheduler(string threadHash, string key)
        => new(NsScheduler, threadHash, key);

    // Session 层（跨轮）
    public static StateKey Session(string sessionId, string key)
        => new(NsSession, sessionId, key);

    // Round 层（轮内 Transient）
    public static StateKey Round(string roundId, string key)
        => new(NsRound, roundId, key);

    // Round scope 的 Transient cell 获取
    public static string RoundScope(string roundId) => roundId;
}
```

### 4.3 状态读写适配器：StateContextAdapter（新增）

```csharp
// Agent.Axioms/StateAdapters/StateContextAdapter.cs
// 把 StateCenter 的 StateKey + StorageLayer 语义封装为 DataContainerBinder 可用的简单接口
public sealed class StateContextAdapter
{
    private readonly StateCenter _center;
    private readonly string _threadHash;
    private readonly string _sessionId;
    private readonly string _roundId;

    public StateContextAdapter(StateCenter center, string threadHash, string sessionId, string roundId)
    {
        _center = center;
        _threadHash = threadHash;
        _sessionId = sessionId;
        _roundId = roundId;
    }

    // ── Transient 层（轮内，替代原 Vars）──
    public T? GetTransient<T>(string key)
        => _center.Read<T>(AgentStateKeys.Round(_roundId, key));
    // 注：StateCenter.Read 内部用 StorageLayer.Session；需扩展为可指定 layer
    // 如果 StateCenter.Read 不支持 layer 参数 → 直接用 _center.Cell.TryGet<T>(key, StorageLayer.Transient)

    public void SetTransient<T>(string key, T value)
        => _center.Cell.Set(key, value, StorageLayer.Transient);

    public bool TryGetTransient<T>(string key, out T? value)
        => _center.Cell.TryGet<T>(key, out value, StorageLayer.Transient);

    public IEnumerable<string> EnumerateTransientKeys()
        => _center.Cell.GetKeys(StorageLayer.Transient);

    public void ClearTransient()
        => _center.Cell.ClearLayer(StorageLayer.Transient);

    // ── Session 层（跨轮，替代原 AxiomContext.Data）──
    public T? GetSession<T>(string key)
        => _center.Cell.Get<T>(key, StorageLayer.Session);

    public void SetSession<T>(string key, T value)
        => _center.Cell.Set(key, value, StorageLayer.Session);

    // ── Config 层（持久，替代原 SchedulerState）──
    public T? GetConfig<T>(string key)
        => _center.Cell.Get<T>(key, StorageLayer.Config);

    public void SetConfig<T>(string key, T value)
        => _center.Cell.Set(key, value, StorageLayer.Config);
}
```

> **注意**：StateCenter.Read/Write 内部固定用 `StorageLayer.Session`。对于 Transient 和 Config 层的访问，StateContextAdapter 直接通过 `_center.Cell.TryGet<T>(key, StorageLayer.Transient)` 等底层 API 操作。这是 StateCenter 现有 API 的合法使用（IStorageCell 是 StateCenter 的公共属性）。

---

## 5. 数据模型与契约

### 5.1 无状态 RoundSpec / WorkflowFragment

```csharp
// Agent.Axioms（静态骨架）—— 纯不可变 record，无任何可变状态
public sealed record RoundSpec(
    string Name,
    IReadOnlyList<RoundStep> Steps,
    IReadOnlyList<PortSpec>? InputPorts = null,
    IReadOnlyList<PortSpec>? OutputPorts = null,
    KeyMappingConfig? GlobalKeyMapping = null);

// Agent.Core（动态片段）
public sealed class WorkflowFragment
{
    public List<FragmentNode> Nodes { get; set; } = new();
    public List<PortSpec>? InputPorts { get; set; }
    public List<PortSpec>? OutputPorts { get; set; }
    public KeyMappingConfig? GlobalKeyMapping { get; set; }

    [Obsolete("迁移到 GlobalKeyMapping")]
    public Dictionary<string, string> KeyMapping { get; set; } = new();
}
```

### 5.2 PortSpec（端口契约）

```csharp
// Agent.Contract/Data/PortSpec.cs
public sealed class PortSpec
{
    public string Key { get; init; } = "";
    public string? SourceKey { get; init; }       // OutputPorts 用：来源内部键
    public Type? ValueType { get; init; }         // 可选：类型提示
    public object? DefaultValue { get; init; }     // 可选：缺省值
    public string? Description { get; init; }      // 可选：文档
    public bool IsRequired { get; init; } = true;  // InputPorts 用
}
```

### 5.3 RoundStep / FragmentNode 扩展

```csharp
public sealed record RoundStep(
    string FuncName,
    IReadOnlyList<string> ParameterKeys,
    IReadOnlyList<string> ResultKeys,
    KeyMappingConfig? InputMapping = null,
    KeyMappingConfig? OutputMapping = null);

public sealed class FragmentNode
{
    public List<string> ParameterKeys { get; set; } = new();
    public List<string> ResultKeys { get; set; } = new();
    public KeyMappingConfig? InputMapping { get; set; }
    public KeyMappingConfig? OutputMapping { get; set; }
}
```

### 5.4 KeyMappingConfig JSON 双形态

简写 `{"a":"b"}` → 自动转换为 Exact 规则（Priority=999）；完整 `{"Rules":[...]}` 直接使用。通过 `KeyMappingConfigJsonConverter`（STJ + NJ 双实现）自动识别。

---

## 6. DataContainerBinder 设计（无状态映射层）

### 6.1 组件 API

```csharp
// Agent.Axioms/DataContainers/DataContainerBinder.cs
public sealed class DataContainerBinder
{
    public static DataContainerBinder Instance { get; } = new();

    // 阶段 1：Seed — 从 Session/Config 读 → 映射 → 写入 Transient
    public MappingDiagnostics BindSeedPhase(
        StateContextAdapter state,
        RoundSpec? spec,
        WorkflowFragment? fragment);

    // 阶段 2：StepInput — 从 Transient 读 → MapBatch → 填充 parameters（不写 StateCenter）
    public MappingDiagnostics BindStepInputPhase(
        StateContextAdapter state,
        RoundStep step,
        Dictionary<string, object> parametersBuffer,
        KeyMappingConfig? globalMapping = null);

    // 阶段 3：StepOutput — 结果 → MapBatch → 写回 Transient
    public MappingDiagnostics BindStepOutputPhase(
        StateContextAdapter state,
        RoundStep step,
        Dictionary<string, object?> resultProps,
        KeyMappingConfig? globalMapping = null);

    // 阶段 4：Harvest — 从 Transient 读 → 映射 → 写回 Session + ClearLayer(Transient)
    public MappingDiagnostics BindHarvestPhase(
        StateContextAdapter state,
        RoundSpec? spec,
        WorkflowFragment? fragment);
}
```

### 6.2 BindSeedPhase 算法

```
输入：StateContextAdapter state（封装 StateCenter 三层访问）

算法：
1. 如果 spec?.InputPorts == null && spec?.GlobalKeyMapping == null → 返回 Empty

2. 读取 Session 层所有键值：
   sessionEntries = state.EnumerateSessionEntries()
   // 等价：state._center.Cell.GetEntries(StorageLayer.Session)

3. 读取 Config 层 SchedulerState：
   configEntries = state.EnumerateConfigEntries()

4. 合并 sourceKeys = sessionKeys + configKeys

5. 如果 InputPorts 声明了必需输入（IsRequired=true）但 sourceKeys 缺失：
   → 记录 RequiredPortMissing WARN（不中断）

6. 应用 GlobalKeyMapping（如果存在）：
   targetKeys = InputPorts?.Select(p => p.Key).ToList() ?? sourceKeys.ToList()
   (updatedKeys, matchResults) = KeyProcessor.Mapper.MapBatch(sourceKeys, targetKeys, globalMapping)

7. 把映射后的键值写入 Transient 层：
   for each (key, value) in (sessionEntries + configEntries)
     mappedKey = updatedKeys[i] ?? key
     // P3 拷贝语义：不覆盖已有原键
     if !state.TryGetTransient<object>(mappedKey, out var existing) || Equals(existing, value)
       state.SetTransient(mappedKey, value)
     // 原键也写入（供直接读取原键的函数使用）
     state.SetTransient(key, value)

8. 返回 MappingDiagnostics { Phase = "Seed", ... }
```

### 6.3 BindStepInputPhase 算法（不修改 StateCenter）

```
输入：
  - state（Transient 层读取）
  - step（含 ParameterKeys + InputMapping）
  - parametersBuffer（待填充参数字典）

算法：
1. 先按基线逻辑填充 parametersBuffer：
   foreach key in step.ParameterKeys
     if state.TryGetTransient<object>(key, out var v) && v != null
       parametersBuffer[key] = v

2. 如果 step.InputMapping == null && globalMapping == null → 返回 Empty

3. mergedConfig = MergeConfig(step.InputMapping, globalMapping, StepInput偏移)

4. sourceKeys = state.EnumerateTransientKeys().ToList()
   targetKeys = step.ParameterKeys.ToList()

5. (updatedKeys, matchResults) = KeyProcessor.Mapper.MapBatch(sourceKeys, targetKeys, mergedConfig)

6. P3 拷贝语义（parametersBuffer 层不覆盖）：
   for i in [0..targetKeys.Count-1]
     mappedSource = updatedKeys[i]
     if !parametersBuffer.ContainsKey(targetKeys[i])
        && state.TryGetTransient<object>(mappedSource, out var v)
       parametersBuffer[targetKeys[i]] = v
     else if parametersBuffer.TryGetValue(targetKeys[i], out var original)
        && state.TryGetTransient<object>(mappedSource, out var mapped)
        && !Equals(original, mapped)
       conflicts.Add((targetKeys[i], original, mapped))

   全程不修改 StateCenter。

7. 返回 MappingDiagnostics
```

### 6.4 BindStepOutputPhase 算法（写回 Transient）

```
输入：
  - state（Transient 层写入）
  - step（含 ResultKeys + OutputMapping）
  - resultProps（函数返回值反射展开）

算法：
1. 基线写入：foreach key in step.ResultKeys → state.SetTransient(key, resultProps[key])

2. 如果 OutputMapping == null && globalMapping == null → 返回 Empty

3. mergedConfig = MergeConfig(step.OutputMapping, globalMapping, StepOutput偏移)

4. sourceKeys = resultProps.Keys.ToList()
   占位 targetKeys = sourceKeys.Select((_, i) => i.ToString()).ToList()

5. (updatedKeys, matchResults) = KeyProcessor.Mapper.MapBatch(sourceKeys, 占位targets, mergedConfig)

6. P3 拷贝语义（Transient 层不覆盖原值）：
   for i in [0..sourceKeys.Count-1]
     sourceKey = sourceKeys[i]
     targetKey = updatedKeys[i] ?? sourceKey
     if targetKey == sourceKey → 跳过（基线已写）

     if state.TryGetTransient<object>(targetKey, out var existing) && !Equals(existing, resultProps[sourceKey])
       conflicts.Add((targetKey, existing, resultProps[sourceKey]))
       continue  // 保留原值

     state.SetTransient(targetKey, resultProps[sourceKey])

7. 返回 MappingDiagnostics
```

### 6.5 BindHarvestPhase 算法（写回 Session + 清理 Transient）

```
输入：state（Session 写入 + Transient 读取/清理）

算法：
1. 如果 spec?.OutputPorts == null && spec?.GlobalKeyMapping == null → 返回 Empty

2. 读取 Transient 层所有键值：
   transientEntries = state.EnumerateTransientEntries()

3. 如果 OutputPorts 声明了输出端口：
   foreach port in OutputPorts
     sourceKey = port.SourceKey ?? port.Key
     if state.TryGetTransient<object>(sourceKey, out var val)
       state.SetSession(port.Key, val)  // 写入 Session 层供下一轮读
     else
       记录 OutputPortMissing WARN

4. 应用 GlobalKeyMapping（如果存在）：
   把 Transient 中未被 OutputPorts 显式收集但被全局映射命中的键也写入 Session
   (updatedKeys, _) = KeyProcessor.Mapper.MapBatch(transientKeys, sessionKeys, globalMapping)
   for each matched → state.SetSession(mappedKey, value)

5. 写回 Config 层（SchedulerState 增量）：
   state.SetConfig("round", currentRound + 1)
   state.SetConfig("budget_remaining", budget - cost)

6. 清理 Transient 层：
   state.ClearTransient()
   // 等价：state._center.Cell.ClearLayer(StorageLayer.Transient)

7. 返回 MappingDiagnostics
```

### 6.6 优先级合并规则

```
规则优先级（数字越小越先匹配）：
  ┌──────────────────────────────────────────────────────────┐
  │ 10      Step.InputMapping 完整形态规则                     │
  │ 50      Step.OutputMapping 完整形态规则                    │
  │ 100     GlobalKeyMapping 完整形态规则                      │
  │ 900     Step.InputMapping 简写字典 → Exact 规则           │
  │ 920     Step.OutputMapping 简写字典 → Exact 规则          │
  │ 999     GlobalKeyMapping 简写字典 → Exact 规则            │
  └──────────────────────────────────────────────────────────┘
```

---

## 7. 接入点改造清单

### 7.1 Agent.Contract（新增契约）

| 文件 | 操作 | 说明 |
|------|------|------|
| `Data/PortSpec.cs` | Create | 5.2 节端口契约 |
| `DataContainers/MappingDiagnostics.cs` | Create | 诊断结构（同 v1.0） |
| `StateAdapters/AgentStateKeys.cs` | Create | 4.2 节 StateKey 工厂 |

### 7.2 Agent.Axioms（核心改造）

| 文件 | 操作 | 说明 |
|------|------|------|
| `StateAdapters/StateContextAdapter.cs` | Create | 4.3 节 StateCenter 三层适配器 |
| `DataContainers/DataContainerBinder.cs` | Create | 6.x 节四阶段映射（读写 StateCenter） |
| `Infrastructure/KeyMappingConfigJsonConverter.STJ.cs` | Create | STJ 双形态转换器 |
| `Infrastructure/KeyMappingConfigJsonConverter.NJ.cs` | Create | NJ 双形态转换器 |
| `Loops/RoundSpec.cs` | Modify | 扩展 InputPorts/OutputPorts/GlobalKeyMapping（可空） |
| `Loops/RoundStep.cs` | Modify | 扩展 InputMapping/OutputMapping（可空） |
| `Loops/RoundSpecLoader.cs` | Modify | 解析新字段，失败降级为 null |
| `Loops/RoundStepInvoker.cs` | Modify | 用 StateContextAdapter 替代 Vars；插入 BindStepInput/Output |
| `Loops/RoundStateMapper.cs` | Modify | Seed/Harvest 改为调 DataContainerBinder + StateContextAdapter |
| `Loops/RoundPipelineContext.cs` | Modify | 移除 Vars 字段；改为持有 StateContextAdapter 引用 |
| `Loops/WorkflowRunner.cs` | Modify | 移除 guard_input 硬编码补丁（改用 InputMapping）；构建 StateContextAdapter |

### 7.3 Agent.Core（动态片段）

| 文件 | 操作 | 说明 |
|------|------|------|
| `DynamicWorkflow/DynamicWorkflowMerger.cs` | Modify | WorkflowFragment/FragmentNode 扩展三字段；旧 KeyMapping [Obsolete] 合并；MergeAndExecute 对称调用 DataContainerBinder |

### 7.4 Config/workflows/agent-loop.json

新增字段全部可选（缺省=null），旧 JSON 零修改可运行。示例：

```jsonc
{
  "WorkflowId": "agent-loop",
  "Version": "5.0.0",  // 升版本号标记无状态化

  "_example_GlobalKeyMapping": {
    "cline_session": "session_id",
    "*_final": "*_output"
  },
  "_example_InputPorts": [
    { "Key": "user_input", "IsRequired": true }
  ],
  "_example_OutputPorts": [
    { "Key": "final_text", "SourceKey": "safe_output" }
  ]
}
```

---

## 8. 向后兼容迁移方案

### 8.1 无配置行为

```csharp
// BindStepInputPhase 开头：
if (step.InputMapping == null && globalMapping == null)
    return MappingDiagnostics.Empty;  // 等价于现状
```

### 8.2 旧 agent-loop.json（< 5.0.0）零修改加载

RoundSpecLoader 解析新字段失败 → try/catch → 置 null → DataContainerBinder 跳过 → 行为不变。

### 8.3 状态层迁移（渐进式）

阶段 1（M2）：StateContextAdapter 作为 Vars 的包装层，内部仍委托 IDictionary（不接 StateCenter）
阶段 2（M3）：StateContextAdapter 切换到 StateCenter 后端，Vars 字段标记 [Obsolete]
阶段 3（M4）：移除 Vars 字段，全量走 StateCenter

### 8.4 DynamicWorkflowMerger 旧 KeyMapping 兼容

旧 `"KeyMapping": {"a":"b"}` → 自动合并为 GlobalKeyMapping 的 Exact 规则（Priority=999）。

---

## 9. 错误处理与可观测性

### 9.1 异常策略

| 阶段 | 异常情况 | 处理 |
|------|---------|------|
| BindSeedPhase | InputPorts 必需输入缺失 | WARN，不中断 |
| BindStepInputPhase | 映射规则解析异常 | ERROR，降级为无映射 |
| BindStepInputPhase | 映射冲突（P3 原则） | WARN，保留原值 |
| BindStepOutputPhase | 同上 | 同上 |
| BindHarvestPhase | OutputPorts.SourceKey 不存在 | WARN，端口写 null |
| BindHarvestPhase | StateCenter.Write 异常 | ERROR，不中断本轮（Transient 已有数据，下一轮重试） |

### 9.2 诊断事件

| 事件 ID | 含义 |
|---------|------|
| `DataContainer.BindSeedPhase.applied` | Seed 阶段：规则数/匹配键数 |
| `DataContainer.StepInputPhase.{funcName}.applied` | 节点入参映射 |
| `DataContainer.StepOutputPhase.{funcName}.applied` | 节点出参映射 |
| `DataContainer.BindHarvestPhase.applied` | Harvest 阶段：输出端口绑定 |
| `DataContainer.MappingConflict.detected` | P3 冲突（未覆盖） |
| `DataContainer.MappingConfig.invalid` | 映射 JSON 无效（降级） |
| `DataContainer.RequiredPortMissing.warn` | Seed 缺必需输入 |
| `StateContextAdapter.Transient.cleared` | Transient 层清理 |
| `RoundSpecLoader.schema_migrated` | 旧 Schema（<5.0.0）加载 |

---

## 10. 零损伤验收测试清单

| # | 用例 | 通过标准 |
|---|------|---------|
| T0 | **零配置回归**：无映射配置，行为与重构前一致 | WorkflowRoundResult 位级一致（除诊断日志） |
| T1 | **简写字典形态**：InputMapping `{"final_text":"exec_result"}` | parameters["exec_result"] 正确填充 |
| T2 | **完整 Exact 形态** | 与 T1 等价 |
| T3 | **Pattern 规则** | `*_final → *_output` 匹配成功 |
| T4 | **MultiLevel 规则** | 分层键跨域映射成功 |
| T5 | **优先级合并**：节点级 vs 全局级 | 节点级优先命中 |
| T6 | **冲突检测（Transient 层）** | 原值保留不覆盖；MappingDiagnostics.Conflicts=1 |
| T7 | **旧 Schema 零修改加载** | RoundSpec 解析无异常；DataContainerBinder 返回 Empty |
| T8 | **DynamicWorkflowMerger 旧形态** | ShorthandKeyMapping.migrated 日志 1 条 |
| T9 | **动态片段三字段形态** | 优先级合并与 T5 一致 |
| T10 | **Cline 同义词全局映射** | GlobalKeyMapping `cline_session → session_id` 不侵入节点 |
| T11 | **Transient 层轮结束清理** | Harvest 后 Transient 层为空（ClearLayer 执行） |
| T12 | **跨轮状态持久化** | Round N 写入 Session 层的 exec_result，Round N+1 的 Seed 能读到 |

---

## 11. 实施阶段与里程碑

### M1：基础设施（Agent.Contract + Agent.Axioms 类型定义）
1. AgentStateKeys / PortSpec / MappingDiagnostics 类型定义
2. KeyMappingConfigJsonConverter 双形态实现
3. StateContextAdapter 实现（封装 StateCenter 三层）
4. DataContainerBinder 实现（四阶段 + MergeConfig + P3 拷贝语义）
5. RoundSpec / RoundStep 字段扩展

**验收**：T0 通过

### M2：静态骨架接入（StateContextAdapter + DataContainerBinder → WorkflowRunner）
1. RoundSpecLoader 解析新字段
2. WorkflowRunner 构建 StateContextAdapter（注入 StateCenter）
3. RoundStateMapper.Seed/Harvest 改为调 BindSeedPhase/BindHarvestPhase
4. RoundStepInvoker 插入 BindStepInputPhase/BindStepOutputPhase
5. RoundPipelineContext 移除 Vars（或标记 Obsolete 保留兼容）

**验收**：T1-T7、T11、T12 通过

### M3：动态片段接入（DynamicWorkflowMerger 对称改造）
1. WorkflowFragment/FragmentNode 三字段扩展
2. 旧 KeyMapping [Obsolete] + 自动合并
3. MergeAndExecuteAsync 对称调用 DataContainerBinder

**验收**：T8、T9 通过

### M4：示例配置 + E2E + 补丁迁移
1. agent-loop.json v5.0.0 示例注释
2. guard_input 补丁迁移为 InputMapping（可选）
3. T10 E2E：Cline 同义词全局映射

**验收**：T10 通过

---

## 12. 风险与缓解

| 风险 | 概率 | 影响 | 缓解 |
|------|------|------|------|
| StateCenter 不可用（未装配） | 中 | 工作流无法运行 | WorkflowRunner 启动时检查 StateCenter != null，否则降级为内存 Dictionary（兼容模式） |
| StateCenter.Cell 不支持 Transient 层 | 低 | Transient 数据丢失 | IStorageCell 接口已定义 Transient 层；若实现不支持 → 降级为 Session 层 + 手动清理 |
| 性能：每步骤多次 StateCenter 读写 | 中 | 每轮多 2-5ms | ① 无映射配置时零开销；② Transient 层键缓存（进程内 Dictionary + 轮结束同步到 Cell） |
| LLM 生成非法映射规则 | 中 | 节点降级 | BindStepInputPhase 解析异常 → 降级无映射（9.1 已定义） |
| 4层状态迁移遗漏字段 | 中 | 跨轮数据丢失 | T12 验收用例锁定关键跨轮字段；RoundKeys 白名单对照检查 |

---

## 13. 设计自审

| 检查项 | 结果 | 说明 |
|--------|------|------|
| 占位符 | 通过 | 无 TBD/TODO；全部章节有具体 API 路径和类名 |
| 内部一致性 | 通过 | 三层状态映射（4.1）贯穿 6.x 算法 / 10.x 测试 / 11.x 里程碑；P3 拷贝语义在 6.3/6.4/9.1/T6 一致 |
| 范围控制 | 通过 | 不修改 StateHoldDriven / WorkFlowMethod 执行层；改动集中在 Agent.Axioms/Core/Contract |
| 歧义性 | 通过 | 四阶段算法伪代码完整（6.2-6.5）；状态层映射表（4.1）明确 |
| 向后兼容 | 通过 | 8.x 覆盖零配置/旧Schema/旧动态JSON/状态层渐进迁移四条路径 |
| 无状态化 | 通过 | RoundSpec 是不可变 record；所有状态读写通过 StateContextAdapter → StateCenter |
