# FileDriven 事件绑定逻辑集中化整理方案

> **目标**：在 `FileDriven.Execution/EventBinding/` 下创建独立文件夹，将 FileDriven 框架中**仅与事件绑定相关的逻辑**集中整理到此文件夹中，消除跨项目分散、重复代码、调用链不清晰的问题。

---

## 一、事件绑定架构总览

FileDriven 的事件绑定体系分为 **4 层**，当前分散在 **4 个项目**中：

```
┌─────────────────────────────────────────────────────────────────┐
│  第1层：JSON 配置层（FileDriven.JsonRefer/Event）                  │
│  定义事件绑定在 JSON 配置中的结构和构建方式                          │
├─────────────────────────────────────────────────────────────────┤
│  第2层：事件绑定管线层（FileDriven.Execution/EventPipeline）        │
│  运行时事件绑定、解析、触发、执行的核心逻辑                          │
├─────────────────────────────────────────────────────────────────┤
│  第3层：UI 集成层（FileDriven.UI/Core + FileDriven.Business/UI）   │
│  MainForm 中的事件注册、UIEventBinder、UIConnector 等              │
├─────────────────────────────────────────────────────────────────┤
│  第4层：配置控件树遍历层（FileDriven.Pipeline/Contracts + Core）    │
│  ControlTreeWalker、WinFormsControlFinder 等工具                  │
└─────────────────────────────────────────────────────────────────┘
```

---

## 二、当前分散情况

### 2.1 属于"事件绑定"范畴的文件

| 文件 | 当前位置 | 说明 |
|------|---------|------|
| **`WinFormsEventBinder.cs`** | `EventPipeline/` | 底层反射绑定器 — 将委托绑定到 WinForms 控件事件 |
| **`WinFormsEventRegistrar.cs`** | `EventPipeline/` | 类型安全事件注册器 — 注册/取消注册控件事件 |
| **`EventBindingResolver.cs`** | `EventPipeline/` | 事件绑定解析器 — 从配置中提取事件绑定信息 |
| **`EventBindingInfo.cs`** | `EventPipeline/` | 事件绑定信息 DTO |
| **`IEventBindingService.cs`** | `FileDriven.UI/Core/Abstractions/` | 事件绑定服务接口 |
| **`EventHelper.cs`** | `FileDriven.JsonRefer/Event/` | JSON 配置层的事件构建/校验辅助方法 |
| **`EventBindingFormatter.cs`** | `FileDriven.JsonRefer/Event/` | 事件绑定格式化器 |
| **`ControlTreeWalker.cs`** | `FileDriven.Pipeline/Contracts/` | 配置控件树遍历工具（事件绑定解析依赖） |
| **`WinFormsControlFinder.cs`** | `FileDriven.Pipeline/Core/` | WinForms 运行时控件查找器（事件绑定需要） |

### 2.2 `EventPipeline/` 中不属于事件绑定的文件（保留原位）

| 文件 | 说明 | 保留原因 |
|------|------|---------|
| **`EventPipelineExecutor.cs`** | 事件管线执行器 | 属于**管线执行**范畴，将事件路由到管线骨架 |
| **`EventTriggerService.cs`** | 事件触发服务 | 属于**事件触发/执行**范畴，执行绑定的函数 |
| **`EventHub.cs`** | 生命周期事件中心 | 属于**生命周期管理**范畴，管理框架级事件 |
| **`Models/LoadedConfigInfo.cs`** | 已加载配置信息 DTO | 属于**配置管理**范畴 |

### 2.3 保持原位的外部文件（不属于事件绑定）

| 文件 | 当前位置 | 说明 |
|------|---------|------|
| **`UIEventBinder`** | `ForntEndDriven/Implementation/` | 通用前端驱动框架的 UI 事件绑定器 |
| **`IUIEventBinder`** | `ForntEndDriven/Abstractions/` | UI 事件绑定器接口 |
| **`UIConnectorEventBinder.cs`** | `FileDriven.Business/UI/` | 工作流业务层的事件绑定器 |

---

## 三、各文件详细分析

### WinFormsEventBinder.cs — 底层反射绑定器

**位置**：`EventPipeline/` → **迁移到** `EventBinding/`

**核心职责**：将 C# 委托绑定到 WinForms 控件的原生事件。

| 方法 | 说明 |
|------|------|
| **BindControlEvent(control, eventName, handler)** | 核心方法：通过反射将 `Delegate` 绑定到控件的指定事件 |
| **BindControlEvents(control, eventsConfig)** | **[已废弃]** 旧的事件遍历逻辑，保留向后兼容 |
| **BindSingleEvent(control, eventName, functionName, eventsConfig)** | 内部方法：创建 `EventHandler` 委托，绑定到控件事件 |

**关键机制**：
- **反射缓存**：`ConcurrentDictionary<(Type, string), EventInfo?>` 缓存反射结果，避免重复查找
- **脚本执行模式**：`IsScriptExecutionMode` 静态字段，当管线正在执行 set 命令时跳过运行时事件触发
- **事件处理器**：创建 `EventHandler` 委托，内部调用 `_eventExecutionService.Execute(ctrl.Name, eventName, value)`
- **参数保存**：将 `output`/`inputs` 元数据保存到控件的 `Tag` 属性

**绑定的事件类型**（在 `BindControlEvents` 中定义）：
- `click` → `cfg.Click`
- `doubleclick` → `cfg.DoubleClick`
- `textchanged` → `cfg.TextChanged`
- `checkedchanged` → `cfg.CheckedChanged`
- `selectedindexchanged` → `cfg.SelectedIndexChanged`
- `valuechanged` → `cfg.ValueChanged`
- `CustomEvents` → 自定义事件字典

---

### WinFormsEventRegistrar.cs — 事件注册器

**位置**：`EventPipeline/` → **迁移到** `EventBinding/`

提供**类型安全**的事件注册方法，每个方法返回 `Action` 取消委托：

| 方法 | 控件类型 | 事件 |
|------|---------|------|
| `RegisterClick()` | `Button` | `Click` |
| `RegisterTextChanged()` | `TextBox` | `TextChanged` |
| `RegisterSelectedIndexChanged()` | `ListView` | `SelectedIndexChanged` |
| `RegisterDoubleClick()` | `ListView` | `DoubleClick` |
| `RegisterCheckedChanged()` | `CheckBox` | `CheckedChanged` |
| `RegisterKeyPressDirect()` | `TextBox` | `KeyPress`（直接传入控件，不通过容器查找） |

---

### EventBindingResolver.cs — 事件绑定解析器

**位置**：`EventPipeline/` → **迁移到** `EventBinding/`

**核心职责**：从已加载的 `UIConfiguration` 配置中提取控件的事件绑定信息。

**继承关系**：继承 `BackEndDriven.Execution.EventBindingService`

**关键方法**：
- **FindEventBinding(controlName, eventType)** — 查找控件的事件绑定
- **ResolveEventType(controlName, operationType)** — 根据操作类型推断事件类型（如 `set` → `textchanged`）
- **GetControlType(controlName)** — 获取控件类型
- **TraverseConfigForControls()** — 重写基类方法，利用强类型 `UIConfiguration` 遍历配置

**事件类型映射表**（`EventPropertyAccessors` 字典）：
```
click → events.Click
selectedindexchanged → events.SelectedIndexChanged
textchanged → events.TextChanged
doubleclick → events.DoubleClick
checkedchanged → events.CheckedChanged
valuechanged → events.ValueChanged
```

**线程安全**：使用 `ConcurrentDictionary` 存储已加载配置，快照枚举避免并发修改异常。

---

### EventBindingInfo.cs — 事件绑定信息 DTO

**位置**：`EventPipeline/` → **迁移到** `EventBinding/`

```csharp
class EventBindingInfo {
    string FunctionName    // 计算属性，取 FunctionNames[0]
    string EventType
    IReadOnlyList<string> Inputs
    string? Output
    IReadOnlyList<string> FunctionNames  // 支持多函数绑定
}
```

---

### IEventBindingService.cs — 事件绑定服务接口

**位置**：`FileDriven.UI/Core/Abstractions/` → **迁移到** `EventBinding/`

```csharp
interface IEventBindingService {
    Task<IEventTriggerResult> TriggerEventAsync(string controlName, string eventType, string value);
    string GetEventTypeForControl(string controlName);
    IEventBindingResolver BindingResolver { get; }
}
```

---

### EventHelper.cs — 事件构建/校验辅助方法

**位置**：`FileDriven.JsonRefer/Event/` → **迁移到** `EventBinding/`

定义事件绑定的**配置规范**：

| 元素 | 说明 |
|------|------|
| **标准事件类型** | `click`, `doubleclick`, `textchanged`, `selectedindexchanged`, `checkedchanged`, `valuechanged` |
| **事件字段名** | `"events"`（JSON 中的字段名） |
| **BuildEvent()** | 构建事件字典，支持 `eventName→functionName` 映射 + `bindings`（inputs/output） |
| **GetEventFunctions()** | 从 `ControlConfig.Events` 中提取指定事件类型的函数列表 |
| **IsValidEventType()** | 校验事件类型是否合法 |

**事件绑定的 JSON 结构示例**：
```json
{
  "events": {
    "click": "MyFunction",
    "click_bindings": {
      "inputs": ["param1"],
      "output": "resultControl"
    }
  }
}
```

---

### EventBindingFormatter.cs — 事件绑定格式化器

**位置**：`FileDriven.JsonRefer/Event/` → **迁移到** `EventBinding/`

将 `EventConfig` 中的事件绑定格式化为可读字符串，用于日志/调试。

---

### ControlTreeWalker.cs — 配置控件树遍历工具

**位置**：`FileDriven.Pipeline/Contracts/` → **迁移到** `EventBinding/`

基于 `PlaceholderFramework.TreeWalker` + `ControlConfigTreeNode` 适配器，提供：

| 方法 | 说明 |
|------|------|
| `FindControl(controls, name)` | 在控件列表中按名称递归查找 |
| `FindControl(config, name)` | 在 UIConfiguration 中按名称递归查找 |
| `CollectNames(controls)` | 收集所有控件名称 |
| `ForEachChild(control, action)` | 遍历子控件 |

**迁移理由**：`EventBindingResolver` 依赖 `ControlTreeWalker` 来遍历配置树查找控件绑定信息。

---

### WinFormsControlFinder.cs — WinForms 控件查找器

**位置**：`FileDriven.Pipeline/Core/` → **迁移到** `EventBinding/`

实现 `IControlFinder` 接口，提供 WinForms 运行时控件查找：

| 方法 | 说明 |
|------|------|
| `FindByName(root, name)` | 在控件树中按名称递归查找 |
| `FindByName(name)` | 在所有打开的 Form 中查找 |
| `FindByType<T>(root)` | 按类型查找 |
| `FindAllByType<T>(root)` | 查找所有指定类型的控件 |
| `FindInForm(formName, controlName)` | 跨 Form 查找 |

**迁移理由**：事件绑定需要在运行时查找 WinForms 控件来绑定事件。

---

## 四、事件绑定完整调用链

### 4.1 JSON 配置驱动的事件绑定（推荐方式）

```
MainLayout.json / ConfigSelector.json
  ↓ (ConfigContainer.LoadConfigAsync 加载)
UIConfiguration 对象
  ↓ (DefaultEventBindingService.BindEvents)
EventBindingPipeline (Transformers 管线)
  ↓ (遍历 ControlConfig.Events)
WinFormsEventBinder.BindControlEvent()
  ↓ (反射绑定)
WinForms 原生事件 += EventHandler
```

### 4.2 用户点击触发的事件执行链

```
用户点击控件
  ↓
WinForms 原生事件触发
  ↓
WinFormsEventBinder 中绑定的 EventHandler
  ↓ (检查 IsScriptExecutionMode)
EventExecutionService.Execute(controlName, eventType, value)
  ↓
EventPipelineExecutor.ExecutePipelineInternal(command)    ← 保留在 EventPipeline/
  ↓
BuildEventCommand() → "do {action} {controlName}"
  ↓
SingleCommandPipeline.Execute(context)
  ├─ ParseCommandStage
  ├─ ExecuteCommandStage (通过 bindingFinder 查找绑定)
  │   ├─ EventBindingResolver.FindEventBinding()          ← 迁移到 EventBinding/
  │   └─ FunctionExecutor.Execute(functionName, args)
  └─ ResultCollectStage
```

### 4.3 代码驱动的事件注册（旧方式）

```
MainForm.OnConfigSelectorLoaded
  ↓
new UIEventBinder(container, commandTextBox, eventRegistrar, ...)
  ↓
eventBinder.BindAll()
  ├─ eventRegistrar.RegisterClick(container, "refreshButton", ...)
  ├─ eventRegistrar.RegisterTextChanged(container, "searchTextBox", ...)
  ├─ eventRegistrar.RegisterSelectedIndexChanged(container, "configListView", ...)
  ├─ eventRegistrar.RegisterCheckedChanged(container, "enableProcessingCheckBox", ...)
  └─ eventRegistrar.RegisterKeyPressDirect(commandTextBox, ...)
```

---

## 五、支持的事件类型汇总

| 事件类型 | JSON 配置字段 | 对应 WinForms 事件 | 典型控件 |
|---------|--------------|-------------------|---------|
| `click` | `events.click` | `Click` | Button |
| `doubleclick` | `events.doubleclick` | `DoubleClick` | ListView |
| `textchanged` | `events.textchanged` | `TextChanged` | TextBox |
| `selectedindexchanged` | `events.selectedindexchanged` | `SelectedIndexChanged` | ListView, ComboBox |
| `checkedchanged` | `events.checkedchanged` | `CheckedChanged` | CheckBox |
| `valuechanged` | `events.valuechanged` | `ValueChanged` | NumericUpDown, TrackBar |
| 自定义事件 | `events.customEvents` | 任意事件 | 任意控件 |

---

## 六、集中化目标结构

在 `FileDriven.Execution/EventBinding/` 下创建独立文件夹，**仅将事件绑定相关的逻辑**集中整理：

```
FileDriven.Execution/
├── EventBinding/                            # ★ 新建：事件绑定集中目录
│   ├── README.md                            #   调用链文档
│   │
│   ├── [运行时绑定层 — 从 EventPipeline/ 迁移]
│   ├── WinFormsEventBinder.cs               #   ⬅️ 底层反射绑定器
│   ├── WinFormsEventRegistrar.cs            #   ⬅️ 类型安全事件注册器
│   │
│   ├── [解析层 — 从 EventPipeline/ 迁移]
│   ├── EventBindingResolver.cs              #   ⬅️ 事件绑定解析器
│   ├── EventBindingInfo.cs                  #   ⬅️ 事件绑定信息 DTO
│   │
│   ├── [接口层 — 从 FileDriven.UI/Core/Abstractions/ 迁移]
│   ├── IEventBindingService.cs              #   ⬅️ 事件绑定服务接口
│   │
│   ├── [配置层 — 从 FileDriven.JsonRefer/Event/ 迁移]
│   ├── EventHelper.cs                       #   ⬅️ 事件构建/校验辅助方法
│   ├── EventBindingFormatter.cs             #   ⬅️ 事件绑定格式化器
│   │
│   ├── [控件遍历层 — 从 FileDriven.Pipeline/ 迁移]
│   ├── ControlTreeWalker.cs                 #   ⬅️ 配置控件树遍历工具
│   ├── WinFormsControlFinder.cs             #   ⬅️ WinForms 运行时控件查找器
│   │
│   └── Models/
│       └── ClickBindingInfo.cs              #   ⬅️ 点击绑定信息 DTO
│
├── EventPipeline/                           # ★ 保留：非事件绑定的逻辑保留在此
│   ├── EventPipelineExecutor.cs             #   管线执行器（保留）
│   ├── EventTriggerService.cs               #   事件触发服务（保留）
│   ├── EventHub.cs                          #   生命周期事件中心（保留）
│   └── Models/
│       └── LoadedConfigInfo.cs              #   配置信息 DTO（保留）
│
├── (其他现有文件保持不变)
```

---

## 七、迁移步骤

### Step 1：创建 `EventBinding/` 目录结构

```
mkdir FileDriven.Execution/EventBinding/
mkdir FileDriven.Execution/EventBinding/Models/
```

### Step 2：从 `EventPipeline/` 迁移事件绑定文件

| 源文件 | 目标文件 | 操作 |
|-------|---------|------|
| `EventPipeline/WinFormsEventBinder.cs` | `EventBinding/WinFormsEventBinder.cs` | 复制，更新命名空间为 `FileDriven.Execution.EventBinding` |
| `EventPipeline/WinFormsEventRegistrar.cs` | `EventBinding/WinFormsEventRegistrar.cs` | 复制，更新命名空间 |
| `EventPipeline/EventBindingResolver.cs` | `EventBinding/EventBindingResolver.cs` | 复制，更新命名空间 |
| `EventPipeline/EventBindingInfo.cs` | `EventBinding/EventBindingInfo.cs` | 复制，更新命名空间 |

**命名空间变更**：`FileDriven.Execution.EventPipeline` → `FileDriven.Execution.EventBinding`

**保留在 `EventPipeline/` 中的文件**（不迁移）：
- `EventPipelineExecutor.cs` — 管线执行逻辑
- `EventTriggerService.cs` — 事件触发逻辑
- `EventHub.cs` — 生命周期事件
- `Models/LoadedConfigInfo.cs` — 配置信息

### Step 3：从 `FileDriven.JsonRefer/Event/` 迁移

| 源文件 | 目标文件 | 操作 |
|-------|---------|------|
| `JsonRefer/Event/EventHelper.cs` | `EventBinding/EventHelper.cs` | 复制，更新命名空间为 `FileDriven.Execution.EventBinding` |
| `JsonRefer/Event/EventBindingFormatter.cs` | `EventBinding/EventBindingFormatter.cs` | 复制，更新命名空间 |

**影响分析**：
- `EventHelper.cs` 被 `ContractBuilder.cs`、`ContractValidator.cs`、`JsonConfigBuilder.cs` 引用
- 需要在原位置保留 `[Obsolete]` 类型别名转发，或更新所有引用方

### Step 4：从 `FileDriven.Pipeline/` 迁移

| 源文件 | 目标文件 | 操作 |
|-------|---------|------|
| `Pipeline/Contracts/ControlTreeWalker.cs` | `EventBinding/ControlTreeWalker.cs` | 复制，更新命名空间 |
| `Pipeline/Core/WinFormsControlFinder.cs` | `EventBinding/WinFormsControlFinder.cs` | 复制，更新命名空间 |

**影响分析**：
- `ControlTreeWalker.cs` 被 `EventBindingResolver.cs`（Pipeline 版）、`ContractBuilder.cs` 引用
- `WinFormsControlFinder.cs` 被 `PipelineEntry.cs`、`FileDrivenExecutor.cs`、`FrameworkInitializer.cs`、`FileDrivenPipelineRunner.cs` 引用

### Step 5：从 `FileDriven.UI/Core/Abstractions/` 迁移

| 源文件 | 目标文件 | 操作 |
|-------|---------|------|
| `UI/Core/Abstractions/IEventBindingService.cs` | `EventBinding/IEventBindingService.cs` | 复制，更新命名空间 |

**影响分析**：
- 被 `MainForm.cs` 引用（`EventBindingService` 属性类型）
- 被 `FileDrivenGuiInitializer.cs` 引用

### Step 6：更新引用方

更新所有引用旧命名空间的文件，将 `using FileDriven.Execution.EventPipeline` 改为 `using FileDriven.Execution.EventBinding`（仅针对迁移的文件）。

### Step 7：在原位置保留 `[Obsolete]` 类型别名转发

给其他项目一个迁移缓冲期，避免一次性破坏所有引用。

---

## 八、集中后的完整调用链

```
用户点击 UI 控件
  ↓
WinForms 原生事件（.NET 运行时调度）
  ↓
[EventBinding/WinFormsEventBinder.cs]          ← 事件绑定（EventBinding/）
  → EventExecutionService.Execute()
  ↓
[EventPipeline/EventPipelineExecutor.cs]       ← 管线执行（保留在 EventPipeline/）
  → BuildEventCommand() → "do {action} {name}"
  → SingleCommandPipeline.Execute(context)
  ↓
[EventBinding/EventBindingResolver.cs]         ← 绑定解析（EventBinding/）
  → FindEventBinding(controlName, eventType)
  → 从 ControlConfig.Events 提取函数名
  ↓
[EventPipeline/EventTriggerService.cs]         ← 事件触发（保留在 EventPipeline/）
  → GetControlValueAsync()
  → ExecuteFunctionAsync()
  → WriteOutputAsync()
  ↓
[EventBinding/WinFormsControlFinder.cs]        ← 控件查找（EventBinding/）
  → FindByName() / FindByType()
  ↓
[EventBinding/ControlTreeWalker.cs]            ← 配置树遍历（EventBinding/）
  → FindControl() / CollectNames()
```

**事件绑定相关文件集中在 `EventBinding/`**，管线执行/事件触发逻辑保留在 `EventPipeline/`，职责清晰。

---

## 九、依赖关系图（集中后）

```
EventBinding/ 内部依赖关系：

WinFormsEventBinder
  → IEventExecutionService (BackEndDriven.Abstractions)
  → IControlService (ForntEndDriven)
  → WinForms (System.Windows.Forms)

WinFormsEventRegistrar
  → WinForms (System.Windows.Forms)
  → IWinFormsEventRegistrar (ForntEndDriven.Abstractions)

EventBindingResolver
  → EventBindingService (BackEndDriven.Execution)
  → ControlTreeWalker
  → Modules.ConfigurationModels (UIConfiguration, ControlConfig)
  → 不依赖 WinForms ✅

EventHelper / EventBindingFormatter
  → Modules.ConfigurationModels
  → 不依赖 WinForms ✅

ControlTreeWalker
  → PlaceholderFramework.TreeWalker
  → Modules.ConfigurationModels
  → 不依赖 WinForms ✅

WinFormsControlFinder
  → IControlFinder (CommandFramework.Abstractions)
  → WinForms (System.Windows.Forms)
```

---

## 十、关键设计模式

| 模式 | 使用位置 | 说明 |
|------|---------|------|
| **策略模式** | `EventBindingResolver` 的 `EventPropertyAccessors` 字典 | 事件类型→属性访问器的映射，消除 switch 语句 |
| **享元/缓存** | `WinFormsEventBinder._eventInfoCache` | 反射结果缓存，避免重复反射 |
| **组合模式** | `ControlConfigTreeNode` 适配 `ITreeNode` | 使 `ControlConfig` 可以被 `TreeWalker` 统一遍历 |
| **单例模式** | `WinFormsEventBinder.IsScriptExecutionMode` 静态字段 | 多实例间共享脚本执行模式状态 |

---

## 十一、注意事项

1. **`FileDriven.Execution` 已依赖 `FileDriven.UI` 和 `FileDriven.Pipeline`**，所以将 UI/Pipeline 中的代码迁移到 Execution 不会引入循环依赖 ✅
2. **`FileDriven.Execution` 已依赖 `FileDriven.JsonRefer`**，所以将 EventHelper 从 JsonRefer 迁移到 Execution 可行 ✅
3. **`EventPipeline/` 目录保留**，只移走事件绑定相关的 4 个文件，其余文件保持原位
4. **向后兼容**：在原位置保留 `[Obsolete]` 类型别名转发，给其他项目一个迁移缓冲期

---

## 十二、总结

| 维度 | 集中前 | 集中后 |
|------|--------|--------|
| 涉及项目数 | 4 个（Execution + Pipeline + UI + JsonRefer） | **1 个**（Execution） |
| 事件绑定文件分散数 | 9 个文件分散在 4 个项目 | **全部集中在 `EventBinding/`** |
| 调用链可读性 | 需要跨项目跳转 | **事件绑定逻辑在 `EventBinding/`，管线执行在 `EventPipeline/`** |
| 职责划分 | 事件绑定与管线执行混合在 `EventPipeline/` | **事件绑定与管线执行职责分离** |
| 维护成本 | 修改事件绑定逻辑需改 4 个项目 | **只改 Execution 一个项目** |
