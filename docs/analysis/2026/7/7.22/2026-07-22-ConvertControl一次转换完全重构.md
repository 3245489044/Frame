# ConvertControl 一次转换完全重构

> **日期**：2026-07-22
> **状态**：待实施
> **关联问题**：容器 `contentContainer` 未在 Entity 树中找到（ERR-8）

---

## 1. 问题根因

### 1.1 错误日志

```
[ERROR] 初始化失败: 容器 'contentContainer' 未在 Entity 树中找到。
RootEntity=Entity(1:0)。请检查配置中是否包含名为 'contentContainer' 的容器。
```

### 1.2 调用链

```
MainForm.OnInitializeAsync()
  → ConfigLoader.LoadConfigAsync(_rootContainer, MainLayoutPath)
    → _rootContainer.LoadConfigAsync(config)          // EcsConfigContainer
      → RenderConfigAsync(config)
        → InitializeRenderPipeline()                  // 创建 EcsRenderPipeline + EntityTreeService
        → ExecuteCreateStageAsync(this, config)
          → ExecuteCreateStageInternal()
            → Initialize()                            // 创建 ECS 核心
            → LoadConfiguration(uiConfig)             // 构建 Entity 树（配置→Entity）
            → RegisterRootContainer(targetControl)    // 注册根容器到 ControlPool
            → _controlCreateSystem?.Update()          // 创建控件（Entity→Control）
        → ExecuteLayoutStageAsync(this)
    → _rootContainer.ConfigLoaded 事件触发
  → MainForm.OnRootContainerLoaded()
    → FileDrivenWdUIBridge.FindAndBindContainers()
      → ContainerFinder.FindAndBindMainContainers()
        → rootContainer.FindSubContainer("contentContainer")
          → TreeService.FindContainerByName(rootEntity, "contentContainer")
            → FindEntityByName() → FindEntityRecursive()
              → 递归遍历 Entity 树，比较 ControlTypeComponent.Name 字符串
```

### 1.3 核心缺陷

**`ConvertControl` 分两阶段执行，导致 Entity 树和 Control 树之间存在时间窗口不一致：**

| 阶段 | 操作 | 完成后状态 |
|------|------|-----------|
| 1. `ConfigToEntityConverter.Convert()` | 配置→Entity | Entity 树完整，但 Control 未创建，ControlPool 为空 |
| 2. `ControlCreateSystem.Update()` | Entity→Control | Control 创建完成，ControlPool 已注册 |

**三个具体问题：**

1. **分阶段转换**：配置→Entity 和 Entity→Control 分离，不是原子操作
2. **字符串名称查找**：`FindSubContainer` 通过 `FindEntityRecursive` 递归遍历 Entity 树比较名称字符串，没有利用 Entity 引用或 ControlPool 的直接映射
3. **查找路径冗余**：`FindContainerByName` 的路径是 `Entity树遍历 → 找到Entity → ControlPool.GetControl(Entity)`，而 `ControlPool` 本身已经维护了 `名称↔Control` 的映射，完全不需要遍历 Entity 树

---

## 2. 重构方案

### 2.1 核心思路

**`ConvertControl` 一次转换完全**：在 `ConfigToEntityConverter.ConvertControl` 中同步完成 Entity 创建 + Control 创建 + ControlPool 注册，不再分两阶段。

### 2.2 变更清单

#### 2.2.1 `ConfigToEntityConverter` — 一次转换完全

**当前**：只创建 Entity，不创建 Control

```csharp
public Entity ConvertControl(ControlConfig control, Entity parentEntity, Entity containerAncestor)
{
    var entity = _controlFactory.CreateFromConfig(control, parentEntity);
    // 添加 Component...
    // 递归子控件...
    return entity;
}
```

**重构后**：同步创建 Entity + Control + 注册 ControlPool

```csharp
public Entity ConvertControl(ControlConfig control, Entity parentEntity, Entity containerAncestor)
{
    // 1. 创建 Entity
    var entity = _controlFactory.CreateFromConfig(control, parentEntity);
    
    // 2. 创建 Control（通过 ControlStrategyRegistry）
    var winformsControl = CreateControlInstance(control.Type);
    
    // 3. 注册到 ControlPool（Entity↔ControlName↔Control 三向映射）
    if (winformsControl != null)
    {
        _controlPool.Register(entity, control.Name, winformsControl);
    }
    
    // 4. 添加 Component
    // ...
    
    // 5. 递归子控件
    // ...
    
    return entity;
}
```

**变更**：
- 构造函数增加 `ControlPool` 参数
- 新增 `CreateControlInstance(string type)` 方法，委托到 `ControlStrategyRegistry.Create`
- `ConvertControl` 中同步创建 Control 并注册到 ControlPool
- `ConvertView` 同理（View 对应 Panel 控件）
- `CreateSplitterPanelEntity` 同理（SplitterPanel 对应 Panel 控件）

#### 2.2.2 `ControlCreateSystem` — 简化为后处理

**当前**：遍历 Entity 树创建所有控件

**重构后**：仅处理控件创建后的后处理（如布局、样式、事件绑定）

```csharp
public void Update()
{
    // 阶段 1：已移除（控件创建已移到 ConfigToEntityConverter）
    // 阶段 2：跳过 SplitterPanel（不变）
    // 阶段 3：后处理（布局、样式、事件绑定）
}
```

**变更**：
- 移除阶段 1 的控件创建逻辑
- 保留阶段 2（SplitterPanel 跳过）
- 保留阶段 3（后处理）

#### 2.2.3 `EcsRenderPipeline` — 移除 `_controlCreateSystem?.Update()` 的控件创建职责

**当前**：

```csharp
private Task ExecuteCreateStageInternal(object target, object config)
{
    Initialize();
    LoadConfiguration(uiConfig);     // 只创建 Entity
    RegisterRootContainer(targetControl);
    _controlCreateSystem?.Update();  // 创建控件
    return Task.CompletedTask;
}
```

**重构后**：

```csharp
private Task ExecuteCreateStageInternal(object target, object config)
{
    Initialize();
    LoadConfiguration(uiConfig);     // 一次完成 Entity + Control 创建
    RegisterRootContainer(targetControl);
    _controlCreateSystem?.Update();  // 仅后处理
    return Task.CompletedTask;
}
```

**变更**：
- `LoadConfiguration` 内部调用 `ConfigToEntitySystem.Execute`，后者调用 `_converter.Convert`，现在一次完成
- `RegisterRootContainer` 在 Control 已注册的情况下行为不变
- `_controlCreateSystem?.Update()` 不再创建控件，仅做后处理

#### 2.2.4 `EcsContainer.FindSubContainer` — 直接查 ControlPool

**当前**：

```csharp
public IConfigContainer FindSubContainer(string name)
{
    // ...
    var container = TreeService.FindContainerByName(rootEntity, name);
    // ...
}
```

**重构后**：

```csharp
public IConfigContainer FindSubContainer(string name)
{
    // ...
    // 直接通过 ControlPool 查找，不再遍历 Entity 树
    var control = _controlPool.GetControl(name);
    if (control is IConfigContainer container)
        return container;
    
    throw new InvalidOperationException($"容器 '{name}' 未找到。");
}
```

**变更**：
- 移除对 `TreeService.FindContainerByName` 的依赖
- 直接使用 `ControlPool.GetControl(name)` 通过名称查找
- `ControlPool` 已维护 `名称↔Control` 映射，O(1) 查找

#### 2.2.5 `EntityTreeService` — 简化或移除

**当前**：提供 `FindEntityByName`、`FindControlByName`、`FindContainerByName`、`FindAllContainers`

**重构后**：
- `FindContainerByName` 不再需要（由 `ControlPool.GetControl` 替代）
- `FindControlByName` 不再需要（由 `ControlPool.GetControl` 替代）
- `FindEntityByName` 保留（可能用于其他场景）
- `FindAllContainers` 保留（用于 `GetSubContainers`）

**变更**：
- `FindContainerByName` 改为直接查 ControlPool
- `FindControlByName` 改为直接查 ControlPool
- `DumpEntityTree` 保留用于诊断

---

## 3. 影响范围

### 3.1 需修改的文件

| 文件 | 变更类型 | 说明 |
|------|---------|------|
| `CsharpPrj/BasicMethod/ECS/Conversion/ConfigToEntityConverter.cs` | 重构 | 增加 ControlPool 参数，同步创建 Control |
| `CsharpPrj/Operation/FrontEndDriven/ControlRender/Creation/Systems/ControlCreateSystem.cs` | 简化 | 移除控件创建逻辑 |
| `CsharpPrj/Operation/FrontEndDriven/RenderPipeline/EcsRenderPipeline.cs` | 调整 | 管线执行顺序微调 |
| `CsharpPrj/Share/UniversalMiddleware/Container/EcsContainer.cs` | 简化 | FindSubContainer 直接查 ControlPool |
| `CsharpPrj/Operation/FrontEndDriven/ControlLookup/Services/EntityTreeService.cs` | 简化 | FindContainerByName/FindControlByName 改为查 ControlPool |

### 3.2 不涉及的模块

- `ControlStrategyRegistry` — 不变，仍作为控件创建工厂
- `ControlPool` — 不变，仍作为三向映射存储
- `EntityManager` — 不变
- `ControlFactory` — 不变
- `ContainerFinder` — 不变（调用方不变）
- `MainForm` — 不变（调用方不变）

### 3.3 风险点

1. **Control 创建时机提前**：`ConvertControl` 中创建 Control 时，父控件的 Control 可能尚未创建（递归顺序问题）。需要确保递归创建时先创建父控件，再创建子控件。
2. **Control 的 Parent 设置**：当前 `ControlCreateSystem` 中通过 `FindParent` 设置 `control.Parent = parentControl`。重构后需要在创建时同步设置 Parent。
3. **SplitterPanel 特殊处理**：SplitterPanel 的 Control 不应独立创建，应由 SplitContainer 自身管理。需要在 `ConvertControl` 中跳过 `splitterpanel` 类型的 Control 创建。

---

## 4. 实施步骤

### Step 1: 修改 `ConfigToEntityConverter`

- 构造函数增加 `ControlPool controlPool` 参数
- 新增 `CreateControlInstance(string type)` 方法
- `ConvertControl` 中同步创建 Control 并注册到 ControlPool
- `ConvertView` 中同步创建 Panel Control
- `CreateSplitterPanelEntity` 中跳过 Control 创建（SplitterPanel 由 SplitContainer 管理）
- 递归时先创建父控件，再创建子控件，并设置 Parent

### Step 2: 修改 `ControlCreateSystem`

- 移除阶段 1 的控件创建逻辑
- 保留阶段 2（SplitterPanel 跳过）
- 保留阶段 3（后处理）

### Step 3: 修改 `EcsRenderPipeline`

- 确保 `ConfigToEntitySystem` 的构造函数传递 `ControlPool`
- 管线执行顺序不变，但 `LoadConfiguration` 现在一次完成

### Step 4: 修改 `EcsContainer.FindSubContainer`

- 直接通过 `ControlPool.GetControl(name)` 查找
- 移除对 `TreeService.FindContainerByName` 的依赖

### Step 5: 修改 `EntityTreeService`

- `FindContainerByName` 改为查 ControlPool
- `FindControlByName` 改为查 ControlPool

---

## 5. 验证标准

1. **启动不报错**：`contentContainer` 在 `OnRootContainerLoaded` 时能被找到
2. **容器查找正确**：`FindSubContainer("contentContainer")` 返回正确的 `EcsConfigContainer` 实例
3. **控件树完整**：所有控件正确创建并添加到 WinForms 控件树
4. **Entity 树完整**：Entity 树结构与配置一致
5. **ControlPool 完整**：所有 Entity↔Control 映射正确注册
6. **回归测试**：现有功能（布局、样式、事件绑定）不受影响
