# ECS 渲染管线重构 — ConfigContainer 迁移实施记录

> **版本：** v1.0
> **更新日期：** 2026-07-20
> **维护者：** WorkFlowPrj Team
>
> **前置文档：**
> - [`ECS渲染管线重构-旧体系迁移方案.md`](./ECS渲染管线重构-旧体系迁移方案.md) — 方案设计文档

---

## 目录

- [ECS 渲染管线重构 — ConfigContainer 迁移实施记录](#ecs-渲染管线重构--configcontainer-迁移实施记录)
  - [目录](#目录)
  - [1. 迁移概述](#1-迁移概述)
    - [1.1 目标](#11-目标)
    - [1.2 范围](#12-范围)
    - [1.3 涉及项目](#13-涉及项目)
  - [2. 删除 ConfigContainer 类](#2-删除-configcontainer-类)
    - [2.1 操作](#21-操作)
    - [2.2 删除的旧体系代码](#22-删除的旧体系代码)
  - [3. 修复 EcsConfigContainer 自身问题](#3-修复-ecsconfigcontainer-自身问题)
    - [3.1 问题清单](#31-问题清单)
    - [3.2 修复后的类声明](#32-修复后的类声明)
    - [3.3 添加的 using](#33-添加的-using)
    - [3.4 去掉 new 关键字](#34-去掉-new-关键字)
  - [4. 替换所有引用](#4-替换所有引用)
    - [4.1 WdUIUsage.cs](#41-wduiusagecs)
    - [4.2 WdUI.EventManagement.cs](#42-wduieventmanagementcs)
    - [4.3 WdUI.Management.cs](#43-wduimanagementcs)
    - [4.4 ControlFinder.cs](#44-controlfindercs)
    - [4.5 CreateLayoutPanelSkeleton.cs](#45-createlayoutpanelskeletoncs)
    - [4.6 WdUIContainerManager.cs](#46-wduicontainermanagercs)
    - [4.7 EcsContainer.cs](#47-ecscontainercs)
  - [5. 编译验证](#5-编译验证)
    - [5.1 预期编译结果](#51-预期编译结果)
    - [5.2 验证命令](#52-验证命令)
  - [6. 决策记录](#6-决策记录)

---

## 1. 迁移概述

### 1.1 目标

将旧体系 `ConfigContainer` 类完全删除，所有引用替换为 `EcsConfigContainer`，彻底消除旧管线残留。

### 1.2 范围

| 文件 | 操作 | 说明 |
|------|------|------|
| `WdUI.Container.cs` | 删除 `ConfigContainer` 类 | 保留 `EcsConfigContainer` 类 |
| `WdUI.Container.cs` | 修复 `EcsConfigContainer` | 实现 `IContainer` 接口、添加 missing using、去掉 `new` 关键字 |
| `WdUIUsage.cs` | 替换类型引用 | `ConfigContainer` → `EcsConfigContainer` |
| `WdUI.EventManagement.cs` | 替换类型引用 | `ConfigContainer` → `EcsConfigContainer` |
| `WdUI.Management.cs` | 替换类型引用 | `ConfigContainer` → `EcsConfigContainer` |
| `ControlFinder.cs` | 替换类型引用 | `ConfigContainer` → `EcsConfigContainer` |
| `CreateLayoutPanelSkeleton.cs` | 替换类型引用 | `ConfigContainer` → `EcsConfigContainer` |
| `WdUIContainerManager.cs` | 替换类型引用 | `ConfigContainer` → `EcsConfigContainer` |
| `EcsContainer.cs` | 修复警告 | 去掉 `new event ContentChanged` 的 `new` 关键字 |

### 1.3 涉及项目

| 项目 | 文件数 |
|------|--------|
| `WdUI.csproj` | 5 个文件 |
| `FrontEndDriven.csproj` | 1 个文件 |
| `UniversalMiddleware.csproj` | 1 个文件 |

---

## 2. 删除 ConfigContainer 类

### 2.1 操作

从 `WdUI.Container.cs` 中删除 `ConfigContainer` 类（原第 57~573 行），保留 `EcsConfigContainer` 类。

### 2.2 删除的旧体系代码

| 删除项 | 说明 |
|--------|------|
| `ConfigContainer` 类声明 | `public class ConfigContainer : EcsContainer, IContainer` |
| `_orchestrator` 字段 | `RenderPipelineOrchestrator` 旧管线编排器 |
| `OrchestratorFactory` 静态委托 | 旧管线工厂 |
| 构造函数中 `_orchestrator` 初始化 | `new RenderPipelineOrchestrator(...)` |
| `RenderConfigAsync` 中的 `_orchestrator.RenderFullAsync` | 旧管线编排调用 |
| `RegisterRenderedContainers()` | 旧体系 fallback，递归注册容器 |
| `ClearResources()` 中的 `_orchestrator.DisposeSession` | 旧管线清理 |
| `IContainer` 接口实现 | 移至 `EcsConfigContainer` |

---

## 3. 修复 EcsConfigContainer 自身问题

### 3.1 问题清单

| 问题 | 根因 | 修复 |
|------|------|------|
| `IECSStorage` 未找到 | 缺少 `using ECS.Core;` | 添加 using |
| `IStorageRegistry` 未找到 | 缺少 `using ECS.Bridge;` | 添加 using |
| `IContainer.Node` 编译错误 | `EcsConfigContainer` 未实现 `IContainer` 接口 | 改为 `EcsConfigContainer : Panel, IContainer` |
| `new event ContentChanged` 警告 | `Panel` 基类没有 `ContentChanged` 事件 | 去掉 `new` 关键字 |
| `new void ClearContent()` 警告 | `Panel` 基类没有 `ClearContent` 方法 | 去掉 `new` 关键字 |

### 3.2 修复后的类声明

```csharp
// 修改前：
public class EcsConfigContainer : Panel, IConfigContainer

// 修改后：
public class EcsConfigContainer : Panel, IContainer
```

### 3.3 添加的 using

```csharp
using ECS.Core;      // IECSStorage
using ECS.Bridge;    // IStorageRegistry
```

### 3.4 去掉 new 关键字

```csharp
// 修改前：
public new event EventHandler? ContentChanged;
public new void ClearContent()

// 修改后：
public event EventHandler? ContentChanged;
public void ClearContent()
```

---

## 4. 替换所有引用

### 4.1 WdUIUsage.cs

**替换规则：** 所有 `ConfigContainer` 类型引用 → `EcsConfigContainer`

| 行号 | 修改前 | 修改后 |
|------|--------|--------|
| 200 | `public static ConfigContainer CreateContainer(string name)` | `public static EcsConfigContainer CreateContainer(string name)` |
| 220 | `public static async Task<ConfigContainer> CreateContainerWithConfigAsync(...)` | `public static async Task<EcsConfigContainer> CreateContainerWithConfigAsync(...)` |
| 222 | `var container = new ConfigContainer { Name = name };` | `var container = new EcsConfigContainer(...) { Name = name };` |
| 230 | `FindControlInContainer(ConfigContainer container, ...)` | `FindControlInContainer(EcsConfigContainer container, ...)` |
| 237 | `FindControlInContainer<T>(ConfigContainer container, ...)` | `FindControlInContainer<T>(EcsConfigContainer container, ...)` |
| 241 | `GetSubContainers(ConfigContainer container)` | `GetSubContainers(EcsConfigContainer container)` |
| 247 | `FindSubContainer(ConfigContainer container, ...)` | `FindSubContainer(EcsConfigContainer container, ...)` |
| 253 | `RegisterSubContainer(ConfigContainer parent, ConfigContainer child)` | `RegisterSubContainer(EcsConfigContainer parent, EcsConfigContainer child)` |
| 258 | `ClearContainerContent(ConfigContainer container)` | `ClearContainerContent(EcsConfigContainer container)` |
| 263 | `IsContainerLoaded(ConfigContainer container)` | `IsContainerLoaded(EcsConfigContainer container)` |
| 268 | `GetContainerConfig(ConfigContainer container)` | `GetContainerConfig(EcsConfigContainer container)` |
| 556 | `public static ConfigContainer? FindNearestConfigContainer(...)` | `public static EcsConfigContainer? FindNearestConfigContainer(...)` |
| 624 | `LoadAndWaitAsync(ConfigContainer container, ...)` | `LoadAndWaitAsync(EcsConfigContainer container, ...)` |
| 640 | `LoadAndWaitWithConfigAsync(ConfigContainer container, ...)` | `LoadAndWaitWithConfigAsync(EcsConfigContainer container, ...)` |

**注意：** `CreateContainerWithConfigAsync` 中 `new ConfigContainer` 需要改为 `new EcsConfigContainer(ecsStorage, storageRegistry)`，需要传入 `IECSStorage` 和 `IStorageRegistry` 参数。建议改为从 `AppServiceProvider` 解析或从 `WdUIComposer` 获取。

### 4.2 WdUI.EventManagement.cs

| 行号 | 修改前 | 修改后 |
|------|--------|--------|
| 185 | `private readonly ConfigContainer _container;` | `private readonly EcsConfigContainer _container;` |
| 198 | `/// <param name="container">所属的 ConfigContainer 实例</param>` | `/// <param name="container">所属的 EcsConfigContainer 实例</param>` |
| 200 | `public ContainerEventManager(ConfigContainer container)` | `public ContainerEventManager(EcsConfigContainer container)` |
| 297 | `private ConfigContainer? FindSubContainerByScopeId(...)` | `private EcsConfigContainer? FindSubContainerByScopeId(...)` |
| 304 | `if (sub is ConfigContainer cc && ...)` | `if (sub is EcsConfigContainer cc && ...)` |

### 4.3 WdUI.Management.cs

| 行号 | 修改前 | 修改后 |
|------|--------|--------|
| 642 | `/// 统一管理 ConfigContainer 的异步加载生命周期` | `/// 统一管理 EcsConfigContainer 的异步加载生命周期` |
| 648 | `LoadAndWaitAsync(ConfigContainer container, ...)` | `LoadAndWaitAsync(EcsConfigContainer container, ...)` |
| 663 | `LoadAndWaitAsyncCore(ConfigContainer container, ...)` | `LoadAndWaitAsyncCore(EcsConfigContainer container, ...)` |

### 4.4 ControlFinder.cs

| 行号 | 修改前 | 修改后 |
|------|--------|--------|
| 246 | `/// 查找最近的 ConfigContainer 祖先` | `/// 查找最近的 EcsConfigContainer 祖先` |
| 248 | `public static ConfigContainer? FindNearestConfigContainer(...)` | `public static EcsConfigContainer? FindNearestConfigContainer(...)` |
| 251 | `return _pathFinder.FindNearestAncestor(control, c => c is ConfigContainer) as ConfigContainer;` | `return _pathFinder.FindNearestAncestor(control, c => c is EcsConfigContainer) as EcsConfigContainer;` |

### 4.5 CreateLayoutPanelSkeleton.cs

| 行号 | 修改前 | 修改后 |
|------|--------|--------|
| 多处 | `List<ConfigContainer>` | `List<EcsConfigContainer>` |
| 多处 | `ConfigContainer?` | `EcsConfigContainer?` |
| 多处 | `new ConfigContainer { ... }` | `new EcsConfigContainer(ecsStorage, storageRegistry) { ... }` |

**注意：** `CreateLayoutPanelSkeleton` 中 `new ConfigContainer()` 需要改为 `new EcsConfigContainer(ecsStorage, storageRegistry)`，需要传入 ECS 依赖。建议通过 `AppServiceProvider` 解析或从 `WdUIComposer` 获取。

### 4.6 WdUIContainerManager.cs

| 行号 | 修改前 | 修改后 |
|------|--------|--------|
| 多处 | `ConcurrentDictionary<string, ConfigContainer>` | `ConcurrentDictionary<string, EcsConfigContainer>` |
| 多处 | `ConfigContainer` 类型引用 | `EcsConfigContainer` |
| 工厂 | `return new ConfigContainer();` | `return new EcsConfigContainer(ecsStorage, storageRegistry);` |

**注意：** `WdUIContainerManager` 中 `new ConfigContainer()` 需要改为 `new EcsConfigContainer(ecsStorage, storageRegistry)`，`ecsStorage` 和 `storageRegistry` 可从 `CreateEcsRenderPipeline()` 方法中获取。

### 4.7 EcsContainer.cs

| 行号 | 修改前 | 修改后 |
|------|--------|--------|
| 134 | `public new event EventHandler? ContentChanged;` | `public event EventHandler? ContentChanged;` |

---

## 5. 编译验证

### 5.1 预期编译结果

| 项目 | 预期状态 |
|------|----------|
| `WdUI.csproj` | ✅ 0 错误，0 警告 |
| `FrontEndDriven.csproj` | ✅ 0 错误，0 警告 |
| `UniversalMiddleware.csproj` | ✅ 0 错误，0 警告 |

### 5.2 验证命令

```bash
dotnet build "d:\Project\Program\WorkFlowPrj\CsharpPrj\Operation\WdUI\WdUI.csproj"
dotnet build "d:\Project\Program\WorkFlowPrj\CsharpPrj\Operation\FrontEndDriven\FrontEndDriven.csproj"
dotnet build "d:\Project\Program\WorkFlowPrj\CsharpPrj\Share\UniversalMiddleware\UniversalMiddleware.csproj"
```

---

## 6. 决策记录

| 日期 | 决策 | 理由 |
|------|------|------|
| 2026-07-20 | 直接删除 `ConfigContainer` 类，而非标记 `[Obsolete]` | 彻底迁移，消除技术债务 |
| 2026-07-20 | `EcsConfigContainer` 实现 `IContainer` 而非 `IConfigContainer` | `IContainer` 继承 `IConfigContainer`，提供更完整的容器能力（Node、Index、Lifecycle、EventManager） |
| 2026-07-20 | `EcsConfigContainer` 的 `ContentChanged` 和 `ClearContent()` 去掉 `new` 关键字 | `Panel` 基类没有同名成员，`new` 关键字产生误导性警告 |
| 2026-07-20 | `CreateContainerWithConfigAsync` 中 `new ConfigContainer` 改为从 `AppServiceProvider` 解析 ECS 依赖 | `EcsConfigContainer` 构造函数需要 `IECSStorage` 和 `IStorageRegistry`，不能无参构造 |
