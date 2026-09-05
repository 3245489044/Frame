# ECS 控件创建调用链重构方案

> **日期：** 2026-07-20
> **背景：** `ConfigLoaded` 事件修复后，`OnRootContainerLoaded` 被触发，但 `FindSubContainer("contentContainer")` 返回 null，导致 UI 初始化失败。
> **根本原因：** ECS 控件创建调用链中，ControlPool 的三向映射（Entity↔controlName↔Control）未被注册，导致 `GetControl(Entity)` 永远返回 null，进而 `RegisterSubContainer` 无法执行。

---

## 一、当前调用链分析

### 1.1 完整调用链

```
EcsConfigContainer.RenderConfigAsync(config)
  → pipeline.ExecuteCreateStageAsync(this, config)          [EcsRenderPipelineAdapter]
    → _integration.LoadConfiguration(uiConfig)               [FrontEndEcsIntegration]
      → _configToEntitySystem.Execute(config)                [ConfigToEntitySystem]
        → ControlFactory.CreateFromConfig(config, parent)    [ControlFactory]
          → 添加 ControlHierarchyComponent { Parent = parentEntity }
          → 添加 ControlStateComponent { IsCreated = false }
    → _integration.ExecutePipeline()                         [FrontEndEcsIntegration]
      → _controlCreateSystem.Update()                        [ControlCreateSystem]
        → ControlCreated?.Invoke(entity, type, name)         [事件触发]
          → FrontEndEcsIntegration.ConnectEvents()            [中转]
            → ControlCreated?.Invoke(entity, type, name)     [事件传播]
              → EcsRenderPipelineAdapter.ConnectEvents()      [订阅者]
                → ControlCreateHandler(entity, type, name, parent) [委托]
                  → WdUIContainerManager 中设置的委托          [实际创建]
                    → 创建控件实例
                    → parent.Controls.Add(control)
                    → if (parent is EcsConfigContainer)
                        RegisterSubContainer(container)       [★ 需要 parent 不为 null]
        → state.IsCreated = true
      → _layoutSystem?.Update()
      → _styleSystem?.Update()
      → _eventBindingSystem?.Update()
      → _controlSyncSystem?.SyncAll()
```

### 1.2 问题定位

**问题 1：`ControlCreateSystem.Update()` 不注册 Control 引用到 ControlPool**

`ControlCreateSystem` 有两个方法：
- `Update()` — 遍历未创建的 Entity，触发 `ControlCreated` 事件
- `MarkCreated(Entity, string, Control)` — 注册三向映射到 ControlPool

但 `Update()` 中只触发了事件，**从未调用 `MarkCreated`**。

```csharp
// ControlCreateSystem.cs 第 39-59 行
public void Update()
{
    var entities = _entityManager.GetEntities<ControlTypeComponent, ControlStateComponent>();
    foreach (var entity in entities)
    {
        var state = _entityManager.GetComponent<ControlStateComponent>(entity);
        if (state.IsCreated) continue;

        var typeComp = _entityManager.GetComponent<ControlTypeComponent>(entity);
        ControlCreated?.Invoke(entity, typeComp.Type, typeComp.Name);  // 只触发事件
        state.IsCreated = true;  // 直接标记为已创建
        ...
    }
}
```

**问题 2：`ControlPool.GetControl(Entity)` 永远返回 null**

`ControlPool` 的 `_entityToName` 和 `_nameToControl` 字典只在 `Register(Entity, string, Control)` 三参数版本中写入，但**没有任何地方调用这个三参数版本**。

```csharp
// ControlPool.cs 第 177-183 行
public Control? GetControl(Entity entity)
{
    if (_entityToName.TryGetValue(entity, out var name))  // ← 永远为 false
        return GetControl(name);
    return null;
}
```

**问题 3：`EcsRenderPipelineAdapter.ConnectEvents()` 中 parent 始终为 null**

```csharp
// EcsRenderPipelineAdapter.cs 第 140-147 行
Control? parent = null;
if (...TryGetComponent<ControlHierarchyComponent>(entity, out var hierarchy) && hierarchy.Parent.IsValid)
{
    parent = _integration.ControlPool?.GetControl(hierarchy.Parent);  // ← 永远返回 null
}
ControlCreateHandler(entity, type, name, parent);  // ← parent 始终为 null
```

由于 parent 为 null，`WdUIContainerManager` 中的 `RegisterSubContainer` 无法执行：
```csharp
// WdUIContainerManager.cs 第 110 行
if (parent is EcsConfigContainer parentContainer)
{
    parentContainer.RegisterSubContainer(container);  // ← parent 为 null，不会执行
}
```

---

## 二、重构方案

### 2.1 核心思路

**把"创建"和"注册"整合为一条路径。** 当前调用链通过事件+委托绕了一大圈，创建和注册分离。重构后，`ControlCreateSystem.Update()` 直接完成所有工作：

1. 创建控件实例
2. 注册三向映射到 ControlPool
3. 添加到父控件
4. 注册子容器（如果是 EcsConfigContainer）

### 2.2 具体修改

#### 修改 1：`ControlCreateSystem` — 注入控件工厂委托，整合创建+注册

**文件：** `CsharpPrj/BasicMethod/ECS/Systems/ControlCreateSystem.cs`

```csharp
public class ControlCreateSystem
{
    private readonly EntityManager _entityManager;
    private readonly ControlPool _controlPool;

    /// <summary>
    /// 控件工厂委托 — 根据控件类型创建 WinForms 控件实例
    /// <para>签名：Control(string controlType)</para>
    /// </summary>
    public Func<string, Control>? ControlFactory { get; set; }

    /// <summary>
    /// 父控件查找委托 — 根据 Entity 获取父 Control
    /// <para>签名：Control?(Entity entity)</para>
    /// </summary>
    public Func<Entity, Control?>? ParentFinder { get; set; }

    /// <summary>
    /// 子容器注册委托 — 将子容器注册到父容器的索引
    /// <para>签名：void(Control parent, Control child)</para>
    /// </summary>
    public Action<Control, Control>? SubContainerRegistrar { get; set; }

    public void Update()
    {
        var entities = _entityManager.GetEntities<ControlTypeComponent, ControlStateComponent>();
        foreach (var entity in entities)
        {
            var state = _entityManager.GetComponent<ControlStateComponent>(entity);
            if (state.IsCreated) continue;

            var typeComp = _entityManager.GetComponent<ControlTypeComponent>(entity);

            // ★ 整合：创建控件
            var control = ControlFactory?.Invoke(typeComp.Type);
            if (control == null)
            {
                // 回退到事件触发（兼容旧逻辑）
                ControlCreated?.Invoke(entity, typeComp.Type, typeComp.Name);
                state.IsCreated = true;
                ...
                continue;
            }

            control.Name = typeComp.Name;

            // ★ 整合：注册三向映射到 ControlPool
            _controlPool.Register(entity, typeComp.Name, control);

            // ★ 整合：添加到父控件
            var parent = ParentFinder?.Invoke(entity);
            if (parent != null)
            {
                parent.Controls.Add(control);

                // ★ 整合：注册子容器
                SubContainerRegistrar?.Invoke(parent, control);
            }

            // 更新状态
            state.IsCreated = true;
            state.LastUpdated = DateTime.UtcNow;
            _entityManager.SetComponent(entity, state);
        }
    }
}
```

#### 修改 2：`EcsRenderPipelineAdapter` — 注入工厂委托，移除事件链

**文件：** `CsharpPrj/Operation/FrontEndDriven/ECS/EcsRenderPipelineAdapter.cs`

移除 `ControlCreateHandler` 委托和 `ConnectEvents()` 中的事件订阅逻辑，改为在 `ExecuteCreateStageAsync` 中注入工厂委托到 `ControlCreateSystem`。

```csharp
public class EcsRenderPipelineAdapter : IRenderPipeline, IDisposable
{
    // ★ 移除：不再需要外部 ControlCreateHandler 委托
    // public Action<Entity, string, string, Control>? ControlCreateHandler { get; set; }

    private void ConnectEvents()
    {
        // ★ 移除：不再订阅 ControlCreated 事件
        // _integration.ControlCreated += (entity, type, name) => { ... };
    }

    public Task ExecuteCreateStageAsync(object target, object config)
    {
        // ★ 注入工厂委托到 ControlCreateSystem
        var createSystem = _integration.ControlCreateSystem;
        if (createSystem != null)
        {
            createSystem.ControlFactory = (type) =>
            {
                // 通过 ControlTypeMap 创建控件
                return WdUISettings.Instance.ControlTypes.CreateControl(type);
            };

            createSystem.ParentFinder = (entity) =>
            {
                if (_integration.EntityManager != null &&
                    _integration.EntityManager.TryGetComponent<ControlHierarchyComponent>(entity, out var hierarchy) &&
                    hierarchy.Parent.IsValid)
                {
                    return _integration.ControlPool?.GetControl(hierarchy.Parent);
                }
                return null;
            };

            createSystem.SubContainerRegistrar = (parent, child) =>
            {
                if (parent is EcsConfigContainer container && child is ConfigContainer subContainer)
                {
                    container.RegisterSubContainer(subContainer);
                }
            };
        }

        return ExecuteCreateStageOnlyAsync(target, config);
    }
}
```

#### 修改 3：`WdUIContainerManager` — 移除 ControlCreateHandler 设置

**文件：** `CsharpPrj/Operation/FrontEndDriven/Implementation/WdUIContainerManager.cs`

移除 `ecsPipeline.ControlCreateHandler = ...` 的设置代码，因为控件创建逻辑已整合到 `ControlCreateSystem` 内部。

---

## 三、重构后调用链

```
EcsConfigContainer.RenderConfigAsync(config)
  → pipeline.ExecuteCreateStageAsync(this, config)
    → [注入工厂委托到 ControlCreateSystem]
    → _integration.LoadConfiguration(uiConfig)
      → ConfigToEntitySystem.Execute(config)
        → ControlFactory.CreateFromConfig(config, parent)
    → _integration.ExecutePipeline()
      → _controlCreateSystem.Update()
        → ControlFactory(type) → 创建控件实例
        → _controlPool.Register(entity, name, control)  ← ★ 整合：注册三向映射
        → ParentFinder(entity) → 获取父 Control
        → parent.Controls.Add(control)
        → SubContainerRegistrar(parent, child) → RegisterSubContainer
        → state.IsCreated = true
      → _layoutSystem?.Update()
      → _styleSystem?.Update()
      → _eventBindingSystem?.Update()
      → _controlSyncSystem?.SyncAll()
```

**关键变化：**
1. ❌ 移除事件链：`ControlCreated` → `FrontEndEcsIntegration` → `EcsRenderPipelineAdapter` → `ControlCreateHandler`
2. ✅ 整合为一条路径：`ControlCreateSystem.Update()` 内部完成创建+注册+添加+子容器注册
3. ✅ `ControlPool` 的三向映射在创建时立即注册，后续 `GetControl(Entity)` 正常工作
4. ✅ `RegisterSubContainer` 在 parent 有效时正常执行

---

## 四、影响范围

| 文件 | 修改类型 | 说明 |
|------|---------|------|
| `CsharpPrj/BasicMethod/ECS/Systems/ControlCreateSystem.cs` | 重构 | 注入工厂委托，整合创建+注册逻辑 |
| `CsharpPrj/Operation/FrontEndDriven/ECS/EcsRenderPipelineAdapter.cs` | 重构 | 移除 ControlCreateHandler，改为注入委托 |
| `CsharpPrj/Operation/FrontEndDriven/Implementation/WdUIContainerManager.cs` | 清理 | 移除 ControlCreateHandler 设置代码 |

---

## 五、验证要点

1. `ControlPool.GetControl(Entity)` 返回正确的 Control 引用
2. `EcsRenderPipelineAdapter.ConnectEvents()` 中 parent 不为 null
3. `RegisterSubContainer` 被正确调用
4. `FindSubContainer("contentContainer")` 返回有效的容器
5. UI 正常显示，不再出现 "MainLayout must contain a 'contentContainer'" 错误
