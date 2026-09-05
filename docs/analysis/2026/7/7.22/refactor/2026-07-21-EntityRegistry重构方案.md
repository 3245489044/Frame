# EntityRegistry 重构方案

> **日期：** 2026-07-21
> **状态：** 待实施
> **背景：** `MainLayout must contain a 'contentContainer'` 错误根因分析

---

## 1. 问题背景

### 1.1 错误链路

```
FindAndRegisterContainer("contentContainer")
  → EcsContainer.FindSubContainer("contentContainer")
    → _containerIndex.FindContainer("contentContainer") → null
    → 抛出 "MainLayout must contain a 'contentContainer'"
```

### 1.2 根因

**`ConfigToEntityConverter.ConvertControl` 不处理 `Panel1Controls`/`Panel2Controls`**

在 `MainLayout.json` 中：
- `contentContainer`（type: "container"）在 `contentSplitter` 的 `panel1Controls` 中
- `sideContainer`（type: "container"）在 `mainSplitter` 的 `panel2Controls` 中

但 `ConfigToEntityConverter.ConvertControl` 第 123 行**只处理了 `control.Controls`**：
```csharp
if (control.Controls is { Count: > 0 })
```

没有处理 `control.Panel1Controls` 和 `control.Panel2Controls`。

### 1.3 后果

1. `contentContainer` 和 `sideContainer` 的 **Entity 不存在于 Entity 树中**
2. `ControlCreateSystem.Update()` 不会为它们创建 WinForms 控件
3. `RegisterSubContainersRecursive` 遍历 Entity 树时找不到它们
4. 但它们的 WinForms 控件是通过**旧 WdUI 路径**创建的，存在于 WinForms 控件树中
5. `EcsContainer.FindSubContainer` 只查 `ContainerIndex`（O(1) 索引），找不到
6. 所以 `FindAndRegisterContainer` 抛出异常

---

## 2. 深层问题：Entity 管理分散

当前 Entity 的创建、注册、索引分散在 5 个地方：

| 组件 | 职责 | 问题 |
|------|------|------|
| `ConfigToEntityConverter` | 将 UIConfiguration → Entity 树 | 不处理 `Panel1Controls`/`Panel2Controls` |
| `ControlPool` | Entity↔ControlName↔Control 三向映射 | 注册分散在多个地方 |
| `ContainerIndex` | 容器名称→容器节点 O(1) 索引 | 注册分散在多个地方 |
| `RegisterSubContainersRecursive` | 遍历 Entity 树注册子容器 | 独立遍历，与转换器脱节 |
| `EcsControlCreationStrategy` | 创建控件时注册到 ContainerIndex | 与转换器逻辑重复 |

---

## 3. 重构目标

1. **统一 Entity 管理入口** — 新增 `EntityRegistry` 类，集中管理 Entity 的创建、注册、查找
2. **补齐 Panel1Controls/Panel2Controls** — 使 `contentContainer`/`sideContainer` 的 Entity 被正确创建
3. **消除分散的注册逻辑** — 将 `RegisterSubContainersRecursive` 合并到 `EntityRegistry`
4. **纯 Entity 树方案** — 所有容器查找和注册完全基于 Entity 树 + ContainerIndex，不依赖 WinForms 控件树

---

## 4. 设计方案

### 4.1 新增 `EntityRegistry` 类

**文件：** `CsharpPrj/Operation/FrontEndDriven/ECS/EntityRegistry.cs`

```csharp
/// <summary>
/// Entity 注册中心 — Entity 管理的唯一入口
///
/// 职责：
/// 1. 将 UIConfiguration 完整转换为 Entity 树（含 Panel1Controls/Panel2Controls）
/// 2. 注册根容器到 ControlPool + ContainerIndex
/// 3. 遍历 Entity 树注册所有子容器到 ContainerIndex
/// 4. 统一容器/控件查找
///
/// 设计原则：
/// - 纯 Entity 树方案，不依赖 WinForms 控件树
/// - 所有容器查找通过 ContainerIndex O(1) 完成
/// - 消除分散在 FrontEndEcsIntegration 和 EcsControlCreationStrategy 中的注册逻辑
/// </summary>
public class EntityRegistry
{
    private readonly EntityManager _entityManager;
    private readonly ControlPool _controlPool;
    private readonly ConfigToEntityConverter _converter;
    private EcsConfigContainer? _rootContainer;
    private Entity _rootEntity;

    public EntityRegistry(EntityManager entityManager, ControlPool controlPool,
        ConfigToEntityConverter converter) { ... }

    /// 将 UIConfiguration 转换为 Entity 树
    public Entity Convert(UIConfiguration config) { ... }

    /// 注册根容器到 ControlPool + ContainerIndex
    public void RegisterRoot(EcsConfigContainer rootContainer, Entity rootEntity) { ... }

    /// 遍历 Entity 树注册所有子容器到 ContainerIndex
    /// 通过 ContainerAncestorComponent.IsContainer 判断容器身份
    public void RegisterAllSubContainers() { ... }

    /// 通过 ContainerIndex 查找容器（O(1)）
    public EcsContainer? FindContainer(string name) { ... }

    /// 通过 ControlPool 查找控件
    public Control? FindControl(string name) { ... }

    /// 清理所有注册
    public void Clear() { ... }
}
```

### 4.2 增强 `ConfigToEntityConverter.ConvertControl`

**文件：** `CsharpPrj/BasicMethod/ECS/Conversion/ConfigToEntityConverter.cs`

在现有 `control.Controls` 处理之后，增加 `Panel1Controls` 和 `Panel2Controls` 的处理：

```csharp
// 处理 SplitContainer 的 Panel1Controls
if (control.Panel1Controls is { Count: > 0 })
{
    foreach (var child in control.Panel1Controls)
    {
        var childAncestor = isContainer ? entity : ancestor;
        var childEntity = ConvertControl(child, entity, childAncestor);
        AddChild(entity, childEntity);
    }
}

// 处理 SplitContainer 的 Panel2Controls
if (control.Panel2Controls is { Count: > 0 })
{
    foreach (var child in control.Panel2Controls)
    {
        var childAncestor = isContainer ? entity : ancestor;
        var childEntity = ConvertControl(child, entity, childAncestor);
        AddChild(entity, childEntity);
    }
}
```

### 4.3 简化 `FrontEndEcsIntegration`

**文件：** `CsharpPrj/Operation/FrontEndDriven/ECS/FrontEndEcsIntegration.cs`

- 新增 `EntityRegistry` 属性
- `RegisterRootContainer` 委托给 `EntityRegistry.RegisterRoot`
- `RegisterAllSubContainers` 委托给 `EntityRegistry.RegisterAllSubContainers`
- 删除 `RegisterSubContainersRecursive` 私有方法

### 4.4 简化 `EcsRenderPipelineAdapter`

**文件：** `CsharpPrj/Operation/FrontEndDriven/ECS/EcsRenderPipelineAdapter.cs`

- `ExecuteCreateStageAsync` 通过 `EntityRegistry` 完成注册
- 删除注释中的 `RegisterSubContainersFromControls` 引用

### 4.5 保持 `EcsContainer.FindSubContainer` 不变

**文件：** `CsharpPrj/Share/UniversalMiddleware/Container/EcsContainer.cs`

保持纯 `ContainerIndex` 查找，不增加 WinForms 控件树回退：

```csharp
public EcsContainer? FindSubContainer(string name)
{
    if (string.IsNullOrEmpty(name)) return null;
    return _containerIndex.FindContainer(name) as EcsContainer;
}
```

---

## 5. 修复链路

```
ConfigToEntityConverter 补齐 Panel1Controls/Panel2Controls
  → contentContainer/sideContainer 的 Entity 被正确创建
  → ContainerAncestorComponent.IsContainer = true 被正确设置
  → EntityRegistry.RegisterAllSubContainers() 遍历 Entity 树
  → 通过 ContainerAncestorComponent 找到容器 Entity
  → 注册到祖先容器的 ContainerIndex
  → FindAndRegisterContainer("contentContainer") 通过 ContainerIndex 找到
  → 不再抛出异常
```

---

## 6. 影响范围

| 文件 | 变更类型 | 说明 |
|------|---------|------|
| `CsharpPrj/BasicMethod/ECS/Conversion/ConfigToEntityConverter.cs` | **修改** | 增加 Panel1Controls/Panel2Controls 处理 |
| `CsharpPrj/Operation/FrontEndDriven/ECS/EntityRegistry.cs` | **新建** | 统一 Entity 注册中心 |
| `CsharpPrj/Operation/FrontEndDriven/ECS/FrontEndEcsIntegration.cs` | **修改** | 委托给 EntityRegistry，删除 RegisterSubContainersRecursive |
| `CsharpPrj/Operation/FrontEndDriven/ECS/EcsRenderPipelineAdapter.cs` | **修改** | 通过 EntityRegistry 注册 |
| `CsharpPrj/Share/UniversalMiddleware/Container/EcsContainer.cs` | **不变** | 保持纯 ContainerIndex 查找 |

### 不修改的文件

- `FileDrivenWdUIBridge.cs` — 通过 EntityRegistry 间接使用
- `MainForm.cs` — 不变
- `EcsControlCreationStrategy.cs` — 不变
- `WdUIContainerManager.cs` — 不变

---

## 7. 实施步骤

1. **修改 `ConfigToEntityConverter.ConvertControl`** — 增加 Panel1Controls/Panel2Controls 处理
2. **新建 `EntityRegistry`** — 实现统一注册中心
3. **修改 `FrontEndEcsIntegration`** — 委托给 EntityRegistry，删除 RegisterSubContainersRecursive
4. **修改 `EcsRenderPipelineAdapter`** — 通过 EntityRegistry 注册
5. **编译验证** — `dotnet build` 确认无编译错误
6. **运行测试** — 执行 `math_browser_simple_test.json` 验证修复

---

## 8. 验证标准

- [ ] `dotnet build` 编译通过，无警告
- [ ] `MainLayout must contain a 'contentContainer'` 错误不再出现
- [ ] `contentContainer` 和 `sideContainer` 的 Entity 存在于 Entity 树中
- [ ] `ContainerIndex` 能通过名称查找到 `contentContainer` 和 `sideContainer`
- [ ] `FindAndRegisterContainer("contentContainer")` 返回正确的容器实例
- [ ] UI 正常显示，ConfigSelector 加载成功
