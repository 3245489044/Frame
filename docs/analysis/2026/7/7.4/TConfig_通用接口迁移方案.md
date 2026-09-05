# TConfig 通用接口迁移方案

> **目标**：将 TConfig 改造为自包含的通用接口宿主，消除项目中重复的 `IControlValueAccessor` / `IControlValueWriter` 定义，统一控件值读写契约。

---

## 1. 现状分析

### 1.1 TConfig 当前状态

| 属性 | 值 |
|------|-----|
| 位置 | `CsharpPrj/Plate/TConfig/` |
| 层 | 层 6（平台层） |
| 当前依赖 | `BasicMethod.Transformers`（仅一个依赖） |
| 当前命名空间 | `Modules.Transformers`（RootNamespace） |
| 当前 Assembly | `Modules.TConfig` |
| 当前职责 | 转换器配置框架（ITrConfig、TrConfig、TrConfigRegistry、TrCall、TrPipeline 等） |

### 1.2 当前被 TConfig 引用的项目

- `BasicMethod`（层 1）
- `Registry`（层 2）
- `ExecutionFramework`（层 2）

### 1.3 项目中重复的控件值读写接口

| 接口 | 位置 | 方法签名 | 用途 |
|------|------|---------|------|
| `IControlValueWriter` ⚠️ | `Share/CommandFramework/.../CommandPipeline/` | WriteValue/ReadValue/SupportsType/RegisterAdapter | 旧版控件值写入器，已标记 [Obsolete] |
| `IControlValueAccessor` (A) | `Share/CommandFramework/.../Shared/Abstractions/ICommand.cs` | GetValue/SetValue/GetValue\<T>/IsSupported/RegisterAdapter/RegisterAdapterWithResult/ClearAdapters | CommandFramework 内部使用 |
| `IControlValueAccessor` (B) | `Modules/MetaCommand/ControlAbstractions.cs` | GetValue/SetValue/IsSupported/RegisterAdapter/ClearAdapters | MetaCommand 控件抽象层 |
| `IControlValueAccessor` (C) | `Operation/FrontEndDriven/Abstractions/` | GetValue/SetValue（仅 string 版本） | FrontEndDriven 前端驱动 |
| `IControlValueAccessor` (D) | `Share/FrameworkAbstractions/Controls/` | GetValue/SetValue/ClickControl | FrameworkAbstractions 控件抽象 |

### 1.4 实现类分布

| 实现类 | 位置 | 实现的接口 |
|--------|------|-----------|
| `WinFormsControlValueWriterAdapter` | `Operation/WdUI/UI/WdUI.Core.cs` | `IControlValueWriter` |
| `WinFormsControlValueWriter` | `Operation/FrontEndDriven/Implementation/` | `IControlValueWriter` |
| `CompositeValueWriter` | `Operation/FrontEndDriven/Implementation/` | `IControlValueWriter` |
| `ControlValueAccessorBase` | `Share/CommandFramework/Execution/CommandPipeline/` | `IControlValueAccessor` (A) |
| `WinFormsControlValueAccessor` | `Operation/EventTriggerFramework/WinForms/` | `IControlValueAccessor` (B) |
| `IControlService` | `Modules/MetaCommand/ControlAbstractions.cs` | 继承 `IControlValueAccessor` (B) |

---

## 2. 迁移方案

### 2.1 第一步：TConfig 自包含改造

**目标**：移除 TConfig 对 `BasicMethod.Transformers` 的依赖，使其成为零依赖的自包含项目。

**操作**：
1. 将 TConfig 中实际使用 Transformers 的类型**内联**到 TConfig 内部
2. 移除 `.csproj` 中的 `ProjectReference`
3. 验证编译通过

**影响分析**：
- TConfig 当前只引用了 `Transformers.csproj`
- 需要检查 TConfig 代码中实际使用了 Transformers 的哪些类型，将其复制/内联到 TConfig 内部

### 2.2 第二步：在 TConfig 中定义统一接口

在 `CsharpPrj/Plate/TConfig/Controls/` 目录下创建统一的控件值读写接口。

#### 2.2.1 统一接口设计

综合各版本的共性，设计统一的 `IControlValueAccessor`：

```csharp
// ============================================================
// IControlValueAccessor.cs — 统一控件值读写器接口
//
// 定义在 TConfig（平台层），作为全项目唯一的控件值读写契约。
// 所有层级的控件值读写操作统一通过此接口进行。
//
// 设计原则：
// 1. 使用 object 类型替代具体控件类型，消除对 UI 框架的编译时依赖
// 2. 提供 GetValue/SetValue 核心原语
// 3. 提供 IsSupported 查询能力
// 4. 提供 RegisterAdapter 扩展机制
// ============================================================

namespace TConfig.Controls;

/// <summary>
/// 统一控件值读写器接口
/// <para>全项目唯一的控件值读写契约，所有层级的控件值操作统一通过此接口进行。</para>
/// <para>使用 object 类型替代具体控件类型，消除对 UI 框架的编译时依赖。</para>
/// </summary>
public interface IControlValueAccessor
{
    /// <summary>获取控件的当前值</summary>
    /// <param name="control">控件实例</param>
    /// <returns>控件的当前值，失败返回 null</returns>
    object? GetValue(object control);

    /// <summary>设置控件的值</summary>
    /// <param name="control">控件实例</param>
    /// <param name="value">要设置的值</param>
    void SetValue(object control, object? value);

    /// <summary>判断指定控件是否受支持</summary>
    bool IsSupported(object control);

    /// <summary>注册控件类型适配器</summary>
    void RegisterAdapter(Type controlType, Func<object, object?> getter, Action<object, object?> setter);

    /// <summary>清空所有已注册的适配器</summary>
    void ClearAdapters();
}
```

#### 2.2.2 接口设计说明

| 方法 | 来源 | 说明 |
|------|------|------|
| `GetValue(object)` | 所有版本共识 | 返回 `object?` 而非 `string?`，保留类型信息 |
| `SetValue(object, object?)` | 所有版本共识 | 接受 `object?` 而非 `string?`，支持任意类型值 |
| `IsSupported(object)` | 版本 A/B 共识 | 查询控件是否受支持 |
| `RegisterAdapter(Type, ...)` | 版本 A/B 共识 | 使用 `Type` 而非泛型，简化反射场景 |
| `ClearAdapters()` | 版本 A/B 共识 | 清理所有适配器 |

**不包含的方法**（各版本特有，由消费方自行处理）：
- `GetValue<TResult>()` — 强类型版本，调用方自行转换
- `RegisterAdapter<TControl>()` — 泛型版本，调用方自行包装
- `RegisterAdapterWithResult()` — CommandFramework 特有
- `ClickControl()` — FrameworkAbstractions 特有
- `WriteValue/ReadValue` — 旧版命名，已过时

### 2.3 第三步：标记旧接口为过时

在所有旧接口上添加 `[Obsolete]` 属性，指向 TConfig 的统一接口：

| 文件 | 修改 |
|------|------|
| `IControlValueWriter.cs` | 已有 `[Obsolete]`，更新消息指向 `TConfig.Controls.IControlValueAccessor` |
| `ICommand.cs` 中的 `IControlValueAccessor` | 添加 `[Obsolete("Use TConfig.Controls.IControlValueAccessor")]` |
| `MetaCommand/ControlAbstractions.cs` 中的 `IControlValueAccessor` | 添加 `[Obsolete("Use TConfig.Controls.IControlValueAccessor")]` |
| `FrontEndDriven/Abstractions/IControlValueAccessor.cs` | 添加 `[Obsolete("Use TConfig.Controls.IControlValueAccessor")]` |
| `FrameworkAbstractions/Controls/IControlValueAccessor.cs` | 添加 `[Obsolete("Use TConfig.Controls.IControlValueAccessor")]` |

### 2.4 第四步：迁移实现类

将各实现类改为实现 TConfig 的统一接口：

| 实现类 | 当前接口 | 迁移目标 |
|--------|---------|---------|
| `WinFormsControlValueWriterAdapter` | `IControlValueWriter` | `TConfig.Controls.IControlValueAccessor` |
| `WinFormsControlValueWriter` | `IControlValueWriter` | `TConfig.Controls.IControlValueAccessor` |
| `CompositeValueWriter` | `IControlValueWriter` | `TConfig.Controls.IControlValueAccessor` |
| `ControlValueAccessorBase` | `IControlValueAccessor` (A) | `TConfig.Controls.IControlValueAccessor` |
| `WinFormsControlValueAccessor` | `IControlValueAccessor` (B) | `TConfig.Controls.IControlValueAccessor` |

### 2.5 第五步：迁移消费方

更新所有引用旧接口的地方，改为引用 TConfig 的统一接口。

**警告涉及的消费方**（当前 9 个警告）：

| 文件 | 行号 | 当前引用 | 迁移后 |
|------|------|---------|--------|
| `WorkflowMethodExecutionWorkflow.cs` | 45,54,212 | `IControlValueWriter` | `TConfig.Controls.IControlValueAccessor` |
| `WdUI.Core.cs` | 210,215,514 | `IControlValueWriter` | `TConfig.Controls.IControlValueAccessor` |
| `WdUI.Management.cs` | 50 | `IControlValueWriter` | `TConfig.Controls.IControlValueAccessor` |

**其他消费方**（需全局搜索确认）：

| 文件 | 当前引用 | 迁移后 |
|------|---------|--------|
| `ExecuteCommandStage.cs` | `IControlValueWriter` | `TConfig.Controls.IControlValueAccessor` |
| `ControlValueAccessorBase.cs` | `IControlValueAccessor` (A) | `TConfig.Controls.IControlValueAccessor` |
| `WinFormsControlValueAccessor.cs` | `IControlValueAccessor` (B) | `TConfig.Controls.IControlValueAccessor` |
| `IControlService.cs` | `IControlValueAccessor` (B) | `TConfig.Controls.IControlValueAccessor` |

### 2.6 第六步：添加项目引用

为需要引用 TConfig 统一接口的项目添加 `ProjectReference`：

| 项目 | 当前是否引用 TConfig | 操作 |
|------|-------------------|------|
| `CommandFramework.csproj` | ❌ | 添加 `TConfig` 引用 |
| `MetaCommand.csproj` | ❌ | 添加 `TConfig` 引用 |
| `WdUI.csproj` | ❌ | 添加 `TConfig` 引用 |
| `FrontEndDriven.csproj` | ❌ | 添加 `TConfig` 引用 |
| `EventTriggerFramework.csproj` | ❌ | 添加 `TConfig` 引用 |
| `FrameworkAbstractions.csproj` | ❌ | 添加 `TConfig` 引用 |
| `FunctionStorage.csproj` | ❌ | 添加 `TConfig` 引用 |

---

## 3. 迁移顺序

```
Step 1: TConfig 自包含改造（移除 Transformers 依赖）
    ↓
Step 2: 在 TConfig 中定义统一 IControlValueAccessor 接口
    ↓
Step 3: 标记所有旧接口为 [Obsolete]
    ↓
Step 4: 为消费方项目添加 TConfig 引用
    ↓
Step 5: 迁移实现类（逐个文件修改）
    ↓
Step 6: 迁移消费方（逐个文件修改）
    ↓
Step 7: 全解决方案编译验证
    ↓
Step 8: 清理（删除旧接口文件，如不再使用）
```

---

## 4. 风险与注意事项

### 4.1 接口签名差异处理

| 旧接口方法 | 统一接口方法 | 迁移注意事项 |
|-----------|------------|------------|
| `WriteValue(object, string?)` | `SetValue(object, object?)` | 调用方需改为 SetValue，string 自动向上转型为 object |
| `WriteValue(object, object?)` | `SetValue(object, object?)` | 直接映射 |
| `ReadValue(object)` → `string?` | `GetValue(object)` → `object?` | 调用方需处理 object→string 转换 |
| `SupportsType(object)` | `IsSupported(object)` | 直接映射 |
| `RegisterAdapter<TControl>(writer, reader)` | `RegisterAdapter(Type, getter, setter)` | 调用方需改为 Type 版本 |
| `GetValue<TResult>(object)` | 无 | 调用方自行 `(TResult)GetValue(control)` |
| `RegisterAdapterWithResult<TControl>(...)` | 无 | CommandFramework 内部使用，保留在 CommandFramework 中 |
| `ClickControl(object)` | 无 | FrameworkAbstractions 特有，保留在 FrameworkAbstractions 中 |

### 4.2 向后兼容

- 旧接口保留 `[Obsolete]` 标记但不删除，确保现有代码逐步迁移
- 实现类可同时实现新旧两个接口（适配器模式），过渡期共存
- 建议在 2-3 个迭代周期后清理旧接口文件

### 4.3 循环依赖风险

TConfig 位于层 6（平台层），被层 1/2/3/4 引用。将接口定义在 TConfig 中不会产生循环依赖，因为：
- TConfig 不依赖任何上层项目（自包含后零依赖）
- 所有上层项目引用 TConfig 是单向的（上层→下层）

---

## 5. 验证标准

- [ ] TConfig 自包含改造后编译通过，零依赖
- [ ] 统一 `IControlValueAccessor` 接口定义在 `TConfig.Controls` 命名空间
- [ ] 所有旧接口标记为 `[Obsolete("Use TConfig.Controls.IControlValueAccessor")]`
- [ ] 所有实现类改为实现统一接口
- [ ] 所有消费方改为引用统一接口
- [ ] 全解决方案编译：**0 警告 0 错误**
- [ ] 运行时功能验证：控件值读写功能正常

---

## 6. 附录：接口签名对照表

### 6.1 IControlValueWriter（CommandFramework，已过时）

```csharp
bool WriteValue(object control, string? value);
bool WriteValue(object control, object? value);
string? ReadValue(object control);
bool SupportsType(object control);
void RegisterAdapter<TControl>(Func<TControl, string?, bool> writer, Func<TControl, string?> reader);
```

### 6.2 IControlValueAccessor（CommandFramework.ICommand）

```csharp
object? GetValue(object controlHandle);
void SetValue(object controlHandle, object? value);
TResult? GetValue<TResult>(object controlHandle);
bool IsSupported(object controlHandle);
void RegisterAdapter<TControl>(Func<TControl, object?> getter, Action<TControl, object?> setter);
void RegisterAdapterWithResult<TControl>(Func<TControl, object?> getter, Func<TControl, object?, CommandExecutionResult<object>> setterWithResult);
void ClearAdapters();
```

### 6.3 IControlValueAccessor（MetaCommand）

```csharp
object? GetValue(object control);
void SetValue(object control, object? value);
bool IsSupported(object control);
void RegisterAdapter(Type controlType, Func<object, object?> getter, Action<object, object?> setter);
void ClearAdapters();
```

### 6.4 IControlValueAccessor（FrontEndDriven）

```csharp
string? GetValue(object control);
void SetValue(object control, string? value);
```

### 6.5 IControlValueAccessor（FrameworkAbstractions）

```csharp
string? GetValue(object control);
void SetValue(object control, string? value);
void ClickControl(object control);
```

### 6.6 统一接口（TConfig.Controls）

```csharp
object? GetValue(object control);
void SetValue(object control, object? value);
bool IsSupported(object control);
void RegisterAdapter(Type controlType, Func<object, object?> getter, Action<object, object?> setter);
void ClearAdapters();
```
