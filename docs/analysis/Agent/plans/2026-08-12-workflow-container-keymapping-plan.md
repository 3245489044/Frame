# 工作流无状态化 + KeyMapping 统一 + StateCenter 状态外置 · Implementation Plan (v2.0)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Agent 工作流彻底无状态化——RoundSpec 只保留声明式步骤 + 端口 + KeyMapping；所有运行时状态外置到 StateHoldDriven.StateCenter 的三层 StorageLayer（Config/Session/Transient）；DataContainerBinder 作为无状态映射层，通过 StateContextAdapter 读写 StateCenter。

**Architecture:** StateCenter（现有，零改动）提供分层存储后端；StateContextAdapter（新增）封装三层访问；DataContainerBinder（新增）在四阶段（Seed/StepInput/StepOutput/Harvest）调用 KeyProcessor.Mapper.MapBatch 完成映射；WorkflowRunner/RoundStepInvoker 改为通过 StateContextAdapter 替代原 Vars。

**Tech Stack:** C# 10+ / .NET 6+，StateHoldDriven.StateCenter + IStorageCell，ParameterResolver.KeyMapping，Newtonsoft.Json + System.Text.Json。

---

## 文件变更总览

### Agent.Contract
| 路径 | 操作 | 职责 |
|------|------|------|
| `Data/PortSpec.cs` | Create | 端口契约 |
| `DataContainers/MappingDiagnostics.cs` | Create | 诊断结构 |
| `StateAdapters/AgentStateKeys.cs` | Create | StateKey 三维工厂 |

### Agent.Axioms
| 路径 | 操作 | 职责 |
|------|------|------|
| `StateAdapters/StateContextAdapter.cs` | Create | StateCenter 三层适配器（Transient/Session/Config） |
| `DataContainers/DataContainerBinder.cs` | Create | 四阶段映射 + StateCenter 读写 |
| `Infrastructure/KeyMappingConfigJsonConverter.STJ.cs` | Create | STJ 双形态转换器 |
| `Infrastructure/KeyMappingConfigJsonConverter.NJ.cs` | Create | NJ 双形态转换器 |
| `Loops/RoundSpec.cs` | Modify | 扩展三可空字段 |
| `Loops/RoundStep.cs` | Modify | 扩展两可空字段 |
| `Loops/RoundSpecLoader.cs` | Modify | 解析新字段 |
| `Loops/RoundStepInvoker.cs` | Modify | StateContextAdapter 替代 Vars |
| `Loops/RoundStateMapper.cs` | Modify | Seed/Harvest 走 DataContainerBinder |
| `Loops/RoundPipelineContext.cs` | Modify | 持有 StateContextAdapter |
| `Loops/WorkflowRunner.cs` | Modify | 构建 StateContextAdapter |

### Agent.Core
| 路径 | 操作 | 职责 |
|------|------|------|
| `DynamicWorkflow/DynamicWorkflowMerger.cs` | Modify | 三字段扩展 + 旧 KeyMapping 合并 + 对称调用 |

### 测试
| 路径 | 操作 | 职责 |
|------|------|------|
| `tests/.../DataContainerBinderTests.T0-T6.cs` | Create | T0-T6 核心行为 |
| `tests/.../RoundSpecLoader.SchemaVersionTests.cs` | Create | T7 旧 Schema |
| `tests/.../DynamicWorkflowMerger.ShorthandCompatTests.cs` | Create | T8/T9 动态片段 |
| `tests/.../StateContextAdapterTests.cs` | Create | T11/T12 状态层 |
| `tests/.../ClineGlobalMappingSpec.cs` | Create | T10 E2E |

---

## M1：基础设施（类型定义 + 适配器 + 映射器）

---

### Task 1: AgentStateKeys — StateKey 三维工厂

**Files:**
- Create: `Agent/src/Agent.Contract/StateAdapters/AgentStateKeys.cs`
- Prerequisite: Agent.Contract 需引用 Share.StateManagement（或对应 StateKey 定义的程序集）

- [ ] **Step 1: 检查 Agent.Contract 是否已引用 StateKey 所在程序集**

Run: `Grep "StateManagement\|Share" Agent/src/Agent.Contract/Agent.Contract.csproj`

如未找到，追加 ProjectReference 指向 StateKey 定义所在的 csproj。

- [ ] **Step 2: 创建 AgentStateKeys.cs**

```csharp
using Share.StateManagement;

namespace Agent.Contract.StateAdapters;

/// <summary>
/// Agent 专用的 StateKey 构造约定。
/// 三层映射：Scheduler(Config) / Session(Session) / Round(Transient)。
/// </summary>
public static class AgentStateKeys
{
    public const string NsScheduler = "Agent.Scheduler";
    public const string NsSession   = "Agent.Session";
    public const string NsRound     = "Agent.Round";

    public static StateKey Scheduler(string threadHash, string key)
        => new(NsScheduler, threadHash, key);

    public static StateKey Session(string sessionId, string key)
        => new(NsSession, sessionId, key);

    public static StateKey Round(string roundId, string key)
        => new(NsRound, roundId, key);
}
```

- [ ] **Step 3: 编译验证 + Commit**

```bash
git add Agent/src/Agent.Contract/StateAdapters/AgentStateKeys.cs Agent/src/Agent.Contract/Agent.Contract.csproj
git commit -m "feat(M1): AgentStateKeys — StateKey 三维工厂（Scheduler/Session/Round）"
```

---

### Task 2: PortSpec + MappingDiagnostics 契约类型

**Files:**
- Create: `Agent/src/Agent.Contract/Data/PortSpec.cs`
- Create: `Agent/src/Agent.Contract/DataContainers/MappingDiagnostics.cs`
- Prerequisite: Agent.Contract 需引用 ParameterResolver.KeyMapping

- [ ] **Step 1: 检查引用**

Run: `Grep "ParameterResolver" Agent/src/Agent.Contract/Agent.Contract.csproj`

如未找到，追加 ProjectReference。

- [ ] **Step 2: 创建 PortSpec.cs**（代码同设计 5.2 节）

- [ ] **Step 3: 创建 MappingDiagnostics.cs**（代码同设计 5.3 节 v1.0 中的定义）

- [ ] **Step 4: 编译验证 + Commit**

```bash
git add Agent/src/Agent.Contract/Data/PortSpec.cs Agent/src/Agent.Contract/DataContainers/MappingDiagnostics.cs
git commit -m "feat(M1): PortSpec + MappingDiagnostics 契约类型"
```

---

### Task 3: KeyMappingConfigJsonConverter（STJ + NJ 双形态）

**Files:**
- Create: `Agent/src/Agent.Axioms/Infrastructure/KeyMappingConfigJsonConverter.STJ.cs`
- Create: `Agent/src/Agent.Axioms/Infrastructure/KeyMappingConfigJsonConverter.NJ.cs`
- Prerequisite: Agent.Axioms 引用 ParameterResolver.KeyMapping + Agent.Contract

- [ ] **Step 1: 检查 Agent.Axioms 引用链**

Run: `Grep "ParameterResolver\|Agent.Contract" Agent/src/Agent.Axioms/Agent.Axioms.csproj`

- [ ] **Step 2: 创建 STJ 转换器**（代码同 v1.0 plan Task 4 Step 1）

- [ ] **Step 3: 创建 NJ 转换器**（代码同 v1.0 plan Task 4 Step 2）

- [ ] **Step 4: 编译验证 + Commit**

```bash
git add Agent/src/Agent.Axioms/Infrastructure/KeyMappingConfigJsonConverter.*.cs
git commit -m "feat(M1): KeyMappingConfig 双形态 JsonConverter（简写 dict ↔ 完整 Rules）"
```

---

### Task 4: StateContextAdapter — StateCenter 三层适配器

**Files:**
- Create: `Agent/src/Agent.Axioms/StateAdapters/StateContextAdapter.cs`
- Prerequisite: Agent.Axioms 引用 StateHoldDriven（或 StateCenter 所在程序集）

- [ ] **Step 1: 检查引用**

Run: `Grep "StateHoldDriven\|StateBlock\|StateCenter" Agent/src/Agent.Axioms/Agent.Axioms.csproj`

如未找到，追加 ProjectReference 指向 `StateHoldDriven.csproj`。

- [ ] **Step 2: 创建 StateContextAdapter.cs**（代码同设计 4.3 节）

```csharp
using System.Collections.Generic;
using System.Linq;
using Agent.Contract.StateAdapters;
using Operation.StateBlock.Core;  // StateCenter
using TConfig.ContextFrame.Abstractions;  // IStorageCell, StorageLayer

namespace Agent.Axioms.StateAdapters;

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

    public IStorageCell Cell => _center.Cell;

    // ── Transient 层（轮内临时）──
    public T? GetTransient<T>(string key)
        => _center.Cell.Get<T>(key, StorageLayer.Transient);

    public bool TryGetTransient<T>(string key, out T? value)
        => _center.Cell.TryGet<T>(key, out value, StorageLayer.Transient);

    public void SetTransient<T>(string key, T value)
        => _center.Cell.Set(key, value, StorageLayer.Transient);

    public IEnumerable<string> EnumerateTransientKeys()
        => _center.Cell.GetKeys(StorageLayer.Transient);

    public IEnumerable<KeyValuePair<string, object?>> EnumerateTransientEntries()
        => _center.Cell.GetEntries(StorageLayer.Transient);

    public void ClearTransient()
        => _center.Cell.ClearLayer(StorageLayer.Transient);

    // ── Session 层（跨轮持久）──
    public T? GetSession<T>(string key)
        => _center.Cell.Get<T>(key, StorageLayer.Session);

    public bool TryGetSession<T>(string key, out T? value)
        => _center.Cell.TryGet<T>(key, out value, StorageLayer.Session);

    public void SetSession<T>(string key, T value)
        => _center.Cell.Set(key, value, StorageLayer.Session);

    public IEnumerable<string> EnumerateSessionKeys()
        => _center.Cell.GetKeys(StorageLayer.Session);

    public IEnumerable<KeyValuePair<string, object?>> EnumerateSessionEntries()
        => _center.Cell.GetEntries(StorageLayer.Session);

    // ── Config 层（跨进程持久）──
    public T? GetConfig<T>(string key)
        => _center.Cell.Get<T>(key, StorageLayer.Config);

    public void SetConfig<T>(string key, T value)
        => _center.Cell.Set(key, value, StorageLayer.Config);

    public IEnumerable<KeyValuePair<string, object?>> EnumerateConfigEntries()
        => _center.Cell.GetEntries(StorageLayer.Config);
}
```

- [ ] **Step 3: 编译验证 + Commit**

```bash
git add Agent/src/Agent.Axioms/StateAdapters/StateContextAdapter.cs Agent/src/Agent.Axioms/Agent.Axioms.csproj
git commit -m "feat(M1): StateContextAdapter — StateCenter 三层（Config/Session/Transient）适配器"
```

---

### Task 5: 扩展 RoundSpec / RoundStep（可空字段）

**Files:**
- Modify: `Agent/src/Agent.Axioms/Loops/RoundSpec.cs`
- Modify: `Agent/src/Agent.Axioms/Loops/RoundStep.cs`

- [ ] **Step 1: Read 两个文件当前定义**

- [ ] **Step 2: 扩展 RoundSpec record**（追加 InputPorts/OutputPorts/GlobalKeyMapping 默认 null）

- [ ] **Step 3: 扩展 RoundStep record**（追加 InputMapping/OutputMapping 默认 null）

- [ ] **Step 4: 编译验证 + Commit**

```bash
git add Agent/src/Agent.Axioms/Loops/RoundSpec.cs Agent/src/Agent.Axioms/Loops/RoundStep.cs
git commit -m "feat(M1): RoundSpec/RoundStep 扩展端口+映射字段（可空，零破坏）"
```

---

### Task 6: DataContainerBinder 实现（四阶段 + StateCenter 读写）

**Files:**
- Create: `Agent/src/Agent.Axioms/DataContainers/DataContainerBinder.cs`

- [ ] **Step 1: 创建 DataContainerBinder.cs**，实现设计 6.1-6.6 节的四阶段 API：

  - `BindSeedPhase(StateContextAdapter state, RoundSpec? spec, WorkflowFragment? fragment)` — 设计 6.2
  - `BindStepInputPhase(StateContextAdapter state, RoundStep step, Dictionary<string,object> parametersBuffer, KeyMappingConfig? globalMapping)` — 设计 6.3
  - `BindStepOutputPhase(StateContextAdapter state, RoundStep step, Dictionary<string,object?> resultProps, KeyMappingConfig? globalMapping)` — 设计 6.4
  - `BindHarvestPhase(StateContextAdapter state, RoundSpec? spec, WorkflowFragment? fragment)` — 设计 6.5
  - `MergeConfig` 优先级合并辅助 — 设计 6.6

  核心实现要点：
  1. 所有读操作通过 `state.TryGetTransient<T>` / `state.EnumerateTransientKeys()`
  2. 所有写操作通过 `state.SetTransient<T>` / `state.SetSession<T>` / `state.SetConfig<T>`
  3. 映射必经 `KeyProcessor.Mapper.MapBatch`
  4. 零配置时返回 `MappingDiagnostics.Empty`
  5. P3 拷贝语义：冲突时保留原值 + 记录诊断

- [ ] **Step 2: 编译验证**

Run: `dotnet build Agent/src/Agent.Axioms/Agent.Axioms.csproj -v q`

- [ ] **Step 3: Commit**

```bash
git add Agent/src/Agent.Axioms/DataContainers/DataContainerBinder.cs
git commit -m "feat(M1): DataContainerBinder — 四阶段映射 + StateCenter 读写 + P3 拷贝语义"
```

---

**M1 验收**：T0 零配置回归测试通过（Task 10 写 T0 测试后验证）

---

## M2：静态骨架接入

---

### Task 7: RoundSpecLoader 解析新字段

**Files:**
- Modify: `Agent/src/Agent.Axioms/Loops/RoundSpecLoader.cs`

- [ ] **Step 1: Read 完整 RoundSpecLoader**

- [ ] **Step 2: 在 BuildRoundSpec 中解析 InputPorts/OutputPorts/GlobalKeyMapping/Step.InputMapping/Step.OutputMapping**

  - 用 NJ 转换器 `KeyMappingConfigJsonConverterNJ` 反序列化 KeyMappingConfig
  - 整体 try/catch，失败置 null（T7 旧 Schema 零异常）
  - 版本 < 5.0.0 记录 `RoundSpecLoader.schema_migrated` 诊断

- [ ] **Step 3: 编译验证 + Commit**

```bash
git add Agent/src/Agent.Axioms/Loops/RoundSpecLoader.cs
git commit -m "feat(M2): RoundSpecLoader 解析端口+映射新字段（失败降级 null）"
```

---

### Task 8: WorkflowRunner 构建 StateContextAdapter

**Files:**
- Modify: `Agent/src/Agent.Axioms/Loops/WorkflowRunner.cs`
- Modify: `Agent/src/Agent.Axioms/Loops/RoundPipelineContext.cs`

- [ ] **Step 1: Read 两个文件**

- [ ] **Step 2: RoundPipelineContext 改为持有 StateContextAdapter 引用（而非 Vars）**

```csharp
public sealed class RoundPipelineContext
{
    public StateContextAdapter StateAdapter { get; }
    public CancellationToken CancellationToken { get; set; }
    public string? Error { get; set; }
    public List<MappingDiagnostics> DataContainerDiagnostics { get; } = new();

    public RoundPipelineContext(StateContextAdapter stateAdapter)
    {
        StateAdapter = stateAdapter;
    }
}
```

- [ ] **Step 3: WorkflowRunner.RunRoundAsync 构建 StateContextAdapter**

```csharp
// 在 RunRoundAsync 开头：
var stateAdapter = new StateContextAdapter(
    center: _stateCenter,  // 注入
    threadHash: threadHash,
    sessionId: sessionId,
    roundId: $"round-{roundNumber}");

var ctx = new RoundPipelineContext(stateAdapter);

// BindSeedPhase
DataContainerBinder.Instance.BindSeedPhase(stateAdapter, spec, null);
```

- [ ] **Step 4: 编译验证 + Commit**

```bash
git add Agent/src/Agent.Axioms/Loops/RoundPipelineContext.cs Agent/src/Agent.Axioms/Loops/WorkflowRunner.cs
git commit -m "feat(M2): RoundPipelineContext 持有 StateContextAdapter；WorkflowRunner 构建+Seed 调用"
```

---

### Task 9: RoundStateMapper 改为走 DataContainerBinder

**Files:**
- Modify: `Agent/src/Agent.Axioms/Loops/RoundStateMapper.cs`

- [ ] **Step 1: Read RoundStateMapper**

- [ ] **Step 2: Seed 方法改为从 StateContextAdapter 读取 Session/Config 层 → 写入 Transient**

  - 原 Seed 逻辑：从 AxiomContext.Data/lease 提取 → 写入 vars
  - 新 Seed 逻辑：DataContainerBinder.BindSeedPhase(stateAdapter, spec) 自动处理
  - RoundStateMapper.Seed 简化为：构造 StateContextAdapter（如果 WorkflowRunner 没传）→ 调 BindSeedPhase

- [ ] **Step 3: Harvest 方法改为 BindHarvestPhase**

  - 原 Harvest：从 vars 提取 → 写入 AxiomContext.Data
  - 新 Harvest：DataContainerBinder.BindHarvestPhase(stateAdapter, spec) 自动处理
  - Transient 层自动清理

- [ ] **Step 4: 编译验证 + Commit**

```bash
git add Agent/src/Agent.Axioms/Loops/RoundStateMapper.cs
git commit -m "feat(M2): RoundStateMapper.Seed/Harvest 改走 DataContainerBinder + StateCenter"
```

---

### Task 10: RoundStepInvoker 插入 BindStepInputPhase / BindStepOutputPhase

**Files:**
- Modify: `Agent/src/Agent.Axioms/Loops/RoundStepInvoker.cs`

- [ ] **Step 1: Read RoundStepInvoker**

- [ ] **Step 2: 替换 Vars 读写为 StateContextAdapter**

  - 原 `vars.TryGet(key)` → `stateAdapter.TryGetTransient<object>(key, out var v)`
  - 原 `vars.Set(key, value)` → `stateAdapter.SetTransient(key, value)`
  - 参数填充前：调 `binder.BindStepInputPhase(stateAdapter, step, parameters, globalMapping)`
  - 结果写回后：调 `binder.BindStepOutputPhase(stateAdapter, step, resultProps, globalMapping)`

- [ ] **Step 3: 诊断日志写入 ctx.DataContainerDiagnostics + AgentLog**

- [ ] **Step 4: 编译验证 + Commit**

```bash
git add Agent/src/Agent.Axioms/Loops/RoundStepInvoker.cs
git commit -m "feat(M2): RoundStepInvoker 走 StateContextAdapter + BindStepInput/Output"
```

---

## M3：动态片段接入

---

### Task 11: WorkflowFragment / FragmentNode 三字段 + 旧 KeyMapping 合并

**Files:**
- Modify: `Agent/src/Agent.Core/DynamicWorkflow/DynamicWorkflowMerger.cs`

- [ ] **Step 1: Read 文件**

- [ ] **Step 2: WorkflowFragment 新增 InputPorts/OutputPorts/GlobalKeyMapping；旧 KeyMapping [Obsolete] 保留**

- [ ] **Step 3: FragmentNode 新增 InputMapping/OutputMapping**

- [ ] **Step 4: ParseFragment 注入双形态 JsonConverter + 旧 KeyMapping 自动合并为 GlobalKeyMapping Exact 规则**

- [ ] **Step 5: MergeAndExecuteAsync 对称调用 DataContainerBinder 四阶段**

- [ ] **Step 6: 编译验证 + Commit**

```bash
git add Agent/src/Agent.Core/DynamicWorkflow/DynamicWorkflowMerger.cs
git commit -m "feat(M3): DynamicWorkflowMerger 三字段扩展 + 旧 KeyMapping 合并 + 对称 DataContainerBinder"
```

---

## M4：测试 + 示例配置

---

### Task 12: T0-T6 DataContainerBinder 核心测试

**Files:**
- Create: `tests/Agent.Axioms.Tests/DataContainers/DataContainerBinderTests.T0-T6.cs`

  - T0: 零配置回归（StateContextAdapter 不被修改）
  - T1: 简写 dict InputMapping
  - T2: 完整 Exact 形态
  - T3: Pattern 规则
  - T4: MultiLevel 规则
  - T5: 优先级合并（节点 vs 全局）
  - T6: 冲突检测 P3（Transient 层不覆盖）

  每个测试使用 FakeStateCenter（Mock IStorageCell）或直接构造 StateCenter + StorageCell。

- [ ] **Step 1-6: 逐个 TDD（Red-Green-Refactor）**

- [ ] **Step 7: Commit**

```bash
git add tests/Agent.Axioms.Tests/DataContainers/
git commit -m "test(M4): T0-T6 DataContainerBinder 核心行为测试"
```

---

### Task 13: T7 旧 Schema + T8/T9 动态片段兼容

**Files:**
- Create: `tests/Agent.Axioms.Tests/Loops/RoundSpecLoader.SchemaVersionTests.cs`
- Create: `tests/Agent.Core.Tests/DynamicWorkflow/DynamicWorkflowMerger.ShorthandCompatTests.cs`

---

### Task 14: T10 Cline 同义词全局映射 E2E + T11 Transient 清理 + T12 跨轮持久化

**Files:**
- Create: `tests/Agent.Integration.Tests/EndToEnd/ClineGlobalMappingSpec.cs`
- Create: `tests/Agent.Axioms.Tests/StateAdapters/StateContextAdapterTests.cs`

  - T10: GlobalKeyMapping `cline_session → session_id` E2E
  - T11: Harvest 后 `ClearLayer(Transient)` 执行 → Transient 层为空
  - T12: Round N 写入 Session 层的 exec_result → Round N+1 的 Seed 能读到

---

### Task 15: agent-loop.json v5.0.0 示例注释

**Files:**
- Modify: `Agent/Config/workflows/agent-loop.json`

---

## Self-Review

| 检查项 | 结果 | 说明 |
|--------|------|------|
| Spec 覆盖 | ✅ | 设计 13 节一一对应：Section 2 → Task 1/4；Section 4 → Task 1/4；Section 5 → Task 2/5；Section 6 → Task 6；Section 7 → Task 7-11；Section 10 → Task 12-14 |
| 占位符 | ⚠️ 最小 | Task 12-14 测试断言要点未贴完整代码（TDD 阶段由 subagent 补全）；所有类名/API 路径/方法签名一致 |
| 类型一致性 | ✅ | `StateContextAdapter` 贯穿 Task 4/6/8/9/10/12/14；`DataContainerBinder` 四阶段方法名在 Task 6/9/10/11 一致 |
| 依赖顺序 | ✅ | Task 1→2→3→4→5→6（M1 类型+适配器+映射器）→ 7→8→9→10（M2 接入）→ 11（M3 动态）→ 12→13→14→15（M4 测试） |
