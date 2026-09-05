# FrontEndDriven 代码质量分析报告

> **分析日期**：2026-08-01
> **分析范围**：`CsharpPrj/Operation/FrontEndDriven` 全部核心模块
> **分析角度**：架构缺陷 / 重复逻辑 / 冗余逻辑 / 错误逻辑
> **优化状态**：✅ = 已修复，🔄 = 部分修复/需进一步评估，⬜ = 未处理

---

## 修复记录（2026-08-01 优化批次）

本次优化共修改 **16 个文件**，修复 **P0/P1/P2 级问题 16 项**，构建验证通过（0 警告 0 错误）。

| 问题编号 | 修复内容 | 状态 |
|---------|---------|------|
| AD-2 (P0) | `EcsGate.DispatchAsync` 新增 `ConvertResult` 安全类型转换，替代 `(TResult)(object)` 暴力 cast | ✅ |
| EL-4 (P0) | `UiThreadHelper.DispatchIfNeeded<TResult>` 增加 5 秒超时保护，避免 UI 线程被阻塞时永久挂起 | ✅ |
| EL-1 (P1) | `GetSystem` 区分"控件不存在"与"控件值为空"（未找到 → SetFailure） | ✅ |
| EL-2 (P1) | `LayoutInspectSystem.ExecuteOverlaps` 检测报告改用 `SetSuccess` 而非 `SetFailure` | ✅ |
| EL-6 (P1) | `WinFormApplier.TryFindControl` 语义澄清（返回 true 表示 Entity 解析成功，控件可能为 null；调用方自行判断） | ✅ |
| EL-8 (P1) | `CommandRuntimeContext.Items` 从共享静态字典改为实例级字典，消除跨实例污染 | ✅ |
| RL-1 (P2) | 删除 `EcsInputPort.EnqueueExecutionWithResult`（Obsolete 纯转发） | ✅ |
| RL-2 (P2) | 删除 `CommandInputPort.ExecuteNext`（Obsolete 误导命名包装） | ✅ |
| RL-4 (P2) | 移除 `FunctionExecutionSystem._executionService` 死代码字段 + 构造参数 | ✅ |
| DL-2 (P2) | 提取 `WinFormsControlValueWriter.TrySetComboBoxValue` 静态方法，`SetSystem` 复用消除双轨 | ✅ |
| DL-3 (P2) | `ControlFinder.FindByName<T>` 泛型方法，消除 6 处 `FindAcrossForms` 重复模式 | ✅ |
| AD-1 (P0) | 函数调用双轨制 — **Async 路径轻量化**：移除 EcsGate→Enqueue→Idle→Matrix→Router→Handler 六层中转，改为 Task.Run 直达 WorkflowMethodExecutionService；**门面级统一审计**：Sync/Async 统一 Entry/Exit/Error；**简化 sync 包装**：删除 ExecuteAsyncInternalSync | ✅ |
| AD-3/AD-4 (P1) | 新旧命令架构双轨 / RenderPipelineComposition 职责过重 — 属于大型架构调整，涉及面广，建议单独批次处理 | ⬜ |
| DL-1/DL-4/DL-5 | SplitterMoved 双层订阅 / EntityId 缓存 / 状态映射 — 中等优先级，建议后续批次 | ⬜ |
| RL-3 (P2) | 删除 `NonEventCommandLookupAdapter` 废弃适配器（零调用方） | ✅ |
| RL-7 (P2) | LastAppliedNodeCount 双维护 — 保留待后续批次（涉及 Composition 重构） | ⬜ |
| EL-3 (P2) | async 包装 — 已随 AD-1 优化消除（ExecuteAsyncInternalSync 删除） | ✅ |
| EL-7 (P2) | DataChanged 触发时机 — 待后续批次 | ⬜ |
| RL-5 (P2) | `ParseCommandSystem` 双实例收敛 — `CommandSystemCoordinator.ParseSystem` 共享实例，`CommandInputPort` 复用 | ✅ |
| RL-6 (P2) | 删除 `CommandInputPort.ExecuteAll` 纯转发包装（零调用方） | ✅ |
| EL-5 (P2) | `ProcessEntity` Failed 语义统一 — catch/未知命令分支收敛为 `MarkFailed` 辅助方法 | ✅ |
| EX-1~3, CI-1~2 | 同步 IO/线程阻塞/文档对齐 — P3 技术债务 | ⬜ |

---

## 一、架构缺陷（Architecture Defects）

### AD-1: 函数调用存在两套并行执行链，结果模型不统一
**涉及文件**：`Gate/FunctionCallFacade.cs`、`Gate/EcsGate.cs`、`ECS/Systems/FunctionExecutionSystem.cs`、`CommandExecution/ECS/CommandSystems.cs`

**问题描述**：
- **Sync 路径**（`FunctionCallFacade.ExecuteSync`）：直接调用 `IEventExecutionService.ExecuteFunctionAsync`，绕过了 ECS Interceptor 审计链。
- **Async 路径**（`FunctionCallFacade.ExecuteAsyncInternal`）：经 `EcsGate → EcsInputPort.EnqueueExecution → FunctionExecutionSystem → MatrixEngine.Router → Handler`，链路完整但引入了 `ExecutionResultReadyEvent` Reactor 等待机制。

两套路径的结果模型不同：
- Sync 路径返回 `FunctionCallResult`（统一门面模型）
- Async 路径底层是 `FunctionExecutionResult`（ECS 模型）再适配为 `FunctionCallResult`

**影响**：
1. Sync 路径完全绕过 ECS Interceptors（`AuditLogInterceptor`），审计追踪不完整。
2. 双路径的错误处理语义不一致（Sync 直接 catch，Async 经 TCS 超时/Reactor 推送）。
3. 维护成本高——任何执行逻辑变更需同步修改两条路径。

### AD-2: ECS 操作调度使用 `(TResult)(object)` 暴力转换
**涉及文件**：`Gate/EcsGate.cs` 第 87-107 行

```csharp
private async Task<TResult> DispatchAsync<TResult>(EcsOperation<TResult> op, CancellationToken ct)
{
    if (op is SubmitToMatrixOperation smOp) { ... return (TResult)(object)true; }
    return op switch
    {
        LoadConfigOperation o => (TResult)(object)DoLoadConfig(o),
        EnqueueFunctionOperation o => (TResult)(object)DoEnqueueFunc(o),
        ...
    };
}
```

**问题描述**：
- 所有操作都通过 `(TResult)(object)value` 双次装箱 cast，泛型类型安全完全丢失。
- 若 `TResult` 与实际返回类型不匹配，会在运行时抛 `InvalidCastException` 而非编译错误。
- 这是典型的重度反模式，应该用 `dynamic` 或让操作承载正确的类型化结果。

### AD-3: `CommandInputPort`（ECS 化）与旧 `CommandPipelineContext`/`ICommandPipeline` 双轨并存
**涉及文件**：`CommandExecution/Ports/CommandInputPort.cs`、`CommandExecution/Abstractions/ICommandPipeline.cs`、`CommandExecution/Abstractions/ICommandRunner.cs`

**问题描述**：
- 代码注释明确写了"Command ECS 重构 Phase 1.x 替代旧的 3-stage 管线"。
- 但 `CommandExecution/Abstractions/` 下的 `ICommandEntry`、`ICommandPipeline`、`ICommandRunner` 接口仍然保留。
- `CommandPipelineContext`（423 行）和 `CommandRuntimeContext` 仍在使用 `IEventBindingResolver`、`IFunctionExecutor`、`VirtualStore`、`LoadedConfigs` 等已废弃概念。

**影响**：新旧两代命令执行架构并行残留，调用方难以判断该使用哪条路径，增加心智负担。

### AD-4: `RenderPipelineComposition` 承担过多职责
**涉及文件**：`Initial/RenderPipeline/RenderPipelineComposition.cs`（686 行）

**问题描述**：
- 该类同时承担：三端口组合（IoC 装配）、静态工厂（Create）、五阶段管线执行、分步执行（LoadConfiguration/RegisterRootContainer/ExecuteCreationStage/ExecuteRenderStage）、作用域清理（DestroyScopeEntities）、布局重算（RecalculateLayout）、执行循环（StartExecutionLoop/OnApplicationIdle/OnExecutionResultReady）、资源清理（Clear/Dispose）。
- 单一类集 7+ 职责于一身，违反单一职责原则。
- 职责高度耦合导致每次改动牵一发动全身（从大量"★ 2026-07-XX 修复"注释可见）。

### AD-5: ECS Systems 与命令 System 两套调度机制并行
**涉及文件**：`CommandExecution/ECS/CommandSystems.cs`（`CommandSystemCoordinator`）、`ECS/Systems/FunctionExecutionSystem.cs`

**问题描述**：
- `CommandSystemCoordinator` 自己实现了 Pending 命令轮询 + `ICommandSystem` 接口调度。
- 而 UI 控件的事件函数走 `FunctionExecutionSystem`（ECS 消费 `ExecutionRequestComponent` → MatrixEngine）。
- 两条命令/函数执行链（命令 System vs 函数执行 System）没有统一路由，职责边界模糊。

### AD-6: `LayoutInspectSystem` 手写 WinForms 遍历与 ECS 真源脱节
**涉及文件**：`CommandExecution/ECS/LayoutInspectSystem.cs`

**问题描述**：
- `GetAllControls()`/`GetAllControlsFrom()` 直接遍历 `Application.OpenForms` 递归收集控件。
- 而项目其他部分（如 `ControlFinder.FindAcrossForms` + `FindRecursive`）已有统一的跨 Form 查找工具。
- `ExecuteReport`/`ExecuteJsonReport` 与 `layout positions`、`UIInspector` 功能重叠，输出格式各异。

---

## 二、重复逻辑（Duplicated Logic）

### DL-1: `WinFormsControlAdapter.SubscribeSplitterMoved` 与 `WinFormsOutputPort.UnsubscribeSplitterMoved` 双层取消订阅
**涉及文件**：`WinForms/Adapter/WinFormsControlAdapter.cs`（第 249-296 行）、`WinForms/Applier/../Ports.cs`（WinFormsOutputPort 650-687 行）

**问题描述**：
- `WinFormsControlAdapter` 内部维护 `_splitterHandlerCache`，`SubscribeSplitterMoved` 返回取消委托（内部移除缓存），`UnsubscribeSplitterMoved(splitContainer, handler)` 也做同样的事。
- `WinFormsOutputPort` 又维护自己的 `_splitterUnsubscribeActions` 字典，并按 control 查找取消委托。
- 同一事件拆除逻辑在两层（Adapter 内部 + OutputPort 层）各自维护，且 `UnsubscribeSplitterMoved(object)` 现已不被调用（改用缓存取消委托），但 Adapter 接口仍暴露此方法。

### DL-2: ComboBox 值写入逻辑三处重复
**涉及文件**：
1. `WinForms/ValueSync/WinFormsControlValueWriter.cs`（`RegisterComboBoxAdapter` 第 340-368 行）
2. `CommandExecution/ECS/ClickSetGetSystems.cs`（`SetSystem.PerformDirectSet` 第 156-167 行）
3. `WinForms/Applier/WinFormApplier.cs`（通过 `PropertyApplier` 间接）→ 实际主要是前两处

**问题描述**：
`WinFormsControlValueWriter` 的 ComboBox 适配器已有完整逻辑（文本匹配 → 索引回退）。但 `SetSystem.PerformDirectSet` 又手写了一遍几乎相同的逻辑（`comboBox.Text = value; matched = ...; if (matched != null) ... else if (int.TryParse...)`），两处逻辑实现几乎完全相同，但分别维护。

### DL-3: 控件跨 Form 查找模式 6 处重复
**涉及文件**：
- `CommandExecution/ECS/CommandSystems.cs`（DiscoverSystem 第 673-675 行、AssertSystem.FindControl 第 917-919 行、FindButton 第 925-927 行、ValidateSystem 第 959-961 行）
- `CommandExecution/ECS/ClickSetGetSystems.cs`（ClickSystem.PerformDirectClick 第 64-66 行、SetSystem.PerformDirectSet 第 140-142 行、GetSystem.PerformDirectGet 第 210-212 行）

**问题描述**：
```csharp
ControlFinder.FindAcrossForms(
    formMatcher: _ => true,
    controlLookup: form => ControlFinder.FindRecursive(form, name))
```
此模式在至少 6 处重复。`FindButton` 与 `FindControl` 之间仅有返回类型（Button vs Control）不同，应统一为泛型 `FindControl<T>`。

### DL-4: Entity ID 字符串解析缓存多份独立实现
**涉及文件**：
- `WinForms/Applier/WinFormApplier.cs`（`_entityIdCache` + `TryParseEntityId` 委托 `EntityIdParser`）
- `WinForms/Adapter/WinFormsUIAdapter.cs`（`_entityIdCache` + `TryParseEntityId` 委托 `EntityIdParser`）

**问题描述**：
两个类各自维护一份 `Dictionary<string, Entity>` 缓存 + `TryParseEntityId` 方法。既然 `EntityIdParser` 已是共享类，缓存也应移入其中，避免两处独立管理。

### DL-5: `CommandState` 文本映射与 `CommandSystemCoordinator` 状态管理重复
**涉及文件**：`CommandExecution/Ports/CommandOutputPort.cs`（`CommandResultRow.StateText` switch）、`CommandSystemCoordinator.ProcessEntity`（状态机转换）

**问题描述**：
状态枚举的唯一来源是 `CommandState`，但状态文本映射（switch）在 `CommandResultRow.StateText` 中单独维护，状态语义在 `ProcessEntity` 中再次以逻辑分支体现，没有集中到单一状态机定义。

---

## 三、冗余逻辑（Redundant Logic）

### RL-1: `EnqueueExecutionWithResult` 纯转发方法待清理
**涉及文件**：`Ports.cs` 第 945-947 行

```csharp
[Obsolete("EnqueueExecutionWithResult 为 EnqueueExecution 的纯转发别名（语义无差异）。请直接调用 EnqueueExecution。", true)]
public Entity EnqueueExecutionWithResult(...) => EnqueueExecution(...);
```
已标记 `Obsolete(error: true)`，纯转发。若调用方已全部迁移，应直接删除而非保留。

### RL-2: `ExecuteNext` 误导性命名包装待清理
**涉及文件**：`CommandExecution/Ports/CommandInputPort.cs` 第 209-216 行

```csharp
[Obsolete("... 名不副实 ...", true)]
public bool ExecuteNext() { ExecuteAll(); return HasPendingCommands(); }
```
已标记 `Obsolete(error: true)`，纯转发。应删除。

### RL-3: `NonEventCommandLookupAdapter` 已废弃但保留
**涉及文件**：`CommandExecution/Context/NonEventCommandLookupAdapter.cs`

```csharp
[Obsolete("ECS 化后不再需要桥接，请使用 CommandSystemCoordinator 替代")]
internal class NonEventCommandLookupAdapter : ...
```
整类标记废弃，若确认零调用方应清理。

### RL-4: `FunctionExecutionSystem._executionService` 字段从未使用
**涉及文件**：`ECS/Systems/FunctionExecutionSystem.cs` 第 40 行

```csharp
private readonly IEventExecutionService _executionService;
```
构造函数注入但 `Update()` 中从未使用（提交到 MatrixEngine 而非直接执行）。死代码。

### RL-5: `CommandInputPort` 与 `CommandSystemCoordinator` 重复创建 `ParseCommandSystem`
**涉及文件**：`CommandExecution/Ports/CommandInputPort.cs`（第 46 行）、`CommandExecution/ECS/CommandSystems.cs`（第 51 行）

**问题描述**：
`CommandInputPort` 构造函数创建 `_parseSystem = new ParseCommandSystem(entityManager)` 用于直接调用；`CommandSystemCoordinator.RegisterDefaultSystems()` 又注册了一个新的 `ParseCommandSystem` 实例。同类型不同实例，职责重叠。

### RL-6: `CommandSystemCoordinator.Update()` 与 `CommandInputPort.ExecuteAll()` 完全等价
**涉及文件**：`CommandExecution/Ports/CommandInputPort.cs`（第 195-198 行）、`CommandExecution/ECS/CommandSystems.cs`（第 85-100 行）

```csharp
// CommandInputPort.ExecuteAll:
public void ExecuteAll() => _coordinator.Update();

// CommandSystemCoordinator.Update:
public void Update() { ... }
```
`ExecuteAll` 是纯转发到 `_coordinator.Update()`，冗余包装。

### RL-7: `RenderPipelineComposition.LastAppliedNodeCount` 与 `WinFormsOutputPort.LastAppliedNodeCount` 重复维护
**涉及文件**：`Initial/RenderPipeline/RenderPipelineComposition.cs`（第 127 行）、`Ports.cs`（WinFormsOutputPort 第 501 行）

**问题描述**：
两个类各自维护一遍"最后一次应用节点数"，并且 `Composition.ExecuteRenderStage` 从 `OutputPort.LastAppliedNodeCount` 读取后再存到自己的属性，纯转发冗余。

---

## 四、错误逻辑（Incorrect Logic）

### EL-1: `GetSystem.PerformDirectGet` 对不存在的控件处理错误
**涉及文件**：`CommandExecution/ECS/ClickSetGetSystems.cs` 第 206-214 行

```csharp
private static string? PerformDirectGet(string controlName)
{
    var control = ControlFinder.FindAcrossForms(...);
    if (control == null)
        return null;   // ← 返回 null
    ...
}
```

**问题描述**：
`GetSystem.Execute` 中 `control == null` 时 `PerformDirectGet` 返回 `null`，但 `Execute` 中并没有区分"控件不存在"与"控件值为空"，在日志中统一记录 `value = "(空)"` 而非 `未找到控件`。虽然 `SetSuccess(entity, value ?? "")` 不会崩溃，但语义有误——UI 上显示为成功获取到空值，而非失败。

### EL-2: `LayoutInspectSystem.ExecuteOverlaps` 用 `SetFailure` 上报重叠报告
**涉及文件**：`CommandExecution/ECS/LayoutInspectSystem.cs` 第 229 行

```csharp
SetFailure(entity, output);
```

**问题描述**：
重叠检测成功执行并发现重叠，应将结果作为成功输出（`SetSuccess`），而非失败。用 `SetFailure` 会污染命令状态语义（"失败"表示命令本身出错，而非检测到问题）。

### EL-3: `FunctionCallFacade.ExecuteAsyncInternalSync` 的 async/await 无意义包装
**涉及文件**：`Gate/FunctionCallFacade.cs` 第 79-85 行

```csharp
public static FunctionCallResult ExecuteSyncRequest(FunctionCallRequest request)
{
    return ExecuteAsyncInternalSync(request).GetAwaiter().GetResult();
}
private static async Task<FunctionCallResult> ExecuteAsyncInternalSync(FunctionCallRequest request)
{
    return await ExecuteAsync(request).ConfigureAwait(false);
}
```
两处 `ConfigureAwait(false)` + `GetAwaiter().GetResult()` 的组合，虽然解决了死锁风险，但 `ExecuteSyncRequest` 是同步方法却经过 async/await 中转后再同步阻塞，属于**异步套同步再套异步**的反模式。更好的方案是让 `ExecuteAsync` 本身支持同步执行路径（判断 `SynchronizationContext` 后决定是否等待），而不是调用方做阻塞包装。

### EL-4: `UiThreadHelper.DispatchIfNeeded<TResult>` 中 `GetAwaiter().GetResult()` 可能死锁
**涉及文件**：`WinForms/Utils/UiThreadHelper.cs` 第 145-169 行

**问题描述**：
- `DispatchIfNeeded<TResult>` 使用 `BeginInvoke` 投递到 UI 线程后立即 `tcs.Task.GetAwaiter().GetResult()` 阻塞等待。
- 但 `BeginInvoke` 投递的消息**只有 UI 线程消息循环空闲时**才会执行。如果当前阻塞调用本身就在 UI 线程上下文（`InvokeRequired == false` 分支不阻塞，但若调用者在线程池而 UI 线程被其它同步阻塞），`GetAwaiter().GetResult()` 会等待一个永远不会执行的回调。
- 注释声称"BeginInvoke 只投递不等待，UI 线程恢复后自然执行"，但**随即又 `GetAwaiter().GetResult()` 等待了**——自相矛盾。
- 正确做法：要么完全 Fire-and-Forget（不等待结果），要么接受可能阻塞的语义（明确文档化）。

### EL-5: `CommandSystemCoordinator.ProcessEntity` 中未知命令的 `handled = true` 逻辑不当
**涉及文件**：`CommandExecution/ECS/CommandSystems.cs` 第 110-156 行

```csharp
try
{
    if (_systems.TryGetValue(meta.Action, out var system))
    {
        system.Execute(entity, meta);
        handled = true;
    }
}
catch (Exception ex)
{
    ...
    handled = true;
}
sw.Stop();
if (!handled)
{
    // 未知命令
    ...
}
```

**问题描述**：
- `try` 中 system 未找到时 `handled` 保持 `false`，正确走未知命令分支。
- `catch` 中无论捕获什么异常都设 `handled = true`——但如果 `_systems.TryGetValue` 本身没找到 system，`try` 块内不会抛异常，所以这个 `catch` 的 `handled = true` 只在 system 执行异常时设置——行为正确但语义混乱。
- 真正的问题是：`catch` 分支直接设置 `Failed` 状态（不经过状态机），而 `!handled` 的未知命令分支也设置 `Failed`——**两条路径重复处理 Failed 语义**，且 `sw.Stop()` 后 `DurationMs` 赋值分散在两处。

### EL-6: `WinFormApplier.TryFindControl` 有 Entity 但控件为 null 时仍返回 `true`
**涉及文件**：`WinForms/Applier/WinFormApplier.cs` 第 119-137 行

```csharp
internal bool TryFindControl(ControlDescriptor desc, out Entity entity, out object? control)
{
    control = null;
    if (!TryParseEntityId(desc.Id, out entity))
    {
        ...
        return false;
    }
    control = _controlPool.GetControl(entity);
    if (control == null)
    {
        ...  // 只记录了日志
    }
    return true;  // ← 即使 control 为 null 也返回 true！
}
```

**问题描述**：
- `TryFindControl` 返回 `true` 表示"解析成功"，但 `out control` 可能为 `null`。
- 调用方 `ApplyControls` 中 `if (existingControl != null)` 分支处理了 null；但 `ApplyRelations` 中 `parentControl = _controlPool.GetControl(parentEntity)` 和 `childControl = _controlPool.GetControl(childEntity)` 直接使用，如果返回 null 会跳过——这里没有检查 `TryFindControl` 的返回值。
- 方法名带 `Try` 前缀的惯例是返回 bool 表示是否成功（含 out 参数有效性），这里返回 true 但 out 参数为 null 违反约定。

### EL-7: `EcsInputPort.ApplyDataChange` 触发 `DataChanged` 事件时机不一致
**涉及文件**：`Ports.cs` 第 991-1015 行

```csharp
public void ApplyDataChange<T>(Entity entity, T newValue, Func<T?, T>? updateAction = null)
{
    if (!entity.IsValid) throw ...;
    if (updateAction != null)
    {
        var oldValue = ...;
        var updated = updateAction(oldValue);
        _entityManager.AddOrSetComponent(entity, updated);
    }
    else
    {
        _entityManager.AddOrSetComponent(entity, newValue);
    }
    DataChanged?.Invoke();
}
```

**问题描述**：
- `updateAction` 分支先读 `oldValue`、再调用 `updateAction`、再写入——但若 `updateAction` 内部抛异常，`DataChanged` 不会被触发（虽然组件也未写入，这是正确的）。
- 但更关键的是：**即使写入失败（如 AddOrSetComponent 抛异常），DataChanged 也不会触发**，因为没包 try-finally。虽然这不一定是 bug，但事件触发的一致性语义未明确。

### EL-8: `CommandRuntimeContext.Items` 返回静态只读空字典
**涉及文件**：`CommandExecution/Context/CommandRuntimeContext.cs` 第 44-55 行

```csharp
private static readonly IDictionary<string, object?> _emptyItems = new Dictionary<string, object?>();
public IDictionary<string, object?> Items => _emptyItems;
```

**问题描述**：
- 所有 `CommandRuntimeContext` 实例共享同一个静态字典实例。
- 调用方若尝试修改 `Items`（如 `context.Items["key"] = value`），会直接修改这个共享的静态实例，导致**跨实例污染**。
- 而且返回类型是 `IDictionary<string, object?>`（非只读视图），调用方无法判断写操作会抛异常还是污染共享状态。

---

## 五、异常处理与错误处理缺陷

### EX-1: `SleepSystem` 使用 `Thread.Sleep` 阻塞线程
**涉及文件**：`CommandExecution/ECS/CommandSystems.cs` 第 392 行

```csharp
Thread.Sleep(ms);
```
在 ECS/命令管线中直接阻塞调用线程（可能是 UI 线程），应使用异步等待或至少标记为非 UI 线程执行。

### EX-2: `LoadSystem` 与 `SideloadSystem` 使用 `File.ReadAllText` 同步 IO
**涉及文件**：`CommandExecution/ECS/CommandSystems.cs`（LoadSystem 第 538 行、SideloadSystem 第 465 行）

同步文件 IO 在 UI 线程上执行会卡顿界面。虽然命令执行通常在后台线程，但未明确文档化线程模型。

### EX-3: `EcsGate.DoExecuteFunc` 超时回调与 Reactor 存在竞态（已修复但仍有残留）
**涉及文件**：`Gate/EcsGate.cs` 第 160-206 行

**问题描述**：
代码注释显示竞态已通过"先 TryGetValue 确认再 TrySetResult(Timeout)"修复。但仍有隐患：
- 超时回调 `pendingTcs.TrySetResult(FunctionExecutionResult.Timeout)` 仅在回调时检查 `_pendingResults` 是否还存在。
- 但实际是 Reactor 先 `TryRemove` 再 `TrySetResult`（真实结果）——如果 Reactor `TryRemove` 成功但正准备 `TrySetResult`，超时回调恰在此时 `TryGetValue` 会得到 `false`（已移除），安全。反之若超时回调先 `TrySetResult(Timeout)` 而 Reactor 后 `TryRemove`，则真实结果被丢弃——超时回调没有持有锁，仍存在微小时竞态窗口。

---

## 六、代码注释/文档与实现不一致

### CI-1: `Collect()` 的 XML 注释仍引用旧设计
**涉及文件**：`Ports.cs`（CsgEntryPoint 类注释，第 28-35 行）

> "管线末尾统一收集所有 Entity 的 Component 状态，构建完整中间表示"

但如今实际路径是 `CollectSnapshot()`（走 `WinFormSnapshot`），`Collect()` 已不再是主路径。注释未更新。

### CI-2: `WinFormApplier.Apply` 注释声称"打印完整 snapshot"
**涉及文件**：`WinForms/Applier/WinFormApplier.cs` 第 91 行

```csharp
//  ★ 唯一日志入口：矩阵打印完整 snapshot
LogSnapshot(snapshot);  // 实际只打印 controlCount + relationCount 摘要
```
注释与实现不符——`LogSnapshot` 只记录计数，不再打印完整表格（代码注释里也自相矛盾地承认了这一点）。

---

## 七、问题优先级矩阵

| 优先级 | 问题编号 | 简述 |
|--------|---------|------|
| **P0（必须修复）** | AD-1 | 函数调用双轨制，结果模型不统一 |
| **P0** | AD-2 | `(TResult)(object)` 泛型安全丢失 |
| **P0** | EL-4 | `DispatchIfNeeded<TResult>` 潜在死锁 |
| **P1（应该修复）** | AD-3 | 新旧命令架构双轨并存 |
| **P1** | AD-4 | RenderPipelineComposition 职责过重 |
| **P1** | EL-1 | GetSystem 控件不存在语义错误 |
| **P1** | EL-2 | LayoutInspect 用 SetFailure 报告检测结果 |
| **P1** | EL-6 | TryFindControl 违反 Try 前缀约定 |
| **P2（建议修复）** | DL-1~DL-5 | 重复逻辑统一提取 |
| **P2** | RL-1~RL-7 | 冗余/死代码清理 |
| **P2** | EL-3/EL-5/EL-7/EL-8 | 逻辑语义修正 |
| **P3（技术债务）** | EX-1~EX-3, CI-1~CI-2 | 性能与文档对齐 |

---

## 八、改进建议（概览）

1. **统一函数调用入口**：废弃 `FunctionCallFacade` 的双路径分派，改为单一路径（Async 为主，同步场景通过 `ConfigureAwait(false)` + 文档化线程模型），或明确提出 Sync 路径是"快速通道"并在 ECS 层补齐审计 Interceptor。
2. **修复 `EcsGate.DispatchAsync` 的类型安全**：为每个操作定义类型化的 `ExecuteAsync` 重载，或在 Operation 基类中定义 `Execute<TResult>` 虚拟方法。
3. **合并 UI 线程调度**：统一到 `UiThreadHelper`，废弃 `WinFormsControlAdapter.InvokeOnUiThread` 的独立实现；`DispatchIfNeeded<TResult>` 移除 `GetAwaiter().GetResult()` 阻塞，改为返回 `Task<TResult>`。
4. **清理命令系统双轨**：删除 `CommandExecution/Abstractions/` 下的旧接口（或标记明确废弃），`CommandInputPort` 与 `CommandSystemCoordinator` 收敛为单一调度器。
5. **提取共享控件查找**：将 `ControlFinder.FindAcrossForms` 模式封装为 `ControlFinder.FindControl<T>(string name)` 泛型方法，消除 6 处重复。
6. **删除死代码**：`_executionService` 字段、纯转发 Obsolete 方法、废弃适配器类。