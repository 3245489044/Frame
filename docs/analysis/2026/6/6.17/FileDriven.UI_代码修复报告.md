# FileDriven.UI 代码修复报告

> 分析日期：2026-06-17
> 分析范围：FileDriven/FileDriven.UI 项目
> 修复类型：重复逻辑、冗余逻辑、错误逻辑

---

## 一、严重问题修复

### 修复1+2: UIEventBinder 事件泄漏 + WinFormsEventRegistrar 支持取消订阅

**问题描述：**
- `UIEventBinder.BindConfigSelectorEvents()` 通过 `WinFormsEventRegistrar` 注册了 6 个事件，但 `UnbindConfigSelectorEvents()` 方法体为空，无法取消订阅
- `WinFormsEventRegistrar` 的注册方法不返回取消订阅委托，导致调用方无法执行 `-=` 操作
- 每次 `OnConfigSelectorLoaded` 被触发时，`_eventBinder.BindAll()` 会重新注册事件，但旧的事件从未被取消，导致事件泄漏

**修复方案：**
1. `WinFormsEventRegistrar` 所有注册方法改为返回 `Action` 取消订阅委托
2. `UIEventBinder` 保存这些委托到 `_unsubscribeActions` 列表
3. `UnbindConfigSelectorEvents()` 遍历列表执行所有取消订阅操作

**涉及文件：**
- `FileDriven/FileDriven.UI/Utils/UIEventRegistrar.cs`
- `FileDriven/FileDriven.UI/Event/UIEventBinder.cs`

### 修复3: MainForm.OnLogWritten 日志格式不一致

**问题描述：**
- `OnLogWritten` 自己格式化消息为 `[{Level}] [{Module}] {message}`，然后调用 `UILogger.LogMessage`
- `UILogger.LogMessage` 内部又添加时间戳，导致最终输出格式为 `[{timestamp}] [{Level}] [{Module}] {message}`，与 `LogMessage` 方法的格式一致
- 但 `UILogger.LogMessage` 还会通过 `FileDrivenLogPlaceholderHandler.Log` 记录到统一日志系统，造成日志重复

**修复方案：**
- `OnLogWritten` 改为直接调用 `UILogger.AppendTextSafe`（改为 `internal` 访问级别），避免通过 `LogMessage` 重复记录
- 格式统一为 `[{timestamp}] [{Level}] [{Module}] {message}`

**涉及文件：**
- `FileDriven/FileDriven.UI/Core/MainForm.cs`
- `FileDriven/FileDriven.UI/Utils/UILogger.cs`

### 修复4: FileDrivenWdUIBridge.ClearSideContainer Invoke vs BeginInvoke 统一

**问题描述：**
- `ClearSideContainer` 使用 `Invoke`（同步）进行线程封送
- `UILogger.LogMessage` 使用 `BeginInvoke`（异步）
- 不一致的线程封送方式可能导致线程池饥饿（`Invoke` 阻塞调用线程）

**修复方案：**
- 将 `Invoke` 改为 `BeginInvoke`，与 `UILogger` 保持一致

**涉及文件：**
- `FileDriven/FileDriven.UI/Abstractions/FileDrivenWdUIBridge.cs`

### 修复5: EventTriggerService.ApplyComboBoxFallbackAsync 防重入守卫优化

**问题描述：**
- `ApplyComboBoxFallbackAsync` 使用 `ReentrantGuard` 防重入
- 当 `TryEnter()` 返回 `false` 时，方法直接 `return result`，但 `finally` 块中仍会调用 `Exit()`
- 未进入的调用也调用 `Exit()`，可能破坏守卫状态

**修复方案：**
- 在注释中明确说明 `TryEnter` 返回 `false` 时直接 `return`，不执行 `finally` 中的 `Exit()`
- 当前 `ReentrantGuard` 实现已正确处理此情况（未进入时 `Exit()` 是空操作），注释仅用于文档说明

**涉及文件：**
- `FileDriven/FileDriven.UI/EventTriggerService.cs`

---

## 二、冗余逻辑优化

### 优化1: EventSubscriptionManager Subscribe 方法简化

**问题描述：**
- 三个 `Subscribe` 重载（`EventHandler<TEventArgs>`、`Action<T>`、`EventHandler`）有完全相同的模式：`subscribe(handler)` + `_unsubscribeActions.Add(() => unsubscribe(handler))`
- 重复的样板代码

**修复方案：**
- 提取内部辅助方法 `AddUnsubscribeAction<TDelegate>`，三个重载都委托给此方法

**涉及文件：**
- `FileDriven/FileDriven.UI/Event/EventSubscriptionManager.cs`

### 优化2: WinFormsUIInspector 双重构造函数简化

**问题描述：**
- `WinFormsUIInspector` 有两个构造函数：无参构造和全参构造（所有参数可选）
- 无参构造的初始化逻辑与全参构造的默认值逻辑完全重复

**修复方案：**
- 删除无参构造函数，全参构造的默认参数已覆盖所有情况

**涉及文件：**
- `FileDriven/FileDriven.UI/UIInspector.cs`

### 优化3: Skeleton 同步方法禁用样板代码

**问题描述：**
- `TemplatePreprocessSkeleton`、`ContainerRenderSkeleton`、`ConfigLoadSkeleton` 三个类都有 `[Obsolete("请使用异步版本 ExecuteCoreAsync", true)]` 标记的同步 `ExecuteCore` 方法
- 方法体都是 `throw new NotSupportedException(...)`，是冗余的样板代码

**处理结果：**
- 保留现状。`SkeletonBase` 基类要求必须重写 `ExecuteCore`，无法删除
- `[Obsolete(..., true)]` 已确保编译时禁止调用，运行时抛出异常是安全的防护措施

### 优化4: UIEventBinder.UnbindConfigSelectorEvents 空方法体处理

**处理结果：**
- 已在修复1中完成。`UnbindConfigSelectorEvents` 现在有实际逻辑：遍历 `_unsubscribeActions` 列表执行取消订阅

---

## 三、修改文件清单

| 文件 | 修改类型 | 说明 |
|------|---------|------|
| `Utils/UIEventRegistrar.cs` | 修复+优化 | 注册方法返回取消订阅委托 |
| `Event/UIEventBinder.cs` | 修复 | 实现真正的取消订阅逻辑 |
| `Core/MainForm.cs` | 修复 | OnLogWritten 直接调用 AppendTextSafe |
| `Utils/UILogger.cs` | 修复 | AppendTextSafe 改为 internal |
| `Abstractions/FileDrivenWdUIBridge.cs` | 修复 | Invoke → BeginInvoke |
| `EventTriggerService.cs` | 修复 | 防重入守卫注释优化 |
| `Event/EventSubscriptionManager.cs` | 优化 | 提取 AddUnsubscribeAction 辅助方法 |
| `UIInspector.cs` | 优化 | 合并双重构造函数 |

---

## 四、未修复问题

1. **Skeleton 同步方法样板代码**：基类约束，无法删除 `ExecuteCore` 重写
2. **`LoadWorkflowDetailsSkeleton` 类**：位于外部项目 `SkeletonFlow.App`，不在本分析范围内
3. **`ConfigContainer.FindControl<T>` 与 `Controls.Find` 重复**：`FindControl<T>` 是 `IConfigContainer` 接口方法，`Controls.Find` 是 WinForms 原生方法，两者功能重叠但接口不同，保留
