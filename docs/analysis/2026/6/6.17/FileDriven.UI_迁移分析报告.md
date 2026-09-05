# FileDriven.UI → Execution/Pipeline 迁移分析报告

> 分析日期：2026-06-17
> 分析范围：FileDriven.UI 项目中所有 .cs 文件
> 目标：识别应整理到 FileDriven.Execution 或 FileDriven.Pipeline 的逻辑

---

## 一、项目职责边界（当前架构）

| 项目 | 职责 | 依赖关系 |
|------|------|---------|
| **FileDriven.UI** | WinForms UI 层：MainForm、控件绑定、UI 事件、布局加载、UI 日志 | 依赖 Pipeline、JsonRefer、WdUI |
| **FileDriven.Execution** | 执行层：FileDrivenExecutor（门面）、管线构建/执行、命令注册、控件服务注册 | 依赖 UI、Pipeline、CommandFramework |
| **FileDriven.Pipeline** | 管线核心：PipelineEntry（共享状态）、EventBindingResolver、WinFormsControlFinder | 依赖 BasicMethod、CommandFramework |

---

## 二、高优先级迁移（必须迁移）

### 1. `Loading/ConfigLoader.cs` → **FileDriven.Execution**

**原因**：ConfigLoader 包含完整的配置加载管线逻辑，属于执行层而非 UI 层。

- `LoadConfigAsync(ConfigContainer, string, bool)` — 路径解析→反序列化→校验→渲染管线
- `LoadConfigAsync(ConfigContainer, UIConfiguration, bool)` — 校验→渲染管线
- `LoadConfigSelectorAsync(...)` — 配置选择器加载管线
- `LoadSideContainerAsync(...)` — 侧边容器加载管线（含并发控制锁）

**依赖分析**：
- 依赖 `WdUI.UI.Controls.ConfigContainer`（UI 层）
- 依赖 `Layout.LayoutJsonService.ValidateConfig`（UI 层）
- 依赖 `FileDriven.JsonRefer.JsonProcessing.JsonConfigService`（JsonRefer 层）

**迁移方案**：
1. 在 `FileDriven.Execution` 中创建 `Loading/ConfigLoader.cs`
2. 保留 UI 层中的委托方法（保持向后兼容）
3. 将 `LoadSideContainerAsync` 中的 `SemaphoreSlim` 并发控制逻辑保留在执行层

### 2. `Loading/ConfigLoadInput.cs` → **FileDriven.Execution.Models**

**原因**：这是配置加载操作的输入 DTO，包含 `LoadedConfigs`（配置缓存）和 `VirtualStore`（虚拟控件存储），属于执行上下文的一部分。

**当前状态**：已继承自 `FileDriven.JsonRefer.LayoutJson.Models.ConfigLoadInput`，仅添加 UI 特定字段。

**迁移方案**：
1. 在 `FileDriven.Execution` 中创建 `Models/ConfigLoadInput.cs`
2. UI 层引用 Execution 版本

### 3. `Layout/Contracts/ContractRegistry.cs` → **FileDriven.Pipeline**

**原因**：契约注册中心管理控件类型的 Schema 定义，不依赖 WinForms，属于配置校验的核心基础设施。

- 底层使用 `PlaceholderFramework.SchemaRegistry<string>`
- 所有方法均为静态，无 WinForms 依赖
- 被 `LayoutJsonService.ValidateConfig` 调用，而 `ValidateConfig` 在 UI 层

**迁移方案**：
1. 迁移到 `FileDriven.Pipeline.Contracts` 命名空间
2. UI 层通过项目引用使用

### 4. `Layout/LayoutJsonService.cs` → **FileDriven.Pipeline**

**原因**：`ValidateConfig` 方法执行配置契约校验，不依赖 UI，属于管线预处理阶段的工作。

- 仅包含 `ValidateConfig` 一个方法
- 委托给 `ContractValidator.Validate`
- 不依赖 WinForms

**迁移方案**：
1. 迁移到 `FileDriven.Pipeline.Layout` 命名空间
2. UI 层通过项目引用使用

---

## 三、中优先级迁移（建议迁移）

### 5. `Event/EventHub.cs` → **FileDriven.Pipeline.Core**

**原因**：EventHub 管理框架级事件（初始化完成、配置加载、日志、错误），这些事件被 `FileDrivenExecutor` 和 `ExecutionPipelineEntry` 使用，不依赖 WinForms。

**事件列表**：
- `InitializationCompleted` — 生命周期事件
- `ConfigLoaded` — 配置加载事件
- `LogWritten` — 日志事件（包装 LogRepository.LogWritten）
- `ErrorOccurred` — 错误事件（包装 FrameworkErrors.ErrorOccurred）

**依赖分析**：
- 依赖 `Common.Diagnostics.LogRepository`
- 依赖 `UniversalMiddleware.Abstractions.ContainerConfigLoadedEventArgs`
- 依赖 `UniversalMiddleware.Abstractions.FrameworkErrorEventArgs`
- **无 WinForms 依赖**

**迁移方案**：
1. 迁移到 `FileDriven.Pipeline.Core`
2. UI 层通过项目引用使用
3. 注意：`ContainerConfigLoadedEventArgs` 和 `FrameworkErrorEventArgs` 来自 UniversalMiddleware，Pipeline 项目需要添加此引用

### 6. `Event/EventSubscriptionManager.cs` → **FileDriven.Pipeline.Core**

**原因**：通用的事件订阅生命周期管理器，不依赖 WinForms，属于框架基础设施。

- 支持 `EventHandler<T>`、`Action<T>` 等多种委托类型
- 自动管理订阅生命周期，Dispose 时取消所有订阅
- **无 WinForms 依赖**

**迁移方案**：
1. 迁移到 `FileDriven.Pipeline.Core`
2. 与 EventHub 一起迁移

---

## 四、低优先级迁移（可选）

### 7. `Export/LayoutJsonExporter.cs` → **FileDriven.Pipeline**

**原因**：JSON 布局导出功能，不依赖 WinForms 运行时（只依赖 `ControlMetadata` 模型），属于诊断/调试工具。

- `ExportFormLayoutAsJson(Form)` — 依赖 WinForms Form
- `ExportLayoutAsJson(List<ControlMetadata>)` — 纯数据转换，无 WinForms 依赖

**迁移方案**：
1. 仅迁移 `ExportLayoutAsJson(List<ControlMetadata>)` 方法
2. `ExportFormLayoutAsJson(Form)` 保留在 UI 层

### 8. `Abstractions/FileDrivenWdUIBridge.cs` 生命周期部分 → **FileDriven.Execution**

**原因**：`Initialize()` 和 `Shutdown()` 方法属于框架生命周期管理，由 `FileDrivenExecutor.InitializeFramework` 调用。

**迁移方案**：
1. 将 `Initialize()` / `Shutdown()` / `IsInitialized` 提取到 `FileDriven.Execution`
2. 容器管理（`CreateContainer`、`GetContainer` 等）保留在 UI 层

---

## 五、应保留在 UI 的逻辑（不应迁移）

| 文件 | 理由 |
|------|------|
| `Core/MainForm.cs` | WinForms Form，UI 核心 |
| `Core/CommandHandler.cs` | 处理 UI 命令输入框的 KeyPress 事件 |
| `Core/ConfigSelectorController.cs` | 配置选择器的 UI 交互逻辑 |
| `Event/UIEventBinder.cs` | 绑定 WinForms 控件事件（依赖 WinForms） |
| `Utils/UIEventRegistrar.cs` | WinForms 事件注册辅助类 |
| `Utils/UILogger.cs` | UI 日志输出到 TextBox（依赖 WinForms） |
| `UIInspector.cs` | UI 控件检查器（依赖 WinForms） |
| `EventTriggerService.cs` | 事件触发服务（依赖 WinForms 运行时） |

---

## 六、迁移优先级排序

```
高优先级（必须迁移）：
  [P0] Loading/ConfigLoader.cs          → FileDriven.Execution
  [P0] Loading/ConfigLoadInput.cs       → FileDriven.Execution.Models
  [P0] Layout/Contracts/ContractRegistry.cs → FileDriven.Pipeline ✅ 已完成
  [P0] Layout/LayoutJsonService.cs      → FileDriven.Pipeline ✅ 已完成（保留 ValidateConfig 委托）

中优先级（建议迁移）：
  [P1] Event/EventHub.cs                → FileDriven.Pipeline.Core
  [P1] Event/EventSubscriptionManager.cs → FileDriven.Pipeline.Core

低优先级（可选迁移）：
  [P2] Export/LayoutJsonExporter.cs     → FileDriven.Pipeline（部分）
  [P2] Abstractions/FileDrivenWdUIBridge.cs → FileDriven.Execution（生命周期部分）
```

---

## 七、迁移注意事项

### 7.1 项目依赖关系

迁移前需确保目标项目添加必要的引用：

**FileDriven.Pipeline 需要添加的引用**（如果迁移 EventHub）：
- `UniversalMiddleware.Abstractions`（ContainerConfigLoadedEventArgs、FrameworkErrorEventArgs）
- `Share.Diagnostics`（LogRepository、FrameworkErrors）

**FileDriven.Execution 需要添加的引用**（如果迁移 ConfigLoader）：
- `WdUI`（ConfigContainer 依赖）
- `FileDriven.JsonRefer`（JsonConfigService 依赖）

### 7.2 向后兼容

- 迁移后，原位置保留 `[Obsolete]` 标记的委托方法
- 更新所有 `using` 引用
- 确保 `MainForm.cs` 中的调用路径更新

### 7.3 测试验证

- 迁移后运行 `dotnet build` 验证编译
- 运行 GUI 模式验证配置加载功能
- 运行 CLI 模式验证命令执行功能
