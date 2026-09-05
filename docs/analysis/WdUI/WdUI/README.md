# WdUI Framework

**WdUI** 是一个基于 .NET 10.0 和 Windows Forms 构建的轻量级 UI 框架，核心设计理念是实现**配置驱动的用户界面开发**。通过 JSON 配置文件定义窗体布局、控件属性、事件处理逻辑，实现 UI 与业务代码的完全分离。

---

## 目录

- [框架概述](#框架概述)
- [核心特性](#核心特性)
- [架构设计](#架构设计)
- [项目结构](#项目结构)
- [JSON 配置格式](#json-配置格式)
- [使用指南](#使用指南)
- [布局系统](#布局系统)
- [扩展开发](#扩展开发)
- [最佳实践](#最佳实践)
- [依赖项](#依赖项)
- [许可协议](#许可协议)

---

## 框架概述

框架采用模块化架构设计，各组件通过接口通信，保持高可扩展性和可测试性。核心组件包括：

| 组件 | 职责 |
|------|------|
| **配置加载器** | 解析 JSON 配置文件，支持缓存和热重载 |
| **控件工厂** | 根据配置创建 WinForms 控件实例 |
| **事件中介器** | 基于中介者模式的控件间通信 |
| **布局管理器** | 比例布局、智能布局、重叠检测与优化 |

## 核心特性

### JSON 驱动的窗体生成

通过 JSON 配置定义窗体标题、大小、起始位置、视图等信息，框架自动解析生成 WinForms 窗体。支持多视图切换、嵌套控件结构，调整布局只需修改配置文件，无需重新编译。

```json
{
  "Form": {
    "Title": "我的应用程序",
    "Width": 1024,
    "Height": 768
  },
  "Views": [
    {
      "Name": "MainView",
      "Controls": []
    }
  ]
}
```

支持**热重载**：配置文件变更时自动检测并重新加载，实时更新界面。配置加载器内置缓存机制，避免重复文件读取。

### 控件工厂模式

通过 `ControlFactory` 和注册的控件构建器（`IControlFactoryBuilder`）创建控件。内置 15 种常用控件构建器：

`Panel` · `Button` · `TextBox` · `Label` · `ListView` · `ComboBox` · `CheckBox` · `GroupBox` · `TabControl` · `ListBox` · `PictureBox` · `TrackBar` · `ProgressBar` · `DataGridView`

遵循**开闭原则**，添加新控件类型只需实现 `IControlFactoryBuilder` 接口并注册。

### 事件驱动架构

基于**中介者模式**，`UIMediator` 协调所有控件交互。控件不直接相互引用，通过事件订阅/发布通信，打破耦合。

- **字符串事件**：使用事件名称标识，适合简单通知
- **类型化事件**：使用 .NET 类型标识，适合类型安全场景

内置 `StateManager`（状态管理）和 `CacheManager`（缓存管理，支持过期时间）。

## 架构设计

### 分层结构

```
WdUI/
├── Core/          ← 核心层：配置模型、验证器、辅助类
├── Contracts/     ← 契约层：组件间通信接口
├── Framework/     ← 框架层：窗体基类、事件系统、日志服务
├── Controls/      ← 控制层：控件工厂和构建器
├── Managers/      ← 管理层：布局、数据绑定、视图、事件管理
├── Layout/        ← 布局层：比例布局、智能布局、重叠检测
├── JsonDriven/    ← JSON 驱动层：配置驱动的窗体生成
├── Configuration/ ← 配置层：配置加载和验证
└── Utils/         ← 工具层：通用工具类
```

遵循**依赖倒置原则**，上层依赖下层，下层不依赖上层。Contract 层定义接口规范，确保组件松耦合。

### 核心类

| 类 | 职责 |
|----|------|
| `JsonDrivenForm` | 窗体基类，负责初始化布局、加载配置、创建视图、视图切换 |
| `ControlFactory` | 控件工厂，维护构建器字典，支持动态注册 |
| `ControlBuilder` | 控件构建器，配置控件属性和事件 |
| `UIMediator` | 事件中介器，负责事件订阅、发布和路由 |

### 设计模式应用

| 模式 | 应用 |
|------|------|
| **工厂模式** | `ControlFactory` + 控件构建器 |
| **构建器模式** | `ControlBuilder` 链式调用 |
| **单例模式** | `ConfigurationLoader` |
| **中介者模式** | `UIMediator` 控件间通信 |
| **策略模式** | 布局策略可插拔 |
| **观察者模式** | `StateManager.StateChanged` 事件 |

## 项目结构

```
WdUI/
├── Core/
│   └── Config/           # 核心配置模型
├── UI/
│   ├── Configuration/    # 配置加载和验证
│   ├── Contracts/        # 接口定义
│   ├── Controls/         # 控件工厂和构建器
│   ├── Core/             # 核心功能（验证、异步、单例）
│   ├── Framework/        # 窗体基类和中介器
│   ├── JsonDriven/       # JSON 驱动实现
│   ├── Layout/           # 布局管理
│   ├── Managers/         # 各种管理器
│   └── Utils/            # 工具类
└── Test/                 # 测试项目
```

## JSON 配置格式

### 窗体配置

```json
{
  "Form": {
    "Title": "我的应用程序",
    "Width": 1024,
    "Height": 768,
    "StartPosition": "CenterScreen"
  },
  "Views": [
    {
      "Name": "MainView",
      "Controls": []
    }
  ]
}
```

- `Title`：窗体标题
- `Width`/`Height`：初始尺寸
- `StartPosition`：起始位置（`CenterScreen`/`CenterParent`/`Manual`/`WindowsDefaultLocation`/`WindowsDefaultBounds`）
- `Views`：视图数组，每个视图可包含多个控件

### 控件配置

```json
{
  "Name": "btnSubmit",
  "Type": "button",
  "Text": "提交",
  "Position": {
    "X": 100, "Y": 50,
    "Width": 120, "Height": 30,
    "Dock": "None",
    "Anchor": "Top,Left"
  },
  "Style": { "BackColor": "LightBlue" },
  "Enabled": true,
  "Events": { "Click": "OnSubmitClick" }
}
```

| 属性 | 说明 |
|------|------|
| `Name` | 控件名称，用于代码中查找 |
| `Type` | 控件类型，对应 `ControlFactory` 注册的构建器 |
| `Text` | 显示文本 |
| `Position` | 位置和尺寸，支持 `Dock`/`Anchor` |
| `Style` | 样式定义 |
| `Events` | 事件与处理动作映射 |

### 控件类型详解

| 控件 | 特有属性 |
|------|----------|
| **TextBox** | `Multiline`、`ReadOnly`、`Placeholder`、`AcceptsReturn`、`ScrollBars` |
| **ListView** | `Columns`、`FullRowSelect`、`GridLines` |
| **ComboBox** | `Items`、`ViewIndex` |
| **TabControl** | `Tabs` 数组（每个 TabPage 含 `Text`、`Name`、`Controls`） |
| **DataGridView** | `Columns`、`AutoSizeColumnsMode`、`AllowUserToAddRows` |

## 使用指南

### 环境配置

- 基于 .NET 10.0，需 Windows 10+
- 引用 WdUI 项目 + `FunctionExecution` + `ReflectionMethod`
- TargetFramework 设为 `net10.0-windows`
- 依赖 `Microsoft.Extensions.Logging`

### 基础使用示例

```csharp
public class MainForm : JsonDrivenBaseForm
{
    private Button? _btnSubmit;
    private TextBox? _txtInput;

    public MainForm() : base(new UIMediator(), "config/main.json") { }

    protected override void InitializeControls()
    {
        _btnSubmit = GetControl<Button>("btnSubmit");
        _txtInput = GetControl<TextBox>("txtInput");
        Mediator.Subscribe("OnSubmitClick", HandleSubmit);
    }

    private async Task HandleSubmit(object? data)
    {
        var input = _txtInput?.Text ?? string.Empty;
        await Mediator.NotifyAsync("ProcessData", input);
    }
}

// Program.cs
[STAThread]
static async Task Main()
{
    ApplicationConfiguration.Initialize();
    var form = new MainForm();
    await form.InitializeAsync();
    Application.Run(form);
}
```

### 事件处理

```json
"Events": { "Click": "OnButtonClick" }
```

```csharp
await Mediator.Subscribe("OnButtonClick", async (data) =>
{
    MessageBox.Show("按钮被点击了！");
});
```

### 状态管理

```csharp
// 设置状态
Mediator.State.SetState("CurrentUser", "张三");

// 获取状态
var user = Mediator.State.GetState<string>("CurrentUser");

// 订阅状态变更
Mediator.State.StateChanged += (sender, args) =>
{
    Console.WriteLine($"状态 {args.Key} 从 {args.OldValue} 变更为 {args.NewValue}");
};
```

### 缓存管理

```csharp
// 设置缓存，过期时间 5 分钟
Mediator.Cache.Set("UserList", userList, TimeSpan.FromMinutes(5));

// 获取缓存
var cachedList = Mediator.Cache.Get<List<User>>("UserList");

// 检查缓存是否存在
if (Mediator.Cache.Contains("UserList")) { /* 使用缓存数据 */ }
```

### 配置验证

```csharp
var validator = new ConfigValidator();
var result = validator.ValidateAndGetFirstError(jsonContent);
if (result != null)
{
    Console.WriteLine($"配置错误: {result.Message} (第 {result.Line} 行, 第 {result.Position} 列)");
}
```

### 日志系统

`UIConsoleLogger` 记录以下信息：

- **属性变更日志**：ViewModel 属性变更
- **事件发布日志**：Mediator 事件
- **状态变更日志**：StateManager 状态变更
- **数据绑定日志**：控件与数据绑定
- **操作日志**：用户操作
- **错误日志**：异常信息

## 布局系统

### 架构分层

布局逻辑采用分层架构，WdUI 中的布局代码全部委托到基础类库，消除重复代码，统一布局入口。

```
BasicMethod/
├── UniversalLayoutMath/          ← 纯数学层
│   ├── OverlapMath.cs           重叠检测算法
│   ├── ProportionalMath.cs      比例定位计算
│   ├── StackLayoutMath.cs       堆叠布局计算
│   ├── ConflictGraph.cs         冲突图分析
│   ├── MultiObjectiveOptimizer.cs 多策略评分优化
│   ├── ClampMath.cs             尺寸约束夹持
│   └── LayFrame/                管线化布局计算器
│
├── UniversalJsonMiddleware/
│   ├── Abstractions.cs          布局模型定义
│   └── Infrastructure.cs        布局引擎层
│       ├── Layout.*             各布局计算委托
│       └── Engine.SmartLayoutEngine 智能布局引擎
│           ├── Apply/Refresh/Attach/Detach
│           ├── DetectAndLogOverlaps
│           ├── ResolveOverlaps
│           ├── WinFormsContainerAdapter
│           ├── WinFormsElementAdapter
│           └── WinFormsSmartLayoutExtensions
│
WdUI/
└── UI/
    ├── LayoutApplier.cs         布局应用器（委托到基础类）
    ├── WdUIComposer.cs          布局编排（委托到基础类）
    └── AutoLayoutOptimizer.cs   自动布局优化（委托到基础类）
```

### 委托化映射

| WdUIComposer 方法 | 委托到 | 状态 |
|-------------------|--------|------|
| `ApplySmartLayout(parent, config?)` | `parent.ApplySmartLayout()` | ✅ |
| `RefreshSmartLayout(parent)` | `parent.RefreshSmartLayout()` | ✅ |
| `ApplyProportionalLayout(container)` | `container.ApplyProportionalLayoutFromTag()` | ✅ |
| `DetectOverlaps(parent)` | `BaseOverlapMath.DetectOverlapsInOutput()` | ✅ |
| `DetectOverlapsInDefinition(definition)` | 保留（比例空间重叠检测） | ✅ |
| `OptimizeLayout(parent, logger?)` | 保留（AutoLayoutOptimizer） | ✅ |
| `DrawOverlaps(target, report)` | 保留（WdUI 特有调试功能） | ✅ |
| `EnableRuntimeOverlapMonitoring(target, callback?)` | 保留（WdUI 特有运行时监控） | ✅ |

### 使用方式

#### 智能布局（推荐）

```csharp
// 应用智能布局（从 Tag 读取 LayoutDefinition）
parent.ApplySmartLayout();

// 附加智能布局（带 Resize 事件）
parent.AttachSmartLayout(config);

// 刷新布局
parent.RefreshSmartLayout();

// 分离布局
parent.DetachSmartLayout();
```

#### 比例布局

```csharp
// 直接计算并应用比例布局
ProportionalMath.ApplyProportionalToControl(control, 0.1, 0.1, 0.8, 0.8);

// 从 Tag 递归应用（WdUI 兼容方式）
container.ApplyProportionalLayoutFromTag(tag => {
    if (tag is ControlConfig cfg && cfg.Layout != null)
        return (cfg.Layout.Left, cfg.Layout.Top, cfg.Layout.Width, cfg.Layout.Height);
    return null;
});
```

#### 重叠检测

```csharp
// 检测控件重叠
var report = OverlapMath.DetectOverlapsInOutput(elements, parentWidth, parentHeight);
if (report.HasOverlap)
{
    Console.WriteLine($"检测到 {report.Overlaps.Count} 处重叠");
}
```

### 收益评估

| 指标 | 重构前 | 重构后 | 改善 |
|------|-------|-------|------|
| 重复代码行数 | ~500行 | 0行 | -100% |
| 布局调用方式 | 多种入口 | 统一入口 | 简化 |
| 维护成本 | 高（3处重复需同步修改） | 低（单一维护点） | 显著降低 |
| 扩展性 | 低（逻辑分散） | 高（基础类统一扩展） | 显著提升 |
| 测试覆盖 | 分散难测 | 集中可测 | 提升 |

### 注意事项

1. **兼容性**：`WdUIComposer.ApplySmartLayout` 等方法签名和行为完全一致，现有调用方无需修改
2. **性能**：委托调用增加一层间接，影响可忽略
3. **依赖**：WdUI 需添加对 `UniversalJsonMiddleware` 的项目引用（已配置）
4. **WdUI 特有功能**：`DrawOverlaps`、`EnableRuntimeOverlapMonitoring` 等调试功能保留在 WdUI 中

## 扩展开发

### 自定义控件构建器

```csharp
public class CustomControlBuilder : IControlFactoryBuilder
{
    public string ControlType => "customcontrol";

    public bool CanBuild(string controlType) =>
        controlType.Equals(ControlType, StringComparison.OrdinalIgnoreCase);

    public Control Build(ControlConfig config)
    {
        var control = new CustomControl();
        control.CustomProperty = config.CustomValue;
        return control;
    }
}

// 注册到工厂
var factory = new ControlFactory(mediator);
factory.RegisterControlBuilder(new CustomControlBuilder());
```

### 自定义布局策略

实现 `LayoutStrategies` 中定义的接口，并在配置中指定使用的策略。布局系统采用策略模式，可轻松切换不同布局算法。

## 最佳实践

1. **配置与代码分离**：JSON 配置文件放在独立目录，便于管理和维护
2. **有意义的控件命名**：名称清晰表达用途，便于代码中查找和引用
3. **合理划分视图**：功能相关的控件放在同一视图，通过导航按钮切换
4. **集中事件处理**：事件处理逻辑集中在订阅回调中，保持代码整洁
5. **善用状态和缓存**：避免重复计算和数据获取，提升应用性能

## 依赖项

| 依赖 | 用途 |
|------|------|
| `FunctionExecution` | 函数执行能力 |
| `ReflectionMethod` | 反射功能（动态创建控件、读取配置） |
| `Microsoft.Extensions.Logging` | 日志记录 |
| `System.Text.Json` | JSON 序列化 |
| Windows Forms | 标准控件库 |

## 许可协议

WdUI 框架遵循 MIT 许可协议开源，可自由使用、修改和分发。使用本框架开发的应用程序无需开源，只需在产品中保留框架的版权声明。
