# FrontEndDriven 控件逻辑模块化重构方案

> **版本**: v1.0  
> **日期**: 2026-07-22  
> **范围**: `CsharpPrj/Operation/FrontEndDriven` 中的控件逻辑  
> **目标**: 将分散在 ~40 个文件中的控件逻辑按功能职责划分为 3 个独立大模块，每个模块可单独验证

---

## 1. 现状分析

### 1.1 当前目录结构

```
FrontEndDriven/
├── Abstractions/                          # 接口定义
│   └── IEventBindingService.cs            # 事件绑定与 UI 线程调度接口
├── Container/                             # 容器管理
│   ├── ConfigContainerExtensions.cs       # 容器扩展方法
│   ├── ContainerFinder.cs                # 容器查找
│   ├── ContainerRegistry.cs              # 容器注册（IControlRegistry 实现）
│   └── EcsConfigContainer.cs             # ECS 配置容器（核心容器）
├── Diagnostics/
│   └── FrontEndDrivenLog.cs              # 日志
├── ECS/                                   # ECS 核心（控件生命周期）
│   ├── Components/                       # 11 个 Component 定义
│   │   ├── CheckStateComponent.cs
│   │   ├── ControlHierarchyComponent.cs
│   │   ├── ControlStateComponent.cs
│   │   ├── DateTimeValueComponent.cs
│   │   ├── EventComponent.cs
│   │   ├── ListSelectionComponent.cs
│   │   ├── NumericValueComponent.cs
│   │   ├── PositionComponent.cs
│   │   ├── SelectionComponent.cs
│   │   ├── StyleComponent.cs
│   │   └── TextComponent.cs
│   ├── Services/
│   │   └── EntityTreeService.cs          # Entity 树查找服务
│   ├── Systems/                          # 8 个 System
│   │   ├── ControlCreateSystem.cs        # 控件创建
│   │   ├── ControlSyncSystem.cs          # 布局/样式同步
│   │   ├── ControlValueSyncSystem.cs     # 运行时值同步
│   │   ├── EventBindingSystem.cs         # 事件绑定
│   │   ├── IControlCreationStrategy.cs   # 创建策略接口
│   │   ├── LayoutSystem.cs               # 布局计算
│   │   ├── SplitContainerSystem.cs       # SplitContainer 分割管理
│   │   └── StyleSystem.cs                # 样式合并
│   ├── ControlCreationServiceAdapter.cs  # WdUI 适配器
│   ├── ControlStrategyRegistry.cs        # 控件策略注册表
│   ├── EcsControlCreationStrategy.cs     # ECS 创建策略实现
│   ├── EcsRenderPipeline.cs              # 渲染管线编排
│   ├── EntityRegistry.cs                 # Entity 注册中心
│   ├── IControlStrategy.cs               # 控件策略接口
│   └── StandardControlStrategies.cs      # 34 种标准控件注册
├── Implementation/                       # 服务实现
│   ├── CompositeValueWriter.cs           # 组合值写入器
│   ├── ControlRegistryAwareFinder.cs     # 策略链控件查找器
│   ├── EventSubscriptionManager.cs       # 事件订阅生命周期管理
│   ├── NullEventBindingService.cs        # 空事件绑定服务
│   ├── UnifiedControlService.cs          # 统一控件服务
│   └── WinFormsControlValueWriter.cs     # 17+ 种控件值读写器
├── Loading/
│   └── ConfigLoader.cs                   # 配置加载器
├── Registration/
│   └── ControlServiceCollection.cs       # 控件服务统一注册入口
├── Utils/
│   ├── TypeConversionHelper.cs           # 类型转换
│   └── UiThreadHelper.cs                # UI 线程调度
├── WdUI/                                  # 旧体系（待废弃/迁移）
│   ├── Abstractions/
│   │   └── IControlCreationService.cs    # 控件创建服务接口
│   ├── Diagnostics/
│   │   └── WdUILog.cs                    # WdUI 日志
│   ├── Framework/
│   │   ├── ConfigurationFramework.cs     # 配置框架
│   │   ├── ControlFactoryFramework.cs    # 控件工厂框架（含 ControlConfigApplier）
│   │   └── Shared/
│   │       ├── ControlFinder.cs          # 通用控件查找器
│   │       └── ControlIndex.cs           # O(1) 控件索引
│   ├── Layout/
│   │   ├── LayoutMediator.cs             # 布局中介者
│   │   └── RenderSession.cs              # 渲染会话
│   ├── UI/
│   │   ├── WdUI.Configuration.cs         # WdUI 配置扩展
│   │   ├── WdUI.ControlFactory.cs        # 旧控件工厂
│   │   ├── WdUI.EventManagement.cs       # 旧事件管理
│   │   ├── WdUI.Layout.cs                # 旧布局（已 Obsolete）
│   │   ├── WdUI.Management.cs            # 管理功能
│   │   ├── WdUI.Utilities.cs             # 工具方法
│   │   └── WdUI.ViewManager.cs           # 旧视图管理
│   ├── WinForms/
│   │   └── FrameworkContextWinFormsExtensions.cs
│   ├── WdUI.Settings.cs                  # WdUI 设置
│   └── WdUIComposer.cs                   # WdUI 组合根
├── WinForms/                              # WinForms 事件绑定
│   ├── Abstractions/
│   │   └── IWinFormsEventRegistrar.cs    # 事件注册器接口
│   ├── EventBinding/
│   │   └── UIEventBinder.cs              # UI 事件绑定器
│   ├── Registration/
│   │   └── WinFormsEventRegistrar.cs     # 控件事件注册器
│   └── Utils/
│       ├── UILogger.cs                   # UI 日志
│       └── WinFormsControlMetadataStore.cs # 控件元数据存储
├── FrontEndDriven.csproj
├── FrontEndDrivenConstants.cs
└── GlobalUsings.cs
```

### 1.2 核心问题

1. **职责交叉**：`WdUI/Framework/ControlFactoryFramework.cs` 中的 `ControlConfigApplier` 既负责控件创建又负责属性应用，与 ECS 的 `ControlCreateSystem` + `StyleSystem` 职责重叠
2. **新旧并存**：WdUI 旧体系（`WdUI.Layout.cs`、`WdUI.EventManagement.cs`、`WdUI.ControlFactory.cs`）与 ECS 新体系（`LayoutSystem`、`EventBindingSystem`、`ControlStrategyRegistry`）功能重复
3. **依赖混乱**：`UnifiedControlService` 同时依赖 `IControlFinder`、`IControlValueAccessor`、`IControlRegistry`、`IEventBindingService` 四个接口，耦合度过高
4. **验证困难**：没有模块边界，无法单独测试某个功能领域

---

## 2. 目标目录结构

重构后的目录结构，3 个模块独立目录，命名空间与目录一致：

```
FrontEndDriven/
├── ControlRender/            # 模块 1：控件渲染（创建 + 布局 + 样式）
├── ControlInteraction/       # 模块 2：控件交互（事件 + 值同步）
├── ControlLookup/            # 模块 3：控件查找与注册
├── RenderPipeline/           # 编排层：渲染管线
├── Shared/                   # 共享工具（跨模块通用）
├── Obsolete/                 # 旧体系废弃文件
└── FrontEndDriven.csproj
```

---

## 3. 模块划分方案

### 3.1 模块总览

```
┌─────────────────────────────────────────────────────────────────────┐
│                        RenderPipeline（编排层）                       │
│                                                                     │
│  ┌──────────────────────────────┐  ┌──────────────────────────────┐ │
│  │      ControlRender           │  │    ControlInteraction        │ │
│  │  ┌──────────┐ ┌───────────┐  │  │  ┌──────────┐ ┌──────────┐  │ │
│  │  │ Creation │ │ Layout    │  │  │  │ Event    │ │ Value    │  │ │
│  │  │          │ │ + Style   │  │  │  │ Binding  │ │ Sync     │  │ │
│  │  │ 创建控件  │ │ 布局+样式  │  │  │  │ 事件绑定  │ │ 值同步    │  │ │
│  │  └──────────┘ └───────────┘  │  │  └──────────┘ └──────────┘  │ │
│  └──────────────┬───────────────┘  └──────────────┬───────────────┘ │
│                 │                                  │                │
│  ┌──────────────▼──────────────────────────────────▼───────────────┐│
│  │                     ControlLookup                               ││
│  │  策略链查找（ControlPool→EntityTree→Registry）+ O(1)索引+容器管理  ││
│  └─────────────────────────────────────────────────────────────────┘│
└─────────────────────────────────────────────────────────────────────┘
```

---

### 3.2 模块 1：ControlRender（控件渲染）

**命名空间**：`FrontEndDriven.ControlRender`

**职责**：控件的全生命周期渲染——创建实例、计算布局、合并样式

**边界**：
- 输入：控件类型名称、布局配置、样式配置
- 输出：已创建、已布局、已应用样式的控件
- 不依赖：事件系统、值同步系统

**目标目录结构**：

```
ControlRender/
├── Creation/                            # 控件创建子模块
│   ├── IControlStrategy.cs              # 控件创建策略接口
│   ├── ControlStrategyRegistry.cs       # 策略注册表（ConcurrentDictionary）
│   ├── StandardControlStrategies.cs     # 34 种标准控件注册
│   ├── EcsControlCreationStrategy.cs    # ECS 创建策略实现
│   ├── ControlCreationServiceAdapter.cs # WdUI 适配器
│   ├── Systems/
│   │   ├── IControlCreationStrategy.cs  # 创建策略接口（FindParent + CreateControl）
│   │   └── ControlCreateSystem.cs       # 创建 System（遍历 Entity 创建控件）
│   └── Components/
│       └── ControlHierarchyComponent.cs # 父子层级关系
├── Layout/                              # 布局子模块
│   ├── Systems/
│   │   ├── LayoutSystem.cs              # 布局计算 System
│   │   └── SplitContainerSystem.cs      # SplitContainer 分割管理 System
│   └── Components/
│       └── PositionComponent.cs         # 位置/大小 Component
└── Style/                               # 样式子模块
    ├── Systems/
    │   └── StyleSystem.cs               # 样式合并 System
    └── Components/
        └── StyleComponent.cs            # 样式相关 Component
```

**具体功能清单**：

| 功能 | 说明 | 入口方法 |
|------|------|----------|
| 策略注册 | 注册控件类型→创建工厂的映射 | `ControlStrategyRegistry.Register()` |
| 策略查找 | 根据类型名查找并创建控件 | `ControlStrategyRegistry.Create()` |
| 标准控件注册 | 批量注册 34 种 WinForms 标准控件 | `StandardControlStrategies.RegisterAll()` |
| ECS 创建 | 根据 Entity 的 ControlTypeComponent 创建控件 | `EcsControlCreationStrategy.CreateControl()` |
| 父控件查找 | 根据 ControlHierarchyComponent 查找父控件 | `EcsControlCreationStrategy.FindParent()` |
| 批量创建 | 遍历所有 Entity，拓扑排序后创建控件树 | `ControlCreateSystem.Update()` |
| WdUI 适配 | 将 ControlStrategyRegistry 包装为 IControlCreationService | `ControlCreationServiceAdapter.Create()` |
| 比例→像素 | 将 LayoutComponent 的比例值转换为控件像素位置/大小 | `LayoutSystem.Update()` |
| 全量重算 | 窗口大小变化时重新计算所有布局 | `LayoutSystem.RecalculateAll()` |
| Splitter 分割 | 订阅 SplitterMoved 事件，持久化/恢复 SplitterDistance | `SplitContainerSystem.Update()` |
| Splitter 重算 | 按比例恢复所有 SplitContainer 的分割位置 | `SplitContainerSystem.RecalculateAll()` |
| 样式合并 | 合并 StyleRef + ColorStyle + FontStyle → MergedStyleComponent | `StyleSystem.Update()` |
| 优先级处理 | 内联样式 > StyleRef > 默认样式 | `StyleSystem.Update()` |
| StyleRef 继承 | 从父 Entity 继承 StyleRef | `StyleSystem.Update()` |

**可验证方式**：
```csharp
// 单元测试 1：注册表创建
var btn = ControlStrategyRegistry.Create("button");
Assert.IsType<Button>(btn);

// 单元测试 2：比例→像素转换
layoutSystem.Update();
var layout = entityManager.GetComponent<ComputedLayoutComponent>(entity);
Assert.Equal(expectedWidth, layout.Width);

// 单元测试 3：样式合并
styleSystem.Update();
var merged = entityManager.GetComponent<MergedStyleComponent>(entity);
Assert.Equal(expectedColor, merged.BackColor);
```

**依赖**：`ECS.Core`、`System.Windows.Forms`、`ControlPool`、`EntityManager`

---

### 3.3 模块 2：ControlInteraction（控件交互）

**命名空间**：`FrontEndDriven.ControlInteraction`

**职责**：控件的运行时交互——事件绑定 + 值同步

**边界**：
- 输入：事件配置、值配置
- 输出：事件已绑定、控件属性已同步
- 不依赖：布局系统、样式系统

**目标目录结构**：

```
ControlInteraction/
├── EventBinding/                        # 事件绑定子模块
│   ├── Systems/
│   │   └── EventBindingSystem.cs        # ECS 事件绑定 System
│   ├── UIEventBinder.cs                 # 代码驱动事件绑定器
│   ├── WinFormsEventRegistrar.cs        # 控件事件注册器
│   ├── Abstractions/
│   │   ├── IEventBindingService.cs      # 事件绑定服务接口
│   │   └── IWinFormsEventRegistrar.cs   # WinForms 事件注册器接口
│   ├── Implementation/
│   │   ├── EventSubscriptionManager.cs  # 事件订阅生命周期管理
│   │   └── NullEventBindingService.cs   # 空事件绑定服务（CLI 模式）
│   └── Components/
│       └── EventComponent.cs            # 事件绑定 Component
├── ValueSync/                           # 值同步子模块
│   ├── Systems/
│   │   ├── ControlSyncSystem.cs         # 布局/样式同步 System
│   │   └── ControlValueSyncSystem.cs    # 运行时值同步 System
│   ├── Implementation/
│   │   ├── WinFormsControlValueWriter.cs # 17+ 种控件适配器
│   │   └── CompositeValueWriter.cs      # 组合值写入器
│   └── Components/                      # 值相关 Component
│       ├── TextComponent.cs             # 文本值
│       ├── ControlStateComponent.cs     # Enabled/Visible 状态
│       ├── CheckStateComponent.cs       # CheckBox 选中状态
│       ├── SelectionComponent.cs        # 通用选中状态
│       ├── ListSelectionComponent.cs    # 列表选中状态
│       ├── NumericValueComponent.cs     # 数值
│       └── DateTimeValueComponent.cs    # 日期时间
└── Registration/
    └── ControlServiceCollection.cs      # 控件服务统一注册入口
```

**具体功能清单**：

| 功能 | 说明 | 入口方法 |
|------|------|----------|
| ECS 事件绑定 | 根据 EventBindingsComponent 绑定事件处理器 | `EventBindingSystem.Update()` |
| 代码驱动事件 | 集中管理 MainForm 中散落的事件注册 | `UIEventBinder.BindAll()` |
| 事件注册器 | 统一 Click/TextChanged/SelectedIndexChanged 等注册风格 | `WinFormsEventRegistrar.Register*()` |
| 事件取消 | 每个注册方法返回 Action 取消委托 | `WinFormsEventRegistrar` 返回值 |
| 事件生命周期 | 统一管理事件订阅的注册/取消 | `EventSubscriptionManager` |
| 空实现 | CLI 模式下的空操作事件绑定 | `NullEventBindingService` |
| 布局/样式同步 | 将 ComputedLayout/MergedStyle 同步到控件 | `ControlSyncSystem.SyncAll()` |
| 文本同步 | TextComponent → Control.Text | `ControlValueSyncSystem.Update()` |
| 状态同步 | ControlStateComponent → Control.Enabled/Visible | `ControlValueSyncSystem.Update()` |
| 选中同步 | SelectionComponent → ListView.SelectedItems | `ControlValueSyncSystem.Update()` |
| Check 同步 | CheckStateComponent → CheckBox.Checked | `ControlValueSyncSystem.Update()` |
| 数值同步 | NumericValueComponent → NumericUpDown.Value | `ControlValueSyncSystem.Update()` |
| 日期同步 | DateTimeValueComponent → DateTimePicker.Value | `ControlValueSyncSystem.Update()` |
| 列表选中同步 | ListSelectionComponent → ComboBox.SelectedIndex | `ControlValueSyncSystem.Update()` |
| 控件值读取 | 17+ 种控件类型的值读取 | `WinFormsControlValueWriter.ReadValue()` |
| 控件值写入 | 17+ 种控件类型的值写入 | `WinFormsControlValueWriter.WriteValue()` |
| 属性读写 | 通过反射或适配器读写控件任意属性 | `WinFormsControlValueWriter.WriteProperty()` |
| 服务注册 | 统一注册 IControlFinder/IControlValueAccessor/IControlRegistry/IEventBindingService | `ControlServiceCollection.RegisterAll()` |

**可验证方式**：
```csharp
// 单元测试 1：ECS 事件绑定
eventBindingSystem.Update();
button.PerformClick();
Assert.True(handlerCalled);

// 单元测试 2：控件适配器
writer.SetValue(button, "click me");
Assert.Equal("click me", button.Text);

// 单元测试 3：值同步 System
controlValueSyncSystem.Update();
Assert.Equal(expectedText, button.Text);
Assert.Equal(expectedEnabled, button.Enabled);
```

**依赖**：`ControlPool`、`EntityManager`、`IControlValueAccessor`

---

### 3.4 模块 3：ControlLookup（控件查找与注册）

**命名空间**：`FrontEndDriven.ControlLookup`

**职责**：控件查找（策略链）、注册、索引、容器管理

**边界**：
- 输入：控件名称/类型/谓词
- 输出：控件实例
- 不依赖：布局系统、样式系统、事件系统、值同步系统

**目标目录结构**：

```
ControlLookup/
├── Finder/                              # 查找子模块
│   ├── ControlRegistryAwareFinder.cs    # 策略链查找器
│   ├── ControlFinder.cs                 # 通用控件查找器（递归/谓词）
│   └── ControlIndex.cs                  # O(1) 控件索引
├── Container/                           # 容器子模块
│   ├── EcsConfigContainer.cs            # ECS 配置容器
│   ├── ContainerFinder.cs              # 容器查找
│   ├── ContainerRegistry.cs            # 容器注册（IControlRegistry 实现）
│   └── ConfigContainerExtensions.cs    # 容器扩展方法
├── Services/
│   └── EntityTreeService.cs            # Entity 树查找服务
├── Abstractions/
│   └── IControlRegistry.cs             # 控件注册接口
└── UnifiedControlService.cs            # 统一控件服务（组合 3 模块）
```

**具体功能清单**：

| 功能 | 说明 | 入口方法 |
|------|------|----------|
| 策略链查找 | ControlPool(O(1)) → EntityTree → Registry | `ControlRegistryAwareFinder.FindByName()` |
| 指定根查找 | 在指定根控件下按名称查找 | `ControlRegistryAwareFinder.FindByName(root, name)` |
| 类型查找 | 按类型查找第一个匹配控件 | `ControlRegistryAwareFinder.FindByType<T>()` |
| 谓词查找 | 按谓词查找所有匹配控件 | `ControlRegistryAwareFinder.FindAll()` |
| Form 查找 | 在指定 Form 中查找控件 | `ControlRegistryAwareFinder.FindInForm()` |
| 递归查找 | 递归遍历控件树查找 | `ControlFinder.FindRecursive()` |
| O(1) 索引 | 基于 TreeIndex 的名称→控件映射 | `ControlIndex.FindByName()` |
| Entity 树查找 | 通过 Entity 树遍历查找容器/控件 | `EntityTreeService.FindControlByName()` |
| 容器注册 | 注册/注销/查找容器 | `ContainerRegistry` |
| 子容器管理 | 注册/注销子容器 | `EcsConfigContainer.RegisterSubContainer()` |
| 配置加载 | 从文件或对象加载 UI 配置 | `EcsConfigContainer.LoadConfigAsync()` |
| 统一服务 | 组合查找+读写+注册+事件绑定 | `UnifiedControlService` |

**可验证方式**：
```csharp
// 单元测试 1：策略链顺序（ControlPool → EntityTree → Registry）
var control = finder.FindByName("btn1");
Assert.Same(expectedControl, control);

// 单元测试 2：O(1) 索引查找
var indexed = index.FindByName("btn1");
Assert.NotNull(indexed);

// 单元测试 3：递归查找
var found = ControlFinder.FindRecursive(form, c => c is Button);
Assert.NotNull(found);
```

**依赖**：`ControlPool`、`EntityManager`、`IControlRegistry`

---

### 3.5 编排层：RenderPipeline

**命名空间**：`FrontEndDriven.RenderPipeline`

**职责**：编排 3 个模块的执行顺序，对外暴露 `IRenderPipeline` 接口

**目标目录结构**：

```
RenderPipeline/
├── EcsRenderPipeline.cs                 # 渲染管线编排
├── EntityRegistry.cs                    # Entity 注册中心
└── Loading/
    └── ConfigLoader.cs                  # 配置加载器
```

**具体功能清单**：

| 功能 | 说明 | 入口方法 |
|------|------|----------|
| 管线初始化 | 创建 EcsWorld、ControlPool、所有 System | `EcsRenderPipeline.Initialize()` |
| 配置加载 | 将 UIConfiguration 转换为 Entity 树 | `EcsRenderPipeline.LoadConfiguration()` |
| 根容器注册 | 将 EcsConfigContainer 注册到 ControlPool | `EcsRenderPipeline.RegisterRootContainer()` |
| 完整渲染 | 创建→布局→样式→事件→同步 全流程 | `EcsRenderPipeline.RenderAsync()` |
| 分阶段执行 | 仅执行创建阶段 | `EcsRenderPipeline.ExecuteCreateStageAsync()` |
| 分阶段执行 | 仅执行布局阶段 | `EcsRenderPipeline.ExecuteLayoutStageAsync()` |
| 分阶段执行 | 仅执行事件绑定阶段 | `EcsRenderPipeline.ExecuteBindEventsStageAsync()` |
| 布局重算 | 窗口大小变化时重新计算 | `EcsRenderPipeline.RecalculateLayout()` |
| Entity 查找 | 通过控件名称获取 Entity | `EcsRenderPipeline.GetEntity()` |
| Entity 注册 | 注册/注销 Entity↔Control 映射 | `EcsRenderPipeline.RegisterControl()` |
| 配置加载 | 从文件路径加载配置 | `ConfigLoader.LoadConfig()` |

**可验证方式**：
```csharp
// 集成测试：完整流程
await pipeline.RenderAsync(container, config);
Assert.Equal(expectedControlCount, container.Controls.Count);

// 集成测试：分阶段执行
await pipeline.ExecuteCreateStageAsync(container, config);
await pipeline.ExecuteLayoutStageAsync(container);
```

---

## 4. 共享工具（Shared）

跨模块通用的工具类，不归属于任何特定模块：

```
Shared/
├── Diagnostics/
│   ├── FrontEndDrivenLog.cs             # 日志
│   └── WdUILog.cs                       # WdUI 日志（待废弃）
├── Utils/
│   ├── UiThreadHelper.cs                # UI 线程调度
│   ├── TypeConversionHelper.cs          # 类型转换
│   └── WinFormsTypeConverter.cs         # WinForms 类型转换（从 ControlFactoryFramework 迁移）
├── Abstractions/
│   └── IControlCreationService.cs       # 控件创建服务接口（WdUI 解耦用）
└── Loading/
    └── ConfigLoader.cs                  # 配置加载器
```

---

## 5. 废弃文件清单

以下文件属于旧 WdUI 体系，已被 ECS 体系完全替代，移入 `Obsolete/` 目录：

| 文件 | 替代者 | 废弃原因 |
|------|--------|----------|
| `WdUI/UI/WdUI.Layout.cs` | `ControlRender/Layout` | 布局逻辑已迁移到 ECS System |
| `WdUI/UI/WdUI.EventManagement.cs` | `ControlInteraction/EventBinding` | 事件管理已迁移到 ECS System |
| `WdUI/UI/WdUI.ControlFactory.cs` | `ControlRender/Creation` | 控件创建已迁移到策略注册表 |
| `WdUI/UI/WdUI.ViewManager.cs` | `RenderPipeline/EcsRenderPipeline` | 视图管理已迁移到渲染管线 |
| `WdUI/Framework/ControlFactoryFramework.cs` 中的 `ControlConfigApplier` | `ControlRender/Style` + `ControlInteraction/ValueSync` | 属性应用已拆分 |
| `WdUI/Layout/LayoutMediator.cs` | `ControlRender/Layout` | 布局中介已迁移 |
| `WdUI/Layout/RenderSession.cs` | `RenderPipeline/EcsRenderPipeline` | 渲染会话已迁移 |
| `WdUI/UI/WdUI.Management.cs` | 各模块分散管理 | 管理功能已分散到各模块 |
| `WdUI/UI/WdUI.Utilities.cs` | `Shared/Utils/` | 工具方法已迁移到共享工具 |
| `WdUI/UI/WdUI.Configuration.cs` | `TConfig` 配置体系 | WdUI 特有配置已废弃 |

---

## 6. 实施路线图

### Phase 1：模块边界确立（1-2 天）

1. 创建 3 个模块的目录结构 + `RenderPipeline/` + `Shared/` + `Obsolete/`
2. 将现有文件按模块归属移动到对应目录
3. 调整命名空间为模块级（如 `FrontEndDriven.ControlRender`）
4. 标记废弃文件移入 `Obsolete/` 目录
5. 更新 `FrontEndDriven.csproj` 中的文件引用

### Phase 2：模块接口提取（2-3 天）

1. 每个模块提取公共接口（如 `IControlRenderService`、`IControlInteractionService`）
2. `UnifiedControlService` 拆分为 3 个模块级服务
3. 消除跨模块的直接依赖，改为接口依赖
4. `ControlServiceCollection.RegisterAll()` 改为按模块注册

### Phase 3：模块级测试（2-3 天）

1. 每个模块建立独立的测试项目（`ControlRender.Tests`、`ControlInteraction.Tests`、`ControlLookup.Tests`）
2. 编写模块边界测试（Mock `ControlPool` + `EntityManager`）
3. 编写集成测试（验证模块间协作）
4. 测试覆盖每个模块的入口方法

### Phase 4：旧体系清理（1-2 天）

1. 确认 `Obsolete/` 中文件无引用后删除
2. `ControlConfigApplier` 中的逻辑迁移到 `ControlRender/Style` + `ControlInteraction/ValueSync`
3. `WinFormsTypeConverter` 迁移到 `Shared/Utils/`
4. 删除 `WdUI/` 空目录

---

## 7. 模块依赖关系（最终形态）

```
ControlRender  ←──  ControlInteraction
     ↓                    ↓
ControlLookup  ←──  RenderPipeline
```

- **单向依赖**：箭头方向表示依赖方向，禁止反向依赖
- **模块间通信**：通过 `ControlPool`（Entity↔Control 映射）和 `EntityManager`（Component 读写）
- **模块内通信**：直接方法调用
- **共享工具**：所有模块可依赖 `Shared/` 中的工具类

---

## 8. 附录：文件迁移对照表

| 当前路径 | 目标路径 |
|----------|----------|
| `ECS/IControlStrategy.cs` | `ControlRender/Creation/IControlStrategy.cs` |
| `ECS/ControlStrategyRegistry.cs` | `ControlRender/Creation/ControlStrategyRegistry.cs` |
| `ECS/StandardControlStrategies.cs` | `ControlRender/Creation/StandardControlStrategies.cs` |
| `ECS/EcsControlCreationStrategy.cs` | `ControlRender/Creation/EcsControlCreationStrategy.cs` |
| `ECS/ControlCreationServiceAdapter.cs` | `ControlRender/Creation/ControlCreationServiceAdapter.cs` |
| `ECS/Systems/IControlCreationStrategy.cs` | `ControlRender/Creation/Systems/IControlCreationStrategy.cs` |
| `ECS/Systems/ControlCreateSystem.cs` | `ControlRender/Creation/Systems/ControlCreateSystem.cs` |
| `ECS/Components/ControlHierarchyComponent.cs` | `ControlRender/Creation/Components/ControlHierarchyComponent.cs` |
| `ECS/Systems/LayoutSystem.cs` | `ControlRender/Layout/Systems/LayoutSystem.cs` |
| `ECS/Systems/SplitContainerSystem.cs` | `ControlRender/Layout/Systems/SplitContainerSystem.cs` |
| `ECS/Components/PositionComponent.cs` | `ControlRender/Layout/Components/PositionComponent.cs` |
| `ECS/Systems/StyleSystem.cs` | `ControlRender/Style/Systems/StyleSystem.cs` |
| `ECS/Components/StyleComponent.cs` | `ControlRender/Style/Components/StyleComponent.cs` |
| `ECS/Systems/EventBindingSystem.cs` | `ControlInteraction/EventBinding/Systems/EventBindingSystem.cs` |
| `WinForms/EventBinding/UIEventBinder.cs` | `ControlInteraction/EventBinding/UIEventBinder.cs` |
| `WinForms/Registration/WinFormsEventRegistrar.cs` | `ControlInteraction/EventBinding/WinFormsEventRegistrar.cs` |
| `WinForms/Abstractions/IWinFormsEventRegistrar.cs` | `ControlInteraction/EventBinding/Abstractions/IWinFormsEventRegistrar.cs` |
| `Abstractions/IEventBindingService.cs` | `ControlInteraction/EventBinding/Abstractions/IEventBindingService.cs` |
| `Implementation/EventSubscriptionManager.cs` | `ControlInteraction/EventBinding/Implementation/EventSubscriptionManager.cs` |
| `Implementation/NullEventBindingService.cs` | `ControlInteraction/EventBinding/Implementation/NullEventBindingService.cs` |
| `ECS/Components/EventComponent.cs` | `ControlInteraction/EventBinding/Components/EventComponent.cs` |
| `ECS/Systems/ControlSyncSystem.cs` | `ControlInteraction/ValueSync/Systems/ControlSyncSystem.cs` |
| `ECS/Systems/ControlValueSyncSystem.cs` | `ControlInteraction/ValueSync/Systems/ControlValueSyncSystem.cs` |
| `Implementation/WinFormsControlValueWriter.cs` | `ControlInteraction/ValueSync/Implementation/WinFormsControlValueWriter.cs` |
| `Implementation/CompositeValueWriter.cs` | `ControlInteraction/ValueSync/Implementation/CompositeValueWriter.cs` |
| `ECS/Components/TextComponent.cs` | `ControlInteraction/ValueSync/Components/TextComponent.cs` |
| `ECS/Components/ControlStateComponent.cs` | `ControlInteraction/ValueSync/Components/ControlStateComponent.cs` |
| `ECS/Components/CheckStateComponent.cs` | `ControlInteraction/ValueSync/Components/CheckStateComponent.cs` |
| `ECS/Components/SelectionComponent.cs` | `ControlInteraction/ValueSync/Components/SelectionComponent.cs` |
| `ECS/Components/ListSelectionComponent.cs` | `ControlInteraction/ValueSync/Components/ListSelectionComponent.cs` |
| `ECS/Components/NumericValueComponent.cs` | `ControlInteraction/ValueSync/Components/NumericValueComponent.cs` |
| `ECS/Components/DateTimeValueComponent.cs` | `ControlInteraction/ValueSync/Components/DateTimeValueComponent.cs` |
| `Registration/ControlServiceCollection.cs` | `ControlInteraction/Registration/ControlServiceCollection.cs` |
| `Implementation/ControlRegistryAwareFinder.cs` | `ControlLookup/Finder/ControlRegistryAwareFinder.cs` |
| `WdUI/Framework/Shared/ControlFinder.cs` | `ControlLookup/Finder/ControlFinder.cs` |
| `WdUI/Framework/Shared/ControlIndex.cs` | `ControlLookup/Finder/ControlIndex.cs` |
| `Implementation/UnifiedControlService.cs` | `ControlLookup/UnifiedControlService.cs` |
| `Container/EcsConfigContainer.cs` | `ControlLookup/Container/EcsConfigContainer.cs` |
| `Container/ContainerFinder.cs` | `ControlLookup/Container/ContainerFinder.cs` |
| `Container/ContainerRegistry.cs` | `ControlLookup/Container/ContainerRegistry.cs` |
| `Container/ConfigContainerExtensions.cs` | `ControlLookup/Container/ConfigContainerExtensions.cs` |
| `ECS/Services/EntityTreeService.cs` | `ControlLookup/Services/EntityTreeService.cs` |
| `ECS/EcsRenderPipeline.cs` | `RenderPipeline/EcsRenderPipeline.cs` |
| `ECS/EntityRegistry.cs` | `RenderPipeline/EntityRegistry.cs` |
| `Loading/ConfigLoader.cs` | `RenderPipeline/Loading/ConfigLoader.cs` |
| `Diagnostics/FrontEndDrivenLog.cs` | `Shared/Diagnostics/FrontEndDrivenLog.cs` |
| `Utils/UiThreadHelper.cs` | `Shared/Utils/UiThreadHelper.cs` |
| `Utils/TypeConversionHelper.cs` | `Shared/Utils/TypeConversionHelper.cs` |
| `WdUI/Abstractions/IControlCreationService.cs` | `Shared/Abstractions/IControlCreationService.cs` |
| `WdUI/Diagnostics/WdUILog.cs` | `Shared/Diagnostics/WdUILog.cs` |
| `WinForms/Utils/UILogger.cs` | `Shared/Diagnostics/UILogger.cs` |
| `WinForms/Utils/WinFormsControlMetadataStore.cs` | `Shared/Utils/WinFormsControlMetadataStore.cs` |
| `WdUI/WdUIComposer.cs` | `Obsolete/WdUIComposer.cs` |
| `WdUI/WdUI.Settings.cs` | `Obsolete/WdUI.Settings.cs` |
| `WdUI/Framework/ConfigurationFramework.cs` | `Obsolete/ConfigurationFramework.cs` |
| `WdUI/Framework/ControlFactoryFramework.cs` | `Obsolete/ControlFactoryFramework.cs` |
| `WdUI/UI/WdUI.Configuration.cs` | `Obsolete/WdUI.Configuration.cs` |
| `WdUI/UI/WdUI.ControlFactory.cs` | `Obsolete/WdUI.ControlFactory.cs` |
| `WdUI/UI/WdUI.EventManagement.cs` | `Obsolete/WdUI.EventManagement.cs` |
| `WdUI/UI/WdUI.Layout.cs` | `Obsolete/WdUI.Layout.cs` |
| `WdUI/UI/WdUI.Management.cs` | `Obsolete/WdUI.Management.cs` |
| `WdUI/UI/WdUI.Utilities.cs` | `Obsolete/WdUI.Utilities.cs` |
| `WdUI/UI/WdUI.ViewManager.cs` | `Obsolete/WdUI.ViewManager.cs` |
| `WdUI/Layout/LayoutMediator.cs` | `Obsolete/LayoutMediator.cs` |
| `WdUI/Layout/RenderSession.cs` | `Obsolete/RenderSession.cs` |
| `WdUI/WinForms/FrameworkContextWinFormsExtensions.cs` | `Obsolete/FrameworkContextWinFormsExtensions.cs` |
| `FrontEndDrivenConstants.cs` | `FrontEndDrivenConstants.cs`（保留） |
| `GlobalUsings.cs` | `GlobalUsings.cs`（保留） |
