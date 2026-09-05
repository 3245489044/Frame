# ContextFrame — 通用上下文框架

## 概述

ContextFrame 是 WorkFlowPrj 的通用上下文框架，位于 `BasicMethod/ContextFrame/` 目录下。它从 WorkFlowMethod 上下文体系中提炼核心抽象，提供一套独立于具体业务、可复用的上下文基础设施。

## 设计哲学

### 分层抽象

```
IContext (基础上下文)
  └── IPipelineContext (管线上下文)
        └── IStageContext (编排上下文 — 在 Share 层)
        └── IWorkflowContext (工作流上下文 — 在 Share 层)
```

### 核心原则

1. **接口分离**：每个接口只负责一个维度的能力
2. **渐进增强**：从基础到专用，逐层扩展
3. **零依赖**：接口层不依赖任何外部库
4. **线程安全**：所有实现都考虑多线程场景

## 模块结构

```
BasicMethod/ContextFrame/
├── Abstractions/          ← 纯接口定义（零依赖）
│   ├── IContext.cs        ← 基础上下文接口
│   ├── IPipelineContext.cs← 管线上下文接口
│   ├── IStateStore.cs     ← 状态存储接口
│   ├── IScopedContext.cs  ← 作用域上下文接口
│   └── IContextSnapshot.cs← 快照接口
├── Core/                  ← 基础实现
│   ├── ContextBase.cs     ← IContext 实现
│   ├── PipelineContextBase.cs ← IPipelineContext 实现
│   └── StateStoreBase.cs  ← IStateStore 实现
├── Scoping/               ← 作用域管理
│   ├── AsyncContext.cs    ← AsyncLocal 作用域
│   └── ContextScope.cs    ← 轻量级作用域
├── Builders/              ← 构建器
│   └── ContextBuilder.cs  ← Fluent Builder
├── Adapters/              ← 适配器
│   └── ContextAdapter.cs  ← 通用适配器
└── Utilities/             ← 工具
    ├── ContextExtensions.cs ← 扩展方法
    └── ContextSnapshot.cs ← 快照实现
```

## 接口说明

### IContext — 基础上下文

```csharp
public interface IContext : IDisposable, IAsyncDisposable
{
    // 标识与生命周期
    string Id { get; }
    IContext? Parent { get; }
    bool IsDisposed { get; }
    DateTime CreatedAt { get; }
    CancellationToken CancellationToken { get; }

    // 数据存储（支持值类型和引用类型）
    void SetData<T>(T value, string? key = null);
    T? GetData<T>(string? key = null);       // 返回 T?，不存在时返回 null
    bool TryGetData<T>(out T? value, string? key = null);
    bool RemoveData<T>(string? key = null);
    bool ContainsData<T>(string? key = null);

    // 服务定位
    T GetOrAddService<T>(Func<T> factory) where T : class;
    bool TryGetService<T>(out T? service) where T : class;

    // 数据变更通知
    event EventHandler<ContextDataChangedEventArgs>? DataChanged;
}
```

### IPipelineContext — 管线上下文

```csharp
public interface IPipelineContext : IContext
{
    string Mode { get; set; }
    IReadOnlyDictionary<string, bool> StageSuccess { get; }
    IReadOnlyDictionary<string, string?> StageErrors { get; }
    IDictionary<string, object?> SharedData { get; }
    bool HasError { get; }
    string? FirstError { get; }

    void SetStageResult(string stageName, bool success, string? error = null);
    bool GetStageSuccess(string stageName);
    string? GetStageError(string stageName);
    void Reset();
}
```

### IStateStore — 状态存储

```csharp
public interface IStateStore
{
    T? Get<T>(string key);
    void Set<T>(string key, T value);
    bool Remove<T>(string key);
    bool Contains<T>(string key);
    T? GetOrCreate<T>(string key, Func<T> factory);
    void Clear();
}
```

## 使用示例

### 基础上下文

```csharp
using BasicMethod.ContextFrame.Core;
using BasicMethod.ContextFrame.Builders;

// 使用构建器创建
var context = ContextBuilder.CreateBase()
    .WithId("my-context")
    .WithData("hello", "greeting")
    .Build();

// 读写数据
context.SetData(42, "answer");
var answer = context.GetData<int>("answer"); // 42

// 父子级联
var child = ContextBuilder.CreateBase()
    .WithParent(context)
    .WithData("world", "greeting")
    .Build();

var greeting = child.GetData<string>("greeting"); // "world"（子上下文覆盖）
var answer2 = child.GetData<int>("answer"); // 42（从父上下文级联）
```

### 管线上下文

```csharp
using BasicMethod.ContextFrame.Core;

var pipeline = new PipelineContextBase("my-pipeline");

// 记录阶段结果
pipeline.SetStageResult("validate", true);
pipeline.SetStageResult("process", false, "处理失败");
pipeline.SetStageResult("save", true);

// 检查状态
pipeline.HasError; // true
pipeline.FirstError; // "处理失败"
pipeline.GetStageSuccess("process"); // false
```

### 状态存储

```csharp
using BasicMethod.ContextFrame.Core;

var store = new StateStoreBase();
store.Set("user", new User { Name = "Alice" });
var user = store.Get<User>("user");
```

### 作用域管理

```csharp
using BasicMethod.ContextFrame.Scoping;

// 创建作用域
using (AsyncContext<MyContext>.CreateScope(myContext))
{
    var current = AsyncContext<MyContext>.Current; // myContext
    // 异步调用链中自动传播
    await SomeAsyncMethod();
}
```

### 快照

```csharp
using BasicMethod.ContextFrame.Utilities;

// 创建快照
var snapshot = context.CreateDataSnapshot();

// 修改数据后恢复
context.SetData("key", "new value");
context.RestoreDataFromSnapshot(snapshot);
```

## 与现有代码的关系

| 方面 | 现有代码 (BasicMethod/ContextFrame.cs) | 新框架 (BasicMethod/ContextFrame/) |
|------|--------------------------------------|-----------------------------------|
| 命名空间 | `ContextManagement`, `AsyncContextFramework` | `BasicMethod.ContextFrame.*` |
| 定位 | 项目早期的基础上下文实现 | 从 WorkFlowMethod 提炼的通用框架 |
| 修改 | **不动** | **全新构建** |
| 关系 | 独立共存 | 可逐步迁移 |

## 设计来源

本框架的接口设计参考了 WorkFlowMethod 项目中的以下实现：

| 接口 | 设计来源 |
|------|---------|
| `IContext` | `BasicMethod/ContextFrame.cs` — IContext |
| `IPipelineContext` | `Modules/Pipeline/IPipelineContext` |
| `IStateStore` | `Share/StateManagement/IStateStore` |
| `IScopedContext` | `BasicMethod/ContextFrame.cs` — AsyncContext |
| `IContextSnapshot` | `Operation/WorkFlowMethod` — PipelineContextSnapshot |
