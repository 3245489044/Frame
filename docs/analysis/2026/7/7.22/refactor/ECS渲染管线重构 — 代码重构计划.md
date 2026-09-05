# ECS 渲染管线重构 — 代码重构计划

> **版本：** v2.1
> **日期：** 2026-07-22
> **依据：** `CsharpPrj/Structuring/架构重构模式 — 常见架构缺陷与应对策略.md`
> **目标：** 针对 ECS 渲染管线重构后的 6 种架构缺陷，制定具体的代码级修复方案
>
> **v2.0 更新：** 修正架构方向 — 彻底消除 WinForms 控件树作为数据源，Entity 树作为唯一权威数据源
>
> **v2.1 更新：** 全部 5 项缺陷修复已完成（缺陷 5+3 合并实施），更新实施状态和验证清单

---

## 目录

- [ECS 渲染管线重构 — 代码重构计划](#ecs-渲染管线重构--代码重构计划)
  - [目录](#目录)
  - [1. 架构总纲：Entity 树作为唯一数据源](#1-架构总纲entity-树作为唯一数据源)
    - [1.1 当前架构问题](#11-当前架构问题)
    - [1.2 目标架构](#12-目标架构)
    - [1.3 架构迁移路径](#13-架构迁移路径)
  - [2. 缺陷模式总览](#2-缺陷模式总览)
  - [3. 缺陷 1：透传链过长](#3-缺陷-1透传链过长)
    - [3.1 问题定位](#31-问题定位)
    - [3.2 修复方案](#32-修复方案)
    - [3.3 实施状态](#33-实施状态)
    - [3.4 涉及文件](#34-涉及文件)
  - [4. 缺陷 2：重构残留](#4-缺陷-2重构残留)
    - [4.1 残留项清单](#41-残留项清单)
    - [4.2 修复方案](#42-修复方案)
    - [4.3 实施状态](#43-实施状态)
    - [4.4 涉及文件](#44-涉及文件)
  - [5. 缺陷 3：双数据源不一致](#5-缺陷-3双数据源不一致)
    - [5.1 问题定位](#51-问题定位)
    - [5.2 修复方案](#52-修复方案)
    - [5.3 实施状态](#53-实施状态)
    - [5.4 涉及文件](#54-涉及文件)
  - [6. 缺陷 4：职责错位](#6-缺陷-4职责错位)
    - [6.1 问题定位](#61-问题定位)
    - [6.2 修复方案](#62-修复方案)
    - [6.3 实施状态](#63-实施状态)
    - [6.4 涉及文件](#64-涉及文件)
  - [7. 缺陷 5：隐式顺序依赖](#7-缺陷-5隐式顺序依赖)
    - [7.1 问题定位](#71-问题定位)
    - [7.2 修复方案](#72-修复方案)
    - [7.3 实施状态](#73-实施状态)
    - [7.4 涉及文件](#74-涉及文件)
  - [8. 缺陷 6：逻辑分散](#8-缺陷-6逻辑分散)
    - [8.1 问题定位](#81-问题定位)
    - [8.2 修复方案](#82-修复方案)
    - [8.3 实施状态](#83-实施状态)
    - [8.4 涉及文件](#84-涉及文件)
  - [9. 修复优先级与执行顺序](#9-修复优先级与执行顺序)
  - [10. 验证清单](#10-验证清单)
    - [构建验证](#构建验证)
    - [运行时验证](#运行时验证)
    - [回归验证](#回归验证)

---

## 1. 架构总纲：Entity 树作为唯一数据源

### 1.1 当前架构问题

当前 ECS 渲染管线存在**两棵树并存**的架构缺陷：

```
┌─────────────────────────────────────────────────────────┐
│                    当前架构（问题）                        │
│                                                         │
│  UIConfiguration (JSON)                                  │
│       │                                                  │
│       ▼                                                  │
│  Entity 树 (ECS)  ←── 权威数据源？                        │
│       │                    ↑                             │
│       │                    │ 不一致                       │
│       ▼                    │                             │
│  WinForms 控件树  ─────────┘   ←── 实际使用              │
│       │                                                  │
│       ▼                                                  │
│  屏幕渲染                                                │
└─────────────────────────────────────────────────────────┘
```

**问题根源：**
- `ControlCreateSystem` 创建 WinForms 控件后，父子关系通过 `parent.Controls.Add(control)` 建立在 WinForms 树上
- `LayoutSystem` 操作 WinForms 控件的位置/大小属性
- `EventBindingSystem` 绑定 WinForms 原生事件
- 但 Entity 树也有自己的 `ControlHierarchyComponent.Children` 维护父子关系

**两棵树不同步的表现：**
- 控件创建失败时，Entity 树有 Entity，WinForms 树无控件
- 子容器注册遍历 Entity 树，但实际查找依赖 WinForms 树
- `FindParent` 依赖 ControlPool 的注册顺序（隐式顺序依赖）

### 1.2 目标架构

**Entity 树作为唯一权威数据源，WinForms 控件降级为纯渲染层：**

```
┌─────────────────────────────────────────────────────────┐
│                    目标架构（纯 Entity 树）                │
│                                                         │
│  UIConfiguration (JSON)                                  │
│       │                                                  │
│       ▼                                                  │
│  Entity 树 (ECS)  ←── 唯一权威数据源                      │
│       │                                                  │
│       │  (EntityComponent → 布局数据)                     │
│       │  (EntityComponent → 事件绑定数据)                  │
│       │                                                  │
│       ▼                                                  │
│  WinForms 渲染层  ←── 纯输出，无业务逻辑                  │
│       │                                                  │
│       ▼                                                  │
│  屏幕渲染                                                │
└─────────────────────────────────────────────────────────┘
```

**核心原则：**

| 原则 | 说明 |
|------|------|
| **Entity 树是唯一数据源** | 所有业务逻辑（布局、事件绑定、父子关系）都基于 Entity 树的 Component |
| **WinForms 控件是纯渲染输出** | 只负责最终的屏幕绘制，不参与业务决策 |
| **父子关系由 Entity 树维护** | `ControlHierarchyComponent.Children` 是权威父子关系 |
| **布局数据存储在 Entity Component 中** | 位置、大小等布局属性作为 Component 存储在 Entity 上 |
| **事件绑定数据存储在 Entity Component 中** | 事件配置作为 Component 存储在 Entity 上 |

### 1.3 架构迁移路径

```
Step 1: Entity 树拓扑排序
  └─ 保证 ControlCreateSystem 按父→子顺序遍历 Entity

Step 2: 父子关系收归 Entity 树
  └─ parent.Controls.Add(control) → Entity 树 ControlHierarchyComponent

Step 3: 布局数据迁移到 Entity Component
  └─ control.Location/Size → LayoutComponent on Entity

Step 4: 事件绑定数据迁移到 Entity Component
  └─ WinForms 事件 → EventBindingComponent on Entity

Step 5: WinForms 控件降级为纯渲染器
  └─ RenderSystem 读取 Entity Component → 映射到 WinForms 控件
```

---

## 2. 缺陷模式总览

| # | 缺陷模式 | 严重程度 | 影响范围 | 优先级 | 实施状态 |
|---|---------|---------|---------|-------|---------|
| 1 | 透传链过长 | ⚠️ 中 | MainLayout 加载路径（4 层调用） | P2 | ✅ 已完成 |
| 2 | 重构残留 | 🔴 高 | WdUIComposer、FileDrivenWdUIBridge、EcsPipelineOrchestrator | P1 | ✅ 已完成 |
| 3 | 双数据源不一致 | 🔴 高 | Entity 树 vs WinForms 控件树 | **P0** | ✅ 已完成 |
| 4 | 职责错位 | ⚠️ 中 | ContainerIndex 维护分散在 3 个类 | P1 | ✅ 已完成 |
| 5 | 隐式顺序依赖 | 🔴 高 | ControlCreateSystem 父子关系建立 | **P0** | ✅ 已完成 |
| 6 | 逻辑分散 | ⚠️ 中 | 控件类型分支在 4 处重复 | P2 | ✅ 已完成 |

---

## 3. 缺陷 1：透传链过长

### 3.1 问题定位

**MainLayout 加载路径（4 层调用）：**

```
MainForm.OnInitializeAsync()
  → ConfigLoader.LoadConfigAsync(_rootContainer, path)       // 第 1 层
    → JsonConfigService.LoadConfigAsync(path)                // 第 2 层
    → container.LoadConfigAsync(config)                      // 第 3 层
      → EcsConfigContainer.RenderConfigAsync(config)         // 第 4 层
        → _renderPipeline.ExecutePipeline(config)            // 第 5 层
```

**问题：**
- `ConfigLoader.LoadConfigAsync` 只是 `JsonConfigService.LoadConfigAsync` + `container.LoadConfigAsync` 的简单组合，无实质性处理
- `JsonConfigService.LoadConfigAsync` 内部又调用 `Deserialize` + `Preprocessor.Preprocess`，职责边界模糊
- 新增参数需修改 4-5 层签名

### 3.2 修复方案

**策略：合并中间层，缩短调用链。**

```
// 重构前：4 层
MainForm → ConfigLoader.LoadConfigAsync → JsonConfigService.LoadConfigAsync → container.LoadConfigAsync

// 重构后：2 层
MainForm → container.LoadFromFileAsync(path)   // EcsConfigContainer 直接暴露文件加载方法
```

**具体步骤：**

1. **EcsConfigContainer 新增 `LoadFromFileAsync` 方法**，合并 `JsonConfigService.LoadConfigAsync` + `LayoutJsonService.ValidateConfig` + `LoadConfigAsync` 三步：
   ```csharp
   public async Task<CfgModels.UIConfiguration?> LoadFromFileAsync(string configPath, bool strictMode = true)
   {
       var configLoader = WdUIComposer.Config;
       if (configLoader == null)
       {
           if (strictMode)
               throw new InvalidOperationException("IConfigLoader is not available");
           return null;
       }

       var result = configLoader.LoadConfig(configPath);
       if (result.Success && result.Config is CfgModels.UIConfiguration config)
       {
           await LoadConfigAsync(config);
           return config;
       }

       if (strictMode)
       {
           throw new InvalidOperationException(
               $"Failed to load config from: {configPath}, error: {result.ErrorMessage}");
       }

       return null;
   }
   ```

2. **同步版本 `LoadFromFile`** 也一并提供，适用于非异步场景。

### 3.3 实施状态

| 步骤 | 状态 | 说明 |
|------|------|------|
| EcsConfigContainer 新增 `LoadFromFileAsync` | ✅ 已完成 | 含 strictMode 参数，加载失败时返回 null 或抛出异常 |
| EcsConfigContainer 新增 `LoadFromFile`（同步） | ✅ 已完成 | 内部委托给异步实现 |
| ConfigLoader 标记废弃 | ⏸️ 暂缓 | 保留向后兼容，新代码应使用容器方法 |

### 3.4 涉及文件

| 文件 | 修改类型 |
|------|---------|
| `CsharpPrj/Operation/FrontEndDriven/Container/EcsConfigContainer.cs` | 新增 `LoadFromFileAsync` + `LoadFromFile` |

---

## 4. 缺陷 2：重构残留

### 4.1 残留项清单

| # | 残留项 | 位置 | 说明 |
|---|-------|------|------|
| R1 | `WdUIComposer.RenderPipeline` 属性 | `WdUI/WdUIComposer.cs:45` | 已标记 `[Obsolete]`，EcsConfigContainer 已直接持有 EcsRenderPipeline |
| R2 | `WdUIComposer.RenderPipeline` 的调用方 | `MainForm.cs:691` | `WdUIComposer.RenderPipeline.ExecuteBindEventsStageAsync`（仅剩事件绑定阶段） |
| R3 | `EcsPipelineOrchestrator._eventBindCallback` | `FileDriven.UI/ECS/EcsPipelineOrchestrator.cs:43` | 事件绑定已通过 `EventBindHandler` 委托注入，此回调不再使用 |
| R4 | `EcsPipelineOrchestrator.ConnectEvents` 中的空事件绑定分支 | `EcsPipelineOrchestrator.cs:98-99` | `EventBindRequested` 事件订阅已注释，但分支结构保留 |
| R5 | `FileDrivenWdUIBridge` 中的旧委托方法 | `FileDrivenWdUIBridge.cs` | 大量方法仅委托到 `ConfigLoader`，调用方可直接使用 `ConfigLoader` |

### 4.2 修复方案

**策略：全链路追踪，逐层清理。**

**R1：`WdUIComposer.RenderPipeline` 已标记 `[Obsolete]`，保留向后兼容。**

**R2：替换调用方**

`MainForm.cs` 中 `ExecuteSideloadPipelineAsync` 方法：
```csharp
// ★ 2026-07-22 修复：WdUIComposer.RenderPipeline 已标记 [Obsolete]，
//   改用 FileDrivenWdUIBridge 获取渲染管线实例
var pipeline = FileDrivenWdUIBridge.GetRenderPipeline();
if (pipeline != null)
{
    await pipeline.ExecuteBindEventsStageAsync(_sideContainer, config);
}
```

**R3：移除 `_eventBindCallback` 字段和构造函数参数**

```csharp
// 删除字段
// private readonly Action<string, string, object?>? _eventBindCallback;

// 删除构造函数参数
public EcsPipelineOrchestrator(
    IECSStorage ecsStorage,
    IStorageCell cell,
    IStorageRegistry storageRegistry,
    Func<string, string, Control>? controlFactoryCallback = null)
```

**R4：`ConnectEvents` 中的空分支已清理**（注释块保留，无实际代码影响）。

**R5：`FileDrivenWdUIBridge` 新增 `GetRenderPipeline()` 方法**，替代 `WdUIComposer.RenderPipeline`。

### 4.3 实施状态

| 残留项 | 状态 | 说明 |
|--------|------|------|
| R1: WdUIComposer.RenderPipeline | ✅ 已标记 `[Obsolete]` | 保留向后兼容 |
| R2: MainForm.cs 调用方 | ✅ 已完成 | 改用 `FileDrivenWdUIBridge.GetRenderPipeline()` |
| R3: _eventBindCallback | ✅ 已完成 | 字段和构造函数参数已删除 |
| R4: ConnectEvents 空分支 | ✅ 已完成 | 注释块已清理 |
| R5: FileDrivenWdUIBridge | ✅ 已完成 | 新增 `GetRenderPipeline()` 方法 |

### 4.4 涉及文件

| 文件 | 残留项 | 修改类型 |
|------|--------|---------|
| `CsharpPrj/Operation/FrontEndDriven/WdUI/WdUIComposer.cs` | R1 | 已标记 `[Obsolete]` |
| `FileDriven/FileDriven.UI/Core/MainForm.cs` | R2 | 修改调用 ✅ |
| `FileDriven/FileDriven.UI/ECS/EcsPipelineOrchestrator.cs` | R3, R4 | 删除字段和参数 ✅ |
| `FileDriven/FileDriven.UI/WinForms/Services/FileDrivenWdUIBridge.cs` | R5 | 新增 `GetRenderPipeline()` ✅ |

---

## 5. 缺陷 3：双数据源不一致

### 5.1 问题定位

**两个数据源：**

| 数据源 | 内容 | 生命周期 |
|--------|------|---------|
| Entity 树（ECS） | `ConfigToEntityConverter` 从 `UIConfiguration` 模型创建 | 配置加载时创建，Clear 时销毁 |
| WinForms 控件树 | `ControlCreateSystem` 实际创建的 WinForms 控件 | 控件创建时存在，创建失败时不存在 |

**问题表现：**
- `ControlCreateSystem` 阶段 2 中，`FindParent(entity)` 通过 `ControlPool` 查找父控件
- `ControlPool` 的注册发生在阶段 1（控件创建时）
- 如果某个控件创建失败（如类型不支持），Entity 树中有对应 Entity，但 WinForms 树中没有对应控件
- 子容器注册时遍历 Entity 树，但实际依赖 WinForms 树的存在性

### 5.2 修复方案

**策略：Entity 树作为唯一数据源，控件创建失败时同步删除对应 Entity。**

**核心原则：** Entity 树和 WinForms 控件树必须保持一一对应。Entity 树中只保留成功创建了控件的 Entity。

**具体步骤：**

1. **`ControlCreateSystem` 阶段 1 中，控件创建失败时删除对应 Entity：**

   ```csharp
   // 重构后：创建失败时从 Entity 树中移除
   var control = CreateControl(entity);
   if (control != null)
   {
       _pool.Register(entity, control);
   }
   else
   {
       // ★ 从 Entity 树中移除失败的 Entity，保持两棵树一致
       RemoveEntityFromTree(entity);
   }
   ```

2. **`RemoveEntityFromTree` 实现：**

   ```csharp
   private void RemoveEntityFromTree(Entity entity)
   {
       // 从父 Entity 的 Children 列表中移除
       var hierarchy = _entityManager.GetComponent<ControlHierarchyComponent>(entity);
       if (hierarchy.Parent.IsValid)
       {
           var parentHierarchy = _entityManager.GetComponent<ControlHierarchyComponent>(hierarchy.Parent);
           parentHierarchy.Children.Remove(entity);
           _entityManager.SetComponent(hierarchy.Parent, parentHierarchy);
       }
       
       // 删除 Entity 的所有 Component
       _entityManager.RemoveEntity(entity);
   }
   ```

### 5.3 实施状态

| 步骤 | 状态 | 说明 |
|------|------|------|
| `ControlCreateSystem` 新增 `RemoveEntityFromTree` | ✅ 已完成 | 递归移除子 Entity + 从 ControlPool 注销 |
| 创建失败时调用移除 | ✅ 已完成 | 策略创建失败 + 事件无订阅者时触发 |
| 拓扑排序保证父 Entity 先处理 | ✅ 已完成 | BFS 拓扑排序，与缺陷 5 合并实施 |

### 5.4 涉及文件

| 文件 | 修改类型 |
|------|---------|
| `CsharpPrj/Operation/FrontEndDriven/ECS/Systems/ControlCreateSystem.cs` | 新增 `RemoveEntityFromTree` + 创建失败时调用 ✅ |

---

## 6. 缺陷 4：职责错位

### 6.1 问题定位

**ContainerIndex（子容器注册索引）的维护分散在三个类中：**

| 操作 | 当前负责方 | 问题 |
|------|-----------|------|
| 根容器注册 | `EntityRegistry.RegisterRoot()` | EntityRegistry 维护 EcsConfigContainer 的索引 |
| 根容器注册 | `EcsRenderPipeline.RegisterRootContainer()` | 与 EntityRegistry 功能重叠 |
| 子容器注册 | `EcsConfigContainer.RegisterSubContainer()` | 正确，但被外部调用覆盖 |
| 索引清空 | `EcsConfigContainer.ClearContent()` | 正确 |
| 索引遍历 | `EntityRegistry` 的 `RootContainer` 属性 | 外部类通过 EntityRegistry 访问容器索引 |

### 6.2 修复方案

**策略：谁拥有数据，谁负责维护。ContainerIndex 完全收归 `EcsConfigContainer`。**

**具体步骤：**

1. **`EcsConfigContainer` 内部维护子容器列表**，不依赖外部索引：

   ```csharp
   public class EcsConfigContainer : EcsContainer
   {
       private readonly List<EcsConfigContainer> _subContainers = new();
       
       public IReadOnlyList<EcsConfigContainer> SubContainers => _subContainers.AsReadOnly();
       
       public void RegisterSubContainer(EcsConfigContainer subContainer)
       {
           if (!_subContainers.Contains(subContainer))
           {
               _subContainers.Add(subContainer);
           }
       }
       
       public void UnregisterSubContainer(EcsConfigContainer subContainer)
       {
           _subContainers.Remove(subContainer);
       }
   }
   ```

2. **移除 `EntityRegistry` 中的 `RootContainer` 属性**，调用方直接通过 `EcsConfigContainer` 访问。

3. **`EcsRenderPipeline.RegisterRootContainer` 简化为仅注册到 ControlPool**，不再维护容器索引。

### 6.3 实施状态

| 步骤 | 状态 | 说明 |
|------|------|------|
| EcsConfigContainer 新增 `_subContainers` 列表 | ✅ 已完成 | 含 `SubContainers` 只读属性 |
| EcsConfigContainer 新增 `RegisterSubContainer` | ✅ 已完成 | 同时添加到 Controls 集合 |
| EcsConfigContainer 新增 `UnregisterSubContainer` | ✅ 已完成 | 从子容器列表移除 |
| EntityRegistry 移除 `RootContainer` 属性 | ✅ 已完成 | 同时移除 `_rootContainer` 字段 |
| ClearContainerContent 同步清空子容器列表 | ✅ 已完成 | Dispose 中也同步清理 |

### 6.4 涉及文件

| 文件 | 修改类型 |
|------|---------|
| `CsharpPrj/Operation/FrontEndDriven/Container/EcsConfigContainer.cs` | 新增子容器列表 + 注册/注销方法 ✅ |
| `CsharpPrj/Operation/FrontEndDriven/ECS/EntityRegistry.cs` | 移除 `RootContainer` 属性 ✅ |

---

## 7. 缺陷 5：隐式顺序依赖

### 7.1 问题定位

**`ControlCreateSystem` 当前结构：**

```csharp
public void Update()
{
    var entities = _entityManager.GetEntities<ControlTypeComponent, ControlStateComponent>()
        .Where(e => !_entityManager.GetComponent<ControlStateComponent>(e).IsCreated)
        .ToList();

    foreach (var entity in entities)    // 遍历顺序不确定！
    {
        // 阶段 1：创建控件
        var control = CreateControl(entity);
        _pool.Register(entity, control);
        
        // 阶段 2：建立父子关系
        var parent = FindParent(entity);  // 依赖父控件已在 Pool 中
        if (parent != null)
            parent.Controls.Add(control);
        // ↑ parent == null 时静默失败
    }
}
```

**问题：**
- `entities` 的遍历顺序由 `EntityManager.GetEntities` 决定，不确定
- 子 Entity 在父 Entity 之前处理时，`FindParent` 返回 `null`
- `parent == null` 时静默跳过，不报错

### 7.2 修复方案

**策略：Entity 树拓扑排序 + 两阶段处理。**

**核心思路：** 在 `ControlCreateSystem` 执行前，对 Entity 列表按 `ControlHierarchyComponent` 的层级关系做拓扑排序，保证父 Entity 先于子 Entity 被处理。

**具体步骤：**

1. **Entity 拓扑排序工具方法：** BFS 拓扑排序，从根节点开始遍历。

2. **`ControlCreateSystem.Update` 使用拓扑排序 + 两阶段分离：**
   - 阶段 1：创建所有控件并注册到 ControlPool
   - 阶段 2：建立父子关系（此时所有控件已在 Pool 中）

### 7.3 实施状态

| 步骤 | 状态 | 说明 |
|------|------|------|
| `TopologicalSort` 方法 | ✅ 已完成 | BFS 拓扑排序，支持孤立节点 |
| 阶段 1：创建+注册 | ✅ 已完成 | 拓扑排序后遍历，创建失败时移除 Entity |
| 阶段 2：建立父子关系 | ✅ 已完成 | 所有控件已在 Pool 中，`FindParent` 总能找到 |
| 父控件不存在时记录警告 | ✅ 已完成 | 使用 `System.Diagnostics.Debug.WriteLine` |

### 7.4 涉及文件

| 文件 | 修改类型 |
|------|---------|
| `CsharpPrj/Operation/FrontEndDriven/ECS/Systems/ControlCreateSystem.cs` | 新增拓扑排序 + 两阶段重构 ✅ |

---

## 8. 缺陷 6：逻辑分散

### 8.1 问题定位

**控件类型分支在 4 处重复：**

| 位置 | 分支逻辑 | 文件 |
|------|---------|------|
| Entity 创建 | `ConfigToEntityConverter.ConvertControl` 中判断 `control.Type` | `BasicMethod/ECS/Conversion/ConfigToEntityConverter.cs` |
| 控件实例化 | `ControlCreateSystem` 中 `CreateControl` 根据类型创建不同控件 | `BasicMethod/ECS/Systems/ControlCreateSystem.cs` |
| 布局计算 | `LayoutSystem` 中根据控件类型执行不同布局算法 | `BasicMethod/ECS/Systems/LayoutSystem.cs` |
| 事件绑定 | `EventBindingSystem` 中根据控件类型绑定不同事件 | `BasicMethod/ECS/Systems/EventBindingSystem.cs` |

**新增控件类型（如 `ComboBox`）需修改 4 处，遗漏任一都会导致运行时行为不一致。**

### 8.2 修复方案

**策略：策略模式 + 注册表。**

**步骤 1：定义策略接口**

```csharp
public interface IControlStrategy
{
    string ControlType { get; }
    Control Create();
    void Layout(Control control, Entity entity);
    void BindEvents(Control control, Entity entity);
}
```

**步骤 2：实现具体策略**

```csharp
public class ContainerStrategy : IControlStrategy
{
    public string ControlType => "container";
    public Control Create() => new EcsConfigContainer();
    public virtual void Layout(Control control, Entity entity) { /* 由 LayoutSystem 统一处理 */ }
    public virtual void BindEvents(Control control, Entity entity) { /* 由 EventBindingSystem 统一处理 */ }
}
```

**步骤 3：注册表集中管理**

```csharp
public static class ControlStrategyRegistry
{
    private static readonly ConcurrentDictionary<string, IControlStrategy> _strategyMap = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, Func<Control>> _factoryMap = new(StringComparer.OrdinalIgnoreCase);
    
    public static void Register(IControlStrategy strategy) { ... }
    public static void Register(string controlType, Func<Control> factory) { ... }
    public static Control? Create(string controlType) { ... }
    public static IControlStrategy? GetStrategy(string controlType) { ... }
}
```

**步骤 4：标准控件批量注册**

```csharp
public static class StandardControlStrategies
{
    public static void RegisterAll()
    {
        // 注册 34 种标准 WinForms 控件
        register("button",  () => new Button());
        register("textbox", () => new TextBox());
        // ...
    }
}
```

### 8.3 实施状态

| 步骤 | 状态 | 说明 |
|------|------|------|
| `IControlStrategy` 接口定义 | ✅ 已完成 | 含 `ControlType`/`Create`/`Layout`/`BindEvents` |
| `ContainerStrategy` 实现 | ✅ 已完成 | `Layout`/`BindEvents` 为虚方法 |
| `ControlStrategyRegistry` 注册表 | ✅ 已完成 | 支持完整策略 + 简单工厂双模式 |
| `StandardControlStrategies` 批量注册 | ✅ 已完成 | 34 种标准控件 |
| `EcsControlCreationStrategy` 使用注册表 | ✅ 已完成 | 委托给 `ControlStrategyRegistry.Create` |

### 8.4 涉及文件

| 文件 | 修改类型 |
|------|---------|
| `CsharpPrj/Operation/FrontEndDriven/ECS/IControlStrategy.cs` | 已创建 ✅ |
| `CsharpPrj/Operation/FrontEndDriven/ECS/ControlStrategyRegistry.cs` | 已创建 ✅ |
| `CsharpPrj/Operation/FrontEndDriven/ECS/StandardControlStrategies.cs` | 已创建 ✅ |
| `CsharpPrj/Operation/FrontEndDriven/Container/ContainerStrategy.cs` | 已创建 ✅ |
| `CsharpPrj/Operation/FrontEndDriven/ECS/EcsControlCreationStrategy.cs` | 已重构 ✅ |

---

## 9. 修复优先级与执行顺序

```
P0（运行时正确性）→ P1（数据一致性）→ P2（代码质量）
```

| 执行顺序 | 缺陷 | 原因 | 状态 |
|---------|------|------|------|
| **第 1 步** | 缺陷 5+3：隐式顺序依赖 + 双数据源不一致 | Entity 拓扑排序 + 创建失败时删除 Entity | ✅ 已完成 |
| **第 2 步** | 缺陷 4：职责错位 | ContainerIndex 收归 EcsConfigContainer | ✅ 已完成 |
| **第 3 步** | 缺陷 2：重构残留 | 清理废弃代码，消除混淆 | ✅ 已完成 |
| **第 4 步** | 缺陷 1：透传链过长 | 代码质量优化，不影响运行时 | ✅ 已完成 |
| **第 5 步** | 缺陷 6：逻辑分散 | 代码质量优化，不影响运行时 | ✅ 已完成 |

**第 1 步和第 2 步强关联**，建议一起实施：
- 拓扑排序保证父 Entity 先处理 → `FindParent` 总能找到父控件
- 创建失败时删除 Entity → Entity 树和 WinForms 树始终一致
- 两阶段分离 → 先创建注册，再建立关系

---

## 10. 验证清单

### 构建验证

- [ ] `dotnet build` 0 警告 0 错误
- [ ] 所有 `[Obsolete]` 标记的属性/方法确认无调用方后删除

### 运行时验证

- [ ] MainLayout 加载后 `contentContainer` 和 `sideContainer` 正确显示
- [ ] ConfigSelector 加载后列表正常显示
- [ ] sideload 加载配置后控件正确渲染
- [ ] 控件父子关系正确（Button 在 Panel 内）
- [ ] 事件绑定正常工作（Click / TextChanged 等）
- [ ] 多次 sideload 切换时控件正确清理和重建
- [ ] 窗口大小变化时布局正确重新计算
- [ ] 控件创建失败时 Entity 树同步清理（无残留 Entity）
- [ ] Entity 遍历顺序不影响父子关系建立

### 回归验证

- [ ] CLI 模式不受影响
- [ ] 脚本执行引擎不受影响
- [ ] 日志输出与重构前一致
