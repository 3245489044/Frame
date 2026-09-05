# ECS 容器统一重构方案

> 解决 `MainLayout must contain a 'contentContainer'` 错误的根本原因：ECS 框架与 WinForms 渲染管线不兼容，容器查找依赖渲染后的控件树。

---

## 1. 问题根因分析

### 1.1 错误链路

```
MainLayout.json
  → JsonTemplateProcessor.Process (模板预处理)
    → ConfigContainer.RenderConfigAsync (WinForms 渲染管线)
      → RenderPipelineOrchestrator.RenderFullAsync
        → IRenderPipeline.ExecuteCreateStageAsync
          → CreateLayoutPanelSkeleton (创建 WinForms 控件树)
            → type:"container" → ConfigContainer + RegisterSubContainer
              → _containerIndex.Add(childNode)  ← 依赖渲染管线正确处理
      → FileDrivenWdUIBridge.FindAndBindContainers
        → rootContainer.FindSubContainer("contentContainer")
          → _containerIndex.FindContainer(name)  ← 如果渲染未完成，返回 null
            → ControlFinder.FindByType<ConfigContainer>(rootContainer, "contentContainer")
              → 找不到 → throw "MainLayout must contain a 'contentContainer'"
```

### 1.2 核心矛盾

| 问题 | 描述 |
|------|------|
| **容器查找依赖渲染状态** | `FindSubContainer` 依赖 `_containerIndex`，而 `_containerIndex` 只在 `RegisterSubContainer` 时填充，后者只在 `CreateLayoutPanelSkeleton` 处理 `type:"container"` 时调用 |
| **两套并行的渲染管线** | `ConfigContainer` 使用 `RenderPipelineOrchestrator` + `IRenderPipeline`（WinForms 管线），`EcsConfigContainer` 使用 `EcsRenderPipelineAdapter` + `FrontEndEcsIntegration`（ECS 管线），两者互不兼容 |
| **JSON→Entity 转换分散** | `ConfigToEntityConverter`（BasicMethod）和 `CreateLayoutPanelSkeleton`（WdUI）都做 JSON→控件的转换，但方式不同 |
| **ECS 集成不完整** | `FrontEndEcsIntegration` 创建了 ECS World 和 System 链，但 `ConfigContainer` 仍然使用 WinForms 管线，ECS 管线未被实际使用 |

---

## 2. 重构目标

1. **统一 JSON→Entity 转换**：在 `UniversalMiddleware` 层完成，一处实现，多处复用
2. **容器查找不依赖渲染状态**：`FindSubContainer` 查询 Entity 树，而非渲染后的 `_containerIndex`
3. **统一渲染管线**：ECS System 链替代两套并行的渲染管线
4. **消除 fallback 逻辑**：不再需要 `ControlFinder.FindByType<ConfigContainer>` 回退

---

## 3. 架构设计

### 3.1 模块职责边界

```
┌─────────────────────────────────────────────────────────────────┐
│ UniversalMiddleware（通用中间件层）                                │
│                                                                  │
│ 现有职责：                                                        │
│ ├── JSON 处理（序列化/反序列化/验证/Path导航）                     │
│ ├── 管线（Pipeline）框架                                          │
│ ├── 容器抽象（IContainer/IConfigContainer）                       │
│ ├── 渲染管线基类（RenderPipelineBase）                             │
│ ├── 事件映射服务                                                  │
│ ├── 布局引擎                                                      │
│ └── 配置加载器                                                    │
│                                                                  │
│ 新增职责：                                                        │
│ ├── JsonToEntityConverter（JSON→Entity 转换）                     │
│ └── EcsContainer（基于 Entity 树的统一容器抽象）                   │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│ FrontEndDriven（前端驱动层）                                      │
│                                                                  │
│ 现有职责：                                                        │
│ ├── ECS 集成（FrontEndEcsIntegration）                           │
│ ├── ECS 容器（EcsConfigContainer）→ 合并到 EcsContainer          │
│ ├── ECS 渲染管线适配器 → 移除，由 ECS System 链替代               │
│ └── WdUI 容器管理器                                              │
│                                                                  │
│ 保留职责：                                                        │
│ └── ECS System 链（ControlCreate/Layout/Style/EventBinding/Sync）│
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│ WdUI（WinForms UI 层）                                           │
│                                                                  │
│ 现有职责：                                                        │
│ ├── ConfigContainer（具体容器实现）→ 继承 EcsContainer            │
│ ├── 控件工厂                                                      │
│ ├── 布局策略                                                      │
│ └── 事件绑定                                                      │
└─────────────────────────────────────────────────────────────────┘
```

### 3.2 数据流（重构后）

```
MainLayout.json
  │
  ▼
JsonTemplateProcessor (PlaceholderFramework)
  │  模板预处理（styles/composites 展开）
  ▼
UniversalMiddlewareEntry.JsonToEntityConverter (UniversalMiddleware)
  │  JSON→Entity 树（纯数据，无 UI 依赖）
  │  识别 type:"container" → ContainerComponent
  │  识别 type:"splitContainer" → SplitContainerComponent
  ▼
EcsContainer.LoadEntityTree (UniversalMiddleware)
  │  ContainerIndex 从 Entity 树派生（不依赖渲染状态）
  │  FindSubContainer → 查询 Entity 树中的 ContainerComponent
  ▼
FrontEndEcsIntegration.ExecutePipeline (FrontEndDriven)
  │  ECS System 链：
  │  1. ControlCreateSystem  ← 从 Entity 创建 WinForms 控件
  │  2. LayoutSystem         ← 计算布局
  │  3. StyleSystem          ← 应用样式
  │  4. EventBindingSystem   ← 绑定事件
  │  5. ControlSyncSystem    ← 同步到 UI
  ▼
EcsContainer.FindSubContainer("contentContainer")
  → 查询 Entity 树中的 ContainerComponent（始终可用，不依赖渲染状态）
```

---

## 4. 具体实施步骤

### 步骤 1：创建 `JsonToEntityConverter`（UniversalMiddleware）

**文件**：`CsharpPrj/Share/UniversalMiddleware/Conversion/JsonToEntityConverter.cs`

```csharp
namespace UniversalMiddleware.Conversion;

/// <summary>
/// JSON→Entity 统一转换器
/// <para>将 UIConfiguration 转换为 ECS Entity 树。</para>
/// <para>★ 这是唯一的 JSON→Entity 转换入口。</para>
/// <para>★ 纯数据转换，不依赖任何 UI 框架。</para>
/// </summary>
public class JsonToEntityConverter
{
    private readonly EntityManager _entityManager;
    private readonly ControlFactory _controlFactory;

    public JsonToEntityConverter(EntityManager entityManager, ControlFactory controlFactory)
    {
        _entityManager = entityManager;
        _controlFactory = controlFactory;
    }

    /// <summary>
    /// 将 UIConfiguration 转换为 Entity 树
    /// </summary>
    public Entity Convert(UIConfiguration config)
    {
        var rootEntity = _controlFactory.CreateFromConfig(new ControlConfig
        {
            Type = "form",
            Name = "root",
            Text = config.Form?.Title ?? "Root"
        }, Entity.Invalid);

        if (config.Views is { Count: > 0 })
        {
            foreach (var view in config.Views)
            {
                var viewEntity = ConvertView(view, rootEntity);
                AddChild(rootEntity, viewEntity);
            }
        }
        return rootEntity;
    }

    private Entity ConvertView(ViewConfig view, Entity parentEntity)
    {
        var viewEntity = _controlFactory.CreateFromConfig(new ControlConfig
        {
            Type = "view",
            Name = view.Name,
            Text = view.Name
        }, parentEntity);

        if (view.Controls is { Count: > 0 })
        {
            foreach (var control in view.Controls)
            {
                var childEntity = ConvertControl(control, viewEntity);
                AddChild(viewEntity, childEntity);
            }
        }
        return viewEntity;
    }

    private Entity ConvertControl(ControlConfig control, Entity parentEntity)
    {
        var entity = _controlFactory.CreateFromConfig(control, parentEntity);

        // ★ 识别 type:"container" → 附加 ContainerComponent
        if (control.Type == "container" || control.Container != null)
        {
            _entityManager.AddComponent(entity, new ContainerComponent
            {
                Name = control.Name,
                ContainerConfig = control.Container
            });
        }

        // ★ 识别 type:"splitContainer" → 附加 SplitContainerComponent
        if (control.Type == "splitContainer")
        {
            _entityManager.AddComponent(entity, new SplitContainerComponent
            {
                Orientation = control.Layout?.Orientation ?? "horizontal"
            });
        }

        if (control.Controls is { Count: > 0 })
        {
            foreach (var child in control.Controls)
            {
                var childEntity = ConvertControl(child, entity);
                AddChild(entity, childEntity);
            }
        }
        return entity;
    }

    private void AddChild(Entity parent, Entity child)
    {
        if (!_entityManager.IsEntityValid(parent) || !_entityManager.IsEntityValid(child))
            return;

        var hierarchy = _entityManager.GetComponent<ControlHierarchyComponent>(parent);
        hierarchy.Children.Add(child);
        _entityManager.SetComponent(parent, hierarchy);
    }
}
```

**新增 Component**：`CsharpPrj/Share/UniversalMiddleware/Conversion/Components/ContainerComponent.cs`

```csharp
namespace UniversalMiddleware.Conversion.Components;

/// <summary>
/// 容器组件 — 标记 Entity 为容器
/// <para>★ 框架抽象层：不依赖任何 UI 框架 ★</para>
/// </summary>
public class ContainerComponent
{
    /// <summary>容器名称</summary>
    public string Name { get; set; } = "";

    /// <summary>容器配置</summary>
    public ContainerConfig? ContainerConfig { get; set; }

    /// <summary>子容器 Entity 列表</summary>
    public List<Entity> SubContainers { get; set; } = new();
}
```

### 步骤 2：创建 `EcsContainer`（UniversalMiddleware）

**文件**：`CsharpPrj/Share/UniversalMiddleware/Container/EcsContainer.cs`

```csharp
namespace UniversalMiddleware.Container;

/// <summary>
/// 基于 Entity 树的统一容器抽象
/// <para>★ 替代 ConfigContainer（WdUI）和 EcsConfigContainer（FrontEndDriven）★</para>
/// <para>★ 容器查找不依赖渲染状态，直接查询 Entity 树 ★</para>
/// </summary>
public class EcsContainer : Panel, IContainer
{
    private readonly ContainerNode _containerNode;
    private readonly ContainerIndex _containerIndex = new();
    private Entity _rootEntity = Entity.Invalid;
    private EntityManager? _entityManager;

    /// <summary>ECS World 实例</summary>
    public EcsWorld? World { get; private set; }

    /// <summary>根 Entity</summary>
    public Entity RootEntity => _rootEntity;

    /// <summary>容器名称</summary>
    public string ContainerName => Name;

    // ========== IContainer 接口实现 ==========

    ContainerNode IContainer.Node => _containerNode;
    ContainerIndex IContainer.Index => _containerIndex;

    public EcsContainer()
    {
        _containerNode = new ContainerNode(Name, this);
        _containerIndex.Add(_containerNode);
    }

    /// <summary>
    /// 从 Entity 树加载容器
    /// <para>★ ContainerIndex 从 Entity 树派生，不依赖渲染状态 ★</para>
    /// </summary>
    public void LoadEntityTree(Entity rootEntity, EntityManager entityManager)
    {
        _rootEntity = rootEntity;
        _entityManager = entityManager;

        // 从 Entity 树构建 ContainerIndex
        RebuildIndexFromEntityTree();
    }

    /// <summary>
    /// 从 Entity 树重建 ContainerIndex
    /// <para>★ 核心方法：遍历 Entity 树中的 ContainerComponent，构建索引 ★</para>
    /// </summary>
    private void RebuildIndexFromEntityTree()
    {
        _containerIndex.Clear();
        _containerIndex.Add(_containerNode);

        if (_entityManager == null || !_rootEntity.IsValid)
            return;

        TraverseEntityTree(_rootEntity, _containerNode);
    }

    private void TraverseEntityTree(Entity entity, ContainerNode parentNode)
    {
        if (!_entityManager!.IsEntityValid(entity))
            return;

        // 检查是否有 ContainerComponent
        if (_entityManager.TryGetComponent<ContainerComponent>(entity, out var containerComp))
        {
            var childNode = parentNode.AddChild(containerComp.Name, this);
            _containerIndex.Add(childNode);
        }

        // 递归遍历子 Entity
        if (_entityManager.TryGetComponent<ControlHierarchyComponent>(entity, out var hierarchy))
        {
            foreach (var child in hierarchy.Children)
            {
                TraverseEntityTree(child, parentNode);
            }
        }
    }

    /// <summary>
    /// 查找子容器（O(1) 查找）
    /// <para>★ 查询 Entity 树中的 ContainerComponent，不依赖渲染状态 ★</para>
    /// </summary>
    public EcsContainer? FindSubContainer(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        return _containerIndex.FindContainer(name) as EcsContainer;
    }

    /// <summary>IConfigContainer 接口实现</summary>
    IConfigContainer? IConfigContainer.FindSubContainer(string name)
        => FindSubContainer(name);

    /// <summary>
    /// 创建子容器
    /// </summary>
    public EcsContainer CreateSubContainer(string name, ContainerConfig? config = null)
    {
        var container = new EcsContainer { Name = name };
        Controls.Add(container);
        var childNode = _containerNode.AddChild(name, container);
        _containerIndex.Add(childNode);
        return container;
    }

    /// <summary>IConfigContainer 接口实现</summary>
    IConfigContainer IConfigContainer.CreateSubContainer(string name, ContainerConfig? config)
        => CreateSubContainer(name, config);

    /// <summary>获取所有子容器</summary>
    public IEnumerable<EcsContainer> GetSubContainers()
    {
        foreach (var node in _containerNode.Children)
        {
            if (node.Container is EcsContainer container)
                yield return container;
        }
    }

    /// <summary>IConfigContainer 接口实现</summary>
    IEnumerable<IConfigContainer> IConfigContainer.GetSubContainers()
        => GetSubContainers().Cast<IConfigContainer>();
}
```

### 步骤 3：修改 `ConfigContainer`（WdUI）继承 `EcsContainer`

**文件**：`CsharpPrj/Operation/WdUI/UI/WdUI.Container.cs`

```diff
- public class ConfigContainer : Panel, IContainer
+ public class ConfigContainer : EcsContainer
```

**修改要点**：
- `ConfigContainer` 继承 `EcsContainer`，复用 Entity 树容器查找逻辑
- 移除 `_containerIndex` 字段（由 `EcsContainer` 提供）
- 移除 `FindSubContainer` 方法（由 `EcsContainer` 提供）
- `RenderConfigAsync` 中：
  - 先调用 `JsonToEntityConverter.Convert(config)` 生成 Entity 树
  - 再调用 `LoadEntityTree(rootEntity, entityManager)` 加载到容器
  - 最后调用 `FrontEndEcsIntegration.ExecutePipeline()` 执行 ECS System 链

### 步骤 4：修改 `FrontEndEcsIntegration`（FrontEndDriven）

**文件**：`CsharpPrj/Operation/FrontEndDriven/ECS/FrontEndEcsIntegration.cs`

**修改要点**：
- 新增 `LoadFromJson(UIConfiguration config)` 方法
- 内部使用 `JsonToEntityConverter` 转换 JSON→Entity
- 将 Entity 树加载到 `EcsContainer`
- 执行 ECS System 链

```csharp
/// <summary>
/// 从 JSON 配置加载并执行完整管线
/// </summary>
public void LoadFromJson(UIConfiguration config, EcsContainer container)
{
    // 1. JSON → Entity 树（UniversalMiddleware 层）
    var rootEntity = _jsonToEntityConverter.Convert(config);

    // 2. 加载 Entity 树到容器（UniversalMiddleware 层）
    container.LoadEntityTree(rootEntity, _entityManager!);

    // 3. 执行 ECS System 链（FrontEndDriven 层）
    ExecutePipeline();
}
```

### 步骤 5：简化 `FileDrivenWdUIBridge.FindAndBindContainers`

**文件**：`FileDriven/FileDriven.UI/WinForms/Services/FileDrivenWdUIBridge.cs`

```diff
- private static ConfigContainer FindAndRegisterContainer(
-     ConfigContainer rootContainer, string containerName, string logPrefix)
- {
-     var container = rootContainer.FindSubContainer(containerName);
-     if (container != null)
-         return container;
- 
-     // fallback 诊断逻辑（移除）
-     // ControlFinder.FindByType<ConfigContainer>(rootContainer, containerName) ...
-     throw new InvalidOperationException($"MainLayout must contain a '{containerName}'");
- }
```

**简化后**：
```csharp
private static ConfigContainer FindAndRegisterContainer(
    ConfigContainer rootContainer, string containerName, string logPrefix)
{
    var container = rootContainer.FindSubContainer(containerName);
    if (container != null)
        return container;

    throw new InvalidOperationException($"MainLayout must contain a '{containerName}'");
}
```

### 步骤 6：移除冗余代码

| 文件 | 操作 | 替代 |
|------|------|------|
| `CsharpPrj/BasicMethod/ECS/Conversion/ConfigToEntityConverter.cs` | 删除 | 合并到 `JsonToEntityConverter` |
| `CsharpPrj/Operation/FrontEndDriven/ECS/EcsConfigContainer.cs` | 删除 | 合并到 `EcsContainer` |
| `CsharpPrj/Operation/FrontEndDriven/ECS/EcsRenderPipelineAdapter.cs` | 删除 | ECS System 链直接替代 |
| `CsharpPrj/Operation/WdUI/UI/CreateLayoutPanelSkeleton.cs` 中 `type:"container"` 分支 | 删除 | ECS System 统一处理 |
| `FileDriven/FileDriven.UI/WinForms/Services/FileDrivenWdUIBridge.cs` 中 fallback 逻辑 | 简化 | 直接抛出异常 |

---

## 5. 文件清单

### 新增文件

| 文件 | 模块 | 说明 |
|------|------|------|
| `CsharpPrj/Share/UniversalMiddleware/Conversion/JsonToEntityConverter.cs` | UniversalMiddleware | JSON→Entity 统一转换器 |
| `CsharpPrj/Share/UniversalMiddleware/Conversion/Components/ContainerComponent.cs` | UniversalMiddleware | 容器组件（ECS Component） |
| `CsharpPrj/Share/UniversalMiddleware/Conversion/Components/SplitContainerComponent.cs` | UniversalMiddleware | 分割容器组件（可选） |
| `CsharpPrj/Share/UniversalMiddleware/Container/EcsContainer.cs` | UniversalMiddleware | 基于 Entity 树的统一容器抽象 |

### 修改文件

| 文件 | 修改内容 |
|------|----------|
| `CsharpPrj/Operation/WdUI/UI/WdUI.Container.cs` | `ConfigContainer` 继承 `EcsContainer`，移除重复的容器查找逻辑 |
| `CsharpPrj/Operation/FrontEndDriven/ECS/FrontEndEcsIntegration.cs` | 新增 `LoadFromJson` 方法，整合 `JsonToEntityConverter` |
| `FileDriven/FileDriven.UI/WinForms/Services/FileDrivenWdUIBridge.cs` | 简化 `FindAndRegisterContainer`，移除 fallback 逻辑 |
| `FileDriven/FileDriven.UI/Core/MainForm.cs` | 移除 fallback 相关的诊断日志 |

### 删除文件

| 文件 | 原因 |
|------|------|
| `CsharpPrj/BasicMethod/ECS/Conversion/ConfigToEntityConverter.cs` | 合并到 `JsonToEntityConverter` |
| `CsharpPrj/Operation/FrontEndDriven/ECS/EcsConfigContainer.cs` | 合并到 `EcsContainer` |
| `CsharpPrj/Operation/FrontEndDriven/ECS/EcsRenderPipelineAdapter.cs` | ECS System 链直接替代 |

---

## 6. 风险与注意事项

### 6.1 兼容性风险

- `ConfigContainer` 继承自 `EcsContainer` 后，所有现有代码中对 `ConfigContainer` 的引用仍然有效
- `EcsContainer` 实现了 `IContainer` 和 `IConfigContainer` 接口，与现有接口兼容
- `WdUIContainerManager` 中的 `ConcurrentDictionary<string, ConfigContainer>` 仍然有效

### 6.2 性能考虑

- Entity 树遍历只在 `LoadEntityTree` 时执行一次，后续 `FindSubContainer` 是 O(1) 操作
- `JsonToEntityConverter` 的递归深度受 JSON 配置层级限制，不会出现栈溢出

### 6.3 测试验证

1. 验证 `MainLayout.json` 加载后 `FindSubContainer("contentContainer")` 返回正确结果
2. 验证 `FindSubContainer("sideContainer")` 返回正确结果
3. 验证 ECS System 链正确创建 WinForms 控件
4. 验证事件绑定正常工作
5. 验证 `ClearSideContainer` 正常工作

---

## 7. 实施顺序

```
第 1 步：创建 ContainerComponent（UniversalMiddleware）
  ↓
第 2 步：创建 JsonToEntityConverter（UniversalMiddleware）
  ↓
第 3 步：创建 EcsContainer（UniversalMiddleware）
  ↓
第 4 步：修改 ConfigContainer 继承 EcsContainer（WdUI）
  ↓
第 5 步：修改 FrontEndEcsIntegration（FrontEndDriven）
  ↓
第 6 步：简化 FileDrivenWdUIBridge（FileDriven）
  ↓
第 7 步：删除冗余代码
  ↓
第 8 步：编译验证 + 运行测试
```
