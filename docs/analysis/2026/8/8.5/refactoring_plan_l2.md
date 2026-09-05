# FrontEndDriven 重构计划 L2 — 剩余问题专项方案

> **文档日期**：2026-08-01
> **关联文档**：`analysis_report.md`（L1 批次已完成 16 项修复）
> **适用范围**：`CsharpPrj/Operation/FrontEndDriven`
> **状态图例**：⬜ = 未开始，🔄 = 部分完成/进行中，✅ = 已完成

---

## 总览

本批次处理 L1 分析报告中剩余的 **10 项问题**，按类别分为四组：

| 组 | 问题 | 性质 |
|----|------|------|
| 架构调整 | AD-3, AD-4 | 大型改动，需拆阶段 |
| 重复去重 | DL-1, DL-4, DL-5 | 中等改动，单点收尾 |
| 冗余/时序 | RL-7, EL-7 | 小改动，低风险 |
| P3 技术债务 | EX-1, EX-2, EX-3, CI-1, CI-2 | 文档/边界修复 |

> 每节均含：问题速览 → 现状代码 → 问题本质 → 目标设计 → 实施步骤 → 风险与回滚。

---

## 第一部分：架构调整类

### AD-3: 命令架构双轨清理

#### 问题速览
`CommandInputPort`（ECS 化）与旧 `CommandExecution/Abstractions/`（`ICommandEntry`/`ICommandPipeline`/`ICommandRunner`）双轨并存，`CommandPipelineContext` 携带大量已废弃字段。

#### 现状代码
```csharp
// CommandExecution/Abstractions/ICommandPipeline.cs
public interface ICommandPipeline
{
    Task ExecuteAsync(CommandPipelineContext context, CancellationToken cancellationToken = default);
}
// CommandExecution/Abstractions/ICommandRunner.cs
public interface ICommandRunner
{
    Task RunAsync(CommandPipelineContext context, CancellationToken cancellationToken = default);
}
// CommandPipelineContext 中仍存在的废弃字段：
//   VirtualStore, LoadedConfigs, BindingResolver, FunctionExecutor, TargetForm, UICommandManager
```

#### 问题本质
- 代码注释明确声明「Command ECS 重构 Phase 1.x 替代旧的 3-stage 管线」，但接口与上下文类未清理。
- 调用方无法判断应走 `CommandInputPort` 还是 `ICommandPipeline`，增加维护心智负担。

#### 目标设计
1. `ICommandEntry` / `ICommandPipeline` / `ICommandRunner` 标记 `[Obsolete]`（warning 级）。
2. `CommandPipelineContext` 仅保留 ECS 状态（`ScriptCommands`、`CommandResults`、统计计数、`Options`）。
3. `RemoveVirtualStore`（含 `VirtualStore`/`LoadedConfigs` getter/setter 委托）→ 若 FileDriven 层无引用则删除。
4. `CommandInputPort` 与 `CommandSystemCoordinator` 收敛为单一入口（`CommandInputPort` 作为外观）。

#### 实施步骤
1. **排查引用**：`grep "ICommandPipeline|ICommandRunner|ICommandEntry"` 全仓，确认零外部消费。
2. **标 Obsolete**：三接口加 `[Obsolete("请使用 CommandInputPort", false)]`，构建观察警告数。
3. **清理废弃字段**：`CommandPipelineContext` 删除 `VirtualStore`/`LoadedConfigs`/`BindingResolver`/`FunctionExecutor` 属性及其 `_contextFrame` 委托，验证 `CommandRuntimeContext` 构造不受影响。
4. **收敛入口**：`CommandInputPort` 内部 `_coordinator.Update()` 作为唯一执行点，删除旧路径残留。
5. **构建验证**：`dotnet build FrontEndDriven.csproj` 0 错误。

#### 风险与回滚
- **风险**：`CommandPipelineContext` 被 `FileDriven` 或 `BackEndDriven` 交叉引用，删除字段导致编译失败。
- **回滚**：保留 `[Obsolete]` 而非删除；字段删除前先 `grep` 全仓确认零引用，带 `git checkout` 兜底。

---

### AD-4: `RenderPipelineComposition` 职责拆分

#### 问题速览
686 行类承担 7+ 职责（IoC 装配 / 管线执行 / 作用域清理 / 布局重算 / 执行循环 / 资源清理），高度耦合、改动牵一发动全身。

#### 现状代码（职责清单）
```csharp
public sealed class RenderPipelineComposition : IDisposable
{
    // ① 三端口聚合（InputPort/Collector/OutputPort）
    // ② 静态工厂 Create()（ECS 核心 + 三端口 + System + Matrix Handler 注册）
    // ③ 管线执行：Execute() / ExecuteCreationStage() / ExecuteRenderStage()
    // ④ 作用域清理：DestroyScopeEntities()
    // ⑤ 布局重算：RecalculateLayout()（含 _isRecalculating 重入保护）
    // ⑥ 执行循环：StartExecutionLoop() / OnApplicationIdle() / OnExecutionResultReady()
    // ⑦ 资源清理：Clear() / Dispose()
}
```

#### 问题本质
单一职责原则严重违反——任何一处改动（如重算逻辑、Idle 轮询）都必须审查整个 686 行类，回归成本高。

#### 目标设计（拆分 4 类）
```
RenderPipelineComposition          // 三端口聚合 + Dispose（保留对外 API）
├── PipelineCompositionFactory     // 从 Create() 抽取的静态装配逻辑
├── RenderStageRunner              // Execute / ExecuteCreationStage / ExecuteRenderStage / DestroyScope / RecalculateLayout
└── ExecutionLoopManager           // StartExecutionLoop / OnApplicationIdle / OnExecutionResultReady / Clear
```

**依赖注入**：`RenderStageRunner` 与 `ExecutionLoopManager` 通过构造函数接收 `EntityManager`、`ControlPool`、`FunctionExecutionSystem`、三端口。

#### 实施步骤
1. **抽取 `ExecutionLoopManager`**（风险最低）：迁移 `_executionLoopStarted`/`_rerenderCount`/`_functionExecutionSystem` 订阅/Idle 事件 → `composition.StartExecutionLoop()` 委托给 manager。
2. **抽取 `PipelineCompositionFactory`**：迁移 `Create()` 的全部装配代码（ECS 核心、三端口、System、Matrix Handler）→ `RenderPipelineComposition.Create()` 改为委托 `PipelineCompositionFactory.Create(...)`。
3. **抽取 `RenderStageRunner`**：迁移 `Execute`/`ExecuteCreationStage`/`ExecuteRenderStage`/`DestroyScopeEntities`/`RecalculateLayout` → Composition 委托。
4. **Composition 瘦身**：保留 `public` 属性与方法签名（兼容外部调用），内部实现全部委托。
5. **构建验证 + 冒烟**：编译通过后运行一次 UI 加载，核实布局/事件绑定/布局重算链路正常。

#### 风险与回滚
- **风险**：外部通过 `composition.EntityManager`/`ControlPool`/`MatrixEngine` 直接取值，拆分后属性需保留。
- **回滚**：按 step 顺序反向 revert；每步独立 commit，step 失败不阻塞后续。

---

## 第二部分：重复逻辑去重类

### DL-1: SplitterMoved 双层订阅统一

#### 问题速览
`WinFormsControlAdapter` 与 `WinFormsOutputPort` 各自维护 SplitterMoved 订阅/取消逻辑，`UnsubscribeSplitterMoved(object, handler)` 已无调用方。

#### 现状代码
```csharp
// WinFormsControlAdapter：内部 _splitterHandlerCache，Subscribe 返回取消委托，另有 UnsubscribeSplitterMoved(obj, handler)
// WinFormsOutputPort：_splitterUnsubscribeActions 字典，UnsubscribeSplitterMoved(control) 调取消委托
public Action SubscribeSplitterMoved(object splitContainer, EventHandler handler) { ... }
public void UnsubscribeSplitterMoved(object splitContainer, EventHandler handler) { ... }  // 死方法
```

#### 问题本质
同一事件拆除逻辑在两层各自实现，Adapter 接口暴露的 `UnsubscribeSplitterMoved(obj, handler)` 已无调用方（OutputPort 改用返回的取消委托），接口应收缩。

#### 目标设计
- **删除** `IWinFormsControlAdapter.UnsubscribeSplitterMoved(object, EventHandler)` 方法（含实现与接口声明）。
- 取消订阅能力统一由 `SubscribeSplitterMoved` 返回的 `Action` 承担（已在 OutputPort 使用）。
- `WinFormsControlAdapter._splitterHandlerCache` 保留（`Subscribe` 内部防重复依赖它），但不再对外暴露双向 Unsubscribe。

#### 实施步骤
1. `grep "UnsubscribeSplitterMoved"` 确认 `ControlAdapter` 版本零调用。
2. 删除 `IWinFormsControlAdapter` 接口中的声明 + `WinFormsControlAdapter` 实现。
3. 构建验证（若有遗漏调用方，编译器会报错提示）。

#### 风险与回滚
- **风险**：极低——方法在接口中被显式实现还是隐式实现需检查（若显式实现，删除接口声明即失联）。
- **回滚**：恢复接口声明 + 实现。

---

### DL-4: EntityId 缓存统一

#### 问题速览
`WinFormApplier` 与 `WinFormsUIAdapter` 各自维护 `Dictionary<string, Entity>` + `TryParseEntityId`，且各自 `ClearEntityIdCache`。

#### 现状代码
```csharp
// WinFormApplier
private readonly Dictionary<string, Entity> _entityIdCache = new();
private bool TryParseEntityId(string str, out Entity entity) => EntityIdParser.TryParse(str, out entity, _entityIdCache);
// WinFormsUIAdapter 完全同构
```

#### 问题本质
`EntityIdParser` 已是共享工具（带缓存重载），但缓存实例分散在两个调用方，缓存失效时机（`Apply`/`Clear` 时清空）各管各的，多实例无共享收益。

#### 目标设计
新建 `EntityIdCache` 类（`Shared/Utils/EntityIdCache.cs`）：
```csharp
public sealed class EntityIdCache
{
    private readonly Dictionary<string, Entity> _cache = new();
    public bool TryParse(string str, out Entity entity) => EntityIdParser.TryParse(str, out entity, _cache);
    public void Clear() => _cache.Clear();
}
```
- `WinFormApplier` 与 `WinFormsUIAdapter` 构造函数注入同一个 `EntityIdCache` 实例（由 `RenderPipelineComposition.Create` 创建后传入）。
- 两类的 `_entityIdCache` 字段与私有 `TryParseEntityId` 删除，改为组合 `EntityIdCache`。

#### 实施步骤
1. 新建 `EntityIdCache.cs`。
2. `RenderPipelineComposition.Create` 创建 `entityIdCache = new EntityIdCache()`，注入 `WinFormApplier` 与 `WinFormsUIAdapter`。
3. 两类的缓存字段/方法删除，调用点改为 `_entityIdCache.TryParse(...)`。
4. `ClearEntityIdCache` 方法保留（委托 `_entityIdCache.Clear()`）。
5. 构建验证。

#### 风险与回滚
- **风险**：`WinFormsUIAdapter` 构造签名变更，检查外部直接 `new WinFormsUIAdapter(...)` 的调用点（`RenderPipelineComposition.Create` 为主）。
- **回滚**：恢复字段 + 私有方法。

---

### DL-5: CommandState 状态映射统一

#### 问题速览
`CommandState` 枚举的显示文本 switch 在 `CommandResultRow.StateText` 中单独维护，且包含 `Parsing` 死枚举。

#### 现状代码
```csharp
public enum CommandState { Pending, Parsing, Executing, Success, Failed, ParseError }

// CommandOutputPort.cs
public readonly string StateText => State switch
{
    CommandState.Parsing => "Parsing",   // 死分支：从未被设置为 Parsing
    ...
};
```

#### 问题本质
- `Parsing` 从未在 `CommandInputPort`/`CommandSystemCoordinator` 中被赋值（解析成功→Pending，失败→ParseError）。
- 状态文本映射与枚举定义分离，新增状态需同时改枚举 + switch。

#### 目标设计
1. 新增 `CommandStateExtensions`（`CommandExecution/ECS/CommandComponents.cs` 或独立文件）：
```csharp
public static class CommandStateExtensions
{
    public static string ToDisplayText(this CommandState state) => state switch
    {
        CommandState.Pending => "Pending",
        CommandState.Executing => "Executing",
        CommandState.Success => "Success",
        CommandState.Failed => "Failed",
        CommandState.ParseError => "ParseErr",
        _ => "Unknown"
    };
}
```
2. `CommandResultRow.StateText` 改为 `State.ToDisplayText()`。
3. 移除 `CommandState.Parsing` 枚举值（确认零引用）。

#### 实施步骤
1. 新增扩展类，迁移 switch。
2. `grep "CommandState.Parsing"` 确认零引用后删除枚举值。
3. 构建验证。

#### 风险与回滚
- **风险**：`Parsing` 若有序列化/持久化依赖（如日志 JSON 含枚举名）会语义变化——grep 确认。
- **回滚**：恢复枚举值。

---

## 第三部分：冗余/时序修复类

### RL-7: `LastAppliedNodeCount` 双维护清除

#### 问题速览
`RenderPipelineComposition` 与 `WinFormsOutputPort` 各自维护「最后应用节点数」，Composition 从 OutputPort 读取后再存副本。

#### 现状代码
```csharp
// RenderPipelineComposition
public int LastAppliedNodeCount { get; private set; }
// ExecuteRenderStage: LastAppliedNodeCount = OutputPort.LastAppliedNodeCount;  ← 纯转发
// WinFormsOutputPort
public int LastAppliedNodeCount { get; private set; }  // 真正写入处
```

#### 问题本质
Composition 的 `LastAppliedNodeCount` 是冗余副本，`ExecuteRenderStage` 只是转发读取，无独立信息。

#### 目标设计
- 删除 `RenderPipelineComposition.LastAppliedNodeCount` 属性。
- 外部读取统一走 `composition.OutputPort.LastAppliedNodeCount`（OutputPort 已是 Composition 公开属性）。
- `RenderPipelineComposition.Execute()` 末尾日志中的 `appliedCount` 改用 `OutputPort.LastAppliedNodeCount`。

#### 实施步骤
1. `grep "LastAppliedNodeCount"` 全仓，确认外部消费点。
2. 删除 Composition 副本属性 + `ExecuteRenderStage` 内赋值。
3. 外部引用改为 `OutputPort.LastAppliedNodeCount`。
4. 构建验证。

#### 风险与回滚
- **风险**：外部可能直接读 `composition.LastAppliedNodeCount`（需 grep 确认，若 Buffer/诊断用则改引用路径）。
- **回滚**：恢复属性。

---

### EL-7: `DataChanged` 触发时机修复

#### 问题速览
`EcsInputPort.ApplyDataChange` 中 `DataChanged` 事件在组件写入后无条件触发，写入失败（`AddOrSetComponent` 抛异常）时事件不触发且异常上抛，语义不一致。

#### 现状代码
```csharp
public void ApplyDataChange<T>(Entity entity, T newValue, Func<T?, T>? updateAction = null)
    where T : struct, IComponent
{
    if (!entity.IsValid) throw ...;
    if (updateAction != null) { ... AddOrSetComponent(entity, updated); }
    else { AddOrSetComponent(entity, newValue); }
    DataChanged?.Invoke();   // 若 AddOrSetComponent 抛异常，此行不执行
}
```

#### 问题本质
- 当前行为「写入失败不触发 DataChanged」与「写入成功触发」**实际上是一致的**（因为异常上抛前不会到达 `Invoke`）。
- 但语义未文档化——调用方无法区分「异常上抛 = 写入失败」与「正常返回 = 写入成功」，且 `updateAction` 内部抛异常时同样不触发。
- 修复目标：**显式保证「仅写入成功后触发」**，并文档化异常语义。

#### 目标设计
```csharp
public void ApplyDataChange<T>(...)
{
    if (!entity.IsValid) throw ...;
    try
    {
        if (updateAction != null) { ... AddOrSetComponent(entity, updated); }
        else { AddOrSetComponent(entity, newValue); }
    }
    catch (Exception ex)
    {
        FrontEndDrivenLog.Log("EcsInputPort_ApplyDataChange_failed", new { entityId = ..., error = ex.Message });
        throw;   // 保留异常，调用方知道失败
    }
    DataChanged?.Invoke();   // 仅此处（写入成功）触发
}
```
- 不改变外部行为（异常仍上抛），但增加写入失败日志，明确「未触发 DataChanged」的根因可追溯。

#### 实施步骤
1. 在 `AddOrSetComponent` 两分支外包 try-catch 记录失败日志后 rethrow。
2. `DataChanged?.Invoke()` 保持在 try 外（仅成功到达）。
3. 构建验证。

#### 风险与回滚
- **风险**：无行为变更（仅加日志）。
- **回滚**：去除 try-catch。

---

## 第四部分：P3 技术债务类

### EX-1: `SleepSystem.Thread.Sleep` 阻塞线程

#### 问题速览
`SleepSystem.Execute` 调用 `Thread.Sleep(ms)`，若在 UI 线程执行会冻结界面。

#### 现状代码
```csharp
Thread.Sleep(ms);   // CommandSystems.cs Line ~392
```

#### 目标设计
- **优先方案**：`ICommandSystem.Execute` 保持同步（ECS 调度器为同步轮询），在 `SleepSystem` 与 `CommandSystemCoordinator.Update` 的 XML 注释中明确「命令在后台/非 UI 线程执行」的线程模型约束。
- **备选方案（架构演进）**：`ICommandSystem.Execute` 改为 `Task ExecuteAsync`，`SleepSystem` 用 `await Task.Delay(ms)`；但需同步改造全部 18 个 System 与 `CommandSystemCoordinator`——列为独立批次。

#### 实施步骤
1. `grep "Thread.Sleep"` 确认 `SleepSystem` 是唯一调用点。
2. 在 `CommandSystemCoordinator` 与 `SleepSystem` XML 注释补线程模型说明。
3. 构建验证。

#### 风险与回滚
- **风险**：注释级变更，零风险。
- **回滚**：还原注释。

---

### EX-2: `LoadSystem` / `SideloadSystem` 同步文件 IO

#### 问题速览
`File.ReadAllText` 同步读取 JSON 配置，在后台线程执行无碍，但未文档化线程模型。

#### 现状代码
```csharp
// LoadSystem / SideloadSystem
var json = File.ReadAllText(fullPath);
```

#### 目标设计
- 同 EX-1：文档化「命令 System 在非 UI 线程执行，同步 IO 可接受」。
- 若未来 `ICommandSystem` 异步化，则改为 `File.ReadAllTextAsync`——含在 EX-1 备选方案中。

#### 实施步骤
注释补充 + 构建验证。

---

### EX-3: `EcsGate.DoExecuteFunc` 超时与 Reactor 竞态加固

#### 问题速览
超时回调先 `TryGetValue` 再 `TrySetResult(Timeout)`，与 Reactor 的 `TryRemove` + `TrySetResult(真实结果)` 仍存在微小时竞态窗口。

#### 现状代码
```csharp
// EcsGate.cs
cts.Token.Register(() =>
{
    if (_pendingResults.TryGetValue(requestEntity.Id, out var pendingTcs))
        pendingTcs.TrySetResult(FunctionExecutionResult.Timeout);   // 竞态：Reactor 可能稍后 TryRemove
});
// Reactor:
if (_gate._pendingResults.TryRemove(data.SourceEntityId, out var tcs))
    tcs.TrySetResult(data.ToResult());
```

#### 问题本质
- 若超时回调先 `TrySetResult(Timeout)`，Reactor 后 `TryRemove`，真实结果被丢弃（虽概率极低）。
- `ConcurrentDictionary` 的 `TryRemove` 与 `TryGetValue` 之间无原子性。

#### 目标设计
引入原子标志位，确保「真实结果优先于超时」：
```csharp
// 每个请求添加本地标志：真实结果已确认
var completedFlag = new InterlockedFlag();   // 或 int 原子计数

// 超时回调：
if (completedFlag.TrySetTimeout())          // 若真实结果未完成
    pendingTcs.TrySetResult(FunctionExecutionResult.Timeout);

// Reactor：
if (_gate._pendingResults.TryRemove(...))
{
    completedFlag.MarkCompleted();          // 先标记完成
    tcs.TrySetResult(data.ToResult());
}
```
> 说明：当前实现已用「Reactor 先 TryRemove」缓解，彻底加固需在 `_pendingResults` 存的 `TaskCompletionSource` 包装 `Result` + `IsCompleted` 原子字段，或改用 `TaskCompletionSource.TrySetResult` 的结果（返回 false 表示已有结果）——**最简单方案**是依赖 `TCS.TrySetResult` 本身原子性：
> ```csharp
> // Reactor 已 TryRemove → TCS 只会被 set 一次（无论超时回调还是 Reactor，先到者生效）
> ```
> 实际当前逻辑已安全：Reactor `TryRemove` 后超时回调 `TryGetValue` 会 miss；超时回调 `TrySetResult` 后 Reactor `TryRemove` 虽删除 key 但 TCS 已 Timeout——**唯一残留是「超时先至但真实结果后至」的丢真**。加固点在于让 Reactor 的 `TryRemove` 与 `TrySetResult` 之间不被超时插入：使用 `ConcurrentDictionary.GetOrAdd` 原子注册 + `TCS.TrySetResult` 返回值判断「谁先」。
> **最终落地**：在 Reactor 前先 `TryRemove` 拿回 TCS，再 `tcs.TrySetResult(真实)`；超时回调改为 `tcs.TrySetResult(Timeout)` 后**主动 `TryRemove`**（使 key 消失阻断 Reactor），从而「谁先拿到 key 谁赢」。
```
#### 实施步骤
1. 超时回调内：`TrySetResult(Timeout)` 后追加 `_pendingResults.TryRemove(requestEntity.Id, out _)`。
2. Reactor 侧不变（`TryRemove` 先于 `TrySetResult`）。
3. 构建 + 逻辑审查（超时-完成并发测试可用 Task.Run 模拟）。

#### 风险与回滚
- **风险**：极低（逻辑等价强化）。
- **回滚**：还原超时回调。

---

### CI-1: `Collect()` XML 注释过时

#### 问题速览
`CsgEntryPoint.Collect()` 注释声称「管线末尾统一收集」，实际主路径是 `CollectSnapshot()`（WinFormSnapshot）。

#### 目标设计
更新 `Collect()` 与 `CollectSnapshot()` 的 XML 注释，明确：
- `Collect()` → CanonicalForm（诊断/审计用，非渲染主路径）
- `CollectSnapshot()` → WinFormSnapshot（渲染主路径）

#### 实施步骤
注释更新 + 构建验证。

---

### CI-2: `WinFormApplier.Apply` 注释与实现不符

#### 问题速览
`Apply()` 注释「打印完整 snapshot」，实际 `LogSnapshot` 只打印计数摘要。

#### 目标设计
- `Apply()` 注释改为「记录计数摘要日志（完整控件表由 layout positions 命令显式请求）」。
- `LogSnapshot` 方法注释同步修正。

#### 实施步骤
注释更新 + 构建验证。

---

## 附录：实施批次建议

| 批次 | 问题 | 预估改动量 | 风险 |
|------|------|-----------|------|
| **L2-1（小批量先行）** | CI-1, CI-2, EX-1, EX-2, EL-7 | 注释 + 日志 | 极低 |
| **L2-2（中等）** | DL-1, DL-4, DL-5, RL-7, EX-3 | 单点去重 + 竞态 | 低 |
| **L2-3（架构拆分）** | AD-4（分 3 步） | 大型 | 中 |
| **L2-4（架构收尾）** | AD-3（依赖 L2-3 先行的 Composition 瘦身） | 大型 | 中 |

> 建议按 L2-1 → L2-4 顺序执行，每批次独立 commit，构建验证通过后再进入下一批。