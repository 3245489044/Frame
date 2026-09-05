# 事件加载链路集中式重构方案

> **目标**：以 EventTriggerFramework 作为唯一通用框架，集中构建更通用的抽象，BackEndDriven 和 FileDriven.Execution 直接引用 EventTriggerFramework
> **日期**：2026-07-01 | 作者：Cline
> **状态**：方案设计阶段

---

## 1. 现状：分散的问题

### 1.1 三条并行执行路径

| 路径 | 位置 | 机制 | 状态 |
|------|------|------|------|
| **A** | EventBinderTransformer (EventTriggerFramework) | `Func` 委托 | ❌ 未注入，形同虚设 |
| **B** | WinFormsEventBinder (FileDriven.Execution) | `IEventExecutionService` 接口 | ✅ 唯一有效路径 |
| **C** | 旧版 WinFormsEventBinder (EventTriggerFrameworkInitializer) | 旧版类 | ❌ 未使用，死代码 |

### 1.2 跨框架重复组件

| 组件 | EventTriggerFramework | BackEndDriven |
|------|----------------------|---------------|
| **IEventExecutionService** | ❌ 无（Func 委托替代） | ✅ 定义 |
| **EventBindingContext** | ✅ WinForms 版 | ✅ 通用版 |
| **EventBindingPipeline** | ✅ WinForms 管线工厂 | ✅ 通用管线工厂 |
| **EventBindingPipelineRunner** | ✅ WinForms 版 | ✅ 通用泛型版 |
| **6 个 Transformers** | ✅ WinForms 版 | ✅ 通用版 |
| **EventTriggerServiceBase / EventTriggerEngine** | ✅ 抽象基类 | ✅ 抽象基类 |

### 1.3 根因

```
EventTriggerFramework 最初设计为 WinForms 专用
  → BackEndDriven 提取通用抽象时无法复用
    → 在 BackEndDriven 中重新实现了一套通用版
      → 循环依赖迫使 EventTriggerFramework 使用 Func 委托
        → 最终 3 个框架、3 条路径、10 组重复
```

---

## 2. 方案：集中构建通用框架

### 2.1 核心原则

1. **EventTriggerFramework 去 WinForms 化** — 核心抽象不依赖 WinForms，成为纯通用框架
2. **不迁移文件** — 不在 EventTriggerFramework 中新建 BackEndDriven 的副本，而是直接构建更通用的抽象
3. **其他地方直接引用** — BackEndDriven 和 FileDriven.Execution 引用 EventTriggerFramework 的定义
4. **WinForms 实现作为扩展** — WinForms 特定代码放在 EventTriggerFramework 的 WinForms 子命名空间

### 2.2 具体改造项

#### 2.2.1 EventTriggerFramework 新增自有接口（替代 Func 委托）

| 新增文件 | 说明 |
|---------|------|
| `Abstractions/IEventExecutionService.cs` | 自有事件执行服务接口，替代 Func 委托 |
| `Bridge/EventExecutionServiceAdapter.cs` | 适配器，桥接到 BackEndDriven 的具体实现 |

#### 2.2.2 EventTriggerFramework 改造现有类

| 文件 | 改造内容 |
|------|---------|
| `EventBinding/EventBindingTransformers.cs` | EventBinderTransformer：Func 委托 → IEventExecutionService 接口 |
| `EventBinding/EventBindingPipeline.cs` | 管线工厂方法：Func → 接口；删除 3 个 Obsolete 方法 |
| `DefaultEventBindingService.cs` | Func → 接口；SetEventExecuteFunc → SetEventExecutionService |

#### 2.2.3 BackEndDriven 精简

| 文件 | 改造内容 |
|------|---------|
| `Abstractions/IEventBindingLookup.cs` | 标记 Obsolete，指向 EventTriggerFramework 版 |
| `Abstractions/IEventBindingInfo.cs` | 标记 Obsolete，指向 EventTriggerFramework 版 |
| `EventBinding/` 整个目录 | 标记 Obsolete，指向 EventTriggerFramework 版 |

#### 2.2.4 FileDriven.Execution 精简

| 文件 | 改造内容 |
|------|---------|
| `EventBinding/WinFormsEventBinder.cs` | 移除 wrappedHandler 中的 `_eventExecutionService.Execute` 调用（EventBinderTransformer 接管） |
| `Lifecycle/FrameworkInitializer.cs` | 注入适配器到 DefaultEventBindingService |

### 2.3 改造后架构

```
EventTriggerFramework (通用框架)
  ├── Abstractions/
  │   ├── IEventExecutionService    ← 自有（新增）
  │   ├── IWinFormsEventBinder      ← 已有
  │   ├── IEventTriggerService      ← 已有
  │   └── IEventBindingResolver     ← 已有
  ├── EventBinding/
  │   ├── EventBindingPipeline      ← 改造：Func → 接口
  │   ├── EventBindingTransformers  ← 改造：Func → 接口
  │   └── WinForms/                 ← WinForms 扩展
  ├── DefaultEventBindingService    ← 改造：Func → 接口
  └── Bridge/
      └── EventExecutionServiceAdapter  ← 新增

BackEndDriven (精简)
  ├── Abstractions/                 ← 标记 Obsolete
  ├── Execution/
  │   └── EventExecutionService     ← 保留
  └── EventBinding/                 ← 标记 Obsolete

FileDriven.Execution (精简)
  ├── EventBinding/
  │   └── WinFormsEventBinder       ← 精简：移除双重执行
  ├── EventPipeline/
  │   ├── EventPipelineExecutor     ← 保留
  │   └── EventTriggerService       ← 保留
  └── Lifecycle/
      └── FrameworkInitializer      ← 更新注入链路
```

### 2.4 改造后执行路径

```
用户点击按钮 → wrappedHandler 触发
  → EventBinderTransformer 创建的 handler
    → eventExecutionService.Execute()  ← 唯一路径
      → EventExecutionServiceAdapter
        → EventPipelineExecutor
          → 单命令管线 → 执行函数
```

---

## 3. 实施计划

### 阶段一：EventTriggerFramework 新增接口 + 适配器（低风险）

| 步骤 | 内容 |
|------|------|
| 1.1 | 新建 `Abstractions/IEventExecutionService.cs` — 自有事件执行服务接口 |
| 1.2 | 新建 `Bridge/EventExecutionServiceAdapter.cs` — 适配器桥接 BackEndDriven |

### 阶段二：EventTriggerFramework 内部改造（低风险）

| 步骤 | 内容 |
|------|------|
| 2.1 | `EventBindingTransformers.cs` — EventBinderTransformer：Func → IEventExecutionService |
| 2.2 | `EventBindingPipeline.cs` — 管线工厂方法：Func → 接口；删除 3 个 Obsolete 方法 |
| 2.3 | `DefaultEventBindingService.cs` — Func → 接口；SetEventExecuteFunc → SetEventExecutionService |

### 阶段三：FileDriven.Execution 精简（中风险）

| 步骤 | 内容 |
|------|------|
| 3.1 | `WinFormsEventBinder.cs` — 移除 wrappedHandler 中的 `_eventExecutionService.Execute` 调用 |
| 3.2 | `FrameworkInitializer.cs` — 创建适配器，注入到 DefaultEventBindingService |

### 阶段四：BackEndDriven 标记废弃（低风险）

| 步骤 | 内容 |
|------|------|
| 4.1 | `IEventBindingLookup.cs` — 标记 Obsolete |
| 4.2 | `IEventBindingInfo.cs` — 标记 Obsolete |
| 4.3 | `EventBinding/` 目录 — 标记 Obsolete |

---

## 4. 改造前后对比

| 维度 | 改造前 | 改造后 |
|------|--------|--------|
| **执行路径数** | 3 条（A/B/C） | 1 条（统一路径） |
| **事件执行抽象** | Func 委托 + IEventExecutionService 接口 | 统一使用 IEventExecutionService 接口 |
| **循环依赖** | 通过 Func 委托规避 | 通过自有接口 + 适配器消除 |
| **废弃代码** | 旧版 Binder + 3 个 Obsolete 方法 + 空方法 | 已清理 |
### 2.1 分散性评分矩阵

| 维度 | 评分 | 说明 |
|------|------|------|
| **执行路径数** | 🔴 3 条 | 路径 A（委托，未注入）、路径 B（IEventExecutionService，有效）、路径 C（旧版，未使用） |
| **框架分布** | 🔴 3 个框架 | EventTriggerFramework、BackEndDriven、FileDriven.Execution |
| **重复组件** | 🔴 10 组 | 见 1.4 表格，每个 Transformer 都有 WinForms 版和通用版两份 |
| **职责重叠** | 🔴 高 | EventTriggerServiceBase vs EventTriggerEngine 做同一件事 |
| **循环依赖** | 🟢 已规避 | 但引入了 Func 委托的分散性 |
| **废弃代码** | 🟡 中等 | 旧版 WinFormsEventBinder、3 个 Obsolete 方法、空方法 |

### 2.2 具体分散点

| # | 分散点 | 涉及文件 | 问题描述 | 影响 |
|---|--------|---------|---------|------|
| 1 | **双重执行机制** | EventBindingTransformers.cs + WinFormsEventBinder.cs | 两个地方都封装了事件触发后的执行逻辑 | 维护需同时修改两处，易不一致 |
| 2 | **Func 委托 vs 接口** | EventTriggerFramework 用 Func，FileDriven 用接口 | 同一功能两种实现方式 | 增加认知负担 |
| 3 | **Transformer 重复** | EventTriggerFramework.EventBinding + BackEndDriven.EventBinding.Transformers | 6 个 Transformer 各有 WinForms 版和通用版两份 | 修改需同步两处 |
| 4 | **EventTriggerServiceBase vs EventTriggerEngine** | EventTriggerFramework + BackEndDriven | 两个抽象基类做同一件事（查找绑定→提取输入→执行函数→写入输出） | 重复维护 |
| 5 | **EventBindingPipeline 重复** | EventTriggerFramework + BackEndDriven | 两个管线工厂，组合方式不同 | 调用方混淆 |
| 6 | **EventBindingPipelineRunner 重复** | EventTriggerFramework + BackEndDriven | 两个管线执行器，一个 WinForms 版一个泛型版 | 调用方混淆 |
| 7 | **EventBindingContext 重复** | EventTriggerFramework + BackEndDriven | 两个上下文类，一个含 WinForms 属性一个不含 | 数据传递不一致 |
| 8 | **旧版 WinFormsEventBinder** | EventTriggerFrameworkInitializer.cs | 旧版仍被创建但未被使用 | 死代码，误导开发者 |
| 9 | **FrameworkInitializer 未注入委托** | FrameworkInitializer.cs | DefaultEventBindingService 创建时未传入 eventExecuteFunc | 路径 A 永远为 null |
| 10 | **3 个 Obsolete 方法** | EventBindingPipeline.cs | 标记了 Obsolete 但未删除 | 调用方仍可使用，无法强制迁移 |

### 2.3 根因分析

```
EventTriggerFramework 最初设计为 WinForms 专用框架
  → 包含大量 WinForms 依赖
    → BackEndDriven 提取通用抽象时，无法复用 EventTriggerFramework
      → 在 BackEndDriven 中重新实现了一套通用版
        → EventTriggerFramework 和 BackEndDriven 各有一套 Transformers
          → FileDriven.Execution 作为桥接层，需要同时引用两者
            → 循环依赖问题迫使 EventTriggerFramework 使用 Func 委托
              → 最终形成 3 个框架、3 条路径、10 组重复的混乱局面
```

---

## 3. 集中式框架设计

### 3.1 设计原则

1. **EventTriggerFramework 作为唯一通用框架**：所有事件加载相关的接口、抽象、管线、Transformers 全部集中在 EventTriggerFramework
2. **消除 WinForms 依赖**：EventTriggerFramework 的核心抽象不依赖 WinForms，WinForms 特定实现作为扩展
3. **适配器桥接**：通过适配器模式桥接到 BackEndDriven 的具体实现
4. **BackEndDriven 精简**：BackEndDriven 只保留具体实现，所有抽象定义迁移到 EventTriggerFramework
5. **FileDriven.Execution 精简**：FileDriven.Execution 只保留桥接逻辑，所有通用逻辑迁移到 EventTriggerFramework

### 3.2 架构对比

#### 当前架构（分散）

```
EventTriggerFramework (WinForms 专用)
  ├── IWinFormsEventBinder  IEventTriggerService
  ├── WinForms 版 Transformers (6 个)
  ├── WinForms 版 EventBindingPipeline
  ├── WinForms 版 EventBindingPipelineRunner
  ├── WinForms 版 EventBindingContext
  ├── EventTriggerServiceBase (抽象基类)
  ├── DefaultEventBindingService (Func 委托)
  ├── WinForms/WinFormsEventBinder (旧版，未用)
  └── EventTriggerFrameworkInitializer

BackEndDriven (通用抽象)
  ├── IEventExecutionService  IEventBindingLookup  IEventBindingInfo
  ├── EventExecutionService (通用实现)
  ├── EventTriggerEngine (抽象基类)
  ├── 通用版 Transformers (6 个)
  ├── 通用版 EventBindingPipelineBase
  ├── 通用版 EventBindingPipelineRunner<TContext>
  └── 通用版 EventBindingContext

FileDriven.Execution (具体实现)
  ├── WinFormsEventBinder (新版，唯一有效)
  ├── EventPipelineExecutor (实现 IEventExecutionService)
  ├── EventTriggerService (继承 EventTriggerEngine)
  └── FrameworkInitializer (注入链路)
```

#### 目标架构（集中）

```
EventTriggerFramework (通用框架，自包含)
  │
  ├── Abstractions/                          ← 所有接口定义集中在此
  │   ├── IEventExecutionService             ← 自有（原在 BackEndDriven）
  │   ├── IEventBindingLookup                ← 迁移自 BackEndDriven
  │   ├── IEventBindingInfo                  ← 迁移自 BackEndDriven
  │   ├── IWinFormsEventBinder               ← 已有
  │   ├── IEventTriggerService               ← 已有
  │   ├── IEventBindingResolver              ← 已有
  │   └── IEventNameResolver                 ← 已有
  │
  ├── Core/                                  ← 核心实现
  │   ├── EventTriggerServiceBase            ← 保留（已有）
  │   ├── EventTriggerEngine                 ← 迁移自 BackEndDriven（替代 EventTriggerServiceBase）
  │   ├── EventTriggerResult                 ← 已有
  │   └── EventBindingExecutor               ← 已有
  │
  ├── EventBinding/                          ← 管线 + Transformers（统一版本）
  │   ├── EventBindingContext                ← 通用版（合并 WinForms 版和通用版）
  │   ├── EventBindingPipeline               ← 通用管线工厂（合并两套）
  │   ├── EventBindingPipelineRunner         ← 通用管线执行器（合并两套）
  │   ├── Transformers/                      ← 通用 Transformers（合并两套）
  │   │   ├── ViewExtractorTransformer
  │   │   ├── ControlFinderTransformer
  │   │   ├── ScriptModeGuardTransformer
  │   │   ├── BindingMetadataTransformer
  │   │   ├── EventBinderTransformer
  │   │   ├── RecursiveTraversalTransformer
  │   │   └── BindingStatisticsTransformer
  │   ├── WinForms/                          ← WinForms 特定扩展
  │   │   ├── WinFormsControlFinderTransformer
  │   │   ├── WinFormsEventBinder            ← 新版（迁移自 FileDriven.Execution）
  │   │   └── ControlTreeWalkerTransformer
  │   └── ControlTreeWalker.cs               ← 迁移自 BackEndDriven
  │
  ├── DefaultEventBindingService             ← 使用自有 IEventExecutionService
  │
  └── Bridge/                                ← 适配器
      └── EventExecutionServiceAdapter       ← 桥接到 BackEndDriven 实现

BackEndDriven (精简，仅保留具体实现)
  ├── Abstractions/                          ← 删除（已迁移到 EventTriggerFramework）
  ├── Execution/
  │   └── EventExecutionService              ← 保留（具体实现）
  └── EventBinding/                          ← 删除（已迁移到 EventTriggerFramework）

FileDriven.Execution (精简，仅保留桥接)
  ├── EventBinding/
  │   └── WinFormsEventBinder                ← 删除（已迁移到 EventTriggerFramework.WinForms）
  ├── EventPipeline/
  │   ├── EventPipelineExecutor              ← 保留（FileDriven 特有管线逻辑）
  │   └── EventTriggerService                ← 保留（FileDriven 特有实现）
  └── Lifecycle/
      └── FrameworkInitializer               ← 更新注入链路
```

### 3.3 核心接口设计

#### 3.3.1 EventTriggerFramework 自有 IEventExecutionService

```csharp
// EventTriggerFramework/Abstractions/IEventExecutionService.cs
namespace EventTriggerFramework.Abstractions
{
    /// <summary>
    /// 事件执行服务接口 — EventTriggerFramework 自有定义
    /// <para>与 BackEndDriven.Abstractions.IEventExecutionService 签名一致，</para>
    /// <para>通过适配器模式桥接。</para>
    /// <para>设计目的：消除 EventTriggerFramework 对 BackEndDriven 的依赖，</para>
    /// <para>使 EventTriggerFramework 成为自包含的通用事件框架。</para>
    /// </summary>
    public interface IEventExecutionService
    {
        bool Execute(string controlName, string eventType, string value);
        string BuildEventCommand(string controlName, string eventType);
    }
}
```

#### 3.3.2 迁移 IEventBindingLookup 和 IEventBindingInfo

```csharp
// EventTriggerFramework/Abstractions/IEventBindingLookup.cs
// ★ 迁移自 BackEndDriven.Abstractions
namespace EventTriggerFramework.Abstractions
{
    public interface IEventBindingLookup
    {
        IEventBindingInfo? FindEventBinding(string controlName, string eventType);
    }
}

// EventTriggerFramework/Abstractions/IEventBindingInfo.cs
// ★ 迁移自 BackEndDriven.Abstractions
namespace EventTriggerFramework.Abstractions
{
    public interface IEventBindingInfo
    {
        string FunctionName { get; }
        string EventType { get; }
        IReadOnlyList<string> Inputs { get; }
        string? Output { get; }
        IReadOnlyList<string> FunctionNames { get; }
    }
}
```

#### 3.3.3 适配器

```csharp
// EventTriggerFramework/Bridge/EventExecutionServiceAdapter.cs
namespace EventTriggerFramework.Bridge
{
    /// <summary>
    /// 适配器：将 BackEndDriven 的 IEventExecutionService 桥接到 EventTriggerFramework
    /// <para>FileDriven.Execution 层通过此适配器将具体实现注入到 EventTriggerFramework。</para>
    /// </summary>
    public class EventExecutionServiceAdapter : Abstractions.IEventExecutionService
    {
        private readonly BackEndDriven.Abstractions.IEventExecutionService _inner;

        public EventExecutionServiceAdapter(
            BackEndDriven.Abstractions.IEventExecutionService inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public bool Execute(string controlName, string eventType, string value)
            => _inner.Execute(controlName, eventType, value);

        public string BuildEventCommand(string controlName, string eventType)
            => _inner.BuildEventCommand(controlName, eventType);
    }
}
```

### 3.4 统一 EventBindingContext

```csharp
// EventTriggerFramework/EventBinding/EventBindingContext.cs
// ★ 合并：将 BackEndDriven 的通用版 + EventTriggerFramework 的 WinForms 版合并
// ★ 设计：核心属性不依赖 WinForms，WinForms 特定属性通过子类或 FoundControl 扩展
namespace EventTriggerFramework.EventBinding
{
    public class EventBindingContext
    {
        // === 通用属性（来自 BackEndDriven 版） ===
        public UIConfiguration Config { get; set; } = null!;
        public ViewConfig? CurrentView { get; set; }
        public List<ControlConfig>? CurrentControls { get; set; }
        public ControlConfig? CurrentControlConfig { get; set; }
        public List<ControlBindingInfo> BoundControls { get; } = new();
        public List<ControlBindingInfo> SkippedControls { get; } = new();
        public bool IsScriptExecutionMode { get; set; }
        public bool SkipRemainingTransformers { get; set; }
        public object? FoundControl { get; set; }

        // === 来自 EventTriggerFramework WinForms 版的属性 ===
        // ★ 改造：WinFormsEventBinder 改为 IWinFormsEventBinder 接口，不依赖具体实现
        public IWinFormsEventBinder? WinFormsEventBinder { get; set; }
        public IEventExecutionService? EventExecutionService { get; set; }
        public TransformContext? TransformContext { get; set; }

        public void ResetCurrent()
        {
            CurrentControlConfig = null;
            SkipRemainingTransformers = false;
            FoundControl = null;
        }
    }
}
```

### 3.5 统一 EventBindingPipeline

```csharp
// EventTriggerFramework/EventBinding/EventBindingPipeline.cs
// ★ 合并：将 BackEndDriven.EventBindingPipelineBase + EventTriggerFramework.EventBindingPipeline 合并
// ★ 设计：核心管线不依赖 WinForms，WinForms 扩展通过 AddWinFormsExtensions 方法添加
namespace EventTriggerFramework.EventBinding
{
    public static class EventBindingPipeline
    {
        /// <summary>
        /// 创建核心绑定管线（不依赖 WinForms）
        /// <para>脚本模式守卫 → 绑定元数据提取 → 事件绑定执行</para>
        /// </summary>
        public static List<ITransformer<EventBindingContext, EventBindingContext>> CreateCorePipeline(
            IEventExecutionService? eventExecutionService = null)
        {
            return new List<ITransformer<EventBindingContext, EventBindingContext>>
            {
                new ScriptModeGuardTransformer(),
                new BindingMetadataTransformer(),
                new EventBinderTransformer(eventExecutionService),
            };
        }

        /// <summary>
        /// 创建完整绑定管线（不含 WinForms 特定 Transformer）
        /// <para>视图提取 → 控件查找 → 核心管线 → 递归遍历</para>
        /// </summary>
        public static List<ITransformer<EventBindingContext, EventBindingContext>> CreateFullPipeline(
            IEventExecutionService? eventExecutionService = null)
        {
            var pipeline = new List<ITransformer<EventBindingContext, EventBindingContext>>
            {
                new ViewExtractorTransformer(),
                new ControlFinderTransformer(),
            };
            pipeline.AddRange(CreateCorePipeline(eventExecutionService));
            pipeline.Add(new RecursiveTraversalTransformer());
            return pipeline;
        }

        /// <summary>
        /// 添加 WinForms 特定 Transformer
        /// <para>在 CreateFullPipeline 基础上，添加 WinForms 控件查找和树遍历</para>
        /// </summary>
        public static List<ITransformer<EventBindingContext, EventBindingContext>> AddWinFormsExtensions(
            this List<ITransformer<EventBindingContext, EventBindingContext>> pipeline)
        {
            // 替换 ControlFinderTransformer 为 WinFormsControlFinderTransformer
            var index = pipeline.FindIndex(t => t is ControlFinderTransformer);
            if (index >= 0)
            {
                pipeline[index] = new WinFormsControlFinderTransformer();
            }
            pipeline.Add(new ControlTreeWalkerTransformer());
            return pipeline;
        }

        // ★ 删除 3 个 Obsolete 方法
    }
}
```

### 3.6 统一 EventBindingPipelineRunner

```csharp
// EventTriggerFramework/EventBinding/EventBindingPipelineRunner.cs
// ★ 合并：将 BackEndDriven.EventBindingPipelineRunner<TContext> + EventTriggerFramework 版合并
// ★ 设计：泛型设计，TContext 继承 EventBindingContext，支持 WinForms 扩展
namespace EventTriggerFramework.EventBinding
{
    public class EventBindingPipelineRunner<TContext>
        where TContext : EventBindingContext
    {
        public TContext Execute(TContext context,
            List<ITransformer<EventBindingContext, EventBindingContext>> pipeline)
        {
            // ... 同 BackEndDriven 版的 Template Method 逻辑 ...
        }

        protected virtual void ProcessView(TContext context,
            List<ITransformer<EventBindingContext, EventBindingContext>> pipeline) { }

        protected virtual void ProcessControls(TContext context,
            List<ITransformer<EventBindingContext, EventBindingContext>> pipeline) { }

        protected virtual void ProcessControl(TContext context,
            List<ITransformer<EventBindingContext, EventBindingContext>> pipeline) { }
    }
}
```

### 3.7 统一 EventTriggerEngine

```csharp
// EventTriggerFramework/Core/EventTriggerEngine.cs
// ★ 合并：将 BackEndDriven.EventTriggerEngine + EventTriggerFramework.EventTriggerServiceBase 合并
// ★ 设计：保留 Template Method 模式，统一接口为 IEventBindingLookup（迁移自 BackEndDriven）
namespace EventTriggerFramework.Core
{
    public abstract class EventTriggerEngine
    {
        private readonly Abstractions.IEventBindingLookup _bindingLookup;

        protected EventTriggerEngine(Abstractions.IEventBindingLookup bindingLookup)
        {
            _bindingLookup = bindingLookup;
        }

        public async Task<IEventTriggerResult> TriggerEventAsync(
            string controlName, string eventType,
            CancellationToken cancellationToken = default)
        {
            // Template Method: 查找绑定 → 提取输入值 → 执行函数 → 写入输出
            // 合并 EventTriggerServiceBase 和 EventTriggerEngine 的逻辑
        }

        protected abstract Task<string?> GetControlValueAsync(string controlName);
        protected abstract Task<(bool IsSuccess, object? Value, string? ErrorMessage)> ExecuteFunctionAsync(
            string functionName, Dictionary<string, string?> inputValues,
            CancellationToken cancellationToken);
        protected abstract Task WriteOutputAsync(string outputTarget, object? value,
            CancellationToken cancellationToken);
    }
}
```

### 3.8 统一 Transformers

所有 Transformer 统一使用 `EventTriggerFramework.EventBinding.EventBindingContext`，不再区分 WinForms 版和通用版。

| Transformer | 来源 | 改造方式 |
|------------|------|---------|
| `ViewExtractorTransformer` | 合并两版 | 使用 EventBindingContext，不依赖 WinForms |
| `ControlFinderTransformer` | 合并两版 | 使用 EventBindingContext.FoundControl，不依赖 WinForms |
| `ScriptModeGuardTransformer` | 合并两版 | 使用 EventBindingContext.IsScriptExecutionMode |
| `BindingMetadataTransformer` | 合并两版 | 使用 EventBindingContext，不依赖 WinForms |
| `EventBinderTransformer` | 改造 | Func 委托 → IEventExecutionService 接口 |
| `RecursiveTraversalTransformer` | 合并两版 | 使用 EventBindingContext，不依赖 WinForms |
| `BindingStatisticsTransformer` | 合并两版 | 使用 EventBindingContext.BoundControls/SkippedControls |
| `WinFormsControlFinderTransformer` | 保留 WinForms 版 | 作为 WinForms 扩展，替换 ControlFinderTransformer |
| `ControlTreeWalkerTransformer` | 保留 WinForms 版 | 作为 WinForms 扩展 |

### 3.9 改造后的 DefaultEventBindingService

```csharp
// DefaultEventBindingService.cs — 改造后
public class DefaultEventBindingService : IEventBindingService
{
    private EventBindingPipelineRunner<EventBindingContext> _pipelineRunner;
    private IEventExecutionService? _eventExecutionService;

    public DefaultEventBindingService(
        IEventTriggerService? eventTriggerService = null,
        WinForms.WinFormsEventBus? fallbackEventBus = null,
        IWinFormsEventBinder? winFormsEventBinder = null,
        IEventExecutionService? eventExecutionService = null)
    {
        _eventExecutionService = eventExecutionService;
        _pipelineRunner = new EventBindingPipelineRunner<EventBindingContext>();
    }

    // ★ 改造：SetEventExecuteFunc → SetEventExecutionService
    public void SetEventExecutionService(IEventExecutionService eventExecutionService)
    {
        _eventExecutionService = eventExecutionService;
    }

    public void BindEvents(UIConfiguration config, Control container)
    {
        var context = new EventBindingContext
        {
            Config = config,
            WinFormsEventBinder = _winFormsEventBinder,
            EventExecutionService = _eventExecutionService,
            IsScriptExecutionMode = _winFormsEventBinder?.IsScriptExecutionMode ?? false,
        };

        // 创建 WinForms 完整管线
        var pipeline = EventBindingPipeline.CreateFullPipeline(_eventExecutionService);
        pipeline.AddWinFormsExtensions();

        _pipelineRunner.Execute(context, pipeline);
    }
}
```

### 3.10 改造后的 WinFormsEventBinder（迁移到 EventTriggerFramework）

```csharp
// EventTriggerFramework/EventBinding/WinForms/WinFormsEventBinder.cs
// ★ 迁移：从 FileDriven.Execution.EventBinding 迁移到 EventTriggerFramework
// ★ 改造：移除 wrappedHandler 中的 _eventExecutionService 调用
//   因为 EventBinderTransformer 已通过自有接口处理执行逻辑
public class WinFormsEventBinder : IWinFormsEventBinder
{
    public bool IsScriptExecutionMode { get; set; }

    public void BindControlEvent(Control control, string eventName, Delegate handler,
        string? output = null, string? inputs = null)
    {
        // ... 参数检查 ...
        SaveControlParameters(control, output, inputs);
        var eventInfo = GetOrAddEventInfo(control.GetType(), eventName);
        if (eventInfo == null) return;

        // ★ 改造：直接绑定原始 handler，不再包装
        //   EventBinderTransformer 已通过 IEventExecutionService 处理执行逻辑
        eventInfo.AddEventHandler(control, handler);
    }
}
```

### 3.11 改造后的 FrameworkInitializer

```csharp
// FrameworkInitializer.cs — 改造后
// ★ 改造：注入 IEventExecutionService 到 DefaultEventBindingService
var backEndService = new EventPipelineExecutor(/* ... */); // 实现 IEventExecutionService
var adapter = new EventExecutionServiceAdapter(backEndService);
var eventBindingService = new DefaultEventBindingService(eventExecutionService: adapter);
FileDrivenWdUIBridge.InjectEventBinder(eventBindingService);
```

---

## 4. 实施计划

### 4.1 阶段一：接口迁移（低风险）

| 步骤 | 内容 | 涉及文件 |
|------|------|---------|
| 1.1 | 在 EventTriggerFramework 中定义自有 `IEventExecutionService` | **新建** `EventTriggerFramework/Abstractions/IEventExecutionService.cs` |
| 1.2 | 将 `IEventBindingLookup` 迁移到 EventTriggerFramework | **新建** `EventTriggerFramework/Abstractions/IEventBindingLookup.cs` |
| 1.3 | 将 `IEventBindingInfo` 迁移到 EventTriggerFramework | **新建** `EventTriggerFramework/Abstractions/IEventBindingInfo.cs` |
| 1.4 | 创建适配器 | **新建** `EventTriggerFramework/Bridge/EventExecutionServiceAdapter.cs` |
| 1.5 | 更新 BackEndDriven 的接口引用为 EventTriggerFramework | 修改 BackEndDriven 的 using 语句 |

### 4.2 阶段二：Transformers 合并（中风险）

| 步骤 | 内容 | 涉及文件 |
|------|------|---------|
| 2.1 | 统一 EventBindingContext（合并两版） | `EventBinding/EventBindingContext.cs` |
| 2.2 | 统一 ViewExtractorTransformer | `Transformers/ViewExtractorTransformer.cs` |
| 2.3 | 统一 ControlFinderTransformer | `Transformers/ControlFinderTransformer.cs` |
| 2.4 | 统一 ScriptModeGuardTransformer | `Transformers/ScriptModeGuardTransformer.cs` |
| 2.5 | 统一 BindingMetadataTransformer | `Transformers/BindingMetadataTransformer.cs` |
| 2.6 | 统一 RecursiveTraversalTransformer | `Transformers/RecursiveTraversalTransformer.cs` |
| 2.7 | 统一 BindingStatisticsTransformer | `Transformers/BindingStatisticsTransformer.cs` |
| 2.8 | EventBinderTransformer：Func → IEventExecutionService | `EventBindingTransformers.cs` |

### 4.3 阶段三：管线 + 执行器合并（中风险）

| 步骤 | 内容 | 涉及文件 |
|------|------|---------|
| 3.1 | 统一 EventBindingPipeline（合并两版） | `EventBinding/EventBindingPipeline.cs` |
| 3.2 | 统一 EventBindingPipelineRunner（合并两版） | `EventBinding/EventBindingPipelineRunner.cs` |
| 3.3 | 统一 EventTriggerEngine（合并 EventTriggerServiceBase + EventTriggerEngine） | `Core/EventTriggerEngine.cs` |
| 3.4 | 更新 DefaultEventBindingService | `DefaultEventBindingService.cs` |

### 4.4 阶段四：WinFormsEventBinder 迁移（中风险）

| 步骤 | 内容 | 涉及文件 |
|------|------|---------|
| 4.1 | 将 WinFormsEventBinder 从 FileDriven.Execution 迁移到 EventTriggerFramework | **迁移** `EventBinding/WinForms/WinFormsEventBinder.cs` |
| 4.2 | 移除 wrappedHandler 中的双重执行逻辑 | 同上 |
| 4.3 | 迁移 ControlTreeWalker 到 EventTriggerFramework | **迁移** `EventBinding/ControlTreeWalker.cs` |
| 4.4 | 迁移 ControlConfigTreeNode 到 EventTriggerFramework | **迁移** `EventBinding/ControlConfigTreeNode.cs` |
| 4.5 | 迁移 IControlFinderStrategy 到 EventTriggerFramework | **迁移** `EventBinding/IControlFinderStrategy.cs` |

### 4.5 阶段五：清理废弃代码（低风险）

| 步骤 | 内容 | 涉及文件 |
|------|------|---------|
| 5.1 | 删除 BackEndDriven.EventBinding 目录（已迁移） | 整个目录 |
| 5.2 | 删除 BackEndDriven.Abstractions 中的 IEventBindingLookup/IEventBindingInfo | 2 个文件 |
| 5.3 | 删除 EventTriggerFramework 中的 3 个 Obsolete 方法 | `EventBindingPipeline.cs` |
| 5.4 | 标记旧版 WinFormsEventBinder 为 Obsolete | `EventTriggerFrameworkInitializer.cs` |
| 5.5 | 删除 FileDriven.Execution 中的 WinFormsEventBinder（已迁移） | 1 个文件 |

### 4.6 阶段六：注入链路更新（低风险）

| 步骤 | 内容 | 涉及文件 |
|------|------|---------|
| 6.1 | 更新 FrameworkInitializer 注入链路 | `FrameworkInitializer.cs` |
| 6.2 | 更新 EventTriggerFrameworkInitializer | `EventTriggerFrameworkInitializer.cs` |

### 4.7 依赖关系图（改造后）

```
FrameworkInitializer
  │
  ├─ 创建 EventPipelineExecutor (FileDriven.Execution)
  │   └─ 实现 IEventExecutionService (BackEndDriven.Abstractions)
  │
  ├─ 创建 EventExecutionServiceAdapter (EventTriggerFramework.Bridge)
  │   └─ 桥接 BackEndDriven 的 IEventExecutionService → EventTriggerFramework 的 IEventExecutionService
  │
  ├─ 创建 DefaultEventBindingService(eventExecutionService: adapter)
  │   └─ 内部创建 EventBindingPipelineRunner<EventBindingContext>
  │       └─ 管线包含 EventBinderTransformer(eventExecutionService)
  │
  └─ 创建 WinFormsEventBinder (EventTriggerFramework.EventBinding.WinForms)
      └─ 不再持有 IEventExecutionService，只负责底层反射绑定

[绑定阶段]
  DefaultEventBindingService.BindEvents()
    └─ EventBindingPipelineRunner.Execute()
        └─ EventBinderTransformer 创建 EventHandler
            └─ handler 内部调用 eventExecutionService.Execute()
                ↓
          EventExecutionServiceAdapter.Execute()
                ↓
          EventPipelineExecutor.Execute() (BackEndDriven.IEventExecutionService)
                ↓
          EventExecutionService.Execute()
                ↓
          ExecutePipelineInternal 委托
                ↓
          单命令管线 → 执行函数

[改造后执行路径]
  用户点击按钮 → wrappedHandler 触发
    → EventBinderTransformer 创建的 handler
      → eventExecutionService.Execute()  ← 唯一路径
        → EventExecutionServiceAdapter
          → EventPipelineExecutor
            → 单命令管线 → 执行函数
```

---

## 5. 附录：关键文件索引

### 5.1 新建文件

| 文件路径 | 职责 |
|---------|------|
| `CsharpPrj/Operation/EventTriggerFramework/Abstractions/IEventExecutionService.cs` | EventTriggerFramework 自有事件执行服务接口 |
| `CsharpPrj/Operation/EventTriggerFramework/Abstractions/IEventBindingLookup.cs` | 事件绑定查找接口（迁移自 BackEndDriven） |
| `CsharpPrj/Operation/EventTriggerFramework/Abstractions/IEventBindingInfo.cs` | 事件绑定信息接口（迁移自 BackEndDriven） |
| `CsharpPrj/Operation/EventTriggerFramework/Bridge/EventExecutionServiceAdapter.cs` | 适配器：桥接到 BackEndDriven 的具体实现 |

### 5.2 迁移文件（从 BackEndDriven → EventTriggerFramework）

| 文件路径（原） | 文件路径（新） |
|--------------|--------------|
| `BackEndDriven/EventBinding/EventBindingContext.cs` | `EventTriggerFramework/EventBinding/EventBindingContext.cs` |
| `BackEndDriven/EventBinding/EventBindingPipelineBase.cs` | `EventTriggerFramework/EventBinding/EventBindingPipeline.cs` |
| `BackEndDriven/EventBinding/EventBindingPipelineRunner.cs` | `EventTriggerFramework/EventBinding/EventBindingPipelineRunner.cs` |
| `BackEndDriven/EventBinding/ControlTreeWalker.cs` | `EventTriggerFramework/EventBinding/ControlTreeWalker.cs` |
| `BackEndDriven/EventBinding/ControlConfigTreeNode.cs` | `EventTriggerFramework/EventBinding/ControlConfigTreeNode.cs` |
| `BackEndDriven/EventBinding/IControlFinderStrategy.cs` | `EventTriggerFramework/EventBinding/IControlFinderStrategy.cs` |
| `BackEndDriven/EventBinding/Transformers/*.cs` | `EventTriggerFramework/EventBinding/Transformers/*.cs` |
| `BackEndDriven/Execution/EventTriggerEngine.cs` | `EventTriggerFramework/Core/EventTriggerEngine.cs` |

### 5.3 迁移文件（从 FileDriven.Execution → EventTriggerFramework）

| 文件路径（原） | 文件路径（新） |
|--------------|--------------|
| `FileDriven.Execution/EventBinding/WinFormsEventBinder.cs` | `EventTriggerFramework/EventBinding/WinForms/WinFormsEventBinder.cs` |

### 5.4 修改文件

| 文件路径 | 修改内容 | 风险 |
|---------|---------|------|
| `EventTriggerFramework/EventBinding/EventBindingTransformers.cs` | EventBinderTransformer：Func 委托 → IEventExecutionService 接口 | 低 |
| `EventTriggerFramework/EventBinding/EventBindingPipeline.cs` | 合并 BackEndDriven 版，删除 3 个 Obsolete 方法 | 中 |
| `EventTriggerFramework/DefaultEventBindingService.cs` | Func → 接口；SetEventExecuteFunc → SetEventExecutionService | 低 |
| `EventTriggerFramework/EventTriggerFrameworkInitializer.cs` | 标记旧版 WinFormsEventBinder 为 Obsolete | 低 |
| `FileDriven.Execution/Lifecycle/FrameworkInitializer.cs` | 注入适配器到 DefaultEventBindingService | 中 |
| `BackEndDriven/Abstractions/IEventBindingLookup.cs` | 标记为 Obsolete，指向 EventTriggerFramework 版 | 低 |
| `BackEndDriven/Abstractions/IEventBindingInfo.cs` | 标记为 Obsolete，指向 EventTriggerFramework 版 | 低 |

### 5.5 删除文件

| 文件路径 | 原因 |
|---------|------|
| `BackEndDriven/EventBinding/` 整个目录 | 已迁移到 EventTriggerFramework |
| `FileDriven.Execution/EventBinding/WinFormsEventBinder.cs` | 已迁移到 EventTriggerFramework |

### 5.6 不修改文件

| 文件路径 | 原因 |
|---------|------|
| `BackEndDriven/Execution/EventExecutionService.cs` | 具体实现不变 |
| `BackEndDriven/Execution/EventTriggerEngine.cs` | 迁移到 EventTriggerFramework 后删除原文件 |
| `FileDriven.Execution/EventPipeline/EventPipelineExecutor.cs` | FileDriven 特有管线逻辑，保留 |
| `FileDriven.Execution/EventPipeline/EventTriggerService.cs` | FileDriven 特有实现，保留 |
| `EventTriggerFramework/EventTriggerFramework.csproj` | 不新增对 BackEndDriven 的引用 |

### 5.7 改造前后对比

| 维度 | 改造前 | 改造后 |
|------|--------|--------|
| **框架分布** | 3 个框架（EventTriggerFramework + BackEndDriven + FileDriven.Execution） | 1 个通用框架（EventTriggerFramework）+ 2 个精简层 |
| **执行路径数** | 3 条（A/B/C） | 1 条（统一路径） |
| **重复组件** | 10 组（每个 Transformer 两份） | 0 组（统一版本） |
| **事件执行抽象** | Func 委托 + IEventExecutionService 接口 | 统一使用 IEventExecutionService 接口 |
| **循环依赖** | 通过 Func 委托规避 | 通过自有接口 + 适配器消除 |
| **废弃代码** | 旧版 Binder + 3 个 Obsolete 方法 + 空方法 | 已清理 |
| **维护性** | 修改需同步 3 个框架 | 只需修改 EventTriggerFramework |


