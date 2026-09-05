# 事件绑定与函数执行 ECS 管道重构方案（完整版）

> **版本**: v2.0
> **日期**: 2026-07-26
> **状态**: 方案设计（含全链路分析）
> **关联**: MainLayout.json functionName 绑定失败修复 Phase 0 (2026-07-26)

---

## 目录

1. [背景与问题](#1-背景与问题)
2. [完整调用链追踪](#2-完整调用链追踪)
3. [目标架构](#3-目标架构)
4. [新增 ECS Component](#4-新增-ecs-component)
5. [新增 ECS System](#5-新增-ecs-system)
6. [修改现有文件](#6-修改现有文件)
7. [删除文件](#7-删除文件)
8. [线程模型设计](#8-线程模型设计)
9. [多帧渲染循环](#9-多帧渲染循环)
10. [向后兼容与过渡策略](#10-向后兼容与过渡策略)
11. [实施计划](#11-实施计划)

---

## 1. 背景与问题

### 1.1 当前完整架构（全链路）

从 JSON 解析到函数执行的完整 6 阶段路径：

```
┌─────────────────────────────────────────────────────────────────────────────┐
│ 阶段 A：配置加载 (MainForm.OnLoadAsync)                                     │
│                                                                              │
│ ① ControlConfig 反序列化（functionName: "LoadConfig" 被解析）                │
│ ② ConfigDrivenControlFactory.Convert()                                      │
│    ▸ ControlFactory.CreateFromConfig()                                      │
│      ▸ AddEventComponents() → EventBindingsComponent 写入 ECS               │
│    ▸ AddPendingComponents()                                                 │
│ ③ Composition.ExecuteCreationStage()                                        │
│    ▸ EcsCapabilityApplier.Update()  — 控件创建                              │
│    ▸ LayoutSystem.Update()         — 布局计算                               │
│    ▸ SplitContainerSystem.Update() — Splitter 处理                          │
│    ▸ StyleSystem.Update()          — 样式合并                               │
│    ▸ EventBindingSystem.Update()   — ★ 事件绑定（问题点①）                 │
│ ④ Composition.ExecuteRenderStage()                                          │
│    ▸ CsgEntryPoint.CollectSnapshot() — CSG 收集                             │
│    ▸ WinFormsOutputPort.ApplySnapshot() — WinForms 输出                     │
└──────────────────────────────────────────────────────────────────────────────┘
                               ↓
┌─────────────────────────────────────────────────────────────────────────────┐
│ 阶段 B：事件绑定 (EventBindingSystem.Update) — ★ 问题点①                     │
│                                                                              │
│ EventBindingSystem 遍历 EventBindingsComponent 后的委派链：                  │
│                                                                              │
│ WinFormsOutputPort.BindEvent()                                              │
│   → WinFormApplier.BindEvent()                                              │
│     → FileDrivenEventBinder.BindEvent()           ← DI 解析                 │
│       → EventBindingOrchestrator.LoadAndBindDeclarations()                   │
│         → EventBindingPipelineFactory 的 6 步 StaticPipeline:               │
│           ① ContextInit                                                     │
│           ② ValidateInput                                                   │
│           ③ ControlTreeWalk                                                 │
│              → ControlFinderStep()          (反射查找控件)                   │
│              → BindingMetadataStep()        (提取绑定元数据)                 │
│              → EventBinderStep()                                           │
│                → DeclarationBindingStep()                                   │
│                  → BindSingleEvent()                                       │
│                    → CreateEventHandler()     ← ★ 创建 Click handler        │
│                      → functionResolver.ResolveDelegate() ← DI              │
│                      → functionResolver.ResolveWorkflow() ← DI              │
│           ④ DiagnosticsScope                                                │
│           ⑤ EmptyViewGuard                                                  │
│           ⑥ StatisticsCollect                                                │
└──────────────────────────────────────────────────────────────────────────────┘
                               ↓
┌─────────────────────────────────────────────────────────────────────────────┐
│ 阶段 C：按钮点击 (用户交互)                                                  │
│                                                                              │
│ 用户点击 → WinForms Click 事件触发                                          │
│   → CreateEventHandler 创建的 handler (异步委托)                            │
│     ① parameterBinder.BuildFromSources()                                    │
│     ② functionResolver.ResolveDelegate()       ← ★ 再次 DI 查找            │
│     ③ ExecuteDelegateAsync(del, parameters)    ← DynamicInvoke 执行        │
│     ④ eventBinder.HandleExecutionResult(result) ← 结果回调                 │
│     ★ 这条路径完全在 UI 线程 + DI 容器中执行，ECS World 不知道              │
└──────────────────────────────────────────────────────────────────────────────┘
                               ↓
┌─────────────────────────────────────────────────────────────────────────────┐
│ 阶段 D：命令管线（脚本/CLI 模式下的 exec/echo/sideload 等） — ★ 问题点②     │
│                                                                              │
│ PipelineFactory 命令管线:                                                    │
│   ParseCommandStage → ExecuteCommandStage → ResultCollect                    │
│                                                                              │
│ ExecuteCommandStage 内部:                                                    │
│   ParseCommandSkeleton → FindBindingSkeleton → ExecuteFunctionSkeleton       │
│     → FunctionStorageFunctionExecutor.ExecuteAsync(functionName, ...)       │
│       → ITrConfigRegistry.Functions[functionName] – 在 DI 容器中查找        │
│     → ★ 同一条 DI 路径，完全绕过 ECS                                        │
└──────────────────────────────────────────────────────────────────────────────┘
                               ↓
┌─────────────────────────────────────────────────────────────────────────────┐
│ 阶段 E：结果渲染 — ★ 问题点③                                                 │
│                                                                              │
│ 函数执行完后，结果要通过以下路径反映到 UI：                                  │
│   DynamicInvoke 返回结果                                                     │
│   → eventBinder.HandleExecutionResult()  (写入日志，不写 ECS)               │
│   → 如果需要更新 UI 控件（如 "output=resultTextBox"）                       │
│     → 通过 WinForms 控件引用直接设置值                                      │
│     → 不经过 CSG 管线                                                        │
│   ★ ECS World 中的 Entity 状态不同步                                        │
└──────────────────────────────────────────────────────────────────────────────┘
```

### 1.2 问题清单（全量）

| 编号 | 问题 | 严重 | 涉及阶段 | 说明 |
|------|------|------|----------|------|
| P1 | **事件绑定委派给管线外组件** | 高 | B | EventBindingSystem 将绑定工作委派给 FileDrivenEventBinder → EventBindingOrchestrator，完全脱离 ECS 管线 |
| P2 | **函数注册不经过 ECS** | 高 | A | UIToolbox.RegisterAll() 直接写入 DI 容器中的 FunctionStorageRegistry |
| P3 | **函数执行在 DI 容器中** | 高 | C | Click handler 通过 `functionResolver.ResolveDelegate()` 从 DI 查找并执行函数 |
| P4 | **命令管线函数执行也走 DI** | 高 | D | `exec` 命令的 `ExecuteFunctionSkeleton` 通过 `FunctionStorageFunctionExecutor` 从 DI 查找 |
| P5 | **结果不写回 ECS** | 中 | E | 函数执行结果通过 DI 回调传递，ECS Entity 状态不同步 |
| P6 | **EventBindingOrchestrator 管线冗余** | 中 | B | 6 步 StaticPipeline 中 ContextInit/ValidateInput/ControlTreeWalk 等步骤在 ECS 路径下完全不需要 |
| P7 | **线程安全** | 中 | B→C | UI 线程 Click handler 直接操作 ECS World（非线程安全） |
| P8 | **无法触发重新渲染** | 中 | E | 函数执行后需要重新执行 CSG 收集 + WinForms 输出，当前无触发机制 |

### 1.3 追加分析：为什么已有修复没有让按钮工作

2026-07-26 Phase 0 修复让 `functionName` 被正确写入 `EventBindingsComponent`，但有两个问题：

1. **EcsInputPort 路径（①→②）**：`EventBindingSystem.Update()` 从 ECS 读 `EventBindingsComponent`，然后委派给 `WinFormsOutputPort.BindEvent()` → `FileDrivenEventBinder`。如果这个委派链**没有运行**（因为 RenderPipelineComposition 的 `ExecuteCreationStage()` 在两步流程中被 `FileDrivenApplicationService.LoadUIConfigAsync` 调用），那么按钮并不会真正绑定。

2. **外部 EventBindingOrchestrator 路径**：用户日志中看到 GUI 窗口中按钮没有反应，说明 `EventBindingSystem` → `FileDrivenEventBinder` 链可能没有触发，或者 `FileDrivenEventBinder` 中 `AppServiceProvider.Resolve<EventBindingOrchestrator>()` 返回了 null（因为 `EventBindingOrchestrator` 在 `FrameworkInitializer.InitializeWinFormsSubsystem` 中注册，但 GUI 模式启动时是否一定经过这个路径不确定）。

---

## 2. 完整调用链追踪

### 2.1 三收拢点边界

```
收拢点①: EcsInputPort.LoadConfig() / RegisterFunction()
          └── 所有进入 ECS World 的数据

收拢点②: CsgEntryPoint.CollectSnapshot()
          └── ECS → CanonicalForm 的唯一出口

收拢点③: WinFormsOutputPort.ApplySnapshot()
          └── CanonicalForm → WinForms 控件的唯一输出
```

### 2.2 问题代码的调用链标注

```
ECS World 内部                    ECS World 外部（DI 容器）
─────────────────                ──────────────────────────
EventBindingsComponent ──────→   EventBindingOrchestrator      ← P1
  ↑                                ↑
ControlFactory                   IFunctionResolver              ← P1
  ↑                                ↑
ControlConfig                    FunctionStorageRegistry        ← P2
  ↑                                ↑
ControlConfig.FunctionName       UIToolbox.RegisterAll()       ← P2

                                  Click handler 运行时:
                                  functionResolver.ResolveDelegate() ← P3
                                  ExecuteDelegateAsync()              ← P3

                                  命令管线:
                                  FunctionStorageFunctionExecutor    ← P4
                                  .ExecuteAsync()
```

**所有红色的箭头都是应该被收拢到 ECS 内部的路径。**

### 2.3 目标全链路

```
阶段 A：配置加载
  ControlConfig → EcsInputPort.LoadConfig()
    → EventBindingsComponent (ECS)
    → GlobalFunctionRegistryComponent (ECS) ← ★ 函数注册也在这里

阶段 B：事件绑定
  EventBindingSystem.Update()
    → 读 EventBindingsComponent
    → 读 GlobalFunctionRegistryComponent（校验函数存在性）
    → control.Click += (s,e) => PendingExecutionComponent 入队
    → 移除 PendingEventBindingComponent
    → 添加 ExecutionBindingComponent / EventsBoundComponent
    ★ 不离开 ECS World

阶段 C：按钮点击
  control.Click 触发
    → 写 PendingExecutionComponent (入队)
    → ★ UI 线程到此为止

  FunctionExecutionSystem.Update()（管线阶段）
    → 消费 PendingExecutionComponent
    → 查 GlobalFunctionRegistryComponent
    → 执行函数
    → 结果写 ExecutionResultComponent
    → 标记脏 (Composition.MarkDirty())
    ★ 函数在管线线程执行

阶段 D：结果渲染
  ExecuteRenderStage() 再次执行
    → CsgEntryPoint.CollectSnapshot()
      → 读 ExecutionResultComponent → 填入 ControlDescriptor
    → WinFormsOutputPort.ApplySnapshot()
    → 函数结果反映到 UI
    ★ 经过收拢点②③

阶段 E：命令管线（非事件路径）
  exec Math_GetDomains
    → ParseCommandStage → ExecuteCommandStage
    → EcsInputPort.EnqueueExecution(functionName, parameters) ← ★ 改用 ECS 队列
    → FunctionExecutionSystem.Update() 消费
    → 结果写 ExecutionResultComponent
    → 下一帧渲染
```

---

## 3. 目标架构

### 3.1 原则

1. **所有数据流经 ECS Component**：函数注册、事件绑定、函数执行、执行结果，全在 ECS World 内
2. **System 内完成所有逻辑**：不委派给 DI 容器中的外部组件
3. **三收拢点不变**：EcsInputPort / CsgEntryPoint / WinFormsOutputPort 边界不变
4. **命令管线也走 ECS**：`exec` 等命令通过 ECS 队列执行而非 DI 解析
5. **结果驱动渲染**：函数执行结果写回 ECS → 触发重新渲染 → 结果反映到 UI

### 3.2 新架构

```
┌──────────────────────────────────────────────────────────────────────────────┐
│  ECS World                                                                    │
│                                                                                │
│  [GlobalSystemEntity — 固定 Entity ID]                                        │
│  ├─ GlobalFunctionRegistryComponent ─── Dictionary<string, FunctionEntry>    │
│  │                                     所有已注册函数在这里                   │
│  ├─ ExecutionQueueComponent       ─── Queue<ExecutionRequest>                │
│  │                                     待执行请求队列                         │
│  ├─ ExecutionHistoryComponent     ─── List<ExecutionResult>                  │
│  │                                     最近 N 条执行结果                     │
│  └─ DirtyFlagComponent            ─── bool                                   │
│                                        是否需要重新渲染                       │
│                                                                                │
│  [Button Entity: loadConfigButton]                                            │
│  ├─ EventBindingsComponent          ─── {click → ["LoadConfig", ...]}        │
│  ├─ PendingEventBindingComponent    ─── (待处理标记)                          │
│  └─ ExecutionBindingComponent       ─── {FunctionName="LoadConfig"}          │
│                                                                                │
│  Systems:                                                                      │
│  ├─ EventBindingSystem              ─── 绑定 WinForms 事件                   │
│  │                                     handler 只写 ECS 队列                  │
│  └─ FunctionExecutionSystem         ─── 消费队列，执行函数                    │
│                                        结果写回 ECS + 标记脏                  │
└──────────────────────────────────────────────────────────────────────────────┘
```

### 3.3 外部接口

```
Business 层（UIToolbox）                     ECS 世界
─────────────────────────                   ─────────────────
IFunctionRegistrar.Register()
  → 同时写入 DI + ECS
                                         EcsInputPort.RegisterFunction()
                                           → GlobalFunctionRegistryComponent

命令管线（exec/echo）                      ECS 世界
─────────────────────────                   ─────────────────
EcsInputPort.EnqueueExecution()
                                           → ExecutionQueueComponent
                                           → FunctionExecutionSystem 消费
```

---

## 4. 新增 ECS Component

### 4.1 GlobalFunctionRegistryComponent

```csharp
// 位置: CsharpPrj/BasicMethod/ECS/Components/GlobalFunctionRegistryComponent.cs

namespace TConfig.ECS.Components;

/// <summary>
/// 全局函数注册表 — 附加在 GlobalSystemEntity 上
/// <para>ECS 世界内唯一可信的函数注册来源。</para>
/// <para>★ 2026-07-26 新增：消除事件绑定对 DI 容器中 IFunctionResolver 的依赖。</para>
/// <para>使用 ConcurrentDictionary 确保 UI 线程 Click handler 写入安全。</para>
/// </summary>
public struct GlobalFunctionRegistryComponent : IComponent
{
    public ConcurrentDictionary<string, FunctionRegistration>? Functions;
}

public class FunctionRegistration
{
    public string Name { get; init; }
    public Delegate Handler { get; init; }
    public string Description { get; init; }
}
```

设计说明：使用 `ConcurrentDictionary` 而非普通 `Dictionary`。因为 UI 线程的 Click handler 和管线线程的 FunctionExecutionSystem 可能同时访问注册表（写入 PendingExecution 入口时可能需要查注册表做校验）。

### 4.2 ExecutionRequestComponent (入队)

```csharp
// 位置: CsharpPrj/BasicMethod/ECS/Components/ExecutionRequestComponent.cs

/// <summary>
/// 执行请求 — handler 或命令管线写入此组件入队
/// <para>★ 2026-07-26 新增：替代 functionResolver.ResolveDelegate 的运行时路径。</para>
/// <para>此组件附加在 GlobalSystemEntity 上，支持多次添加（同一 Entity 多个组件）</para>
/// </summary>
public struct ExecutionRequestComponent : IComponent
{
    /// <summary>要执行的函数名称</summary>
    public string FunctionName;

    /// <summary>参数字典（参数名 → 值）</summary>
    public Dictionary<string, object?>? Parameters;

    /// <summary>触发此请求的源 Entity ID（可选，用于结果回写）</summary>
    public int SourceEntityId;

    /// <summary>请求时间戳</summary>
    public DateTime Timestamp;
}
```

设计说明：
- 每次按钮点击或 `exec` 命令，向 `GlobalSystemEntity` **添加**一个 `ExecutionRequestComponent`
- ECS 架构中一个 Entity 可以有多个同类型 Component（通过 `AddComponent` 添加，`GetEntities` 收集）
- FunctionExecutionSystem 通过 `GetEntities<ExecutionRequestComponent>()` 收集所有待执行请求

### 4.3 ExecutionResultComponent (出队 + 结果)

```csharp
// 位置: CsharpPrj/BasicMethod/ECS/Components/ExecutionResultComponent.cs

/// <summary>
/// 执行结果 — FunctionExecutionSystem 写入
/// <para>★ 2026-07-26 新增：结果在 ECS 内部流转。</para>
/// </summary>
public struct ExecutionResultComponent : IComponent
{
    /// <summary>已执行的函数名称</summary>
    public string FunctionName;

    /// <summary>执行结果</summary>
    public object? Result;

    /// <summary>是否成功</summary>
    public bool Success;

    /// <summary>错误信息</summary>
    public string? ErrorMessage;

    /// <summary>请求时间戳</summary>
    public DateTime RequestTimestamp;

    /// <summary>完成时间戳</summary>
    public DateTime CompletedTimestamp;
}
```

### 4.4 DirtyFlagComponent (重新渲染标记)

```csharp
// 位置: CsharpPrj/BasicMethod/ECS/Components/DirtyFlagComponent.cs

/// <summary>
/// 脏标记 — FunctionExecutionSystem 执行完函数后设置
/// <para>★ 2026-07-26 新增：驱动多帧渲染循环。</para>
/// </summary>
public struct DirtyFlagComponent : IComponent
{
    /// <summary>是否需要重新执行渲染管线</summary>
    public bool IsDirty;
}
```

---

## 5. 新增 ECS System

### 5.1 FunctionExecutionSystem — 核心设计

```csharp
// 位置: CsharpPrj/Operation/FrontEndDriven/ECS/Systems/FunctionExecutionSystem.cs

namespace FrontEndDriven.ECS.Systems;

/// <summary>
/// 函数执行系统 — 消费 ExecutionRequestComponent，从 GlobalFunctionRegistry 查找并执行
/// <para>★ 2026-07-26 新增：消除 EventBindingOrchestrator + IFunctionResolver 的外部依赖。</para>
/// <para>替代 CreateEventHandler → ExecuteDelegateAsync + NonEventCommandHandler 的执行路径。</para>
/// </summary>
public class FunctionExecutionSystem
{
    private readonly EntityManager _entityManager;
    private Entity? _systemEntity;

    /// <summary>函数执行完成事件（触发 Composition.MarkDirty）</summary>
    public event Action? ExecutionCompleted;

    public FunctionExecutionSystem(EntityManager entityManager)
    {
        _entityManager = entityManager ?? throw new ArgumentNullException(nameof(entityManager));
    }

    public void SetSystemEntity(Entity entity) => _systemEntity = entity;

    public void Update()
    {
        var sysEntity = _systemEntity;
        if (!sysEntity.HasValue || !_entityManager.Exists(sysEntity.Value))
            return;

        // 1. 收集所有待执行请求
        var pendingEntities = _entityManager
            .GetEntities<ExecutionRequestComponent>(sysEntity.Value);
        if (pendingEntities.Count == 0) return;

        // 2. 获取函数注册表
        if (!_entityManager.TryGetComponent<GlobalFunctionRegistryComponent>(
            sysEntity.Value, out var registry))
            return;

        if (registry.Functions == null || registry.Functions.Count == 0)
            return;

        // 3. 逐个执行
        var executedCount = 0;
        var requests = pendingEntities
            .Select(e => _entityManager.GetComponent<ExecutionRequestComponent>(e))
            .OrderBy(r => r.Timestamp)
            .ToList();

        foreach (var request in requests)
        {
            try
            {
                if (registry.Functions.TryGetValue(request.FunctionName, out var funcReg))
                {
                    // 执行函数
                    var parameters = request.Parameters ?? new Dictionary<string, object?>();
                    var result = funcReg.Handler.DynamicInvoke(parameters.Values.ToArray());

                    // 处理 Task 返回
                    if (result is Task task)
                    {
                        task.GetAwaiter().GetResult(); // 同步等待
                        if (task.GetType().IsGenericType)
                        {
                            var resultProp = task.GetType().GetProperty("Result");
                            result = resultProp?.GetValue(task);
                        }
                        else result = null;
                    }

                    // 结果写回
                    _entityManager.AddComponent(sysEntity.Value,
                        new ExecutionResultComponent
                        {
                            FunctionName = request.FunctionName,
                            Result = result,
                            Success = true,
                            RequestTimestamp = request.Timestamp,
                            CompletedTimestamp = DateTime.UtcNow
                        });
                }
                else
                {
                    _entityManager.AddComponent(sysEntity.Value,
                        new ExecutionResultComponent
                        {
                            FunctionName = request.FunctionName,
                            Success = false,
                            ErrorMessage = $"函数 '{request.FunctionName}' 未注册",
                            RequestTimestamp = request.Timestamp,
                            CompletedTimestamp = DateTime.UtcNow
                        });
                }
            }
            catch (Exception ex)
            {
                _entityManager.AddComponent(sysEntity.Value,
                    new ExecutionResultComponent
                    {
                        FunctionName = request.FunctionName,
                        Success = false,
                        ErrorMessage = ex.Message,
                        RequestTimestamp = request.Timestamp,
                        CompletedTimestamp = DateTime.UtcNow
                    });
            }

            executedCount++;
        }

        // 4. 清除所有已处理请求
        var toRemove = _entityManager
            .GetEntities<ExecutionRequestComponent>(sysEntity.Value);
        foreach (var e in toRemove)
            _entityManager.RemoveComponent<ExecutionRequestComponent>(e);

        // 5. 如果有执行结果，设置脏标记并触发事件
        if (executedCount > 0)
        {
            _entityManager.AddComponent(sysEntity.Value,
                new DirtyFlagComponent { IsDirty = true });

            // 触发 Composition 重新渲染
            ExecutionCompleted?.Invoke();
        }
    }
}
```

### 5.2 EcsInputPort — 新增 EnqueueExecution

```csharp
// 在 EcsInputPort 中新增方法

/// <summary>
/// 入队执行请求 — 供命令管线调用
/// <para>★ 2026-07-26 新增：替代 FunctionStorageFunctionExecutor 的 DI 路径。</para>
/// </summary>
public void EnqueueExecution(
    string functionName,
    Dictionary<string, object?>? parameters = null)
{
    var sysEntity = GetOrCreateSystemEntity();

    _entityManager.AddComponent(sysEntity,
        new ExecutionRequestComponent
        {
            FunctionName = functionName,
            Parameters = parameters,
            Timestamp = DateTime.UtcNow
        });
}
```

### 5.3 RenderPipelineComposition — 多帧执行循环

```csharp
// 在 RenderPipelineComposition 中新增

/// <summary>
/// 执行完整管线（支持多帧）
/// <para>★ 2026-07-26 新增：支持函数执行结果驱动的再渲染。</para>
/// </summary>
public void ExecuteLoop(UIConfiguration config, Control? rootContainer = null, string scope = "MainLayout")
{
    ThrowIfDisposed();
    if (config == null) throw new ArgumentNullException(nameof(config));

    // 第 1 帧：配置加载 + 控件创建 + 事件绑定 + 渲染
    FrontEndDrivenLog.Log("RenderPipelineComposition_ExecuteLoop_frame1");
    Execute(config, rootContainer, scope);

    // 监听执行完成事件
    FunctionExecutionSystem.ExecutionCompleted += OnExecutionCompleted;
}

private void OnExecutionCompleted()
{
    var sysEntity = GetSystemEntity();
    if (!sysEntity.HasValue) return;

    // 读取执行结果 → 收集 → 输出
    var results = _entityManager.GetEntities<ExecutionResultComponent>(sysEntity.Value);
    if (results.Count == 0) return;

    FrontEndDrivenLog.Log("RenderPipelineComposition_ExecuteLoop_rerender",
        new { resultCount = results.Count });

    // 重新执行 CSG 收集 + WinForms 输出
    ExecuteRenderStage();

    // 清除脏标记
    _entityManager.RemoveComponent<DirtyFlagComponent>(sysEntity.Value);

    // 清除执行结果（已消费）
    foreach (var e in results)
        _entityManager.RemoveComponent<ExecutionResultComponent>(e);
}
```

---

## 6. 修改现有文件

### 6.1 EventBindingSystem.cs — 重写（不委派）

```csharp
// 位置: CsharpPrj/Operation/FrontEndDriven/ControlInteraction/EventBinding/EventBindingSystem.cs

public class EventBindingSystem
{
    private readonly EntityManager _entityManager;
    private readonly ControlPool _controlPool;
    private Entity? _systemEntity;

    public void SetSystemEntity(Entity entity) => _systemEntity = entity;

    public void Update()
    {
        var sysEntity = _systemEntity;
        if (!sysEntity.HasValue) return;

        var entities = _entityManager.GetEntities<
            EventBindingsComponent, PendingEventBindingComponent>();

        if (entities.Count == 0) return;

        // 获取全局注册表（用于校验）
        _entityManager.TryGetComponent<GlobalFunctionRegistryComponent>(
            sysEntity.Value, out var registry);

        foreach (var entity in entities)
        {
            var bindings = _entityManager.GetComponent<EventBindingsComponent>(entity);
            var control = _controlPool.GetControl(entity) as Control;
            if (control == null) continue;

            foreach (var binding in bindings.Bindings)
            {
                var functionName = binding.FunctionName;

                // 校验函数存在（Loose 模式下跳过）
                if (registry.Functions != null &&
                    !registry.Functions.ContainsKey(functionName))
                    continue;

                switch (binding.EventName.ToLowerInvariant())
                {
                    case "click":
                        control.Click += (s, e) =>
                        {
                            // ★ 不再调用 DI 容器
                            // ★ 入队 ECS 请求
                            _entityManager.AddComponent(sysEntity.Value,
                                new ExecutionRequestComponent
                                {
                                    FunctionName = functionName,
                                    SourceEntityId = entity.Id,
                                    Timestamp = DateTime.UtcNow
                                });
                        };
                        break;

                    // 可按需扩展 TextChanged / SelectedIndexChanged 等
                }

                // 标记已绑定
                _entityManager.AddComponent(entity, new ExecutionBindingComponent
                {
                    FunctionName = functionName,
                    EventName = binding.EventName
                });
            }

            _entityManager.RemoveComponent<PendingEventBindingComponent>(entity);
            _entityManager.AddComponent(entity, new EventsBoundComponent());
        }
    }
}
```

### 6.2 RenderPipelineComposition.cs — 创建 GlobalSystemEntity

```csharp
// Create() 工厂方法末尾增加

// ★ 2026-07-26 重构：创建全局 System Entity
var systemEntity = entityManager.CreateEntity();
entityManager.AddComponent(systemEntity, new GlobalFunctionRegistryComponent
{
    Functions = new ConcurrentDictionary<string, FunctionRegistration>(
        StringComparer.OrdinalIgnoreCase)
});

// 将 System Entity 注入各 System
eventBindingSystem.SetSystemEntity(systemEntity);
functionExecutionSystem.SetSystemEntity(systemEntity);

// 连接函数执行完成事件
functionExecutionSystem.ExecutionCompleted += () =>
{
    // 读取结果，重新渲染
    var results = entityManager.GetEntities<ExecutionResultComponent>(systemEntity);
    if (results.Count > 0)
    {
        // 触发 CSG 收集 + WinForms 输出
        csgEntryPoint.CollectSnapshot();
        winFormsOutputPort.ApplySnapshot(snapshot);
    }
};
```

### 6.3 EcsInputPort.cs — 新增 RegisterFunction + GetOrCreateSystemEntity

```csharp
public void RegisterFunction(string name, Delegate handler, string description)
{
    var sysEntity = GetOrCreateSystemEntity();

    var registry = _entityManager.GetComponent<GlobalFunctionRegistryComponent>(sysEntity);
    registry.Functions ??= new ConcurrentDictionary<string, FunctionRegistration>(
        StringComparer.OrdinalIgnoreCase);

    registry.Functions[name] = new FunctionRegistration
    {
        Name = name,
        Handler = handler,
        Description = description
    };

    _entityManager.SetComponent(sysEntity, registry);
}

public void EnqueueExecution(string functionName, Dictionary<string, object?>? parameters = null)
{
    var sysEntity = GetOrCreateSystemEntity();
    _entityManager.AddComponent(sysEntity, new ExecutionRequestComponent
    {
        FunctionName = functionName,
        Parameters = parameters,
        Timestamp = DateTime.UtcNow
    });
}

private Entity GetOrCreateSystemEntity()
{
    var entities = _entityManager.GetEntities<GlobalFunctionRegistryComponent>();
    foreach (var entity in entities)
        return entity;

    var sysEntity = _entityManager.CreateEntity();
    _entityManager.AddComponent(sysEntity, new GlobalFunctionRegistryComponent
    {
        Functions = new ConcurrentDictionary<string, FunctionRegistration>(
            StringComparer.OrdinalIgnoreCase)
    });
    return sysEntity;
}
```

### 6.4 Ports.cs — WinFormsOutputPort 移除 BindEvent

```csharp
// WinFormsOutputPort — 移除 IEventBindingOutput 的 BindEvent 实现

// ★ 2026-07-26 删除：事件绑定不再通过端口 C 委派
// public void BindEvent(object control, string eventName, string functionName, object bindingData)
// {
//     _winFormApplier.BindEvent(control, eventName, functionName);
// }

// IEventBindingOutput 接口也可以移除，或改为空实现
public void BindEvent(object control, string eventName, string functionName, object bindingData)
{
    // ★ 2026-07-26 空操作：事件绑定已由 EventBindingSystem 内部处理
}
```

### 6.5 IFunctionRegistrar 接口（新增）

```csharp
// 位置: CsharpPrj/Share/FrameworkAbstractions/ComponentRegistry/IFunctionRegistrar.cs

namespace FrameworkAbstractions.ComponentRegistry;

/// <summary>
/// 函数注册器 — 桥梁接口
/// <para>连接 Business 层（UIToolbox）与 ECS 管线和旧 DI 容器。</para>
/// <para>★ 2026-07-26 新增：重构方案的一部分。</para>
/// </summary>
public interface IFunctionRegistrar
{
    void Register(string name, Delegate handler, string description);
}

/// <summary>
/// 默认实现 — 同时写入 ECS 和旧注册表
/// </summary>
public class DefaultFunctionRegistrar : IFunctionRegistrar
{
    private readonly Action<string, Delegate, string> _ecsRegister;
    private readonly ITrConfigRegistry _fallbackRegistry;

    public DefaultFunctionRegistrar(
        Action<string, Delegate, string> ecsRegister,
        ITrConfigRegistry fallbackRegistry)
    {
        _ecsRegister = ecsRegister;
        _fallbackRegistry = fallbackRegistry;
    }

    public void Register(string name, Delegate handler, string description)
    {
        _ecsRegister(name, handler, description);        // ECS World
        _fallbackRegistry.Register(name, handler, description); // DI （过渡期兼容）
    }
}
```

### 6.6 UIToolbox.cs — 修改签名

```csharp
// 当前:
public static void RegisterAll(ITrConfigRegistry registry)

// 重构后:
public static void RegisterAll(IFunctionRegistrar registrar)
{
    registrar.Register("ClearOutput",
        (Func<string>)(() => { ... }),
        "清空输出文本框");
    // ... 其余注册不变
}
```

### 6.7 FileDrivenLifecycle.cs — 修改调用

```csharp
// 当前:
UIToolbox.RegisterAll(_registry);

// 重构后:
var registrar = AppServiceProvider.Resolve<IFunctionRegistrar>();
UIToolbox.RegisterAll(registrar);
```

### 6.8 FrameworkInitializer.cs — 注册 IFunctionRegistrar

```csharp
// 在 InitializeAppServiceProvider() 中增加

// ★ 2026-07-26 重构：注册 IFunctionRegistrar
var ecsInputPort = AppServiceProvider.Resolve<EcsInputPort>();
var funcRegistry = AppServiceProvider.Resolve<ITrConfigRegistry>();

var registrar = new DefaultFunctionRegistrar(
    ecsRegister: (name, handler, desc) =>
        ecsInputPort?.RegisterFunction(name, handler, desc),
    fallbackRegistry: funcRegistry);

AppServiceProvider.Register<IFunctionRegistrar>(registrar);
```

### 6.9 命令管线：FunctionStorageFunctionExecutor 改为 ECS

```csharp
// 在 PipelineFactory.cs 或 ExecuteCommandStage 中

// 当前: 通过 FunctionStorageFunctionExecutor.ExecuteAsync() → DI
// 重构后:
if (command.IsFunctionExecution)
{
    // 改为入队 ECS
    ecsInputPort.EnqueueExecution(
        command.FunctionName,
        command.Parameters);

    // 立即触发 FunctionExecutionSystem 消费（同步模式）
    functionExecutionSystem.Update();

    // 等待执行结果
    var sysEntity = ecsInputPort.GetSystemEntity();
    var results = entityManager.GetEntities<ExecutionResultComponent>(sysEntity);
    // ... 读取最新结果
}
```

---

## 7. 删除文件

| 文件 | 原因 | 替代 |
|------|------|------|
| `FileDrivenEventBinder.cs` | 事件绑定在 EventBindingSystem 内部完成 | 直接绑定 WinForms 事件 |
| `IWinFormEventBinder.cs` | 接口不再需要 | — |
| `FunctionStorageFunctionExecutor.cs`（待定）| 命令管线改用 ECS 队列 | EcsInputPort.EnqueueExecution |

---

## 8. 线程模型设计

### 8.1 三线程协作

```
┌─────────────────────────────────────────────────────────────────────┐
│ UI 线程 (WinForms 消息泵)                                            │
│                                                                      │
│ 职责: 处理用户交互、控件属性读写                                     │
│ 操作:                                                                 │
│   • control.Click += handler                                        │
│   • handler 中写 ExecutionRequestComponent 到 GlobalSystemEntity   │
│   • Control.BeginInvoke 调度 UI 操作到本线程                         │
│                                                                      │
│ 写 ECS 约束: 只允许 ExecutionRequestComponent.AddComponent           │
│             不允许修改其他 Component                                  │
└──────────────────────────────────────────────────────────────────────┘
                      │
                      │ 写 ECS（AddComponent 是线程安全的 ECS 操作）
                      ▼
┌─────────────────────────────────────────────────────────────────────┐
│ 管线线程 (Composition.Execute 调用方线程)                           │
│                                                                      │
│ 职责: 消费执行队列、执行函数                                         │
│ 操作:                                                                 │
│   • FunctionExecutionSystem.Update()                                │
│     • 收集 ExecutionRequestComponent                                │
│     • 从 GlobalFunctionRegistryComponent 查函数                      │
│     • DynamicInvoke 执行                                             │
│     • 写 ExecutionResultComponent                                    │
│     • 设置 DirtyFlagComponent.IsDirty = true                         │
│   • 触发 ExecutionCompleted 事件                                     │
│                                                                      │
│ 约束: 函数操作控件需通过 Control.BeginInvoke 调度到 UI 线程          │
└──────────────────────────────────────────────────────────────────────┘
                      │
                      │ ExecutionCompleted 事件
                      ▼
┌─────────────────────────────────────────────────────────────────────┐
│ 渲染线程 (同管线线程)                                                │
│                                                                      │
│ 职责: 重新执行 CSG 收集 + WinForms 输出                              │
│ 操作:                                                                 │
│   • CsgEntryPoint.CollectSnapshot()                                 │
│   • WinFormsOutputPort.ApplySnapshot()                              │
│   • 清除 DirtyFlagComponent                                          │
│   • 清除已消费的 ExecutionResultComponent                             │
└──────────────────────────────────────────────────────────────────────┘
```

### 8.2 线程安全措施

1. **GlobalFunctionRegistryComponent.Functions**: 使用 `ConcurrentDictionary`，支持并发读写
2. **ExecutionRequestComponent 写入**: 每次按钮点击 `AddComponent`（ECS API 设计上支持同 Entity 多次 Add 同类型 Component）
3. **函数执行结果**: 只读（FunctionExecutionSystem 写，其他只读），无并发冲突
4. **UI 控件操作**: 如果 `UIToolbox` 中的函数需要操作 WinForms 控件（如 `ClearOutput` 清空 TextBox），需要使用 `Control.BeginInvoke` 调度到 UI 线程

---

## 9. 多帧渲染循环

### 9.1 问题

当前管线是**一次性**的：`ExecuteCreationStage → ExecuteRenderStage` 完成后管线就结束了。函数执行结果无法触发重新渲染。

### 9.2 方案：Dirty Flag 驱动的再渲染

```
第 1 帧:
  ExecuteCreationStage() → EventBindingSystem 绑定事件
  ExecuteRenderStage()   → CSG 收集 + WinForms 输出
  → UI 显示（但无函数执行结果）

用户点击按钮:
  → Write ExecutionRequestComponent (ECS)
  → FunctionExecutionSystem 不在这一帧执行（等待管线调度）

第 1.5 帧（主动触发）:
  FunctionExecutionSystem.Update()
    → 消费队列 → 执行函数 → 写 ExecutionResultComponent
    → 设置 DirtyFlagComponent.IsDirty = true

第 2 帧:
  ExecuteRenderStage() 再次执行
    → CsgEntryPoint.CollectSnapshot()
      → 读 ExecutionResultComponent → 填入 ControlDescriptor
    → WinFormsOutputPort.ApplySnapshot()
    → UI 显示函数执行结果
```

### 9.3 实现

```csharp
// RenderPipelineComposition

public void StartExecutionLoop()
{
    // 连接事件
    _functionExecutionSystem.ExecutionCompleted += OnExecutionResultReady;
}

private void OnExecutionResultReady()
{
    var sysEntity = GetSystemEntity();
    if (!sysEntity.HasValue) return;

    // 检查脏标记
    if (!_entityManager.TryGetComponent<DirtyFlagComponent>(
        sysEntity.Value, out var dirty) || !dirty.IsDirty)
        return;

    // 重新渲染
    ExecuteRenderStage();

    // 清除脏标记
    _entityManager.RemoveComponent<DirtyFlagComponent>(sysEntity.Value);
}
```

### 9.4 与 MainForm 的集成

```csharp
// MainForm.cs — 启动执行循环
await _app.LoadUIConfigAsync(...);   // 阶段 1
await _app.RenderAsync();            // 阶段 2（首次渲染）

_app.Pipeline.StartExecutionLoop();  // ★ 启动执行循环
```

---

## 10. 向后兼容与过渡策略

### 10.1 兼容层

| 组件 | 过渡期行为 | 最终行为 |
|------|-----------|---------|
| `IFunctionRegistrar` | 同时写入 ECS 和 DI | 只写 ECS（DI 保留为只读回退） |
| `EventBindingOrchestrator` | 保留但不再被 EventBindingSystem 调用 | 保留给命令管线或其他自定义场景 |
| `FunctionStorageRegistry` | IFunctionResolver 仍可通过它查询 | 降级为 ECS 的回退查询源 |
| `FileDrivenEventBinder` | Phase 1 标识为废弃，Phase 4 删除 | 删除 |

### 10.2 阶段划分

| 阶段 | 内容 | 影响文件 | 状态 |
|------|------|----------|------|
| **Phase 0** | ControlConfig 添加 FunctionName + ControlFactory 写入 EventBindingsComponent | 2 文件 | ✅ 已完成 |
| **Phase 1** | EventBindingSystem 不再委派，内部绑定 WinForms 事件 + 写 ECS 队列 | EventBindingSystem.cs, Composition.cs, Ports.cs | ❌ 待实施 |
| **Phase 2** | 新增 ECS Component + FunctionExecutionSystem | 5 新文件, Composition.cs | ❌ 待实施 |
| **Phase 3** | IFunctionRegistrar + UIToolbox 改造 + 命令管线迁移 | UIToolbox.cs, Lifecycle.cs, PipelineFactory.cs | ❌ 待实施 |
| **Phase 4** | 删除废弃文件 + 全链路验证 | 删除 2-3 文件 | ❌ 待实施 |

### 10.3 验证标准

每个阶段完成后必须通过以下验证：

1. **Phase 1 验证**: GUI 模式下按钮点击，日志显示 `ExecutionRequestComponent` 入队（而非 `functionResolver.ResolveDelegate`）
2. **Phase 2 验证**: `FunctionExecutionSystem.Update()` 消费队列并执行函数，`ExecutionResultComponent` 有值
3. **Phase 3 验证**: `UIToolbox` 注册的函数在 ECS 的 `GlobalFunctionRegistryComponent` 中可查
4. **Phase 4 验证**: `FileDrivenEventBinder` 删除后 GUI 功能正常，命令管线正常

---

## 11. 文件变更清单（总表）

### 新增文件（6个）

| 路径 | 内容 |
|------|------|
| `CsharpPrj/BasicMethod/ECS/Components/GlobalFunctionRegistryComponent.cs` | 函数注册表 + FunctionRegistration |
| `CsharpPrj/BasicMethod/ECS/Components/ExecutionRequestComponent.cs` | 执行请求（入队） |
| `CsharpPrj/BasicMethod/ECS/Components/ExecutionResultComponent.cs` | 执行结果 |
| `CsharpPrj/BasicMethod/ECS/Components/DirtyFlagComponent.cs` | 脏标记 |
| `CsharpPrj/Operation/FrontEndDriven/ECS/Systems/FunctionExecutionSystem.cs` | 函数执行 System |
| `CsharpPrj/Share/FrameworkAbstractions/ComponentRegistry/IFunctionRegistrar.cs` | 函数注册器接口 + DefaultFunctionRegistrar |

### 修改文件（9个）

| 路径 | 修改 |
|------|------|
| `CsharpPrj/Operation/FrontEndDriven/ControlInteraction/EventBinding/EventBindingSystem.cs` | 重写：不委派，内部绑定 |
| `CsharpPrj/Operation/FrontEndDriven/Ports.cs` | EcsInputPort 新增 RegisterFunction/EnqueueExecution；WinFormsOutputPort BindEvent 改为空操作 |
| `CsharpPrj/Operation/FrontEndDriven/Initial/RenderPipeline/RenderPipelineComposition.cs` | Create() 创建 System Entity；新增 ExecuteLoop/OnExecutionCompleted |
| `CsharpPrj/Operation/FrontEndDriven/ECS/Creation/ConfigDrivenControlFactory.cs` | AddPendingComponents 增加初始状态（无需修改） |
| `FileDriven/FileDriven.Business/Registrations/UI/UIToolbox.cs` | RegisterAll 参数从 ITrConfigRegistry 改为 IFunctionRegistrar |
| `FileDriven/FileDriven.Execution/Initial/FileDrivenLifecycle.cs` | 调用 UIToolbox 时传入 IFunctionRegistrar |
| `FileDriven/FileDriven.Execution/Initial/FrameworkInitializer.cs` | 注册 IFunctionRegistrar 到 DI；移除 FileDrivenEventBinder 注册 |
| `FileDriven/FileDriven.Execution/Initial/MainForm.cs` | OnLoadAsync 启动后调用 Composition.StartExecutionLoop() |
| `FileDriven/FileDriven.Execution/Pipeline/PipelineFactory.cs` | 命令管线的函数执行改为 EcsInputPort.EnqueueExecution |

### 删除文件（2个）

| 路径 | 原因 |
|------|------|
| `CsharpPrj/Operation/FrontEndDriven/WinForms/Applier/FileDrivenEventBinder.cs` | 不再需要 |
| `CsharpPrj/Operation/FrontEndDriven/WinForms/Abstractions/IWinFormEventBinder.cs` | 不再需要 |

---

## 12. 风险矩阵

| 风险 | 概率 | 影响 | 缓解措施 |
|------|------|------|----------|
| ECS Component 写入线程不安全 | 中 | 高 | 使用 ConcurrentDictionary；限制 UI 线程只写 ExecutionRequestComponent |
| DynamicInvoke 性能 | 低 | 中 | 函数调用频率低（按钮点击），可接受 |
| 多帧渲染导致无限循环 | 低 | 高 | DirtyFlag 机制确保函数无结果时不触发再渲染；设置最大循环次数 |
| 命令管线迁移破坏现有功能 | 中 | 高 | 保留旧 DI 路径作为回退（Feature Flag 控制） |
| IFunctionRegistrar 在 Business 层不可用 | 低 | 中 | 接口在 FrameworkAbstractions 中定义，Business 层已引用该程序集 |