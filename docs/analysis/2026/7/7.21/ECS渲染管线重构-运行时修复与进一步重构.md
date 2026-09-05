# ECS 渲染管线重构 — 运行时修复与进一步重构

> **基于** `ECS渲染管线重构-rootContainer纳入ECS体系.md` 的实施回顾  
> **撰写日期**：2026-07-20  
> **状态**：Phase 0-4 已实施，构建通过，运行时发现 `contentContainer` 未注册到 `ContainerIndex`

---

## 1. 运行时错误根因分析

### 1.1 错误现象

```
[FATAL] UI 初始化失败: MainLayout must contain a 'contentContainer'
  → 根容器类型: EcsConfigContainer
  → 根容器名称: rootContainer
  → 可用子容器: []
```

`rootContainer.FindSubContainer("contentContainer")` 返回 `null`，因为 `contentContainer` 未注册到 `ContainerIndex`。

### 1.2 完整调用链

```
MainForm.OnInitializeAsync()
  → _rootContainer.LoadConfigAsync(MainLayout.json)
    → EcsConfigContainer.RenderConfigAsync(config)
      → Controls.Clear()              // 清空 WinForms 控件树
      → ContainerIndex.Clear()        // ★ 清空 rootContainer 的 ContainerIndex！
      → pipeline.ExecuteCreateStageAsync(this, config)
        → integration.LoadConfiguration(uiConfig)    // 创建 Entity 树
        → integration.ExecuteStage(ControlCreate)    // ControlCreateSystem 创建控件
          → ControlCreateSystem.Update()
            → ControlFactory?.Invoke("container")    // → new EcsConfigContainer()
            → ParentFinder?.Invoke(entity)           // → rootContainer
            → parent.Controls.Add(control)           // 添加到 rootContainer
            → SubContainerRegistrar?.Invoke(parent, control)  // ★ 被设为 null！
        → RegisterSubContainersFromEntityTree(target) // ★ 遍历 Entity 树注册子容器
```

### 1.3 根因：两个不兼容点

#### 不兼容点 1：`SubContainerRegistrar` 被设为 `null`

在 `EcsRenderPipelineAdapter.ExecuteCreateStageAsync` 中，`createSystem.SubContainerRegistrar = null`（第 267 行），理由是"`RegisterSubContainersFromEntityTree` 会替代它"。

但这两个机制的**执行时机不同**：

| 机制 | 执行时机 | 当前状态 |
|------|---------|---------|
| `SubContainerRegistrar` | `ControlCreateSystem.Update()` **内部**，每个控件创建时立即注册 | ❌ 被设为 `null` |
| `RegisterSubContainersFromEntityTree` | `ExecuteStage` **之后**，所有控件创建完成后遍历 Entity 树 | ✅ 但时机太晚 |

旧的非 ECS 路径（`CreateLayoutPanelSkeleton`）中，子容器注册是在创建控件时通过 `RegisterSubContainer` 立即完成的。ECS 路径中 `SubContainerRegistrar` 被禁用后，`contentContainer` 创建时没有注册到 `rootContainer.ContainerIndex`。

#### 不兼容点 2：`RegisterSubContainersFromEntityTree` 的依赖链

`RegisterSubContainersFromEntityTree` 依赖 `_integration.RootEntity` 和 `_integration.ControlPool` 来遍历 Entity 树。虽然 `RootEntity` 在 `LoadConfiguration` 后有效，但这个方法：

1. 只在 `ExecuteStage` **之后**调用一次
2. 依赖 Entity 树结构，如果 Entity 树与 WinForms 控件树不一致，可能遗漏注册
3. 没有处理 `contentContainer` 这种由 `MainForm.OnInitializeAsync` 回退路径创建的容器

---

## 2. 修复方案

### 2.1 方案 A（推荐）：恢复 `SubContainerRegistrar` + 简化 `RegisterSubContainersFromEntityTree`

**核心思路**：在 `ControlCreateSystem.Update()` 内部完成子容器注册，`RegisterSubContainersFromEntityTree` 作为补充兜底。

#### 修改 1：`EcsRenderPipelineAdapter.ExecuteCreateStageAsync`

```csharp
// 恢复 SubContainerRegistrar 委托
createSystem.SubContainerRegistrar = (parent, child) =>
{
    if (parent is WdEcsConfigContainer parentContainer
        && child is WdEcsConfigContainer childContainer)
    {
        parentContainer.RegisterSubContainer(childContainer);
    }
};

// 执行控件创建阶段
_integration.ExecuteStage(PipelineStage.ControlCreate);

// RegisterSubContainersFromEntityTree 保留作为补充兜底
// 处理 SubContainerRegistrar 遗漏的情况（如非 EcsConfigContainer 的容器）
RegisterSubContainersFromEntityTree(target);
```

#### 修改 2：`RegisterSubContainersFromEntityTree` 增加防重复

```csharp
private void RegisterSubContainersFromEntityTree(WdEcsConfigContainer target)
{
    var rootEntity = _integration.RootEntity;
    if (!rootEntity.IsValid)
        return;

    RegisterSubContainersRecursive(rootEntity, target);
}

private void RegisterSubContainersRecursive(Entity entity, WdEcsConfigContainer parentContainer)
{
    var children = _integration.EntityManager?.GetChildren(entity);
    if (children == null) return;

    foreach (var childEntity in children)
    {
        var control = _integration.ControlPool?.GetControl(childEntity);
        if (control is WdEcsConfigContainer childContainer
            && childContainer != parentContainer)
        {
            // ★ 防重复：检查是否已注册
            var existing = parentContainer.ContainerIndex.FindContainer(childContainer.Name);
            if (existing == null)
            {
                parentContainer.RegisterSubContainer(childContainer);
            }
        }

        RegisterSubContainersRecursive(childEntity,
            control is WdEcsConfigContainer ec ? ec : parentContainer);
    }
}
```

### 2.2 方案 B（更彻底）：在 `ControlFactory` 中完成注册

**核心思路**：在创建 `EcsConfigContainer` 时，立即将其注册到父容器的 `ContainerIndex`，不依赖 `SubContainerRegistrar` 或 `RegisterSubContainersFromEntityTree`。

```csharp
// ControlFactory 委托中
createSystem.ControlFactory = (type) =>
{
    if (string.Equals(type, "container", StringComparison.OrdinalIgnoreCase))
    {
        var container = new WdEcsConfigContainer();
        // 不在这里注册，由 SubContainerRegistrar 处理
        return container;
    }
    // ...
};
```

方案 B 需要 `ControlFactory` 知道父容器信息，而 `ControlFactory` 的签名是 `Func<string, Control>`，没有父容器参数。所以方案 A 更可行。

---

## 3. 进一步重构：ECS 体系完整性检查

### 3.1 当前 ECS 体系状态

| 组件 | 状态 | 说明 |
|------|------|------|
| `EcsWorld` / `EntityManager` | ✅ 就绪 | 核心 ECS 框架 |
| `ConfigToEntitySystem` | ✅ 就绪 | JSON → Entity 树 |
| `ControlCreateSystem` | ✅ 就绪 | 控件创建 + 注册 |
| `LayoutSystem` | ✅ 就绪 | 布局计算 |
| `StyleSystem` | ✅ 就绪 | 样式合并 |
| `EventBindingSystem` | ✅ 就绪 | 事件绑定 |
| `ControlSyncSystem` | ✅ 就绪 | 全量同步 |
| `ControlValueSyncSystem` | ✅ 新增 | 运行时值同步 |
| `ControlPool` | ✅ 就绪 | Entity↔Control 三向映射 |
| `ContainerIndex` | ✅ 就绪 | O(1) 容器查找 |
| `SubContainerRegistrar` | ❌ 被禁用 | 需恢复 |
| `RegisterSubContainersFromEntityTree` | ✅ 但需增强 | 需防重复 |

### 3.2 待修复清单

#### P0 — 运行时阻塞（立即修复）

- [ ] 恢复 `SubContainerRegistrar` 委托
- [ ] `RegisterSubContainersFromEntityTree` 增加防重复检查
- [ ] 运行时验证：启动应用，确认 `contentContainer` 可被 `FindSubContainer` 找到

#### P1 — 重构完整性（下次迭代）

- [ ] 移除 `RegisterSubContainersFromEntityTree` 方法（如果 `SubContainerRegistrar` 已覆盖所有场景）
- [ ] 统一 `EcsConfigContainer.RenderConfigAsync` 中的 `ContainerIndex.Clear()` 行为：清空后确保重新注册
- [ ] 为 `ControlFactory` 委托增加父容器参数（`Func<string, Control?, Control>`），使工厂方法能感知父容器上下文

#### P2 — 架构优化（远期）

- [ ] 将 `SubContainerRegistrar` 从委托提升为 `ControlCreateSystem` 的正式方法
- [ ] 将 `ParentFinder` 从委托提升为 `ControlCreateSystem` 的正式方法
- [ ] 考虑将 `ContainerIndex` 的维护逻辑从 `EcsContainer` 基类中抽离为独立的 `ContainerIndexService`

### 3.3 验证标准

```
P0 验证：
  rootContainer.FindSubContainer("contentContainer") != null  → ✅
  rootContainer.GetSubContainers().Any()                      → ✅
  MainForm 初始化完成，UI 正常显示                              → ✅

P1 验证：
  RenderConfigAsync 重复调用时 ContainerIndex 正确重建          → ✅
  子容器注册无重复条目                                         → ✅

P2 验证：
  ControlCreateSystem 不依赖外部委托，自身完成所有职责           → ✅
```

---

## 4. 架构决策记录

### 4.1 为什么 `SubContainerRegistrar` 不能由 `RegisterSubContainersFromEntityTree` 替代

`SubContainerRegistrar` 和 `RegisterSubContainersFromEntityTree` 虽然目标相同（注册子容器到 `ContainerIndex`），但：

1. **执行时机不同**：前者在控件创建时立即执行，后者在所有控件创建完成后执行
2. **数据源不同**：前者基于 WinForms 控件树（`parent.Controls.Add(control)` 之后），后者基于 Entity 树
3. **可靠性不同**：前者是"推"模式（创建即注册），后者是"拉"模式（事后遍历，可能遗漏）

两者应该是**互补关系**，而非替代关系。

### 4.2 为什么 `ContainerIndex.Clear()` 是安全的

`EcsConfigContainer.RenderConfigAsync` 中先 `ContainerIndex.Clear()` 再重新注册，这是正确的做法：

1. 每次 `RenderConfigAsync` 都是全新的渲染，旧索引应该清空
2. 重新注册由 `SubContainerRegistrar` + `RegisterSubContainersFromEntityTree` 双重保障
3. 如果某个子容器在重新渲染后不存在了，它的索引条目也应该被清除

### 4.3 后续重构方向

当前 ECS 体系已经覆盖了控件创建、布局、样式、事件绑定、值同步等核心流程，但仍有以下改进空间：

1. **委托 → 正式方法**：`ControlFactory`、`ParentFinder`、`SubContainerRegistrar` 三个委托应该成为 `ControlCreateSystem` 的正式方法或策略接口
2. **Entity 树 ↔ WinForms 树一致性**：确保 Entity 树和 WinForms 控件树始终保持一致，避免两棵树不同步的问题
3. **ContainerIndex 统一管理**：将 `ContainerIndex` 的维护逻辑集中到一处，避免分散在多个地方

---

## 5. 委托 → 策略接口重构（2026-07-20 实施）

### 5.1 重构内容

将 `ControlCreateSystem` 的三个委托属性（`ControlFactory`、`ParentFinder`、`SubContainerRegistrar`）提升为正式的 `IControlCreationStrategy` 策略接口。

#### 新增文件

| 文件 | 位置 | 说明 |
|------|------|------|
| `IControlCreationStrategy.cs` | `BasicMethod/ECS/Systems/` | 策略接口定义 |
| `EcsControlCreationStrategy.cs` | `FrontEndDriven/ECS/` | ECS 路径的策略实现 |

#### 修改文件

| 文件 | 修改内容 |
|------|---------|
| `ControlCreateSystem.cs` | 移除三个委托属性，构造函数接收 `IControlCreationStrategy` |
| `FrontEndEcsIntegration.cs` | `Initialize()` 中创建 `EcsControlCreationStrategy` 并注入 |
| `EcsRenderPipelineAdapter.cs` | 移除 `ExecuteCreateStageAsync` 中的委托注入代码 |
| `ECS.csproj` | 添加 `IControlCreationStrategy.cs` 编译项 |

### 5.2 架构变化

```
重构前：
  EcsRenderPipelineAdapter.ExecuteCreateStageAsync()
    → 设置 createSystem.ControlFactory = (type) => { ... }    // 委托注入
    → 设置 createSystem.ParentFinder = (entity) => { ... }    // 委托注入
    → 设置 createSystem.SubContainerRegistrar = null          // 委托注入
    → integration.ExecuteStage(ControlCreate)

重构后：
  FrontEndEcsIntegration.Initialize()
    → var strategy = new EcsControlCreationStrategy(em, pool)  // 策略创建
    → new ControlCreateSystem(em, pool, strategy)              // 构造函数注入

  ControlCreateSystem.Update()
    → _strategy.CreateControl(type)     // 策略方法调用
    → _strategy.FindParent(entity)      // 策略方法调用
    → _strategy.RegisterSubContainer()  // 策略方法调用
```

### 5.3 设计优势

1. **编译时类型安全**：委托是运行时绑定，策略接口是编译时契约
2. **职责集中**：所有 ECS 路径的控件创建逻辑集中在 `EcsControlCreationStrategy` 中
3. **可测试**：可以独立测试策略逻辑，也可以 mock 策略测试 `ControlCreateSystem`
4. **可扩展**：新增策略实现只需实现 `IControlCreationStrategy` 接口

### 5.4 后续优化方向

1. **Entity 树 ↔ WinForms 树一致性**：当前 `RegisterSubContainersFromEntityTree` 在 `ExecuteStage` 之后执行，与 `SubContainerRegistrar` 互补。后续可考虑将子容器注册逻辑也纳入策略接口，在 `ControlCreateSystem.Update()` 内部完成。
2. **ContainerIndex 统一管理**：`ContainerIndex` 的维护逻辑目前分散在 `EcsContainer` 基类、`EcsConfigContainer`、`RegisterSubContainersFromEntityTree` 中。后续可考虑抽离为独立的 `ContainerIndexService`。
3. **WinForms 树 ECS 重构**：见第 6 章分析。


