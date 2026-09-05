# MetaCommand 统一控件处理框架重构方案

> **核心理念：** 控件 = 状态机。MetaCommand 作为状态机抽象层，WinForms/文件系统/注册表等作为具体执行器。
>
> **目标：** 消除 BasicMethod/Common/WinForms、Share/CommandFramework、Share/FrameworkAbstractions、Operation/WdUI 四处的控件处理逻辑重复。

---

## 目录

1. [现状分析与重复清单](#1-现状分析与重复清单)
2. [核心设计理念](#2-核心设计理念)
3. [第一阶段：MetaCommand 基础框架](#3-第一阶段metacommand-基础框架)
4. [第二阶段：WinForms 领域应用](#4-第二阶段winforms-领域应用)
5. [第三阶段：其他领域执行器](#5-第三阶段其他领域执行器)
6. [ControlValueHelper + ControlExtensions 迁移路径](#6-controlvaluehelper--controlextensions-迁移路径)

---

## 1. 现状分析与重复清单

### 1.1 重复全景

| 功能 | BasicMethod/Common/WinForms | Share/CommandFramework | Share/FrameworkAbstractions | Operation/WdUI |
|------|:--------------------------:|:---------------------:|:--------------------------:|:-------------:|
| **值读写** | `ControlValueHelper` (460行) | `ControlValueAccessorBase` (163行) + `IControlValueWriter` | `IControlValueAccessor` | `WdUIControlValueWriterAdapter` (164行) |
| **控件查找** | `ControlExtensions.FindChild/FindByName` (591行中) | `IControlFinder` | `IControlFinder` + `WinFormsControlFinder` (71行) | `WdUIControlFinderAdapter` (135行) |
| **事件绑定** | `WinFormsEventBinder` (453行) | `IWinFormsEventTrigger` + `IEventBindingResolver` | ❌ | `WinFormsEventHelper` (88行) + `WdUIEventTrigger` (89行) |
| **UI线程调度** | `ControlExtensions.InvokeIf` (591行中) | ❌ | `AsyncUiHelper` (85行) | `AsyncUiHelper` (WdUI内部) |
| **命令引擎** | `WinFormsCommandEngine` (699行) | `CommandEngine` + `ICommandEngine` | ❌ | `WinFormsCommandEngine` (343行) |
| **Do/Get/Look** | ❌ | `IEnvironmentAdapter` + `EnvironmentAdapterBase` (114行) | ❌ | 通过适配器实现 |

### 1.2 关键发现

1. **`ControlValueHelper` 和 `ControlValueAccessorBase` 功能完全重复**——都是"按控件类型注册 getter/setter 适配器 + 继承链查找"。
2. **控件查找逻辑分散在 5 处**：`ControlExtensions.FindChild`、`WinFormsEventBinder.FindControl`、`WinFormsControlFinder`、`WdUIControlFinderAdapter`、`ControlFinder`（WdUI.Framework.Shared）。
3. **事件绑定逻辑分散在 3 处**：`WinFormsEventBinder`、`WinFormsEventHelper`、`WdUIEventTrigger`。
4. **UI 线程调度逻辑分散在 3 处**：`ControlExtensions.InvokeIf`、`AsyncUiHelper`（FrameworkAbstractions）、`AsyncUiHelper`（WdUI）。
5. **命令引擎重复**：`BasicMethod.Common.WinForms.WinFormsCommandEngine` 和 `WdUI.UI.Commands.WinFormsCommandEngine` 都实现了类似的 set/get/click 命令。

### 1.3 量化统计

| 指标 | 重构前 | 重构后 | 减少 |
|------|:-----:|:-----:|:----:|
| 接口数量 | 8 (分散在 3 个项目) | 5 (集中在 MetaCommand) | 3 |
| 实现类数量 | 12+ | 3 (ControlServiceBase + WinFormsControlService + WinFormsExecutor) | 9+ |
| 代码行数 | ~3000+ | ~1000 | ~2000+ |
| 废弃文件数 | 2 (ControlValueHelper + ControlExtensions) | 0 | 2 |
| WdUI 适配器文件数 | 4 | 0 | 4 |
| 外部引用数 | 23 | 0 | 23 |

---

## 2. 核心设计理念

### 2.1 控件 = 状态机

```
控件 = 有限状态机 (Finite State Machine)

每个控件是一个独立的状态机实例：
  - 当前状态: { Text="Hello", Checked=true, Enabled=true, ... }
  - 状态转换: SetValue → 改变状态
  - 状态读取: GetValue → 读取状态
  - 事件通知: TextChanged / Click → 状态转换事件

控件树 = 嵌套状态机：
  Form (根状态机)
  ├── TextBox (子状态机: {Text})
  ├── CheckBox (子状态机: {Checked})
  └── Button (子状态机: {Enabled})
```

### 2.2 万物皆状态机

| 环境 | 状态机实例 | 状态 | 事件 |
|------|-----------|------|------|
| WinForms 控件 | `TextBox` | `{Text}` | `TextChanged` |
| 文件系统 | `文件路径` | `{内容}` | `FileSystemWatcher.Changed` |
| 注册表 | `注册表键` | `{值}` | `RegNotifyChangeKeyValue` |
| 配置项 | `配置路径` | `{值}` | `配置变更回调` |
| 数据库 | `表.字段` | `{字段值}` | `SqlDependency.OnChange` |

### 2.3 5 个动词 = 5 个状态机操作

```
MetaVerb.Do     → 状态转换 (SetState)
MetaVerb.Get    → 状态读取 (GetState)
MetaVerb.Look   → 状态枚举 (Discover/Inspect)
MetaVerb.Wait   → 等待状态转换 (AwaitTransition)
MetaVerb.Assert → 状态断言 (VerifyState)
```

### 2.4 控件的 5 个原语

| 原语 | 含义 | WinForms 示例 |
|------|------|:-------------:|
| **GetValue** | 读取状态 | `textBox.Text` |
| **SetValue** | 写入状态 | `textBox.Text = "x"` |
| **Find** | 定位控件 | `Controls.Find("name")` |
| **Bind** | 关联事件 | `button.Click += handler` |
| **Invoke** | 线程调度 | `control.Invoke(action)` |

---

## 3. 第一阶段：MetaCommand 基础框架

> **实施状态：** ✅ 已完成（2026-06-16）
> **位置：** `BasicMethod/MetaCommand/`
> **文件清单：** `ControlAbstractions.cs` / `ControlServiceBase.cs` / `ControlExecutorBase.cs` / `ControlServiceExtensions.cs`
> **构建验证：** `dotnet build` 0 Error 0 Warning

### 3.1 接口族（ControlAbstractions.cs）

5 个接口定义控件的 5 个原语，`IControlService` 合并全部：

| 接口 | 对应原语 | 关键方法 |
|------|---------|---------|
| `IControlValueAccessor` | GetValue / SetValue | `GetValue(object)` / `SetValue(object, object?)` / `RegisterAdapter(Type, ...)` |
| `IControlFinder` | Find | `FindByName(object, string)` / `FindByType(object, Type)` / `FindAll(object, Func<...>)` |
| `IControlEventBinder` | Bind | `BindEvent(object, string, Delegate)` / `GetSupportedEvents(Type)` |
| `IControlInvoker` | Invoke | `InvokeIfRequired(object, Action)` / `RunOnUiThread(object, Func<Task>)` |
| `IControlService` | 全部 | 继承以上 4 个接口 |

### 3.2 抽象基类（ControlServiceBase.cs）

提供 `IControlService` 的完整默认实现：

| 功能 | 实现方式 | 子类职责 |
|------|---------|---------|
| 值读写 | `ConcurrentDictionary<Type, (Getter, Setter)>` + 继承链/接口匹配 | 在 `RegisterDefaultAdapters()` 中注册控件适配器 |
| 控件查找 | 反射递归树遍历（`Controls` 属性） | 可重写 `FindByName(string)` 实现跨容器查找 |
| 事件绑定 | 反射 + `EventInfo.AddEventHandler` + 事件别名映射 | 调用 `RegisterEventMapping(alias, actual)` 注册别名 |
| UI 线程调度 | 抽象方法 | 实现 `InvokeIfRequired` / `RunOnUiThread` / `ExecuteOnUiThread` |

### 3.3 执行器基类（ControlExecutorBase.cs）

将 `ICommandExecutor` 的 5 个动词映射到 `IControlService` 的 5 个原语：

| 动词 | 映射逻辑 | 子类可重写 |
|------|---------|-----------|
| `DoAsync` | `action` 分发到 `DoSetAsync` / `DoClickAsync` / `DoEnableAsync` / `DoVisibleAsync` | 各 `Do*Async` 方法 |
| `GetAsync` | `FindByName` → `GetValue` | — |
| `LookAsync` | 调用抽象 `BuildWorldModelAsync` | ✅ 必须重写 |
| `WaitAsync` | `ParseCondition` → `BindEvent` → 等待条件满足 | — |
| `AssertAsync` | `ParseCondition` → `GetValue` → 比较 | — |

### 3.4 扩展方法（ControlServiceExtensions.cs）

| 方法 | 用途 |
|------|------|
| `SetItems(this IControlService, object, IEnumerable<string>)` | 填充列表控件（ListView/ComboBox/ListBox） |
| `GetPreviewValue(this IControlService, object)` | 获取控件预览值（日志/调试用） |

---

## 4. 第二阶段：WinForms 领域应用

> **实施状态：** ✅ 已完成（2026-06-16）
> **位置：** `BasicMethod/Common/WinForms/`
> **文件清单：** `WinFormsControlService.cs` / `WinFormsExecutor.cs`
> **构建验证：** `dotnet build` 0 Error 0 Warning

### 4.1 WinFormsControlService

继承 `ControlServiceBase`，提供 WinForms 特定实现：

| 功能 | 实现 |
|------|------|
| **控件适配器** | 注册 20+ 内置控件（TextBox/ComboBox/CheckBox/ListView/TreeView/DataGridView 等） |
| **事件别名** | `click`→`Click` / `textchanged`→`TextChanged` / `selectedindexchanged`→`SelectedIndexChanged` 等 |
| **UI 线程调度** | `control.InvokeRequired` → `control.Invoke(action)` / 直接执行 |
| **跨容器查找** | 重写 `FindByName(string)` → 遍历 `Application.OpenForms` |
| **辅助方法** | `FindInForm(formName, controlName)` / `GetOpenForms()` |

### 4.2 WinFormsExecutor

继承 `ControlExecutorBase`，实现 `BuildWorldModelAsync` 构建控件树模型。

### 4.3 废弃旧类

| 旧类 | 状态 | 替代 |
|------|------|------|
| `ControlValueHelper` | `[Obsolete]` → 委托到 `WinFormsControlService.Instance` |
| `ControlExtensions` | `[Obsolete]` → 委托到 `WinFormsControlService.Instance` |
| `WinFormsEventBinder` | `[Obsolete]` → 委托到 `WinFormsControlService.Instance` |
| `WinFormsCommandEngine` (BasicMethod) | `[Obsolete]` → 委托到 `WinFormsExecutor` |
| `ControlValueAccessorBase` | 删除 | `ControlServiceBase` |
| `WdUIControlValueWriterAdapter` | 删除 | `WinFormsExecutor` |
| `WdUIControlFinderAdapter` | 删除 | `WinFormsExecutor` |
| `WdUIEventTrigger` | 删除 | `WinFormsExecutor` |
| `WinFormsEventHelper` | 删除 | `WinFormsControlService` |
| `WdUI.UI.WinFormsCommandEngine` | 删除 | `WinFormsExecutor` 注册到 `CommandRouter` |
| `WdUI.UI.AsyncUiHelper` | 删除 | `WinFormsControlService` |
| `Share.FrameworkAbstractions.UI.AsyncUiHelper` | 删除 | `WinFormsControlService` |
| `IEnvironmentAdapter` | 删除 | `MetaCommand.ICommandExecutor` |
| `EnvironmentAdapterBase` | 删除 | `MetaCommand.BaseCommandExecutor` |

### 4.4 Share/CommandFramework 接口保留说明

以下接口**保留**（非废弃），因为 FileDriven 管线运行时通过 `ComponentRegistry` 注入依赖：

| 接口 | 保留原因 | 引用处 |
|------|---------|-------|
| `IControlValueWriter` | FileDriven 管线运行时契约 | Pipeline / ControlServices / UICommands 等 20+ 处 |
| `IControlFinder` | FileDriven 管线运行时契约 | Pipeline / ControlServices / UICommands 等 20+ 处 |
| `IWinFormsEventTrigger` | ExecuteCommandStage 依赖 | FileDriven.Execution |
| `ControlValueAccessorBase` | CommandFrameworkHost 依赖 | FileDriven.Execution |

> **说明：** `MetaCommand.IControlService` 是新的统一抽象，与 Share/CommandFramework 接口是**互补关系**：
> - `IControlValueWriter` / `IControlFinder` → FileDriven 运行时注入（ComponentRegistry）
> - `IControlService` → MetaCommand 统一抽象（继承 ControlServiceBase）

---

## 5. 第三阶段：其他领域执行器

> **实施状态：** ✅ 已完成（2026-06-16）
> **位置：** `BasicMethod/Common/{FileSystem,Registry,Config}/`
> **文件清单：** `FileSystemExecutor.cs` / `RegistryExecutor.cs` / `ConfigExecutor.cs`
> **构建验证：** `dotnet build` 0 Error

### 5.1 文件系统执行器（FileSystemExecutor.cs）

| 动词 | 功能 |
|------|------|
| `DoAsync` | `write`(写文件) / `delete`(删除) / `mkdir`(创建目录) |
| `GetAsync` | 读取文件内容 |
| `LookAsync` | 列出目录下的文件和子目录 |
| `WaitAsync` | 使用 `FileSystemWatcher` 等待文件变化 |
| `AssertAsync` | 断言文件存在性 |

### 5.2 注册表执行器（RegistryExecutor.cs）

| 动词 | 功能 |
|------|------|
| `DoAsync` | 设置注册表值 |
| `GetAsync` | 读取注册表值 |
| `LookAsync` | 列出子键和值名称 |
| `AssertAsync` | 断言注册表值 |

### 5.3 配置执行器（ConfigExecutor.cs）

| 动词 | 功能 |
|------|------|
| `DoAsync` | 设置配置项（内存 `ConcurrentDictionary`） |
| `GetAsync` | 读取配置项 |
| `LookAsync` | 列出所有配置项（支持 scope 前缀过滤） |
| `WaitAsync` | 等待配置项变化 |
| `AssertAsync` | 断言配置项值 |

### 5.4 废弃旧版执行器

| 旧类 | 替代 |
|------|------|
| `Share/CommandFramework/Execution/FileSystemExecutor.cs` | `MetaCommand.FileSystemExecutor` |
| `Share/CommandFramework/Execution/RegistryExecutor.cs` | `MetaCommand.RegistryExecutor` |
| `Share/CommandFramework/Execution/ConfigExecutor.cs` | `MetaCommand.ConfigExecutor` |

### 5.5 待办：注册到 CommandRouter

```csharp
var router = new CommandRouter(logger, statistics);
router.RegisterExecutor("winforms", new WinFormsExecutor());
router.RegisterExecutor("filesystem", new FileSystemExecutor());
router.RegisterExecutor("registry", new RegistryExecutor());
router.RegisterExecutor("config", new ConfigExecutor());
```

---

## 6. ControlValueHelper + ControlExtensions 迁移路径

> **目标：** 彻底删除 `ControlValueHelper.cs`（461行）和 `ControlExtensions.cs`（592行），
> 所有外部引用改为使用 `WinFormsControlService.Instance`。
>
> **现状：** 两个文件已标记 `[Obsolete]`，仍有 20+ 处外部引用（FileDriven、Operation/WdUI）。

### 6.1 外部引用全景

#### ControlValueHelper（20处）

| # | 文件 | 替换方式 |
|---|------|---------|
| 1-4 | `FileDriven.UI/UICommands.cs` L66/73/239/280 | `WinFormsControlService.Instance.GetValue/SetValue` |
| 5-6 | `FileDriven.UI/Event/EventTriggerService.cs` L232/384 | `WinFormsControlService.Instance.GetValue/SetValue` |
| 7-8 | `FileDriven.Execution/Command/ControlServices.cs` L273/548 | `WinFormsControlService.Instance.SetValue/GetValue` |
| 9-11 | `FileDriven.Business/UI/WinFormsEventBinder.cs` L290/553/616 | `WinFormsControlService.Instance.GetValue/SetItems` |
| 12-13 | `Operation/WdUI/UI/WinFormsCommandEngine.cs` L155/177 | `WinFormsControlService.Instance.SetValue/GetValue` |
| 14 | `Operation/WdUI/UI/WdUI.Utilities.cs` L50 | `WinFormsControlService.Instance.SetValue` |
| 15 | `Operation/WdUI/Adapters/WdUIEventTrigger.cs` L69 | `WinFormsControlService.Instance.SetValue` |
| 16-20 | `Operation/WdUI/Adapters/WdUIControlValueWriterAdapter.cs` L35/95/114/130/151 | `WinFormsControlService.Instance.*` |

#### ControlExtensions（3处）

| # | 文件 | 替换方式 |
|---|------|---------|
| 1 | `Operation/WdUI/UI/WdUI.Management.cs` L155 | `WinFormsControlService.Instance.SetValue(control, value)` |
| 2 | `Operation/WdUI/UI/WdUI.Management.cs` L343 | `EnableDoubleBuffering()` 保留为独立扩展 |
| 3 | `Operation/WdUI/UI/WdUI.Utilities.cs` L44-58 | `SetControlValue` 内部委托到 `WinFormsControlService.Instance` |

### 6.2 前置条件：WinFormsControlService 补充 SetItems

`WinFormsControlService` 已有 `GetValue`/`SetValue`/`IsSupported`/`RegisterAdapter`，需补充 `SetItems` 方法（从 `ControlValueHelper.SetItems` 迁移，通过 `InvokeIfRequired` 确保 UI 线程安全，支持 ListView/ComboBox/ListBox）。

### 6.3 分步骤迁移计划

| Step | 操作 | 验证 |
|:----:|------|:----:|
| 1 | 在 `WinFormsControlService.cs` 中添加 `SetItems` 方法 | `dotnet build` 0 Error |
| 2 | 迁移 FileDriven 层 8 处引用（UICommands / EventTriggerService / ControlServices / WinFormsEventBinder） | `dotnet build` 0 Error |
| 3 | 迁移 Operation/WdUI 层 6 处引用（WinFormsCommandEngine / WdUI.Utilities / WdUIEventTrigger / WdUIControlValueWriterAdapter） | `dotnet build` 0 Error |
| 4 | 迁移 ControlExtensions 3 处引用（WdUI.Management / WdUI.Utilities） | `dotnet build` 0 Error |
| 5 | 删除 `ControlValueHelper.cs` + `ControlExtensions.cs`，更新 `BasicMethod.csproj` | `dotnet build` 0 Error |
| 6 | 删除已废弃的 WdUI 适配器（WdUIControlValueWriterAdapter / WdUIControlFinderAdapter / WdUIEventTrigger / WinFormsEventHelper） | `dotnet build` 0 Error |

### 6.4 风险与注意事项

1. **API 签名兼容：** `ControlValueHelper.GetValue` 返回 `object?`，`WinFormsControlService.Instance.GetValue` 也返回 `object?`，直接替换。
2. **静态→实例方法：** `GetValueStatic` 替换为 `Instance.GetValue`，需确保 `Instance` 可访问。
3. **UI 线程安全：** `WinFormsControlService.Instance.InvokeIfRequired` 已内置 UI 线程调度，外部无需额外 `Invoke`。
4. **WdUI.Utilities.SetControlValue：** 此扩展方法在 WdUI 命名空间内，与 BasicMethod 的 `ControlExtensions` 不同，迁移后保留签名但内部委托到新服务。
