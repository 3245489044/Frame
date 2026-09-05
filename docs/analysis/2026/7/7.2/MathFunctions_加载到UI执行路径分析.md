# MathFunctions.json 从加载到 UI 点击执行的路径分析

## 一、JSON 配置结构

**文件路径**: `FileDriven/DrivenConfig/JsonConfig/MathFunctions.json`

MathFunctions.json 定义了数学函数列表（Add、Subtract、Multiply、Divide 等），每个函数包含：
- `name` / `displayName` — 函数名称
- `category` — 分类（如 "Basic"）
- `parameters` — 参数定义
- `expression` — 表达式模板

这些函数通过 UI 配置选择器加载到侧边容器中，以按钮/列表形式展示。

---

## 二、完整路径：JSON → UI 渲染 → 事件绑定

### 阶段 1：框架初始化（FrameworkInitializer）

```
FrameworkInitializer.Initialize()
  ├── 1. 设置项目根目录
  ├── 2. ★ 创建 DefaultEventBindingService（无参构造函数）
  │      → _winFormsEventBinder = null（未传入）
  │      → _pipelineRunner = new EventBindingPipelineRunner(null)
  │      → FileDrivenWdUIBridge.InjectEventBinder(eventBindingService)
  │      → WdUIUsage.RenderPipeline.EventBinder = eventBindingService
  ├── 3. 初始化 FunctionStorageLifecycle
  ├── 4. 初始化 FileDrivenLifecycle
  ├── 5. 创建 UICommandManager
  └── 6. 注册 ExecutionCenter 处理器
```

### 阶段 2：GUI 模式启动（FileDrivenExecutor.StartGuiMode）

```
FileDrivenExecutor.StartGuiMode()
  ├── CreateExecutionEntry() → PipelineEntry + ExecutionPipelineEntry
  ├── GuiModeRunner.Run()
  │     ├── 加载 MainLayout.json（主布局）
  │     │     └── 渲染管线执行 BindEventsSkeleton
  │     │           └── DefaultEventBindingService.BindEvents()
  │     │                 └── _pipelineRunner.Execute(target, config, _winFormsEventBinder, ...)
  │     │                       └── _winFormsEventBinder = null ★★★
  │     │                             └── EventBinderTransformer.Transform()
  │     │                                   → binder == null → 跳过绑定 ← 问题根源
  │     │
  │     ├── 加载 ConfigSelector.json（配置选择器布局）
  │     ├── 用户选择 MathFunctions.json
  │     └── 加载 MathFunctions.json 到侧边容器
  │           └── 渲染管线执行 BindEventsSkeleton
  │                 └── 同样因 _winFormsEventBinder = null 跳过绑定
  │
  └── Application.Run(mainForm)
```

### 阶段 3：用户点击按钮

```
用户点击按钮（如 "Add"）
  └── 事件未绑定 → 无反应 ★★★
```

---

## 三、事件绑定管线详解

### 管线结构

```
EventBindingPipelineRunner.Execute()
  ├── 创建 EventBindingContext
  │     └── context.WinFormsEventBinder = _winFormsEventBinder（传入的参数）
  │
  └── 执行管线链：
        ├── ViewExtractorTransformer — 提取视图
        ├── ControlListExtractorTransformer — 提取控件列表
        ├── WinFormsControlFinderTransformer — 查找 WinForms 控件
        ├── [对每个控件执行子管线]：
        │     ├── ScriptModeGuardTransformer — 检查脚本模式
        │     ├── BindingMetadataTransformer — 提取 output/inputs 元数据
        │     └── EventBinderTransformer — ★ 实际绑定事件 ★
        │            → 检查 binder（即 context.WinFormsEventBinder）
        │            → binder == null → 跳过绑定，记录 SkippedCount++
        │
        ├── ControlTreeWalkerTransformer — 递归遍历子控件
        └── BindingStatisticsWrapperTransformer — 统计结果
```

### EventBinderTransformer 跳过条件（第240行）

```csharp
if (control == null || config?.Events == null || binder == null)
    // → 跳过绑定，记录 SkippedCount++
```

其中 `binder` 来自 `context.WinFormsEventBinder`，即 `EventBindingPipelineRunner.Execute()` 传入的 `_winFormsEventBinder` 参数。

---

## 四、根因分析

### 直接原因

**`DefaultEventBindingService._winFormsEventBinder` 为 null**，导致 `EventBinderTransformer` 跳过所有事件绑定。

### 调用链

```
FrameworkInitializer.Initialize() 第116行
  → new DefaultEventBindingService()  ← 无参构造函数，_winFormsEventBinder = null
  → FileDrivenWdUIBridge.InjectEventBinder(eventBindingService)
  → WdUIUsage.RenderPipeline.EventBinder = eventBindingService
  → 渲染管线执行 BindEventsSkeleton
  → DefaultEventBindingService.BindEvents()
  → _pipelineRunner.Execute(target, config, _winFormsEventBinder, ...)
  → EventBinderTransformer.Transform()
  → binder == null → 跳过绑定
```

### 关键代码位置

| 位置 | 文件 | 行号 |
|------|------|------|
| 创建 DefaultEventBindingService（无参） | `FileDriven.Execution/Lifecycle/FrameworkInitializer.cs` | 116 |
| 注入到渲染管线 | `FileDriven.Execution/Lifecycle/FrameworkInitializer.cs` | 117 |
| 传入 _winFormsEventBinder | `EventTriggerFramework/DefaultEventBindingService.cs` | 161 |
| 检查 binder 是否为 null | `EventTriggerFramework/EventBinding/EventBindingTransformers.cs` | 240 |

### 为什么 _winFormsEventBinder 为 null

`DefaultEventBindingService` 的构造函数有 4 个可选参数：
```csharp
public DefaultEventBindingService(
    IEventTriggerService? eventTriggerService = null,
    WinForms.WinFormsEventBus? fallbackEventBus = null,
    IWinFormsEventBinder? winFormsEventBinder = null,     // ← 未传入
    IEventExecutionService? eventExecutionService = null)
```

`FrameworkInitializer` 中调用的是无参构造函数，所有参数均为 null。

---

## 五、修复方案

### 方案 A：在 FrameworkInitializer 中创建 WinFormsEventBinder 并注入

**修改文件**: `FileDriven/FileDriven.Execution/Lifecycle/FrameworkInitializer.cs`

在 `new DefaultEventBindingService()` 之后，创建 `WinFormsEventBinder` 并设置到 `WinFormsEventBinder` 属性：

```csharp
var eventBindingService = new DefaultEventBindingService();

// 创建 WinFormsEventBinder
var backEndExecutionService = new BackEndDriven.Execution.EventExecutionService(
    EventTriggerFramework.EventTypeMapper.Default,
    (cmd, ctx) => true,  // 占位委托
    null);
var executionServiceAdapter = new BackEndDriven.Bridge.EventExecutionServiceAdapter(backEndExecutionService);
var winFormsEventBinder = new EventBinding.WinFormsEventBinder(executionServiceAdapter);
eventBindingService.WinFormsEventBinder = winFormsEventBinder;

FileDrivenWdUIBridge.InjectEventBinder(eventBindingService);
```

**问题**：`BackEndDriven.Execution.EventExecutionService` 的 `executePipeline` 委托是必选参数，FrameworkInitializer 阶段没有可用的管线执行器，只能传占位委托。事件触发时占位委托返回 true，但不会真正执行管线。

### 方案 B：延迟注入（推荐）

在 `GuiModeRunner.Run()` 或 `FileDrivenExecutor.StartGuiMode()` 中，当 `EventPipelineExecutor` 创建完成后，通过 `DefaultEventBindingService.WinFormsEventBinder` 属性延迟注入。

**优点**：可以获取到真正的 `EventPipelineExecutor`，事件触发时能正确执行管线。

### 方案 C：在 DefaultEventBindingService 内部延迟创建

修改 `DefaultEventBindingService.BindEvents()` 方法，当 `_winFormsEventBinder` 为 null 时，尝试从 `ComponentRegistry` 或 DI 容器中获取。

---

## 六、影响范围

- **所有 JSON 配置中的按钮事件均无法绑定**
- 用户点击 UI 按钮无任何响应
- 不仅影响 MathFunctions.json，所有通过 UI 配置加载的 events 定义均受影响
- CLI 模式不受影响（CLI 模式不经过事件绑定管线）
