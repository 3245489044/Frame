# WdUI 非基础UI框架逻辑 → 通用框架迁移方案

## 概述

本文档基于对 WdUI 全部核心源码的分析，识别出 **10 类非基础UI框架逻辑**，并规划将其迁移到三个通用框架项目中：

| 目标项目 | 路径 | 定位 |
|---------|------|------|
| **UniversalJsonMiddleware** | `BasicMethod/UniversalJsonMiddleware/` | 配置驱动、事件总线、中介器、数据绑定、命令执行 |
| **BasicMethod.Pipeline** | `BasicMethod/Pipeline/` | 管线/阶段化执行框架 |
| **UniversalLayout** | `BasicMethod/UniversalLayout/` | 纯数学布局计算、重叠检测、布局优化 |

---

## 迁移总览

| 编号 | 框架逻辑 | 当前在WdUI的位置 | 目标项目 | 迁移类型 |
|------|---------|----------------|---------|---------|
| 1 | 渲染管线（Pipeline） | `Layout/Pipeline/*.cs` | **BasicMethod.Pipeline** | 已有基础，需补充 |
| 2 | 布局策略模式 | `Layout/Strategies/*.cs` | **UniversalLayout** | 新增 |
| 3 | 布局中介器（LayoutMediator） | `UI/LayoutApplier.cs` | **UniversalJsonMiddleware** | 已有基础，需补充 |
| 4 | 配置继承与合并系统 | `UI/WdUI.Configuration.cs` | **UniversalJsonMiddleware** | 新增 |
| 5 | ConfigContainer 嵌套容器系统 | `UI/ConfigContainer.cs` | **UniversalJsonMiddleware** | 新增抽象 |
| 6 | 数据绑定系统 | `UI/Managers.cs` | **UniversalJsonMiddleware** | 已有基础，需补充 |
| 7 | 重叠检测与自动优化 | `UI/AutoLayoutOptimizer.cs` | **UniversalLayout** | 已有基础，需补充 |
| 8 | 事件总线系统 | `UI/WdUI.Events.cs` | **UniversalJsonMiddleware** | 已有基础 |
| 9 | Fluent API 配置构建器 | `WdUIComposer.cs` | **UniversalJsonMiddleware** | 新增 |
| 10 | 布局上下文与状态管理 | `Layout/LayoutContext.cs` | **UniversalLayout** | 新增 |

---

## 详细迁移方案

### 1. 渲染管线（Pipeline）→ BasicMethod.Pipeline

#### 当前状态
WdUI 的 `Layout/Pipeline/` 已基于 `BasicMethod.Pipeline.StagePipeline` 构建，但 WdUI 的 `IPipelineStage`、`PipelineBuilder`、`PipelineContext` 是 WdUI 自己的包装层。

#### 迁移内容
| 文件 | 操作 | 说明 |
|------|------|------|
| `WdUI/Layout/Pipeline/IPipelineStage.cs` | **删除** | 接口已由 `BasicMethod.Pipeline.IStage<Control, UIConfiguration, PipelineContext>` 覆盖 |
| `WdUI/Layout/Pipeline/Pipeline.cs` | **保留并简化** | 去掉对 `BasicMethod.Pipeline.StagePipeline` 的包装，直接使用 |
| `WdUI/Layout/Pipeline/PipelineBuilder.cs` | **删除** | 功能已由 `StagePipelineBuilder` 覆盖 |
| `WdUI/Layout/Pipeline/PipelineContext.cs` | **删除** | 已继承 `BasicMethod.Pipeline.PipelineContext`，无需额外包装 |

#### 需补充到 BasicMethod.Pipeline
- **`IStage<TTarget, TConfig, TContext>`** ✅ 已存在
- **`StagePipeline<TTarget, TConfig, TContext>`** ✅ 已存在
- **`StagePipelineBuilder<TTarget, TConfig, TContext>`** ✅ 已存在
- **`PipelineContext`** ✅ 已存在

**结论：无需迁移，WdUI 的 Pipeline 层可直接删除包装代码，直接使用 BasicMethod.Pipeline。**

---

### 2. 布局策略模式（Strategy Pattern）→ UniversalLayout

#### 当前状态
WdUI 的 `Layout/Strategies/` 包含 `ILayoutStrategy`、`SplitContainerStrategy`、`PanelStrategy`、`GroupBoxStrategy`，以及 `LayoutStrategyRegistry`。

#### 迁移内容
| 文件 | 操作 | 说明 |
|------|------|------|
| `WdUI/Layout/Strategies/ILayoutStrategy.cs` | **迁移到 UniversalLayout** | 抽象为通用接口，去除 WinForms 依赖 |
| `WdUI/Layout/Strategies/SplitContainerStrategy.cs` | **保留在 WdUI** | WinForms 特定实现 |
| `WdUI/Layout/Strategies/PanelStrategy.cs` | **保留在 WdUI** | WinForms 特定实现 |
| `WdUI/Layout/Strategies/GroupBoxStrategy.cs` | **保留在 WdUI** | WinForms 特定实现 |
| `WdUI/Layout/LayoutStrategyRegistry.cs` | **迁移到 UniversalLayout** | 通用注册表逻辑 |

#### 需在 UniversalLayout 中新增
```csharp
// UniversalLayout/LayoutStrategy/ILayoutStrategy.cs
namespace UniversalLayoutMath.LayoutStrategy
{
    /// <summary>
    /// 通用布局策略接口 - 适配任意 UI 框架
    /// </summary>
    public interface ILayoutStrategy<TControl, TConfig, TContext>
    {
        string ContainerType { get; }
        int StageOrder { get; }
        void Execute(TControl control, TConfig config, TContext context);
    }

    /// <summary>
    /// 布局策略注册表 - 通用实现
    /// </summary>
    public class LayoutStrategyRegistry<TControl, TConfig, TContext>
    {
        private readonly Dictionary<string, ILayoutStrategy<TControl, TConfig, TContext>> _strategies = new(StringComparer.OrdinalIgnoreCase);
        
        public void RegisterStrategy(ILayoutStrategy<TControl, TConfig, TContext> strategy) { ... }
        public ILayoutStrategy<TControl, TConfig, TContext>? GetStrategy(string containerType) { ... }
        public bool TryExecute(TControl control, TConfig config, TContext context) { ... }
    }
}
```

---

### 3. 布局中介器（LayoutMediator）→ UniversalJsonMiddleware

#### 当前状态
WdUI 的 `UI/LayoutApplier.cs` 中的 `LayoutMediator` 封装了 `SmartLayoutEngine`、`ILayoutCalculator`、`ILayoutValidator`，提供 `ConvertFromConfig()`、`Apply()` 等方法。

UniversalJsonMiddleware 的 `Abstractions.cs` 已定义：
- `ILayoutCalculator` ✅
- `ILayoutValidator` ✅
- `ILayoutContainer` ✅
- `ILayoutElement` ✅
- `ISmartLayoutEngine` ✅
- `LayoutDefinition`、`ElementDefinition`、`LayoutPosition` 等模型 ✅

#### 迁移内容
| 文件 | 操作 | 说明 |
|------|------|------|
| `WdUI/UI/LayoutApplier.cs` 中的 `LayoutMediator` | **迁移到 UniversalJsonMiddleware** | 补充 `ConvertFromConfig`、`Apply` 等方法 |
| `WdUI/UI/LayoutApplier.cs` 中的 `ILayoutMediator` | **迁移到 UniversalJsonMiddleware** | 接口定义 |

#### 需在 UniversalJsonMiddleware 中新增
```csharp
// UniversalJsonMiddleware/Layout/LayoutMediator.cs
namespace UniversalJsonMiddleware.Layout
{
    public interface ILayoutMediator
    {
        LayoutDefinition CreateDefinition(string name, LayoutType type);
        JsonValidationResult Validate(LayoutDefinition definition);
        LayoutOutput Calculate(LayoutDefinition definition, LayoutInput input);
        bool Apply(ILayoutContainer container, LayoutDefinition definition);
        LayoutDefinition ConvertFromConfig<TConfig>(TConfig config, Func<TConfig, LayoutPosition?> getPosition);
        LayoutDefinition ConvertFromConfigs<TConfig>(IEnumerable<TConfig> configs, Func<TConfig, LayoutPosition?> getPosition);
    }
}
```

---

### 4. 配置继承与合并系统 → UniversalJsonMiddleware

#### 当前状态
WdUI 的 `UI/WdUI.Configuration.cs` 包含完整的配置模型和继承合并逻辑。

#### 迁移内容
| 文件 | 操作 | 说明 |
|------|------|------|
| `WdUI/UI/WdUI.Configuration.cs` 中的 `IConfigurationLoader` | **迁移到 UniversalJsonMiddleware** | 通用配置加载器接口 |
| `WdUI/UI/WdUI.Configuration.cs` 中的 `ConfigurationLoader` | **迁移到 UniversalJsonMiddleware** | 实现，含 Extends 继承、Merge、缓存 |
| `WdUI/UI/WdUI.Configuration.cs` 中的配置模型类 | **迁移到 UniversalJsonMiddleware** | `UIConfiguration`、`FormConfig`、`ViewConfig`、`ControlConfig` 等 |

#### 需在 UniversalJsonMiddleware 中新增
```csharp
// UniversalJsonMiddleware/Configuration/IConfigurationLoader.cs
namespace UniversalJsonMiddleware.Configuration
{
    public interface IConfigurationLoader<TConfig> where TConfig : class
    {
        Task<TConfig> LoadConfigAsync(string configPath);
        TConfig GetConfig(string configPath);
        Task SaveConfigAsync(string configPath, TConfig config);
        Task ReloadConfigAsync(string configPath);
        void ClearCache();
    }

    // 配置继承支持
    public interface IExtendableConfig
    {
        string? Extends { get; set; }
    }

    // 通用配置合并器
    public interface IConfigMerger<TConfig>
    {
        TConfig Merge(TConfig baseConfig, TConfig derivedConfig);
    }
}
```

---

### 5. ConfigContainer 嵌套容器系统 → UniversalJsonMiddleware

#### 当前状态
WdUI 的 `UI/ConfigContainer.cs` 是 WinForms 特定的嵌套容器实现。

#### 迁移内容
| 文件 | 操作 | 说明 |
|------|------|------|
| `WdUI/UI/ConfigContainer.cs` 中的 `IConfigContainer` | **迁移到 UniversalJsonMiddleware** | 通用接口 |
| `WdUI/UI/ConfigContainer.cs` 中的 `ContainerConfig` | **迁移到 UniversalJsonMiddleware** | 配置模型 |
| `WdUI/UI/ConfigContainer.cs` 中的 `ContainerSourceTypes` | **迁移到 UniversalJsonMiddleware** | 枚举 |

#### 需在 UniversalJsonMiddleware 中新增
```csharp
// UniversalJsonMiddleware/Configuration/IConfigContainer.cs
namespace UniversalJsonMiddleware.Configuration
{
    /// <summary>
    /// 通用嵌套容器接口 - 适配任意 UI 框架
    /// </summary>
    public interface IConfigContainer
    {
        string ContainerName { get; }
        bool IsLoaded { get; }
        event EventHandler? ContentChanged;
        
        Task LoadConfigAsync(string configPath);
        Task LoadConfigFromJsonAsync(string json);
        Task LoadConfigAsync<TConfig>(TConfig config) where TConfig : class;
        
        IConfigContainer? FindSubContainer(string name);
        IConfigContainer CreateSubContainer(string name, ContainerConfig? config = null);
        IEnumerable<IConfigContainer> GetSubContainers();
        
        object? FindControlByName(string name);
        T? FindControl<T>(string name) where T : class;
        
        void ClearContent();
    }

    public class ContainerConfig
    {
        public ContainerSourceTypes SourceType { get; set; } = ContainerSourceTypes.Dynamic;
        public bool AutoLoad { get; set; } = true;
        public string? DefaultConfig { get; set; }
    }

    public enum ContainerSourceTypes
    {
        Dynamic,
        Static
    }
}
```

---

### 6. 数据绑定系统 → UniversalJsonMiddleware

#### 当前状态
UniversalJsonMiddleware 的 `Infrastructure.cs` 中已有 `DataBindingManager`（在 `UniversalJsonMiddleware.Binding` 命名空间下），WdUI 的 `Managers.cs` 中有另一个 `DataBindingManager`。

#### 迁移内容
| 文件 | 操作 | 说明 |
|------|------|------|
| `WdUI/UI/Managers.cs` 中的 `DataBindingManager` | **删除** | 功能已被 `UniversalJsonMiddleware.Binding.DataBindingManager` 覆盖 |
| `WdUI/UI/Managers.cs` 中的 `BindingInfo` | **删除** | 内部类，无需迁移 |

#### 需补充到 UniversalJsonMiddleware
UniversalJsonMiddleware 的 `IDataBindingManager` 已定义，但缺少以下方法：
- `BindControl(Control control, string dataSourceKey, string propertyName)` — WinForms 特定，保留在 WdUI
- `SubscribeToProperty<T>(string key, string propertyName, Action<T> handler)` — 通用，可补充

**结论：WdUI 的 DataBindingManager 可删除，直接使用 UniversalJsonMiddleware 的版本。**

---

### 7. 重叠检测与自动优化 → UniversalLayout

#### 当前状态
UniversalLayout 已有完整的数学层：
- `OverlapMath` ✅ — 重叠检测核心算法
- `ConflictGraph` ✅ — 冲突图分析
- `MultiObjectiveOptimizer` ✅ — 多目标优化
- `ProportionalPreflightChecker` ✅ — 比例空间预检
- `ProportionalMath` ✅ — 比例计算
- `SmartSpacingCalculator` ✅ — 智能间距计算

WdUI 的 `AutoLayoutOptimizer.cs` 中的 `AutoLayoutOptimizer` 和 `OptimizationResult` 是**应用层**代码，依赖 `ElementOutput`（来自 UniversalJsonMiddleware.Abstractions）。

#### 迁移内容
| 文件 | 操作 | 说明 |
|------|------|------|
| `WdUI/UI/AutoLayoutOptimizer.cs` 中的 `AutoLayoutOptimizer` | **迁移到 UniversalLayout** | 使用 UniversalLayout 的 `OverlapMath.ElementDesc` 替代 `ElementOutput` |
| `WdUI/UI/AutoLayoutOptimizer.cs` 中的 `OptimizationResult` | **迁移到 UniversalLayout** | 使用 UniversalLayout 的 `OverlapReport` |
| `WdUI/UI/AutoLayoutOptimizer.cs` 中的 `LayoutOptimizationExtensions` | **迁移到 UniversalLayout** | 扩展方法 |

#### 需在 UniversalLayout 中新增
```csharp
// UniversalLayout/AutoLayoutOptimizer.cs
namespace UniversalLayoutMath
{
    /// <summary>
    /// 自动布局优化器 - 基于重叠计算的自动排布优化
    /// 纯数学实现，零 UI 框架依赖
    /// </summary>
    public class AutoLayoutOptimizer
    {
        public int MaxIterations { get; set; } = 20;
        public double ConvergenceThreshold { get; set; } = 0.01;
        
        public OptimizationResult Optimize(List<OverlapMath.ElementDesc> elements, int parentWidth, int parentHeight) { ... }
    }

    public class OptimizationResult
    {
        public List<OverlapMath.ElementDesc> FinalElements { get; set; } = new();
        public List<OverlapReport> History { get; set; } = new();
        public int IterationsUsed { get; set; }
        public bool Converged { get; set; }
        public OverlapReport? InitialReport { get; set; }
        public OverlapReport? FinalReport { get; set; }
    }
}
```

---

### 8. 事件总线系统 → UniversalJsonMiddleware

#### 当前状态
UniversalJsonMiddleware 的 `Infrastructure.cs` 中已有完整的 `EventBus` 实现 ✅

WdUI 的 `WdUI.Events.cs` 只是对 `IEventBus` 的静态包装。

#### 迁移内容
| 文件 | 操作 | 说明 |
|------|------|------|
| `WdUI/UI/WdUI.Events.cs` | **保留在 WdUI** | 静态便捷方法，WinForms 特定 |

**结论：无需迁移，UniversalJsonMiddleware 已有完整实现。**

---

### 9. Fluent API 配置构建器 → UniversalJsonMiddleware

#### 当前状态
WdUI 的 `WdUIComposer.cs` 中包含 `UiConfigBuilder`、`ViewConfigBuilder`、`ControlConfigBuilder`。

#### 迁移内容
| 文件 | 操作 | 说明 |
|------|------|------|
| `WdUI/UI/WdUIComposer.cs` 中的 `UiConfigBuilder` | **迁移到 UniversalJsonMiddleware** | 通用配置构建器 |
| `WdUI/UI/WdUIComposer.cs` 中的 `ViewConfigBuilder` | **迁移到 UniversalJsonMiddleware** | 通用 View 构建器 |
| `WdUI/UI/WdUIComposer.cs` 中的 `ControlConfigBuilder` | **迁移到 UniversalJsonMiddleware** | 通用 Control 构建器 |

#### 需在 UniversalJsonMiddleware 中新增
```csharp
// UniversalJsonMiddleware/Configuration/ConfigBuilder.cs
namespace UniversalJsonMiddleware.Configuration
{
    public class ConfigBuilder<TConfig> where TConfig : class, new()
    {
        protected readonly TConfig _config = new();
        public TConfig Build() => _config;
    }

    // 具体构建器由各 UI 框架实现
    public interface IViewConfigBuilder<TViewConfig>
    {
        IViewConfigBuilder<TViewConfig> WithName(string name);
        IViewConfigBuilder<TViewConfig> WithType(string type);
        IViewConfigBuilder<TViewConfig> AddControl(string type, string name, Action<IControlConfigBuilder>? configure = null);
    }

    public interface IControlConfigBuilder
    {
        IControlConfigBuilder At(int x, int y);
        IControlConfigBuilder WithSize(int width, int height);
        IControlConfigBuilder WithText(string text);
        IControlConfigBuilder WithLayout(double left = -1, double top = -1, double width = -1, double height = -1);
    }
}
```

---

### 10. 布局上下文与状态管理 → UniversalLayout

#### 当前状态
WdUI 的 `Layout/LayoutContext.cs` 和 `Layout/LayoutState.cs` 提供布局上下文和运行时状态存储。

#### 迁移内容
| 文件 | 操作 | 说明 |
|------|------|------|
| `WdUI/Layout/ILayoutContext.cs` | **迁移到 UniversalLayout** | 通用接口，去除 WinForms 依赖 |
| `WdUI/Layout/LayoutState.cs` | **迁移到 UniversalLayout** | 通用状态模型 |
| `WdUI/Layout/LayoutContext.cs` | **保留在 WdUI** | WinForms 特定实现 |

#### 需在 UniversalLayout 中新增
```csharp
// UniversalLayout/LayoutContext/ILayoutContext.cs
namespace UniversalLayoutMath.LayoutContext
{
    /// <summary>
    /// 通用布局上下文接口 - 适配任意 UI 框架
    /// </summary>
    public interface ILayoutContext<TControl, TConfig>
    {
        // 运行时状态存储
        IWeakStateManager<TControl, LayoutState> State { get; }
        
        // 在指定父容器中查找控件
        TControl? FindControlInParent(TControl parent, string name);
        
        // 应用比例布局
        void ApplyLayout(TControl parent, List<TConfig> configs);
        
        // 执行布局策略
        bool TryExecuteStrategy(TControl control, TConfig config);
        
        // 订阅 Resize 事件（自动去重）
        void SubscribeResize(TControl control, Action<TControl> callback);
    }

    /// <summary>
    /// 布局运行时状态 - 通用
    /// </summary>
    public class LayoutState
    {
        public double? SplitterRatio { get; set; }
        public object? Config { get; set; }
    }

    /// <summary>
    /// 弱引用状态管理器 - 基于 ConditionalWeakTable
    /// </summary>
    public interface IWeakStateManager<TKey, TValue> where TKey : class where TValue : class
    {
        TValue GetOrCreate(TKey key);
        bool TryGet(TKey key, out TValue? value);
        bool Remove(TKey key);
    }
}
```

---

## 迁移优先级与路线图

### 第一阶段：基础层迁移（无依赖）
| 优先级 | 迁移项 | 目标 | 工作量 |
|--------|--------|------|--------|
| P0 | 重叠检测与自动优化 → UniversalLayout | 纯数学，无外部依赖 | 小 |
| P0 | 布局策略注册表 → UniversalLayout | 泛型化，无外部依赖 | 小 |
| P0 | 布局上下文接口 → UniversalLayout | 泛型化，无外部依赖 | 小 |

### 第二阶段：配置层迁移（依赖第一阶段）
| 优先级 | 迁移项 | 目标 | 工作量 |
|--------|--------|------|--------|
| P1 | 配置继承与合并系统 → UniversalJsonMiddleware | 依赖配置模型 | 中 |
| P1 | Fluent API 配置构建器 → UniversalJsonMiddleware | 依赖配置模型 | 小 |
| P1 | ConfigContainer 接口 → UniversalJsonMiddleware | 依赖配置模型 | 中 |

### 第三阶段：编排层迁移（依赖前两阶段）
| 优先级 | 迁移项 | 目标 | 工作量 |
|--------|--------|------|--------|
| P2 | 布局中介器补充 → UniversalJsonMiddleware | 依赖布局模型和配置模型 | 中 |
| P2 | 数据绑定补充 → UniversalJsonMiddleware | 已有基础 | 小 |

### 第四阶段：WdUI 清理
| 优先级 | 迁移项 | 操作 | 工作量 |
|--------|--------|------|--------|
| P3 | Pipeline 包装层删除 | 删除 `IPipelineStage`、`PipelineBuilder`、`PipelineContext` | 小 |
| P3 | DataBindingManager 删除 | 改用 UniversalJsonMiddleware 版本 | 小 |
| P3 | 所有迁移后的文件引用更新 | 更新 using 和命名空间 | 中 |

---

## 迁移后 WdUI 的依赖关系

```
WdUI
 ├── BasicMethod.Pipeline          (管线框架)
 ├── UniversalJsonMiddleware       (配置、事件总线、中介器、数据绑定、布局中介器)
 └── UniversalLayout               (数学计算、重叠检测、布局优化、策略注册表、上下文)
```

WdUI 将只保留：
1. **WinForms 特定实现**（ControlFactory、ConfigContainer、ViewManager）
2. **布局策略的 WinForms 实现**（SplitContainerStrategy、PanelStrategy、GroupBoxStrategy）
3. **WdUIComposer 编排器**（作为 WinForms 的入口点）
4. **WdUILayout 便捷方法**（WinForms 特定的布局应用）

---

## 文件迁移对照表

### 从 WdUI 迁移到 UniversalLayout

| 源文件 | 目标文件 | 说明 |
|--------|---------|------|
| `WdUI/Layout/Strategies/ILayoutStrategy.cs` | `UniversalLayout/LayoutStrategy/ILayoutStrategy.cs` | 泛型化 |
| `WdUI/Layout/LayoutStrategyRegistry.cs` | `UniversalLayout/LayoutStrategy/LayoutStrategyRegistry.cs` | 泛型化 |
| `WdUI/Layout/ILayoutContext.cs` | `UniversalLayout/LayoutContext/ILayoutContext.cs` | 泛型化 |
| `WdUI/Layout/LayoutState.cs` | `UniversalLayout/LayoutContext/LayoutState.cs` | 通用化 |
| `WdUI/UI/AutoLayoutOptimizer.cs` | `UniversalLayout/AutoLayoutOptimizer.cs` | 使用 `ElementDesc` 替代 `ElementOutput` |
| `WdUI/UI/OverlapDetection.cs` | `UniversalLayout/OverlapDetection.cs` | 补充 `EnhancedValidationResult` |

### 从 WdUI 迁移到 UniversalJsonMiddleware

| 源文件 | 目标文件 | 说明 |
|--------|---------|------|
| `WdUI/UI/WdUI.Configuration.cs` (部分) | `UniversalJsonMiddleware/Configuration/` | 配置模型 + 加载器 + 合并器 |
| `WdUI/UI/LayoutApplier.cs` (部分) | `UniversalJsonMiddleware/Layout/LayoutMediator.cs` | 布局中介器 |
| `WdUI/UI/ConfigContainer.cs` (接口部分) | `UniversalJsonMiddleware/Configuration/IConfigContainer.cs` | 容器接口 |
| `WdUI/UI/WdUIComposer.cs` (Builder部分) | `UniversalJsonMiddleware/Configuration/ConfigBuilder.cs` | Fluent API |
| `WdUI/UI/Managers.cs` (DataBinding部分) | 已存在，删除WdUI版本 | 数据绑定 |

### 从 WdUI 删除（被 BasicMethod.Pipeline 覆盖）

| 文件 | 说明 |
|------|------|
| `WdUI/Layout/Pipeline/IPipelineStage.cs` | 由 `IStage<Control, UIConfiguration, PipelineContext>` 覆盖 |
| `WdUI/Layout/Pipeline/PipelineBuilder.cs` | 由 `StagePipelineBuilder` 覆盖 |
| `WdUI/Layout/Pipeline/PipelineContext.cs` | 由 `BasicMethod.Pipeline.PipelineContext` 覆盖 |

---

## 迁移后的接口依赖关系

```
UniversalLayout (纯数学，零依赖)
  ├── Rect, ProportionalRect
  ├── OverlapMath, ConflictGraph, MultiObjectiveOptimizer
  ├── ProportionalPreflightChecker, ProportionalMath
  ├── AutoLayoutOptimizer
  ├── ILayoutStrategy<TControl, TConfig, TContext>  ← 泛型
  ├── LayoutStrategyRegistry<TControl, TConfig, TContext>  ← 泛型
  └── ILayoutContext<TControl, TConfig>  ← 泛型

BasicMethod.Pipeline (零依赖)
  ├── IStage<TTarget, TConfig, TContext>
  ├── StagePipeline<TTarget, TConfig, TContext>
  ├── StagePipelineBuilder<TTarget, TConfig, TContext>
  └── PipelineContext

UniversalJsonMiddleware (依赖 BasicMethod.Pipeline + UniversalLayout)
  ├── Abstractions: IMediator, IEventBus, ICacheManager, IStateManager
  ├── Abstractions: ILayoutCalculator, ILayoutValidator, ILayoutContainer
  ├── Abstractions: LayoutDefinition, ElementDefinition, LayoutPosition
  ├── Configuration: IConfigurationLoader<TConfig>, IConfigMerger<TConfig>
  ├── Configuration: IConfigContainer, ConfigBuilder
  ├── Layout: ILayoutMediator, LayoutMediator
  ├── Messaging: Mediator, EventBus, CacheManager, StateManager
  └── Binding: DataBindingManager
```

---

## 迁移步骤清单

- [ ] **第一阶段：UniversalLayout 补充**
  - [ ] 创建 `UniversalLayout/LayoutStrategy/` 目录，迁移 `ILayoutStrategy` 和 `LayoutStrategyRegistry`（泛型化）
  - [ ] 创建 `UniversalLayout/LayoutContext/` 目录，迁移 `ILayoutContext` 和 `LayoutState`（泛型化）
  - [ ] 迁移 `AutoLayoutOptimizer` 到 `UniversalLayout/AutoLayoutOptimizer.cs`（使用 `ElementDesc`）
  - [ ] 迁移 `OverlapDetection.cs` 中的 `EnhancedValidationResult`

- [ ] **第二阶段：UniversalJsonMiddleware 补充**
  - [ ] 创建 `UniversalJsonMiddleware/Configuration/` 目录
  - [ ] 迁移配置模型（`UIConfiguration`、`FormConfig`、`ViewConfig`、`ControlConfig` 等）
  - [ ] 迁移 `IConfigurationLoader` 和 `ConfigurationLoader`（含 Extends 继承、Merge、缓存）
  - [ ] 迁移 `IConfigContainer` 接口和 `ContainerConfig`
  - [ ] 迁移 Fluent API 构建器（`ConfigBuilder`、`ViewConfigBuilder`、`ControlConfigBuilder`）
  - [ ] 创建 `UniversalJsonMiddleware/Layout/LayoutMediator.cs`，迁移 `ILayoutMediator`

- [ ] **第三阶段：WdUI 清理**
  - [ ] 删除 `WdUI/Layout/Pipeline/IPipelineStage.cs`
  - [ ] 删除 `WdUI/Layout/Pipeline/PipelineBuilder.cs`
  - [ ] 删除 `WdUI/Layout/Pipeline/PipelineContext.cs`
  - [ ] 简化 `WdUI/Layout/Pipeline/Pipeline.cs`（直接使用 `StagePipeline`）
  - [ ] 删除 `WdUI/UI/Managers.cs` 中的 `DataBindingManager`（改用 UniversalJsonMiddleware 版本）
  - [ ] 更新所有 using 和命名空间引用

- [ ] **第四阶段：测试与验证**
  - [ ] 验证 UniversalLayout 编译通过
  - [ ] 验证 UniversalJsonMiddleware 编译通过
  - [ ] 验证 WdUI 编译通过
  - [ ] 验证 WdUI 功能正常（渲染、布局、事件、数据绑定）
