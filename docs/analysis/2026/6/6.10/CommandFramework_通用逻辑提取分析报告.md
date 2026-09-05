# CommandFramework 通用逻辑提取分析报告

## 概述

对 `FileDriven.Pipeline/CommandFramework/` 目录中的文件进行综合分析，识别应提取分离到项目其他位置的通用逻辑。

## 目录结构现状

```
FileDriven.Pipeline/
├── CommandFramework/          ← 旧版（遗留代码，完全孤立）
│   ├── FileDrivenPipelineContext.cs
│   ├── PipelineEntry.cs
│   ├── PipelineFactory.cs
│   ├── Models/LoadResult.cs
│   └── Skeletons/
│       ├── NonEventCommandHandler.cs
│       └── ScriptLoadSkeleton.cs
├── Core/                      ← 核心层
│   ├── ControlHelper.cs
│   ├── EventCore.cs
│   ├── NonEventCommands.cs
│   └── Skeletons/
│       └── ControlTreeWalker.cs
├── Skeletons/                 ← 新版骨架（根目录）
│   ├── ControlTreeWalker.cs
│   ├── NonEventCommandHandler.cs
│   └── ScriptLoadSkeleton.cs
├── PipelineFactory.cs         ← 新版工厂（外观层）
├── PipelineEntry.cs           ← 新版入口（实例类）
└── FileDrivenPipelineContext.cs  ← 新版上下文
```

## 关键发现

### 1. `CommandFramework/` 子目录完全孤立

- 命名空间 `FileDriven.Pipeline.CommandFramework` **没有任何外部引用**
- 所有引用都来自该目录内部的文件自身
- 该目录是**完全安全的遗留代码**，可整体删除

### 2. 新版（根目录）已完成的迁移

| 旧版位置 | 新版位置 | 状态 |
|---------|---------|------|
| `CommandFramework/FileDrivenPipelineContext.cs` | `FileDrivenPipelineContext.cs` | ✅ 已迁移 |
| `CommandFramework/PipelineEntry.cs` | `PipelineEntry.cs` | ✅ 已迁移 |
| `CommandFramework/PipelineFactory.cs` | `PipelineFactory.cs` | ✅ 已迁移 |
| `CommandFramework/Skeletons/ScriptLoadSkeleton.cs` | `Skeletons/ScriptLoadSkeleton.cs` | ✅ 已迁移 |
| `CommandFramework/Skeletons/NonEventCommandHandler.cs` | `Skeletons/NonEventCommandHandler.cs` | ✅ 已迁移 |

### 3. 本次提取的通用逻辑

#### 3.1 `PipelineFactory.CreateFileDrivenPipeline()` → `Core/PipelineFactory.cs`

**提取内容：** 管线编排逻辑（`PipelineBuilder` 的构建流程）

**提取理由：**
- 纯管线编排逻辑，不依赖实例状态
- 通过泛型参数 `TContext` 和委托参数解耦，避免循环引用
- 可被 CLI 和 GUI 两种模式复用

**依赖关系：**
- `SkeletonFlow.ISkeleton<TContext, TContext>`（来自 `BasicMethod`）
- `BasicMethod.Pipeline.IPipeline<TContext, TContext>`（来自 `BasicMethod`）
- `Share.CentralPipeline.PipelineBuilder<TContext>`（来自 `Share/Pipeline`）
- `CommandContext`（来自 `Share/CommandFramework`）
- `FileDrivenPipelineContext`（仅 `itemsSelector` 和 `onItemAfter` 中通过 `is` 模式匹配使用）

**设计决策：**
- `BuildCommandPipeline()` **不迁移到 Core**，因为它依赖 `Share/CommandFramework.Pipeline.PipelineEntry`，而 Core 项目不引用 `Share/CommandFramework`
- 根目录 `PipelineFactory` 作为**外观层**，负责构建 `commandPipeline` 并委托给 Core

#### 3.2 `PipelineEntry.BuildResults()` → 保留在 `PipelineEntry` 中

**分析结论：** 不提取。

**理由：**
- 依赖 `FileDrivenPipelineContext` 和 `CommandFileLineResult`
- 是 `PipelineEntry` 实例类的静态便捷方法
- 提取到 Core 需要额外引入 `FileDrivenPipelineContext` 的引用

#### 3.3 `NonEventCommandHandler` 中的 echo/sleep/wait 逻辑

**分析结论：** 已在 `Share/CommandFramework/Execution/NonEventCommandHandler.cs` 中有对应实现。

**现状：**
- `FileDriven.Pipeline/Skeletons/NonEventCommandHandler.cs`（新版）已委托给 `Share` 版本
- `FileDriven.Pipeline/CommandFramework/Skeletons/NonEventCommandHandler.cs`（旧版）是遗留副本

#### 3.4 `FileDrivenPipelineContext` 中的便捷属性

**分析结论：** 不提取。

**理由：**
- 便捷属性（`ParsedVerb`、`ParsedAction` 等）是 `FileDrivenPipelineContext` 的特有职责
- 它们委托给 `ParsedCommand`，属于上下文包装器模式
- 提取到基类会破坏单一职责原则

## 已执行的操作

### ✅ 创建 `Core/PipelineFactory.cs`

将 `CreateFileDrivenPipeline()` 的管线编排逻辑提取到 Core 层：

```csharp
// Core/PipelineFactory.cs — 管线编排逻辑
public static CentralPipeline<TContext> CreateFileDrivenPipeline<TContext>(
    CliOptions options,
    ISkeleton<TContext, TContext> scriptLoadSkeleton,
    BP.IPipeline<TContext, TContext> commandPipeline,
    ...)
    where TContext : CommandContext
```

### ✅ 重构 `PipelineFactory.cs`（根目录）

作为外观层，委托给 Core：

```csharp
// PipelineFactory.cs — 外观层
public static CentralPipeline<FileDrivenPipelineContext> CreateFileDrivenPipeline(...)
{
    var commandPipeline = BuildCommandPipeline(bindingResolver, functionExecutor);
    return Core.PipelineFactory.CreateFileDrivenPipeline(
        options, new ScriptLoadSkeleton(), commandPipeline, ...);
}
```

## 建议的后续操作

1. **删除 `CommandFramework/` 子目录**（确认无外部引用后）
2. **将 `PipelineEntry.BuildResults()` 标记为 `[Obsolete]`**，引导使用者直接访问 `context.CommandResults`
3. **考虑将 `BuildCommandPipeline()` 也泛型化**，使其可被其他上下文类型复用
4. **统一 `NonEventCommandHandler` 的委托路径**，确保所有调用都经过 `Share` 版本

## 依赖关系图（提取后）

```
FileDriven.Pipeline (根目录)
├── PipelineFactory.cs ──────────────→ Core.PipelineFactory
│   └── BuildCommandPipeline() ──────→ Share/CommandFramework.Pipeline.PipelineEntry
├── PipelineEntry.cs ────────────────→ Share/CommandFramework (CommandContext)
├── FileDrivenPipelineContext.cs ────→ Share/CommandFramework (CommandContext)
└── Skeletons/
    ├── ScriptLoadSkeleton.cs ───────→ BasicMethod (SkeletonFlow)
    └── NonEventCommandHandler.cs ───→ Share/CommandFramework

FileDriven.Pipeline/Core
├── PipelineFactory.cs ──────────────→ BasicMethod (SkeletonFlow, Pipeline)
│                                      Share/CentralPipeline (PipelineBuilder)
│                                      Share/CommandFramework (CommandContext)
├── ControlHelper.cs
├── EventCore.cs
├── NonEventCommands.cs
└── Skeletons/ControlTreeWalker.cs
```
