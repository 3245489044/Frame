# WdUI 与 WinForms 解耦：通用框架重构方案

> 基于 Share/UniversalMiddleware 构建 6 个通用框架，覆盖 WdUI 全部逻辑领域

---

## 目录

1. [现状分析](#1-现状分析)
2. [WdUI 逻辑领域全景](#2-wdui-逻辑领域全景)
3. [通用框架设计方案](#3-通用框架设计方案)
4. [框架依赖关系](#4-框架依赖关系)
5. [WdUI 适配方案](#5-wdui-适配方案)
6. [迁移路径](#6-迁移路径)
7. [收益总结](#7-收益总结)

---

## 1. 现状分析

### 1.1 当前架构问题

```
UniversalMiddleware (net10.0-windows, UseWinForms=true)  ← 强制依赖 WinForms
├── Abstractions.cs (514行)    ← 接口 + 模型 + 枚举 混在一起
├── Core.cs (359行)            ← JSON 管线 + 中间件管线
├── Infrastructure.cs (650行)  ← 中介器/缓存/状态/日志/布局/扩展
├── Container.cs (236行)       ← 容器接口 + 管理器（与 ConfigurationModels 重复）
├── BusinessLogic.cs (441行)   ← 事件总线/数据绑定/命令执行/控件工厂/布局引擎
├── Serialization.cs (561行)   ← 序列化/验证/导航/合并
├── Utilities.cs (138行)       ← 锁工具/颜色映射/间距计算
├── ConfigurationModels/       ← 独立项目，纯 POCO 配置模型
├── Serialization/             ← 序列化子目录
├── Abstractions/              ← 抽象基类
├── Stages/                    ← 管线阶段
├── Configuration/             ← 配置构建
└── Extensions/                ← 扩展方法
```

### 1.2 核心问题

| 问题 | 描述 |
|------|------|
| **接口重复** | `Container.cs` 中的 `IConfigContainer` 与 `ConfigurationModels/Abstractions/IConfigContainer.cs` 功能重复 |
| **模型重复** | `Container.cs` 中的 `ContainerConfig` 与 `ConfigurationModels/Models/ContainerConfig.cs` 重复 |
| **WinForms 耦合** | csproj 中 `UseWindowsForms=true`，但大量代码实际上不依赖 WinForms |
| **职责混乱** | 一个项目同时包含：JSON 管线、布局计算、事件总线、命令执行、序列化、DI 容器 |
| **命名空间混乱** | `UniversalJsonMiddleware.Abstractions`、`UniversalJsonMiddleware.Core`、`UniversalJsonMiddleware.Messaging` 等混用 |
| **依赖混乱** | 引用 `UniversalLayoutFramework.ILayoutContainer` 但自己又定义了 `ILayoutContainer` |

---

## 2. WdUI 逻辑领域全景

通过对 WdUI 全部 11 个 UI 文件 + RenderPipeline + Layout 层的深入分析，识别出以下逻辑领域：

| # | 逻辑领域 | 所在文件 | 当前状态 | 能否通用化 |
|---|---------|---------|---------|-----------|
| 1 | **配置模型** | `ConfigurationModels/` | 纯 POCO，已通用 | ✅ 可直接复用 |
| 2 | **配置加载/缓存/合并** | `WdUI.Configuration.cs` + `ConfigurationLoader` | 部分通用 | ✅ 可提取 |
| 3 | **序列化/反序列化** | `Serialization/` + `WdUIJson.cs` | 部分通用 | ✅ 可提取 |
| 4 | **事件总线** | `WdUI.EventManagement.cs` (WdUIEventBus) | 已通过接口解耦 | ✅ 可通用 |
| 5 | **容器管理** | `WdUI.Container.cs` (ConfigContainer) | WinForms 强耦合 | ⚠️ 需抽象接口 |
| 6 | **渲染管线** | `WdUI.RenderPipeline.cs` | WinForms 强耦合 | ⚠️ 需抽象接口 |
| 7 | **布局计算** | `Layout/` + `UniversalLayoutMath` | 数学部分已通用 | ✅ 可直接复用 |
| 8 | **布局策略** | `Layout/Strategies/` | WinForms 耦合 | ⚠️ 需抽象接口 |
| 9 | **命令执行** | `WinFormsCommandEngine.cs` | 已基于 CommandFramework | ✅ 可直接复用 |
| 10 | **视图管理** | `WdUI.Core.cs` (ViewManager) | WinForms 强耦合 | ⚠️ 需抽象接口 |
| 11 | **控件工厂** | `WdUI.Core.cs` (ControlFactory) | WinForms 强耦合 | ⚠️ 需抽象接口 |
| 12 | **数据绑定** | `BusinessLogic.cs` (DataBindingManager) | 已通用 | ✅ 可直接复用 |
| 13 | **中介器/缓存/状态** | `Infrastructure.cs` (Mediator) | 已通用 | ✅ 可直接复用 |
| 14 | **事件绑定** | `WdUI.EventManagement.cs` (ContainerEventManager) | 部分通用 | ⚠️ 需抽象接口 |
| 15 | **日志** | `Infrastructure.cs` (ConsoleLogger) | 已通用 | ✅ 可直接复用 |
| 16 | **JSON 管线** | `Core.cs` (JsonPipeline) | 已通用 | ✅ 可直接复用 |
| 17 | **中间件管线** | `Core.cs` (MiddlewarePipeline) | 已通用 | ✅ 可直接复用 |
| 18 | **重叠检测** | `WdUI.Container.cs` (OverlapDetection) | 数学部分已通用 | ✅ 可直接复用 |

---

## 3. 通用框架设计方案

基于以上分析，在 `Share/UniversalMiddleware` 中构建 **6 个通用框架**，全部使用 `net10.0`（无 Windows 依赖）。

### 3.1 框架 1：`Share.UniversalMiddleware.Abstractions`

**目标**：零依赖的纯接口库，定义所有通用契约。

```
Share.UniversalMiddleware.Abstractions/
├── Share.UniversalMiddleware.Abstractions.csproj (net10.0)
│   └── 依赖: 无
│
├── Pipeline/
│   ├── IJsonTransform.cs          # JSON 变换器接口
│   ├── IJsonPipeline.cs           # 管线接口
│   └── JsonPipelineContext.cs     # 管线上下文
│
├── Messaging/
│   ├── IMediator.cs               # 中介器接口
│   ├── IEventBus.cs               # 事件总线接口
│   ├── ICacheManager.cs           # 缓存管理器接口
│   └── IStateManager.cs           # 状态管理器接口
│
├── Serialization/
│   ├── IJsonSerializer.cs         # 序列化器接口
│   ├── IJsonValidator.cs          # 验证器接口
│   └── IJsonPathEngine.cs         # JSON Path 引擎接口
│
├── Container/
│   ├── IConfigContainer.cs        # 配置容器接口（唯一版本）
│   └── ContainerConfig.cs         # 容器配置模型（唯一版本）
│
├── Layout/
│   ├── ILayoutContainer.cs        # 布局容器接口（纯抽象）
│   ├── ILayoutElement.cs          # 布局元素接口
│   ├── ILayoutCalculator.cs       # 布局计算器接口
│   └── LayoutModels.cs            # 布局模型（Position/Definition/Constraints）
│
├── UI/
│   ├── IUIManager.cs              # UI 管理器泛型接口
│   ├── IControlFactory.cs         # 控件工厂泛型接口
│   ├── IViewManager.cs            # 视图管理器泛型接口
│   └── IDataBindingManager.cs     # 数据绑定管理器接口
│
├── Commands/
│   ├── ICommandExecutor.cs        # 命令执行器接口
│   └── ICommandHandler.cs         # 命令处理器接口
│
├── Rendering/
│   ├── IRenderPipeline.cs         # 渲染管线接口
│   ├── IRenderSession.cs          # 渲染会话接口
│   └── IRenderStage.cs            # 渲染阶段接口
│
├── Events/
│   ├── IEventBindingService.cs    # 事件绑定服务接口
│   ├── ContainerEventArgs.cs      # 容器事件参数
│   └── EventsBoundEventArgs.cs    # 事件绑定完成参数
│
├── Logging/
│   ├── ILogger.cs                 # 日志器接口
│   └── LogLevel.cs                # 日志级别枚举
│
└── Common/
    ├── IResizeNotifiable.cs       # 尺寸变化通知接口
    ├── Guard.cs                   # 重入保护
    └── StateChangedEventArgs.cs   # 状态变更事件参数
```

**覆盖 WdUI 中的接口**：
- `IContainerRenderPipeline` → `IRenderPipeline`
- `IContainerEventManager` → 合并到 `IConfigContainer` + `IEventBus`
- `IControlFinder` → 合并到 `IConfigContainer.FindControlByName`
- `IContainerConfigLoader` → `IConfigurationLoader`
- `IWinFormsLayoutContext` → `ILayoutContainer` + `ILayoutCalculator`
- `IControlFactory` → `IControlFactory<Control, ControlConfig>`
- `IUILayoutManager` → `IUILayoutManager<Control>`
- `IViewManager` → `IViewManager<Form>`
- `IUIManager` → `IUIManager<Control, Form, ControlConfig>`

### 3.2 框架 2：`Share.UniversalMiddleware.Core`

**目标**：实现所有接口，不依赖任何 UI 框架。

```
Share.UniversalMiddleware.Core/
├── Share.UniversalMiddleware.Core.csproj (net10.0)
│   └── 依赖: Share.UniversalMiddleware.Abstractions
│
├── Pipeline/
│   ├── JsonPipelineBuilder.cs     # 管线构建器
│   ├── TransformBase.cs           # 变换器基类
│   └── MiddlewarePipeline.cs      # 洋葱模型中间件
│
├── Messaging/
│   ├── Mediator.cs                # 中介器实现
│   ├── EventBus.cs                # 事件总线实现
│   ├── CacheManager.cs            # 缓存管理器
│   └── StateManager.cs            # 状态管理器
│
├── Serialization/
│   ├── UnifiedJsonSerializer.cs   # 统一序列化器
│   ├── JsonValidator.cs           # 验证器
│   ├── JsonPathEngine.cs          # JSON Path 引擎
│   ├── JsonMerger.cs              # JSON 合并器
│   └── ModelValidator.cs          # 模型验证器
│
├── Layout/
│   ├── LayoutCalculator.cs        # 布局计算器
│   └── LayoutValidator.cs         # 布局验证器
│
├── Commands/
│   ├── CommandExecutor.cs         # 命令执行器
│   └── GenericControlFactory.cs   # 通用控件工厂
│
├── Binding/
│   └── DataBindingManager.cs      # 数据绑定管理器
│
├── Engine/
│   └── SmartLayoutEngine.cs       # 智能布局引擎
│
├── Diagnostics/
│   ├── JsonDiagnostics.cs         # 性能诊断
│   └── OverlapColorMapper.cs      # 颜色映射
│
├── Utilities/
│   ├── LockHelper.cs              # 锁工具
│   └── SmartSpacingCalculator.cs  # 间距计算
│
└── Extensions/
    ├── StringJsonExtensions.cs    # 字符串扩展
    ├── ObjectJsonExtensions.cs    # 对象扩展
    └── PipelineExtensions.cs      # 管线扩展
```

**覆盖 WdUI 中的实现**：
- `Mediator` → 从 `Infrastructure.cs` 迁移
- `EventBus` → 从 `BusinessLogic.cs` 迁移
- `CacheManager` / `StateManager` → 从 `Infrastructure.cs` 迁移
- `UnifiedJsonSerializer` → 从 `Serialization.cs` 迁移
- `CommandExecutor` → 从 `BusinessLogic.cs` 迁移
- `DataBindingManager` → 从 `BusinessLogic.cs` 迁移
- `LayoutCalculator` / `LayoutValidator` → 从 `Infrastructure.cs` 迁移
- `SmartLayoutEngine` → 从 `BusinessLogic.cs` 迁移

### 3.3 框架 3：`Share.UniversalMiddleware.Configuration`

**目标**：纯 POCO 配置模型 + 加载/合并/验证服务。

```
Share.UniversalMiddleware.Configuration/
├── Share.UniversalMiddleware.Configuration.csproj (net10.0)
│   └── 依赖: Share.UniversalMiddleware.Abstractions
│
├── Models/
│   ├── UIConfiguration.cs         # UI 配置根模型
│   ├── FormConfig.cs              # 表单配置
│   ├── ViewConfig.cs              # 视图配置
│   ├── ControlConfig.cs           # 控件配置
│   ├── ContainerConfig.cs         # 容器配置（唯一版本）
│   ├── LayoutPosition.cs          # 布局位置
│   ├── StyleConfig.cs             # 样式配置
│   ├── EventConfig.cs             # 事件配置
│   ├── DataBindingConfig.cs       # 数据绑定配置
│   ├── NavigationConfig.cs        # 导航配置
│   ├── TabPageConfig.cs           # 标签页配置
│   ├── ColumnConfig.cs            # 列配置
│   ├── ListViewColumnConfig.cs    # 列表视图列配置
│   └── DataLoadConfig.cs          # 数据加载配置
│
├── Services/
│   ├── IConfigurationLoader.cs    # 配置加载器接口
│   ├── ConfigurationLoader.cs     # 配置加载器实现
│   └── ConfigurationMerger.cs     # 配置合并器
│
└── Builders/
    ├── UiConfigBuilder.cs         # Fluent API 构建器
    └── ControlConfigBuilder.cs    # 控件配置构建器
```

**覆盖 WdUI 中的**：
- `ConfigurationModels/` 全部模型
- `ConfigurationLoader` + `ConfigurationMerger`
- `WdUI.Configuration.cs` 中的配置相关逻辑

### 3.4 框架 4：`Share.UniversalMiddleware.Serialization`

**目标**：独立的序列化框架，包含预处理、自定义转换器、工厂模式。

```
Share.UniversalMiddleware.Serialization/
├── Share.UniversalMiddleware.Serialization.csproj (net10.0)
│   └── 依赖: Share.UniversalMiddleware.Abstractions
│
├── JsonSerializerFactory.cs       # 序列化器工厂
├── JsonConverterRegistrar.cs      # 转换器注册器
├── JsonPreprocessor.cs            # JSON 预处理器
├── JsonPrettyPrinter.cs           # JSON 美化工具
└── Converters/
    ├── JsonStringEnumConverter.cs # 枚举转换器
    └── ...                        # 其他自定义转换器
```

**覆盖 WdUI 中的**：
- `WdUIJson.cs` 的序列化逻辑
- `JsonPreprocessor` 预处理管线
- `JsonSerializerFactory` 选项管理

### 3.5 框架 5：`Share.UniversalMiddleware.Rendering`

**目标**：平台无关的渲染管线抽象，定义渲染阶段、会话管理、生命周期。

```
Share.UniversalMiddleware.Rendering/
├── Share.UniversalMiddleware.Rendering.csproj (net10.0)
│   └── 依赖: Share.UniversalMiddleware.Abstractions
│            Share.UniversalMiddleware.Configuration
│
├── Pipeline/
│   ├── RenderPipelineBase.cs      # 渲染管线基类
│   ├── RenderSession.cs           # 渲染会话
│   └── RenderStage.cs             # 渲染阶段基类
│
├── Stages/
│   ├── CreateStage.cs             # 创建阶段
│   ├── LayoutStage.cs             # 布局阶段
│   └── BindEventsStage.cs         # 事件绑定阶段
│
├── Lifecycle/
│   ├── IRenderLifecycle.cs        # 渲染生命周期
│   └── SessionManager.cs          # 会话管理器
│
└── Events/
    ├── RenderCompletedEvent.cs    # 渲染完成事件
    └── LayoutInvalidatedEvent.cs  # 布局失效事件
```

**覆盖 WdUI 中的**：
- `RenderPipeline` 的抽象版本
- `RenderSession` 会话管理
- 渲染阶段（Create → Layout → BindEvents）的抽象定义

### 3.6 框架 6：`Share.UniversalMiddleware.Container`

**目标**：平台无关的容器管理，包含容器注册、查找、嵌套、事件传播。

```
Share.UniversalMiddleware.Container/
├── Share.UniversalMiddleware.Container.csproj (net10.0)
│   └── 依赖: Share.UniversalMiddleware.Abstractions
│            Share.UniversalMiddleware.Configuration
│            Share.UniversalMiddleware.Rendering
│
├── ContainerManager.cs            # 容器管理器
├── ContainerNode.cs               # 容器节点（树结构）
├── ContainerIndex.cs              # 容器索引（O(1) 查找）
│
├── Events/
│   ├── ContainerEventManager.cs   # 容器事件管理器
│   └── EventPropagation.cs        # 事件传播机制
│
└── Lifecycle/
    ├── ContainerLifecycle.cs      # 容器生命周期
    └── ContainerDisposer.cs       # 容器资源清理
```

**覆盖 WdUI 中的**：
- `ConfigContainer` 的容器管理逻辑（子容器注册、查找）
- `ContainerEventManager` 事件管理
- `SubContainerRegistrar` 子容器注册
- `TreeIndex<ConfigContainer, string>` 容器索引

---

## 4. 框架依赖关系

```
WdUI (WinForms 项目, net10.0-windows)
  ├── Share.UniversalMiddleware.Abstractions
  ├── Share.UniversalMiddleware.Core
  ├── Share.UniversalMiddleware.Configuration
  ├── Share.UniversalMiddleware.Serialization
  ├── Share.UniversalMiddleware.Rendering
  ├── Share.UniversalMiddleware.Container
  └── System.Windows.Forms (仅 WinForms 特有代码)

Share.UniversalMiddleware.Container (net10.0)
  ├── Share.UniversalMiddleware.Abstractions
  ├── Share.UniversalMiddleware.Configuration
  └── Share.UniversalMiddleware.Rendering

Share.UniversalMiddleware.Rendering (net10.0)
  ├── Share.UniversalMiddleware.Abstractions
  └── Share.UniversalMiddleware.Configuration

Share.UniversalMiddleware.Core (net10.0)
  └── Share.UniversalMiddleware.Abstractions

Share.UniversalMiddleware.Serialization (net10.0)
  └── Share.UniversalMiddleware.Abstractions

Share.UniversalMiddleware.Configuration (net10.0)
  └── Share.UniversalMiddleware.Abstractions

Share.UniversalMiddleware.Abstractions (net10.0)
  └── 零依赖
```

### 依赖关系图（文本）

```
                    ┌─────────────────────┐
                    │  WdUI (WinForms)    │
                    │  net10.0-windows    │
                    └──────┬──┬──┬──┬──┬──┘
                           │  │  │  │  │
         ┌─────────────────┘  │  │  │  └──────────────────┐
         │                    │  │  │                     │
    ┌────▼────┐    ┌─────────▼──▼──▼─────────┐    ┌──────▼──────┐
    │Container│    │  Core  Configuration    │    │Serialization│
    │(net10.0)│    │  Rendering (net10.0)    │    │ (net10.0)   │
    └────┬────┘    └─────────┬──┬──┬─────────┘    └──────┬──────┘
         │                  │  │  │                      │
         └──────────┬───────┘  │  └──────────┐           │
                    │          │             │           │
              ┌─────▼──────────▼─────────────▼───────────▼──┐
              │     Share.UniversalMiddleware.Abstractions  │
              │              (net10.0, 零依赖)              │
              └────────────────────────────────────────────┘
```

---

## 5. WdUI 适配方案

### 5.1 ConfigContainer 重构

**当前**：`ConfigContainer : Panel`，内部包含渲染、事件、容器管理所有逻辑。

```csharp
// 当前：WinForms 强耦合，761 行
public class ConfigContainer : Panel, IConfigContainer
{
    // 渲染管线
    // 容器管理（子容器索引）
    // 事件管理
    // 配置加载
    // 控件查找
    // 资源清理
}
```

**重构后**：

```csharp
// 通用容器节点（平台无关）
public class ContainerNode
{
    private readonly ContainerIndex _index = new();
    private readonly ContainerEventManager _eventManager;
    
    public void RegisterSubContainer(string name, IConfigContainer container) { ... }
    public IConfigContainer? FindSubContainer(string name) { ... }
    public void Clear() { ... }
}

// WdUI 中的 ConfigContainer（仅保留 WinForms 特有逻辑）
public class ConfigContainer : Panel
{
    private readonly ContainerNode _containerNode;
    private readonly RenderSession _renderSession;
    
    public ConfigContainer()
    {
        _containerNode = new ContainerNode();
        _renderSession = new RenderSession();
    }
    
    public async Task LoadConfigAsync(string path)
    {
        var config = ConfigurationLoader.Load(path);
        _renderSession.Render(config, new WinFormsRenderAdapter(this));
    }
    
    public ConfigContainer? FindSubContainer(string name)
        => _containerNode.FindSubContainer(name) as ConfigContainer;
}
```

### 5.2 RenderPipeline 重构

**当前**：`RenderPipeline` 直接操作 `Control`，包含 SkeletonFlow 管线。

```csharp
// 当前：WinForms 强耦合，469 行
public class RenderPipeline : IContainerRenderPipeline
{
    private readonly Flow<Control, Control> _createFlow;
    private readonly Flow<Control, Control> _applyFlow;
    // ... 直接操作 Control
}
```

**重构后**：

```csharp
// 通用渲染管线基类（平台无关）
public abstract class RenderPipelineBase<TTarget>
{
    protected abstract void ExecuteCreateStage(TTarget target, RenderSession session);
    protected abstract void ExecuteLayoutStage(TTarget target, RenderSession session);
    protected abstract void ExecuteBindEventsStage(TTarget target, RenderSession session);
    
    public void Render(TTarget target, UIConfiguration config) { ... }
    public void ApplyLayout(TTarget target) { ... }
    public void BindEvents(TTarget target) { ... }
}

// WdUI 中的 WinForms 渲染管线
public class WinFormsRenderPipeline : RenderPipelineBase<Control>
{
    private readonly Flow<Control, Control> _createFlow;
    private readonly Flow<Control, Control> _applyFlow;
    
    protected override void ExecuteCreateStage(Control target, RenderSession session)
    {
        _createFlow.Execute(target, CreateSkeletonContext(target, session));
    }
    
    protected override void ExecuteLayoutStage(Control target, RenderSession session)
    {
        _applyFlow.Execute(target, CreateSkeletonContext(target, session));
    }
}
```

### 5.3 ViewManager 重构

**当前**：`ViewManager` 直接操作 `Form`。

```csharp
// 当前：WinForms 强耦合
public class ViewManager : Framework.IViewManager
{
    private readonly ConcurrentDictionary<string, Form> _forms;
    // ... 直接操作 Form
}
```

**重构后**：

```csharp
// 通用视图管理器接口（平台无关）
public interface IViewManager<TForm>
{
    void RegisterForm(TForm form);
    void UnregisterForm(string formName);
    void ShowForm(string formName);
    void HideForm(string formName);
    TForm? GetForm(string formName);
    void CloseForm(string formName);
    object? GetControl(string formName, string controlName);
    void NavigateTo(string viewName);
    void NavigateBack();
}

// WdUI 中的 WinForms 视图管理器
public class WinFormsViewManager : IViewManager<Form>
{
    private readonly ConcurrentDictionary<string, Form> _forms;
    
    public void RegisterForm(Form form) { ... }
    public void ShowForm(string formName) { ... }
    // ... 实现 IViewManager<Form>
}
```

### 5.4 ControlFactory 重构

**当前**：`ControlFactory` 直接创建 WinForms 控件。

```csharp
// 当前：WinForms 强耦合
public class ControlFactory : Framework.IControlFactory
{
    public Control CreateControl(ControlConfig config, IMediator mediator)
    {
        Control control = _frameworkCreator.CreateControl(config.Type);
        ControlConfigApplier.ApplyConfig(control, config);
        return control;
    }
}
```

**重构后**：

```csharp
// 通用控件工厂接口（平台无关）
public interface IControlFactory<TControl, TConfig>
{
    TControl CreateControl(TConfig config);
    TControl CreateControl(string type);
}

// WdUI 中的 WinForms 控件工厂
public class WinFormsControlFactory : IControlFactory<Control, ControlConfig>
{
    public Control CreateControl(ControlConfig config)
    {
        Control control = CreateControlInstance(config.Type);
        ApplyConfig(control, config);
        return control;
    }
}
```

### 5.5 WdUIComposer 重构

**当前**：`WdUIComposer` 是静态入口，直接引用所有 WinForms 实现。

```csharp
// 重构后：WdUIComposer 通过组合通用框架实现
public static class WdUIComposer
{
    // 通用组件（来自框架）
    public static IMediator Mediator => Core.Mediator.Instance;
    public static IEventBus Events => Core.EventBus.Instance;
    public static IConfigurationLoader Config => Configuration.ConfigurationLoader.Instance;
    
    // WinForms 特有组件（适配器）
    public static IControlFactory<Control, ControlConfig> Controls => WinFormsControlFactory.Instance;
    public static IViewManager<Form> Views => WinFormsViewManager.Instance;
    public static WinFormsRenderPipeline RenderPipeline => WinFormsRenderPipeline.Instance;
    
    // 布局
    public static LayoutStrategyRegistry StrategyRegistry { get; }
    public static WdUILayoutContext LayoutContext { get; }
}
```

---

## 6. 迁移路径

### Phase 1：创建 Abstractions 项目（低风险）

1. 创建 `Share.UniversalMiddleware.Abstractions` 项目（`net10.0`）
2. 从现有 `Abstractions.cs` 中提取所有接口，按目录拆分
3. 从 `Container.cs` 中提取 `IConfigContainer` 接口
4. 从 `ConfigurationModels/Abstractions/IConfigContainer.cs` 合并接口
5. **消除重复**：删除 `Container.cs` 中的重复定义
6. **验证**：现有代码改用新命名空间引用

### Phase 2：创建 Core 项目（中风险）

1. 创建 `Share.UniversalMiddleware.Core` 项目（`net10.0`）
2. 从 `Infrastructure.cs` 迁移 Mediator/CacheManager/StateManager
3. 从 `BusinessLogic.cs` 迁移 EventBus/DataBindingManager/CommandExecutor
4. 从 `Core.cs` 迁移 JsonPipelineBuilder/MiddlewarePipeline
5. 从 `Serialization.cs` 迁移 UnifiedJsonSerializer/JsonValidator/JsonPathEngine
6. **消除重复**：删除 `Container.cs` 中的 `ContainerManager`
7. **验证**：所有功能通过新项目编译通过

### Phase 3：创建 Configuration 项目（低风险）

1. 创建 `Share.UniversalMiddleware.Configuration` 项目（`net10.0`）
2. 从 `ConfigurationModels/` 迁移所有模型和服务
3. **消除重复**：删除 `Container.cs` 中的 `ContainerConfig` 模型
4. **验证**：WdUI 和 FileDriven 等上层应用正常引用

### Phase 4：创建 Serialization 项目（低风险）

1. 创建 `Share.UniversalMiddleware.Serialization` 项目（`net10.0`）
2. 从 `Serialization/` 子目录迁移 JsonSerializerFactory/JsonConverterRegistrar/JsonPreprocessor
3. 从 `Serialization.cs` 迁移 JsonPrettyPrinter
4. **验证**：序列化功能独立可用

### Phase 5：创建 Rendering 项目（中风险）

1. 创建 `Share.UniversalMiddleware.Rendering` 项目（`net10.0`）
2. 定义 `RenderPipelineBase<TTarget>` 抽象基类
3. 定义 `RenderSession` / `RenderStage` 等基础类型
4. **验证**：WdUI 的 RenderPipeline 可继承基类

### Phase 6：创建 Container 项目（中风险）

1. 创建 `Share.UniversalMiddleware.Container` 项目（`net10.0`）
2. 定义 `ContainerNode` / `ContainerIndex` / `ContainerEventManager`
3. **验证**：WdUI 的 ConfigContainer 可组合使用

### Phase 7：WdUI 适配（高风险）

1. 创建 WinForms 适配器（`WinFormsControlFactory` / `WinFormsViewManager` / `WinFormsRenderPipeline`）
2. 重构 `ConfigContainer` 使用通用框架
3. 重构 `WdUIComposer` 使用通用框架
4. **验证**：WdUI 全部功能正常

---

## 7. 收益总结

### 架构收益

| 维度 | 重构前 | 重构后 |
|------|--------|--------|
| **WinForms 依赖** | 全部项目强制依赖 | 仅 WdUI 项目依赖 |
| **接口重复** | 3 处 `IConfigContainer` 重复 | 唯一版本在 Abstractions |
| **模型重复** | 2 处 `ContainerConfig` 重复 | 唯一版本在 Configuration |
| **框架职责** | 1 个项目 7 个职责 | 6 个框架各司其职 |
| **平台兼容** | 仅 Windows | 核心框架支持跨平台 |
| **可测试性** | 需 WinForms 环境 | 核心框架可独立单元测试 |

### 业务收益

- **配置模型**可被 FileDriven 等非 UI 项目独立使用
- **序列化框架**可被任何项目复用
- **渲染管线**可适配 Avalonia/MAUI 等新 UI 框架
- **容器框架**可被任何需要嵌套容器的场景使用
- **事件总线**可被 EventTriggerFramework 等事件系统复用

### 渐进式迁移

每个 Phase 可独立交付，低风险 Phase（1-4）优先执行，高风险 Phase（7）最后执行。每个 Phase 完成后，现有功能不受影响。
