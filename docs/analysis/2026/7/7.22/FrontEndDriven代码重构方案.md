# FrontEndDriven 代码重构方案

> **版本：** v1.0
> **更新日期：** 2026-07-21
> **维护者：** WorkFlowPrj Team
>
> **定位：** 本文档是 [`控件逻辑整合方案.md`](./控件逻辑整合方案.md) 的**代码级补充**，聚焦于具体文件的重复/冗余/错误逻辑修复方案。
>
> **前置文档：**
> - [`控件逻辑整合方案.md`](./控件逻辑整合方案.md)（宏观架构整合）
> - [`架构重构模式 — 常见架构缺陷与应对策略.md`](../Structuring/架构重构模式%20—%20常见架构缺陷与应对策略.md)（缺陷模式参考）

---

## 目录

- [FrontEndDriven 代码重构方案](#frontenddriven-代码重构方案)
  - [目录](#目录)
  - [第一部分：问题清单](#第一部分问题清单)
    - [1.1 重复逻辑（5 项）](#11-重复逻辑5-项)
    - [1.2 冗余逻辑（6 项）](#12-冗余逻辑6-项)
    - [1.3 错误逻辑（9 项）](#13-错误逻辑9-项)
  - [第二部分：重构方案](#第二部分重构方案)
    - [2.1 UI 线程调度统一（P0）](#21-ui-线程调度统一p0)
      - [现状](#现状)
      - [目标](#目标)
      - [具体修改](#具体修改)
      - [影响文件](#影响文件)
    - [2.2 控件值读写路径精简（P0）](#22-控件值读写路径精简p0)
      - [现状](#现状-1)
      - [目标](#目标-1)
      - [具体修改](#具体修改-1)
      - [影响文件](#影响文件-1)
    - [2.3 ControlValueSyncSystem 重构（P0）](#23-controlvaluesyncsystem-重构p0)
      - [现状](#现状-2)
      - [目标](#目标-2)
      - [具体修改](#具体修改-2)
      - [影响文件](#影响文件-2)
    - [2.4 其他重复/冗余消除（P1）](#24-其他重复冗余消除p1)
      - [2.4.1 FrontEndDrivenLog 前缀清理提取（D4）](#241-frontenddrivenlog-前缀清理提取d4)
      - [2.4.2 DisposeContainer 双重查找消除（R2）](#242-disposecontainer-双重查找消除r2)
      - [2.4.3 UnifiedControlService 构造函数重复模式消除（R3）](#243-unifiedcontrolservice-构造函数重复模式消除r3)
      - [2.4.4 ControlRegistryAwareFinder 显式接口实现精简（R4）](#244-controlregistryawarefinder-显式接口实现精简r4)
      - [2.4.5 删除未使用的 using（R5）](#245-删除未使用的-usingr5)
      - [2.4.6 删除冗余重载（R6）](#246-删除冗余重载r6)
    - [2.5 次要问题修复（P2）](#25-次要问题修复p2)
      - [2.5.1 RegisterAdapter 注释修复（B4）](#251-registeradapter-注释修复b4)
      - [2.5.2 IControlRegistry 注册条件修复（B5）](#252-icontrolregistry-注册条件修复b5)
      - [2.5.3 PictureBox Image 资源泄漏修复（B6）](#253-picturebox-image-资源泄漏修复b6)
      - [2.5.4 ConfigLoader 异步包装同步修复（B7）](#254-configloader-异步包装同步修复b7)
      - [2.5.5 Find 忽略返回值修复（B8）](#255-find-忽略返回值修复b8)
  - [第三部分：实施步骤](#第三部分实施步骤)
    - [3.1 执行顺序](#31-执行顺序)
    - [3.2 影响范围](#32-影响范围)
    - [3.3 验证方法](#33-验证方法)
  - [附录：文件修改清单](#附录文件修改清单)

---

## 第一部分：问题清单

### 1.1 重复逻辑（5 项）

| # | 严重度 | 问题 | 涉及文件 | 说明 |
|---|:------:|------|---------|------|
| D1 | 🔴 HIGH | **UI 线程调度三套实现** | `WinFormsControlValueWriter.cs`、`UiThreadHelper.cs`、`IEventBindingService.cs`、`UnifiedControlService.cs` | WinFormsControlValueWriter 内部有 InvokeIfNeeded，UiThreadHelper 有 RunOnUiThread/ExecuteOnUiThread，IEventBindingService 接口也定义了 UI 线程调度方法 |
| D2 | 🔴 HIGH | **控件值读写路径双重检查** | `UnifiedControlService.cs`、`CompositeValueWriter.cs` | UnifiedControlService.GetValue/SetValue 先检查 SupportsType 再调用 ReadValue/WriteValue，而 CompositeValueWriter 内部已做 IsSupported 检查 |
| D3 | 🟡 MEDIUM | **ControlValueSyncSystem 与 WinFormsControlValueWriter 适配器逻辑重复** | `ControlValueSyncSystem.cs`、`WinFormsControlValueWriter.cs` | SyncSelection/SyncCheckState/SyncNumericValue/SyncDateTimeValue 包含与适配器重复的类型分支和属性访问 |
| D4 | 🟡 MEDIUM | **FrontEndDrivenLog 前缀清理重复** | `FrontEndDrivenLog.cs` | 两个 Log 重载包含相同的 `messageId.StartsWith(KeyPrefix)` 清理逻辑 |
| D5 | ⚪ LOW | **EcsRenderPipelineAdapter 构造函数空 ConnectEvents 调用** | `EcsRenderPipelineAdapter.cs` | 两个构造函数调用空的 ConnectEvents() |

### 1.2 冗余逻辑（6 项）

| # | 严重度 | 问题 | 涉及文件 | 说明 |
|---|:------:|------|---------|------|
| R1 | 🔴 HIGH | **SyncBoolProperty 使用反射而非 _valueWriter** | `ControlValueSyncSystem.cs` | 通过 `GetProperty(...)` 反射直接操作，而 WinFormsControlValueWriter 已有 WriteProperty/ReadProperty |
| R2 | 🔴 HIGH | **DisposeContainer 中重复的容器查找** | `WdUIContainerManager.cs` | 先 FindContainer(name) 获取实例，又 FindByName(name) 获取节点 |
| R3 | 🟡 MEDIUM | **UnifiedControlService 无参构造函数重复模式** | `UnifiedControlService.cs` | 四个 Resolve + null 检查代码块结构完全相同 |
| R4 | 🟡 MEDIUM | **ControlRegistryAwareFinder 显式接口实现仅为委托** | `ControlRegistryAwareFinder.cs` | 5 个显式接口实现仅仅是委托给公共方法 |
| R5 | ⚪ LOW | **EventSubscriptionManager 未使用的 using** | `EventSubscriptionManager.cs` | 引入 System.Collections.Concurrent、TConfig 等但未使用 |
| R6 | ⚪ LOW | **WriteValue(object, string) 冗余重载** | `WinFormsControlValueWriter.cs` | 完全委托给 WriteValue(object, object) |

### 1.3 错误逻辑（9 项）

| # | 严重度 | 问题 | 涉及文件 | 说明 |
|---|:------:|------|---------|------|
| B1 | 🔴 CRITICAL | **SyncValue 中 struct 副本修改时序问题** | `ControlValueSyncSystem.cs:74-98` | GetComponent 返回 struct 副本，SetValue 在 updateTimestamp 之前执行，事件回调读到旧时间戳 |
| B2 | 🔴 CRITICAL | **SyncEnabledVisible 中 Visible 时间戳更新丢失** | `ControlValueSyncSystem.cs:146-170` | Enabled 和 Visible 都变化时，Visible 的 LastUpdated 修改在 SetComponent 之后，修改丢失 |
| B3 | 🟠 HIGH | **SyncSelection ComboBox 分支逻辑缺陷** | `ControlValueSyncSystem.cs:186-201` | 传入索引字符串给 SetValue，适配器按文本匹配而非直接设置 SelectedIndex |
| B4 | 🟠 HIGH | **RegisterAdapter 回退路径注释与代码不一致** | `UnifiedControlService.cs:188-212` | 注释说"通过 _eventBinding 处理"，实际调用 accessor.RegisterAdapter |
| B5 | 🟡 MEDIUM | **IControlRegistry 注册条件缺陷** | `ControlServiceCollection.cs:49-61` | controlRegistry 为 null 时创建 UnifiedControlRegistry 但不注册到 AppServiceProvider |
| B6 | 🟡 MEDIUM | **PictureBox 适配器 Image 资源泄漏** | `WinFormsControlValueWriter.cs:618` | 设置新 Image 时未释放旧 Image，导致 GDI+ 资源泄漏 |
| B7 | 🟡 MEDIUM | **ConfigLoader.LoadConfigAsync 异步包装同步反模式** | `ConfigLoader.cs:80-134` | async Task 包装同步方法，无异步收益 |
| B8 | ⚪ LOW | **UnifiedControlRegistry.Find 忽略 TryGetValue 返回值** | `UnifiedControlRegistry.cs:77-81` | 调用 TryGetValue 但忽略 bool 返回值 |
| B9 | ⚪ LOW | **EventSubscriptionManager 去重字典持续增长** | `EventSubscriptionManager.cs:100` | HashSet 条目从不移除，长期运行可能增长 |

---

## 第二部分：重构方案

### 2.1 UI 线程调度统一（P0）

**对应问题：** D1

#### 现状

三套 UI 线程调度实现并存：

```
WinFormsControlValueWriter.InvokeIfNeeded(Action)          — 内部实现
WinFormsControlValueWriter.InvokeIfNeeded<T>(Func<T>)      — 内部实现
UiThreadHelper.FireAndForget(Func<Task>, onError)          — 静态类
UiThreadHelper.RunOnUiThread(Control, Func<Task>)          — 静态类
UiThreadHelper.ExecuteOnUiThread(Control, Action)          — 静态类
IEventBindingService.InvokeIfRequired / RunOnUiThread / ExecuteOnUiThread  — 接口定义
UnifiedControlService 委托给 _eventBinding                — 间接调用
```

#### 目标

统一为 `UiThreadHelper` 一处实现，其他位置改为委托调用。

#### 具体修改

**步骤 1：增强 UiThreadHelper**

```csharp
// 新增方法：同步执行（带返回值）
public static T ExecuteOnUiThread<T>(Control control, Func<T> func)
{
    if (!control.InvokeRequired)
        return func();
    return (T)control.Invoke(func);
}

// 新增方法：Fire-and-Forget（同步 Action 重载）
public static async void FireAndForget(Action action, Action<Exception>? onError = null)
{
    try
    {
        await Task.Run(action);
    }
    catch (Exception ex)
    {
        onError?.Invoke(ex);
    }
}
```

**步骤 2：修改 WinFormsControlValueWriter**

- 删除 `InvokeIfNeeded(Action)` 和 `InvokeIfNeeded<T>(Func<T>)` 方法
- 所有调用点改为 `UiThreadHelper.ExecuteOnUiThread(control, ...)`

**步骤 3：修改 IEventBindingService 接口**

- 将 UI 线程调度方法标记为 `[Obsolete("请使用 UiThreadHelper")]`
- 或直接删除接口中的 UI 线程调度方法定义

**步骤 4：修改 UnifiedControlService**

- 将对 `_eventBinding` 的 UI 线程调度委托改为直接调用 `UiThreadHelper`

#### 影响文件

| 文件 | 修改类型 | 说明 |
|------|---------|------|
| `Utils/UiThreadHelper.cs` | 增强 | 新增 ExecuteOnUiThread\<T\>、FireAndForget(Action) |
| `Implementation/WinFormsControlValueWriter.cs` | 删除 + 替换 | 删除 InvokeIfNeeded，替换为 UiThreadHelper |
| `WdUI/Abstractions/IEventBindingService.cs` | 废弃标记 | 标记 UI 线程调度方法为 Obsolete |
| `Implementation/UnifiedControlService.cs` | 替换 | 替换 _eventBinding 委托为 UiThreadHelper |

---

### 2.2 控件值读写路径精简（P0）

**对应问题：** D2

#### 现状

```
UnifiedControlService.GetValue(control, propertyName)
  → _writer.SupportsType(control.GetType())        ← 第 1 次检查
  → _writer.ReadValue(control, propertyName)
    → CompositeValueWriter.ReadValue(control, propertyName)
      → IsSupported(control.GetType())              ← 第 2 次检查（重复）
      → 遍历 _writers → 匹配适配器 → 读取
```

#### 目标

消除 `UnifiedControlService` 层的 `SupportsType` 前置检查，让 `CompositeValueWriter` 内部统一处理。

#### 具体修改

**修改 UnifiedControlService.GetValue/SetValue**

```csharp
// 修改前
public object? GetValue(Control control, string propertyName)
{
    if (!_writer.SupportsType(control.GetType()))
        return null;  // ← 冗余检查
    return _writer.ReadValue(control, propertyName);
}

// 修改后
public object? GetValue(Control control, string propertyName)
{
    return _writer.ReadValue(control, propertyName);
    // CompositeValueWriter 内部已处理 IsSupported 检查
}
```

同理修改 `SetValue`。

#### 影响文件

| 文件 | 修改类型 | 说明 |
|------|---------|------|
| `Implementation/UnifiedControlService.cs` | 精简 | 移除 GetValue/SetValue 中的 SupportsType 前置检查 |

---

### 2.3 ControlValueSyncSystem 重构（P0）

**对应问题：** B1, B2, B3, D3, R1

这是本次分析中**最严重的问题集群**，涉及 2 个 CRITICAL bug、1 个 HIGH bug、1 个重复逻辑、1 个冗余逻辑。

#### 现状

`ControlValueSyncSystem` 中存在以下问题：

1. **B1（CRITICAL）**：`SyncValue<T>` 泛型方法中，`_valueWriter.SetValue` 在 `updateTimestamp` 之前执行，事件回调读到旧时间戳
2. **B2（CRITICAL）**：`SyncEnabledVisible` 中 Enabled 和 Visible 都变化时，Visible 的 `LastUpdated` 修改丢失
3. **B3（HIGH）**：`SyncSelection` ComboBox 分支传入索引字符串给 SetValue，适配器按文本匹配
4. **D3（MEDIUM）**：同步方法包含与 WinFormsControlValueWriter 适配器重复的类型分支
5. **R1（HIGH）**：`SyncBoolProperty` 使用反射而非委托给 `_valueWriter`

#### 目标

完全重构 ControlValueSyncSystem，消除所有类型分支，统一委托给 `_valueWriter`，修复 struct 副本时序问题。

#### 具体修改

**步骤 1：修复 SyncValue 时序问题（B1）**

```csharp
// 修改前
private void SyncValue<TComponent>(...) where TComponent : struct, IComponent
{
    var comp = _entityManager.GetComponent<TComponent>(entity);  // 副本
    var targetValue = valueSelector(comp);
    var currentValue = currentValueReader(control);
    if (comparer?.Invoke(currentValue, targetValue) ?? currentValue != targetValue)
    {
        _valueWriter.SetValue(control, targetValue);  // ← 先执行
        updateTimestamp(comp);                         // ← 后执行
        _entityManager.SetComponent(entity, comp);     // 写回
    }
}

// 修改后
private void SyncValue<TComponent>(...) where TComponent : struct, IComponent
{
    var comp = _entityManager.GetComponent<TComponent>(entity);
    var targetValue = valueSelector(comp);
    var currentValue = currentValueReader(control);
    if (comparer?.Invoke(currentValue, targetValue) ?? currentValue != targetValue)
    {
        updateTimestamp(comp);                         // ← 先更新时间戳
        _entityManager.SetComponent(entity, comp);     // ← 先写回（确保事件回调读到最新数据）
        _valueWriter.SetValue(control, targetValue);   // ← 后执行 SetValue（触发事件时 Component 已是最新）
    }
}
```

**步骤 2：修复 SyncEnabledVisible 时间戳丢失（B2）**

```csharp
// 修改前 — 两个分支独立 SetComponent，第二个分支的修改丢失
if (control.Enabled != typeComp.Enabled)
{
    control.Enabled = typeComp.Enabled;
    typeComp.EnabledLastUpdated = DateTime.UtcNow;
    _entityManager.SetComponent(entity, typeComp);  // 写回
}
if (control.Visible != typeComp.Visible)
{
    control.Visible = typeComp.Visible;
    typeComp.VisibleLastUpdated = DateTime.UtcNow;  // 修改的是旧副本
    _entityManager.SetComponent(entity, typeComp);  // 但 typeComp 是新的副本
}

// 修改后 — 统一修改，一次 SetComponent
bool changed = false;
if (control.Enabled != typeComp.Enabled)
{
    control.Enabled = typeComp.Enabled;
    typeComp.EnabledLastUpdated = DateTime.UtcNow;
    changed = true;
}
if (control.Visible != typeComp.Visible)
{
    control.Visible = typeComp.Visible;
    typeComp.VisibleLastUpdated = DateTime.UtcNow;
    changed = true;
}
if (changed)
{
    _entityManager.SetComponent(entity, typeComp);
}
```

**步骤 3：消除类型分支，统一委托给 _valueWriter（D3, R1）**

将 `SyncSelection`、`SyncCheckState`、`SyncNumericValue`、`SyncDateTimeValue`、`SyncBoolProperty` 中的类型分支和反射逻辑，全部替换为对 `_valueWriter` 的统一调用。

```csharp
// SyncSelection 修改示例
private void SyncSelection()
{
    SyncValue<SelectionComponent>(
        valueSelector: comp => comp.SelectedItem ?? comp.SelectedIndex.ToString(),
        currentValueReader: control => control.Text,
        updateTimestamp: comp => comp.LastUpdated = DateTime.UtcNow);
}

// SyncCheckState 修改示例
private void SyncCheckState()
{
    SyncValue<CheckStateComponent>(
        valueSelector: comp => comp.Checked.ToString(),
        currentValueReader: control => control.Text,
        updateTimestamp: comp => comp.LastUpdated = DateTime.UtcNow);
}

// SyncNumericValue 修改示例
private void SyncNumericValue()
{
    SyncValue<NumericValueComponent>(
        valueSelector: comp => comp.Value.ToString(),
        currentValueReader: control => control.Text,
        updateTimestamp: comp => comp.LastUpdated = DateTime.UtcNow);
}

// SyncDateTimeValue 修改示例
private void SyncDateTimeValue()
{
    SyncValue<DateTimeValueComponent>(
        valueSelector: comp => comp.Value.ToString("yyyy-MM-dd HH:mm:ss"),
        currentValueReader: control => control.Text,
        updateTimestamp: comp => comp.LastUpdated = DateTime.UtcNow);
}
```

**步骤 4：删除 SyncBoolProperty 方法（R1）**

`SyncBoolProperty` 的反射逻辑完全由 `_valueWriter.WriteProperty/ReadProperty` 覆盖，直接删除该方法，将 `Enabled`/`Visible` 的同步合并到 `SyncEnabledVisible` 中直接通过 Control 属性访问（这是合理的，因为 Enabled/Visible 是所有 Control 共有的属性，不需要适配器）。

#### 影响文件

| 文件 | 修改类型 | 说明 |
|------|---------|------|
| `ECS/Systems/ControlValueSyncSystem.cs` | 重构 | 修复 B1/B2/B3，消除 D3/R1，精简代码量约 40% |

---

### 2.4 其他重复/冗余消除（P1）

#### 2.4.1 FrontEndDrivenLog 前缀清理提取（D4）

**修改 FrontEndDrivenLog.cs：**

```csharp
// 提取私有辅助方法
private static string CleanMessageId(string messageId)
{
    return messageId.StartsWith(KeyPrefix)
        ? messageId.Substring(KeyPrefix.Length)
        : messageId;
}

// 两个 Log 重载中替换为：
var cleanMsgId = CleanMessageId(messageId);
```

#### 2.4.2 DisposeContainer 双重查找消除（R2）

**修改 WdUIContainerManager.cs：**

```csharp
// 修改前
public void DisposeContainer(string name)
{
    var container = FindContainer(name);  // 第 1 次查找
    var node = FindByName(name);          // 第 2 次查找
    // ...
}

// 修改后
public void DisposeContainer(string name)
{
    var container = FindContainer(name);  // 只查找一次
    if (container == null) return;
    // 通过 container 获取所需信息，不再调用 FindByName
    // ...
}
```

#### 2.4.3 UnifiedControlService 构造函数重复模式消除（R3）

```csharp
// 提取私有辅助方法
private T ResolveRequired<T>() where T : class
{
    var service = AppServiceProvider.Resolve<T>();
    if (service == null)
        throw new InvalidOperationException(
            $"UnifiedControlService: {typeof(T).Name} 未注册");
    return service;
}

// 构造函数中简化为：
_writer = ResolveRequired<IControlValueAccessor>();
_registry = ResolveRequired<IControlRegistry>();
_finder = ResolveRequired<IControlFinder>();
_eventBinding = ResolveRequired<IEventBindingService>();
```

#### 2.4.4 ControlRegistryAwareFinder 显式接口实现精简（R4）

如果接口签名与公共方法完全兼容，删除显式接口实现，让公共方法直接满足接口。否则保留但添加注释说明。

#### 2.4.5 删除未使用的 using（R5）

删除 `EventSubscriptionManager.cs` 中未使用的 `using` 指令。

#### 2.4.6 删除冗余重载（R6）

删除 `WinFormsControlValueWriter.WriteValue(object, string)`，调用方改为使用 `WriteValue(object, object)`。

---

### 2.5 次要问题修复（P2）

#### 2.5.1 RegisterAdapter 注释修复（B4）

```csharp
// 修改前
// 回退路径改为通过 _eventBinding 处理
if (_writer is TConfig.Controls.IControlValueAccessor accessor)
{
    accessor.RegisterAdapter(type, adapter);
}

// 修改后 — 修正注释，与实际代码一致
// 回退路径：直接注册到 IControlValueAccessor
if (_writer is TConfig.Controls.IControlValueAccessor accessor)
{
    accessor.RegisterAdapter(type, adapter);
}
```

#### 2.5.2 IControlRegistry 注册条件修复（B5）

```csharp
// 修改前
if (controlRegistry != null)
    AppServiceProvider.Register<IControlRegistry>(controlRegistry);

// 修改后 — 始终注册
AppServiceProvider.Register<IControlRegistry>(
    controlRegistry ?? new UnifiedControlRegistry());
```

#### 2.5.3 PictureBox Image 资源泄漏修复（B6）

```csharp
// 在设置新 Image 前释放旧 Image
var oldImage = pictureBox.Image;
if (oldImage != null)
{
    pictureBox.Image = null;  // 先解除引用
    oldImage.Dispose();       // 释放资源
}
pictureBox.Image = Image.FromFile(value);
```

#### 2.5.4 ConfigLoader 异步包装同步修复（B7）

```csharp
// 修改前
public static async Task LoadConfigAsync(EcsConfigContainer container, string configPath, bool strictMode = true)

// 修改后 — 改为同步方法
public static void LoadConfig(EcsConfigContainer container, string configPath, bool strictMode = true)
```

同时更新所有调用方。

#### 2.5.5 Find 忽略返回值修复（B8）

```csharp
// 修改前
public Control? Find(string name)
{
    _controls.TryGetValue(name, out var control);
    return control;
}

// 修改后
public Control? Find(string name)
{
    if (string.IsNullOrEmpty(name))
        return null;
    return _controls.TryGetValue(name, out var control) ? control : null;
}
```

---

## 第三部分：实施步骤

### 3.1 执行顺序

```
P0 — 第 1 步：ControlValueSyncSystem 重构（修复 2 个 CRITICAL bug）
P0 — 第 2 步：UI 线程调度统一
P0 — 第 3 步：控件值读写路径精简
P1 — 第 4 步：其他重复/冗余消除
P2 — 第 5 步：次要问题修复
```

### 3.2 影响范围

| 步骤 | 修改文件数 | 风险 | 回退策略 |
|------|:----------:|:----:|---------|
| 1. ControlValueSyncSystem 重构 | 1 | 高（核心同步逻辑） | 保留原文件，新文件并行 |
| 2. UI 线程调度统一 | 4 | 中（多处调用点） | 保留 InvokeIfNeeded 为内部方法 |
| 3. 值读写路径精简 | 1 | 低（仅移除前置检查） | 直接回退 |
| 4. 其他重复消除 | 5 | 低 | 逐文件回退 |
| 5. 次要问题修复 | 5 | 低 | 逐文件回退 |

### 3.3 验证方法

| 验证项 | 方法 | 通过标准 |
|--------|------|---------|
| ControlValueSyncSystem 同步正确性 | 单元测试 + 手动验证 | 所有控件类型的值同步正确，时间戳更新正确 |
| UI 线程调度 | 多线程场景测试 | 跨线程调用不抛出 InvalidOperationException |
| 值读写路径 | 回归测试 | 所有控件类型的 GetValue/SetValue 行为一致 |
| 编译 | dotnet build | 零错误零警告 |
| 资源泄漏 | 内存分析 | PictureBox 切换图片后旧 Image 被释放 |

---

## 附录：文件修改清单

| 文件路径 | 修改类型 | 涉及问题 | 预计行数变化 |
|----------|---------|:--------:|:-----------:|
| `ECS/Systems/ControlValueSyncSystem.cs` | 重构 | B1, B2, B3, D3, R1 | -120 行 |
| `Utils/UiThreadHelper.cs` | 增强 | D1 | +30 行 |
| `Implementation/WinFormsControlValueWriter.cs` | 删除 + 替换 | D1, B6, R6 | -40 行 |
| `Implementation/UnifiedControlService.cs` | 精简 | D2, R3, B4 | -30 行 |
| `Implementation/WdUIContainerManager.cs` | 精简 | R2 | -10 行 |
| `Implementation/ControlRegistryAwareFinder.cs` | 精简 | R4 | -30 行 |
| `Implementation/EventSubscriptionManager.cs` | 清理 | R5 | -5 行 |
| `Registration/ControlServiceCollection.cs` | 修复 | B5 | +2 行 |
| `Registration/UnifiedControlRegistry.cs` | 修复 | B8 | +3 行 |
| `Loading/ConfigLoader.cs` | 修复 | B7 | -10 行 |
| `Diagnostics/FrontEndDrivenLog.cs` | 提取 | D4 | +5 行 |
| **合计** | | **20 项问题** | **-205 行** |
