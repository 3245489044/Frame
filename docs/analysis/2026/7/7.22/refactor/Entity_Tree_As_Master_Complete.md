# Entity 树成为唯一主树 — 完整重构方案

> **版本：** v1.0
> **更新日期：** 2026-07-22
> **维护者：** WorkFlowPrj Team
>
> **前置文档：** [`ECS_Based_Control_Refactoring.md`](./ECS_Based_Control_Refactoring.md)（SplitterPanel 修复 + Entity 树查找服务引入）
>
> **核心目标：** 消除系统中所有对 WinForms 控件树的查找依赖，Entity 树成为**唯一**主树。

---

## 目录

- [Entity 树成为唯一主树 — 完整重构方案](#entity-树成为唯一主树--完整重构方案)
  - [目录](#目录)
  - [第一部分：现状分析](#第一部分现状分析)
    - [1.1 已完成的工作](#11-已完成的工作)
    - [1.2 遗留问题](#12-遗留问题)
    - [1.3 问题根源](#13-问题根源)
  - [第二部分：方案总览](#第二部分方案总览)
    - [2.1 核心原则](#21-核心原则)
    - [2.2 架构变更](#22-架构变更)
    - [2.3 文件变更清单](#23-文件变更清单)
  - [第三部分：详细设计](#第三部分详细设计)
    - [3.1 阶段 A：删除冗余组件](#31-阶段-a删除冗余组件)
      - [A1. 删除 `IdentityComponent.cs`](#a1-删除-identitycomponentcs)
    - [3.2 阶段 B：`EcsContainer` 完全迁移到 Entity 树](#32-阶段-becscontainer-完全迁移到-entity-树)
      - [B1. `FindControlByName` 改为委托到 `IEntityTreeService`](#b1-findcontrolbyname-改为委托到-ientitytreeservice)
      - [B2. `GetSubContainers` 改为基于 Entity 树](#b2-getsubcontainers-改为基于-entity-树)
      - [B3. 删除 `FindRecursive` 私有方法](#b3-删除-findrecursive-私有方法)
    - [3.3 阶段 C：`IEntityTreeService` 接口扩展](#33-阶段-cientitytreeservice-接口扩展)
      - [C1. 新增 `FindAllContainers` 方法](#c1-新增-findallcontainers-方法)
    - [3.4 阶段 D：`ControlRegistryAwareFinder` 接入 Entity 树](#34-阶段-dcontrolregistryawarefinder-接入-entity-树)
      - [D1. 注入 `IEntityTreeService`](#d1-注入-ientitytreeservice)
      - [D2. 移除 WinForms 控件树回退](#d2-移除-winforms-控件树回退)
    - [3.5 阶段 E：`ControlFinder.cs` 标记废弃并迁移调用方](#35-阶段-econtrolfindercs-标记废弃并迁移调用方)
      - [E1. 标记 `FindRecursive` 为 `[Obsolete]`](#e1-标记-findrecursive-为-obsolete)
      - [E2. 迁移 `WdUI.ViewManager`](#e2-迁移-wduiviewmanager)
      - [E3. 迁移 `LayoutMediator`](#e3-迁移-layoutmediator)
    - [3.6 阶段 F：集成测试与验证](#36-阶段-f集成测试与验证)
  - [第四部分：实施步骤](#第四部分实施步骤)
    - [4.1 推荐实施顺序](#41-推荐实施顺序)
    - [4.2 回滚方案](#42-回滚方案)
  - [第五部分：验证标准](#第五部分验证标准)
    - [5.1 编译验证](#51-编译验证)
    - [5.2 运行时验证](#52-运行时验证)
    - [5.3 回归验证](#53-回归验证)

---

## 第一部分：现状分析

### 1.1 已完成的工作

前置重构（`ECS_Based_Control_Refactoring.md`）已完成：

| 阶段 | 内容 | 状态 |
|------|------|------|
| 1 | 新增 `SplitterPanelComponent` | ✅ |
| 2 | 修改 `ConfigToEntityConverter`（Panel1Controls/Panel2Controls 创建中间 Entity） | ✅ |
| 3 | 修改 `ControlCreateSystem`（处理 splitterpanel 类型） | ✅ |
| 4 | 修改 `EcsControlCreationStrategy.FindParent` | ✅ |
| 5 | 新增 `IEntityTreeService` 接口 + `EntityTreeService` 实现 | ✅ |
| 6 | 修改 `EcsContainer.FindSubContainer`（注入 IEntityTreeService，移除回退） | ✅ |
| 7 | 修改 `ContainerFinder`（委托到 EntityTreeService） | ✅ |

### 1.2 遗留问题

重构后，系统中仍有 **4 条路径** 依赖 WinForms 控件树查找：

```
路径 1：EcsContainer.FindControlByName → FindRecursive(this, name)
         ↓
         IConfigContainer 接口暴露的方法，但实现仍基于 WinForms 树

路径 2：ControlRegistryAwareFinder.FindByName
         → ControlPool(O(1)) → IControlRegistry → ControlFinder.FindRecursive(form, name)
         ↓
         策略链第 3 步仍回退到 WinForms 树

路径 3：ControlFinder.FindRecursive / FindAcrossOpenForms / FindInForm
         ↓
         整个工具类基于 WinForms 树，被多处调用

路径 4：WdUI.ViewManager 中的 ControlFinder.FindRecursive
         ↓
         视图管理器的控件查找仍依赖 WinForms 树
```

### 1.3 问题根源

`IEntityTreeService` 接口定义在 `UniversalMiddleware.Abstractions`，但它的实现 `EntityTreeService` 在 `FrontEndDriven` 层。这导致：

1. **`UniversalMiddleware` 层**（如 `EcsContainer`）只能通过接口调用，但 `FindControlByName` 和 `GetSubContainers` 尚未迁移
2. **`WdUI` 层**（如 `ControlFinder`、`ControlRegistryAwareFinder`、`ViewManager`）**完全没有接入 Entity 树查找**
3. **`IdentityComponent`** 作为冗余组件未被清理

---

## 第二部分：方案总览

### 2.1 核心原则

1. **Entity 树是唯一主树** — 所有控件/容器查找必须基于 Entity 树
2. **WinForms 控件树是纯渲染视图** — 仅用于显示和用户交互，不承担查找职责
3. **查找路径必须唯一** — 不允许存在多条查找路径（Entity 树 vs WinForms 树）
4. **无回退逻辑** — Entity 树查找失败即抛异常，不降级到 WinForms 树

### 2.2 架构变更

```
重构前查找路径（多条路径，不一致）：
  FindSubContainer → IEntityTreeService ✅
  FindControlByName → WinForms FindRecursive ❌
  GetSubContainers → WinForms Controls 遍历 ❌
  ControlRegistryAwareFinder → ControlPool → WinForms FindRecursive ❌
  ViewManager.FindControl → WinForms FindRecursive ❌

重构后查找路径（唯一路径，一致）：
  所有查找 → IEntityTreeService → EntityManager(Entity 树) → ControlPool(映射)
```

### 2.3 文件变更清单

| 文件 | 变更类型 | 说明 |
|------|---------|------|
| `FrontEndDriven/ECS/Components/IdentityComponent.cs` | **删除** | 与 `ControlTypeComponent` 重复 |
| `UniversalMiddleware/Abstractions/IEntityTreeService.cs` | **修改** | 新增 `FindAllContainers` 方法 |
| `UniversalMiddleware/Container/EcsContainer.cs` | **修改** | `FindControlByName`/`GetSubContainers` 迁移到 Entity 树；删除 `FindRecursive` |
| `FrontEndDriven/Implementation/ControlRegistryAwareFinder.cs` | **修改** | 注入 `IEntityTreeService`，移除 WinForms 回退 |
| `FrontEndDriven/WdUI/Framework/Shared/ControlFinder.cs` | **修改** | 标记 `FindRecursive` 为 `[Obsolete]` |
| `FrontEndDriven/WdUI/UI/WdUI.ViewManager.cs` | **修改** | 注入 `IEntityTreeService` |
| `FrontEndDriven/WdUI/Layout/LayoutMediator.cs` | **修改** | 注入 `IEntityTreeService` |
| `FrontEndDriven/ECS/Services/EntityTreeService.cs` | **修改** | 实现 `FindAllContainers` |

---

## 第三部分：详细设计

### 3.1 阶段 A：删除冗余组件

#### A1. 删除 `IdentityComponent.cs`

**文件：** `CsharpPrj/Operation/FrontEndDriven/ECS/Components/IdentityComponent.cs`

**分析：** 该组件包含 `Name`、`Type`、`Text`、`Enabled` 四个字段，与 `TConfig.ECS.Components.ControlTypeComponent` 完全重复。重构阶段 0 已经删除了精简版 `ControlTypeComponent`，统一使用 `TConfig` 版本，但 `IdentityComponent` 作为另一个"精简版"被遗漏了。

**搜索引用：** 未在任何 System 中被查询或使用（`GetEntities<IdentityComponent>` 等），仅定义了 struct。

**操作：** 直接删除文件。

---

### 3.2 阶段 B：`EcsContainer` 完全迁移到 Entity 树

#### B1. `FindControlByName` 改为委托到 `IEntityTreeService`

**当前代码（第 294-299 行）：**
```csharp
public Control? FindControlByName(string name)
{
    if (string.IsNullOrEmpty(name)) return null;
    return FindRecursive(this, name);
}
```

**修改后：**
```csharp
/// <summary>
/// 按名称查找控件
/// <para>★ 2026-07-22 重构：基于 Entity 树查找，不再依赖 WinForms 控件树。</para>
/// <para>   委托到 IEntityTreeService，与 FindSubContainer 一致。</para>
/// </summary>
/// <param name="name">控件名称</param>
/// <returns>找到的 Control，未找到返回 null</returns>
/// <exception cref="ArgumentNullException">name 为 null 或空时抛出</exception>
/// <exception cref="InvalidOperationException">TreeService 未注入或根 Entity 不可用时抛出</exception>
public Control? FindControlByName(string name)
{
    if (string.IsNullOrEmpty(name))
        throw new ArgumentNullException(nameof(name), "控件名称不能为空");

    if (TreeService == null)
        throw new InvalidOperationException(
            $"EntityTreeService 未注入，无法查找控件 '{name}'。请确保在 InitializeRenderPipeline 中创建了 EntityTreeService。");

    var rootEntity = GetRootEntity();
    if (rootEntity == null)
        throw new InvalidOperationException(
            $"根 Entity 不可用，无法查找控件 '{name}'。请确保渲染管线已初始化。");

    return TreeService.FindControlByName(rootEntity, name);
}
```

#### B2. `GetSubContainers` 改为基于 Entity 树

**当前代码（第 277-284 行）：**
```csharp
public IEnumerable<IConfigContainer> GetSubContainers()
{
    foreach (Control c in Controls)
    {
        if (c is IConfigContainer container)
            yield return container;
    }
}
```

**修改后：**
```csharp
/// <summary>
/// 获取所有子容器
/// <para>★ 2026-07-22 重构：基于 Entity 树查找，不再依赖 WinForms 控件树。</para>
/// </summary>
/// <returns>子容器集合</returns>
/// <exception cref="InvalidOperationException">TreeService 未注入或根 Entity 不可用时抛出</exception>
public IEnumerable<IConfigContainer> GetSubContainers()
{
    if (TreeService == null)
        throw new InvalidOperationException(
            "EntityTreeService 未注入，无法获取子容器列表。请确保在 InitializeRenderPipeline 中创建了 EntityTreeService。");

    var rootEntity = GetRootEntity();
    if (rootEntity == null)
        throw new InvalidOperationException(
            "根 Entity 不可用，无法获取子容器列表。请确保渲染管线已初始化。");

    return TreeService.FindAllContainers(rootEntity);
}
```

#### B3. 删除 `FindRecursive` 私有方法

**当前代码（第 301-310 行）：**
```csharp
private static Control? FindRecursive(Control parent, string name)
{
    foreach (Control c in parent.Controls)
    {
        if (c.Name == name) return c;
        var found = FindRecursive(c, name);
        if (found != null) return found;
    }
    return null;
}
```

**操作：** 直接删除此方法。`FindControlByName` 已改为委托到 `IEntityTreeService`，不再需要 WinForms 树递归查找。

---

### 3.3 阶段 C：`IEntityTreeService` 接口扩展

#### C1. 新增 `FindAllContainers` 方法

**当前接口：**
```csharp
public interface IEntityTreeService
{
    Control? FindControlByName(object rootEntity, string name);
    IConfigContainer? FindContainerByName(object rootEntity, string name);
}
```

**修改后：**
```csharp
public interface IEntityTreeService
{
    /// <summary>通过 Entity 名称查找对应的 WinForms 控件</summary>
    Control? FindControlByName(object rootEntity, string name);

    /// <summary>通过 Entity 名称查找对应的 IConfigContainer</summary>
    IConfigContainer? FindContainerByName(object rootEntity, string name);

    /// <summary>获取根 Entity 下的所有子容器</summary>
    IEnumerable<IConfigContainer> FindAllContainers(object rootEntity);
}
```

**`EntityTreeService` 实现：**
```csharp
/// <summary>
/// 获取根 Entity 下的所有子容器
/// <para>★ 2026-07-22 新增：替代 WinForms 控件树的 Controls 遍历。</para>
/// </summary>
public IEnumerable<IConfigContainer> FindAllContainers(object rootEntity)
{
    if (rootEntity is not Entity entity || !entity.IsValid)
        yield break;

    if (!_entityManager.TryGetComponent<ControlHierarchyComponent>(entity, out var hierarchy))
        yield break;

    foreach (var child in hierarchy.Children)
    {
        var control = _controlPool.GetControl(child);
        if (control is IConfigContainer container)
            yield return container;
    }
}
```

---

### 3.4 阶段 D：`ControlRegistryAwareFinder` 接入 Entity 树

#### D1. 注入 `IEntityTreeService`

**当前构造函数：**
```csharp
public class ControlRegistryAwareFinder : IControlFinder, TConfig.IControlFinder
{
    private readonly IControlRegistry _registry;

    public ControlRegistryAwareFinder(IControlRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }
}
```

**修改后：**
```csharp
public class ControlRegistryAwareFinder : IControlFinder, TConfig.IControlFinder
{
    private readonly IControlRegistry _registry;
    private readonly IEntityTreeService? _entityTreeService;

    public ControlRegistryAwareFinder(IControlRegistry registry, IEntityTreeService? entityTreeService = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _entityTreeService = entityTreeService;
    }
}
```

#### D2. 移除 WinForms 控件树回退

**当前 `FindByName` 策略链：**
```
1. ControlPool(O(1)) ✅
2. IControlRegistry ✅
3. ControlFinder.FindRecursive(form, name) ❌ WinForms 树
4. 跨 Form 查找 ❌ WinForms 树
```

**修改后策略链：**
```
1. ControlPool(O(1)) ✅
2. IEntityTreeService.FindControlByName ✅ Entity 树
3. IControlRegistry ✅
```

**修改后代码：**
```csharp
public object? FindByName(string name)
{
    if (string.IsNullOrWhiteSpace(name))
    {
        FrontEndDrivenLog.Log("ControlRegistryAwareFinder_FindByName_null_or_empty");
        return null;
    }

    // 策略 1：优先通过 ControlPool 进行 O(1) 查找
    var controlPool = WdUIComposer.ControlPool;
    if (controlPool != null)
    {
        var control = controlPool.GetControl(name);
        if (control != null)
        {
            FrontEndDrivenLog.Log("ControlRegistryAwareFinder_Found_in_ControlPool", new { name });
            return control;
        }
    }

    // ★ 2026-07-22 重构：策略 2 — 通过 IEntityTreeService 查找 Entity 树
    if (_entityTreeService != null)
    {
        // 遍历所有 Form 的根 Entity
        foreach (Form form in Application.OpenForms)
        {
            if (form is IEntityTreeRoot root)
            {
                var control = _entityTreeService.FindControlByName(root.GetRootEntity(), name);
                if (control != null)
                {
                    FrontEndDrivenLog.Log("ControlRegistryAwareFinder_Found_in_EntityTree", new { name });
                    return control;
                }
            }
        }
    }

    // 策略 3：先查注册中心
    var registered = _registry.Find(name);
    if (registered != null)
    {
        FrontEndDrivenLog.Log("ControlRegistryAwareFinder_Found_in_registry", new { name });
        return registered;
    }

    FrontEndDrivenLog.Log("ControlRegistryAwareFinder_Not_found", new { name, openFormsCount = Application.OpenForms.Count });
    return null;
}
```

> **注意：** 需要新增 `IEntityTreeRoot` 接口，由 `EcsConfigContainer` 实现，提供 `GetRootEntity()` 方法。

---

### 3.5 阶段 E：`ControlFinder.cs` 标记废弃并迁移调用方

#### E1. 标记 `FindRecursive` 为 `[Obsolete]`

```csharp
[Obsolete("请使用 IEntityTreeService.FindControlByName 替代，Entity 树是主树，WinForms 树是渲染视图。")]
public static Control? FindRecursive(Control parent, string name, RecursionPolicy? policy = null)
```

同时标记以下方法：
- `FindAcrossOpenForms` → `[Obsolete("请使用 IEntityTreeService.FindControlByName 替代")]`
- `FindInForm` → `[Obsolete("请使用 IEntityTreeService.FindControlByName 替代")]`
- `FindByName` → `[Obsolete("请使用 IEntityTreeService.FindControlByName 替代")]`

保留以下不涉及查找的工具方法（不移除）：
- `FindForm` — 基于 Parent 链查找 Form，与 Entity 树无关
- `IsDescendantOf` — 基于 Parent 链判断，与 Entity 树无关
- `GetDepth` — 基于 Parent 链计算深度，与 Entity 树无关
- `FindNearestConfigContainer` — 基于 Parent 链查找，与 Entity 树无关

#### E2. 迁移 `WdUI.ViewManager`

**当前代码（查找控件）：**
```csharp
return ControlFinder.FindRecursive(form, controlName);
```

**修改后：**
```csharp
// ★ 2026-07-22 重构：通过 IEntityTreeService 查找，不再依赖 WinForms 控件树
if (form is IEntityTreeRoot root && _entityTreeService != null)
{
    return _entityTreeService.FindControlByName(root.GetRootEntity(), controlName) as T;
}
return null;
```

`ViewManager` 需要注入 `IEntityTreeService`：
```csharp
public class ViewManager : IViewManager<Form>, IUIController
{
    private readonly IEntityTreeService? _entityTreeService;

    public ViewManager(IEntityTreeService? entityTreeService = null)
    {
        _entityTreeService = entityTreeService;
    }
}
```

#### E3. 迁移 `LayoutMediator`

**当前代码：**
```csharp
var found = WdUI.Framework.Shared.ControlFinder.FindRecursive(_control, name);
return found != null ? new WinFormsElementAdapter(found) : null;
```

**修改后：**
```csharp
// ★ 2026-07-22 重构：通过 IEntityTreeService 查找
if (_entityTreeService != null && _control is IEntityTreeRoot root)
{
    var found = _entityTreeService.FindControlByName(root.GetRootEntity(), name);
    return found != null ? new WinFormsElementAdapter(found) : null;
}
return null;
```

`LayoutMediator` 需要注入 `IEntityTreeService`：
```csharp
public class LayoutMediator
{
    private readonly IEntityTreeService? _entityTreeService;

    public LayoutMediator(Control control, IEntityTreeService? entityTreeService = null)
    {
        _control = control;
        _entityTreeService = entityTreeService;
    }
}
```

---

### 3.6 阶段 F：集成测试与验证

**验证项：**

| 验证项 | 预期结果 | 验证方式 |
|--------|---------|---------|
| 编译通过 | 无编译错误 | `dotnet build` |
| `FindSubContainer("contentContainer")` | 返回 `EcsConfigContainer` | 启动应用 |
| `FindControlByName("contentContainer")` | 返回 `EcsConfigContainer` | 启动应用 |
| `GetSubContainers()` | 返回所有子容器 | 启动应用 |
| `ControlRegistryAwareFinder.FindByName("outputTextBox")` | 返回 `TextBox` | 启动应用 |
| `ControlFinder.FindRecursive` | 编译警告 `[Obsolete]` | 编译检查 |
| 容器不存在时 | 抛出 `InvalidOperationException` | 启动应用 |

---

## 第四部分：实施步骤

### 4.1 推荐实施顺序

| 优先级 | 阶段 | 内容 | 预估工时 | 风险 |
|--------|------|------|---------|------|
| **P0** | A1 | 删除 `IdentityComponent.cs` | 5 min | 低 |
| **P0** | C1 | `IEntityTreeService` 新增 `FindAllContainers` | 10 min | 低 |
| **P0** | B1-B3 | `EcsContainer` 完全迁移（`FindControlByName` + `GetSubContainers` + 删除 `FindRecursive`） | 20 min | **中** |
| **P0** | D1-D2 | `ControlRegistryAwareFinder` 接入 Entity 树 | 20 min | **中** |
| **P1** | E1 | `ControlFinder.cs` 标记 `[Obsolete]` | 10 min | 低 |
| **P1** | E2 | `WdUI.ViewManager` 迁移 | 15 min | 中 |
| **P1** | E3 | `LayoutMediator` 迁移 | 10 min | 中 |
| **P1** | F | 集成测试 | 30 min | - |

**推荐实施顺序：** A1 → C1 → B1+B2+B3 → D1+D2 → E1 → E2 → E3 → F

### 4.2 回滚方案

每个阶段完成后立即编译验证，确保可回滚：

| 阶段 | 回滚方式 |
|------|---------|
| A1 | `git checkout -- IdentityComponent.cs` |
| C1 | `git checkout -- IEntityTreeService.cs` |
| B1-B3 | `git checkout -- EcsContainer.cs` |
| D1-D2 | `git checkout -- ControlRegistryAwareFinder.cs` |
| E1 | `git checkout -- ControlFinder.cs` |
| E2 | `git checkout -- WdUI.ViewManager.cs` |
| E3 | `git checkout -- LayoutMediator.cs` |

---

## 第五部分：验证标准

### 5.1 编译验证

```bash
dotnet build "d:\Project\Program\WorkFlowPrj\CsharpPrj\MainProj.sln"
```

- ✅ 无编译错误
- ✅ `ControlFinder.FindRecursive` 调用处产生 `[Obsolete]` 警告

### 5.2 运行时验证

启动应用后验证：

- ✅ `contentContainer` 和 `sideContainer` 正确绑定
- ✅ `FindControlByName("outputTextBox")` 返回 `TextBox`
- ✅ `GetSubContainers()` 返回所有子容器
- ✅ `ControlRegistryAwareFinder.FindByName("outputTextBox")` 返回 `TextBox`
- ✅ 容器不存在时抛出 `InvalidOperationException`

### 5.3 回归验证

- ✅ SplitContainer 的子控件正确显示在 Panel1/Panel2 中
- ✅ 普通容器（无 SplitContainer）正常工作
- ✅ 普通控件（Button/TextBox 等）正常工作
- ✅ 嵌套 SplitContainer 正常工作
