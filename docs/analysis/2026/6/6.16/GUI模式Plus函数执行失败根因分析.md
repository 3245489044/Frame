# GUI 模式 `Plus` 函数执行失败根因分析

## 问题描述

在 GUI 模式下，点击 `addButton` 触发 `Plus` 函数时，日志报错：

```
[13:26:04] [ERROR] [FileDriven] [ERROR] addButton.click: Function 'Plus' 执行失败: Argument building failed: could not resolve parameters for 'Plus'
```

## 执行路径追踪

### 1. 事件触发链

```
用户点击 addButton
  → WinFormsEventBinder.BindSingleEvent 绑定的 Click 处理器被调用
    → FireAndForgetTriggerAsync 执行
      → 查找 addButton 的 click 绑定
        → EventBindingResolver.GetBindingInfo("addButton", "click")
          → 返回 binding: { FunctionName="Plus", Inputs=[], Output="resultTextBox" }
```

### 2. 关键问题：`Inputs` 为空数组

`EventBindingResolver.GetBindingInfo()` 返回的 `Inputs` 是 **空数组 `[]`**，而不是预期的 `["valueATextBox", "valueBTextBox"]`。

### 3. 参数传递链

```
binding.Inputs = []  (空数组)
  → PipelineFactory 中: Parameter = string.Join(",", []) = ""  (空字符串)
    → ExecuteCommandStage.HandleClick 中: inputControls = ""  (空字符串)
      → string.IsNullOrEmpty("") = true
        → parameters = null  (不进入 if 分支)
          → executor.ExecuteAsync("Plus", null)
            → FunctionExecutor.ExecuteDelegateAsync(del, descriptor, null)
              → ArgumentBuilder.BuildFromDescriptor(descriptor, null)
                → parameters 为 null，paramTypes = [double, double] (长度 2)
                  → 不走快速路径 1 (parameters is string?[] → false)
                  → 不走快速路径 2 (parameters is object?[] → false)
                  → 走降级路径 BuildViaParameterBuilder
                    → ParameterBuilder.Build(paramDescriptors, null)
                      → MatchParameters 中匹配器链全部失败
                        → 返回 ParameterBuildResult.Failure
                          → BuildViaParameterBuilder 返回 null
                            → BuildFromDescriptor 返回 null
                              → ExecuteDelegateAsync 返回 "Argument building failed"
```

## 根因分析

### 直接原因

`EventBindingResolver.GetBindingInfo()` 返回的 `Inputs` 为空数组 `[]`，导致 `HandleClick` 中 `inputControls` 为空字符串，`parameters` 保持 `null`，最终 `ArgumentBuilder.BuildFromDescriptor` 无法构建参数。

### 根本原因

**`buildEvents: true` 覆盖了显式定义的 `events` 配置。**

在 `composites.json` 中，`button` 模板定义了 `buildEvents: true`：

```json
{
  "button": {
    "type": "button",
    "defaults": {
      "style": "primary-button",
      "font": { "size": 9 }
    },
    "parameters": {
      "name": { "required": true },
      "text": { "required": true },
      "functionName": { "required": true },
      "layout": { "required": true },
      "events": { "default": {} }
    },
    "buildEvents": true
  }
}
```

当 `buildEvents: true` 时，模板展开逻辑会**自动生成**事件绑定，覆盖用户在 JSON 配置中显式定义的 `events` 配置。

用户配置中显式定义了：

```json
{
  "events": {
    "click": "Plus",
    "bindings": {
      "inputs": ["valueATextBox", "valueBTextBox"],
      "output": "resultTextBox"
    }
  }
}
```

但由于 `buildEvents: true` 的自动生成逻辑，`bindings.inputs` 被覆盖为空数组 `[]`，导致 `Plus` 函数无法获取输入参数。

### 代码路径确认

1. **`EventBindingResolver.GetBindingInfo()`** — 返回的 `Inputs` 为空数组
2. **`PipelineFactory` 第 180 行** — `Parameter = result.Inputs?.Count > 0 ? string.Join(",", result.Inputs) : null`
   - 由于 `Inputs.Count == 0`，`Parameter` 为 `null`
3. **`ExecuteCommandStage.HandleClick` 第 217 行** — `inputControls` 为 `null`，`parameters` 保持 `null`
4. **`ArgumentBuilder.BuildFromDescriptor` 第 50 行** — `parameters` 为 `null`，不走快速路径
5. **`ArgumentBuilder.BuildViaParameterBuilder` 第 188-189 行** — `ParameterBuilder.Build` 返回失败

---

## 框架层面混乱的深层原因分析

### 混乱一：`buildEvents` 的设计缺陷

**问题：** `buildEvents` 是一个布尔开关，只有"开/关"两种状态，无法表达"部分覆盖"的语义。

当 `buildEvents: true` 时，模板展开逻辑会**整体替换**整个 `events` 对象，而不是**深度合并**。这导致：

- 用户显式定义的 `bindings.inputs` 被整体丢弃
- 用户显式定义的 `bindings.output` 被整体丢弃
- 自动生成的 `bindings` 中 `inputs` 为空数组（因为自动生成逻辑不知道用户想输入什么）

**本质：** `buildEvents` 的语义是"自动构建事件绑定"，但它的实现是"整体替换"，而不是"补充缺失字段"。

### 混乱二：参数传递链过长（6 层转换）

从用户配置到最终函数调用，参数经历了 6 层转换：

```
JSON 配置中的 bindings.inputs
  → 1. EventBindingResolver.GetBindingInfo()  → Inputs 列表
    → 2. PipelineFactory.ToFindBindingFunc()  → string.Join(",", Inputs) 拼接为逗号字符串
      → 3. ExecuteCommandStage.HandleClick  → Split 回数组 → string?[]
        → 4. FunctionExecutor.ExecuteAsync  → object? parameters
          → 5. ArgumentBuilder.BuildFromDescriptor  → 类型转换
            → 6. Delegate.DynamicInvoke  → 最终调用
```

每一层都有自己的"空值处理逻辑"：

| 层级 | 空值处理 | 问题 |
|------|----------|------|
| 1. GetBindingInfo | 返回 `[]` 空列表 | 语义模糊：是"没有输入"还是"输入未知"？ |
| 2. PipelineFactory | `Count > 0 ? Join : null` | 空列表 → null |
| 3. HandleClick | `IsNullOrEmpty ? skip : build` | null → 跳过参数构建 |
| 4. ExecuteAsync | 透传 null | null → 传给 ArgumentBuilder |
| 5. BuildFromDescriptor | null → 走降级路径 | 降级路径失败 → 返回 null |
| 6. ExecuteDelegateAsync | null → 报错 | 最终错误 |

**本质：** 每一层都在用自己的方式处理"空值"，但没有统一的语义约定——"空"到底代表"没有参数"还是"参数未知/未配置"？

### 混乱三：`EventBindingResolver` 与 `PipelineFactory` 的职责重叠

`EventBindingResolver` 负责从配置中查找事件绑定，返回 `(FunctionName, Inputs, Output)`。

`PipelineFactory` 收到这个结果后，又把 `Inputs` 列表 `string.Join` 拼接成逗号字符串，作为 `BindingResult.Parameter`。

然后 `HandleClick` 又把 `Parameter` 字符串 `Split` 回数组。

**问题：** 为什么不在 `EventBindingResolver` 直接返回 `string?` 类型的 `Parameter`（逗号分隔的控件名），而非要返回 `List<string>?` 类型的 `Inputs`，让 `PipelineFactory` 再拼接一次？

**本质：** `EventBindingResolver` 和 `PipelineFactory` 对"参数"的表示方式不一致，导致不必要的序列化/反序列化。

### 混乱四：`ArgumentBuilder` 的三条路径设计

`ArgumentBuilder.BuildFromDescriptor` 有三条路径：

1. **快速路径 1**：`string?[]` → `TypeConverter.ConvertArguments`（类型转换）
2. **快速路径 2**：`object?[]` → 类型检查 + 转换
3. **降级路径**：其他 → `ParameterBuilder` 匹配器链

当 `parameters` 为 `null` 时，三条路径都走不通：

- 快速路径 1：`null is string?[]` → false
- 快速路径 2：`null is object?[]` → false
- 降级路径：`ParameterBuilder.Build` 对 `null` 参数无法匹配 → 返回失败

**问题：** `null` 是一个合法的输入（表示"没有参数"），但 `ArgumentBuilder` 没有处理这种情况。对于无参函数，应该直接返回空数组；对于有参函数，应该尝试使用默认值。

**本质：** `ArgumentBuilder` 没有区分"参数为 null"和"参数类型不匹配"这两种不同的失败场景。

### 混乱五：GUI 模式与 CLI 模式的参数来源不一致

- **GUI 模式**：参数从 `GuiControlSource`（真实控件）读取
- **CLI 模式**：参数从 `VirtualStoreSource`（虚拟存储）读取

但 `HandleClick` 中，如果 `inputControls` 为空，两种模式都直接跳过参数构建，`parameters = null`。

**问题：** 即使 `inputControls` 为空，函数可能仍然需要参数（例如从上下文中获取默认值）。跳过参数构建意味着函数永远无法执行。

**本质：** `HandleClick` 将"没有输入控件"等同于"没有参数"，但这两者并不等价。

---

## 框架层面整体优化方案

### 优化一：统一"空值"语义约定

在整个参数传递链中，统一约定：

| 值 | 语义 |
|----|------|
| `null` | 未配置/未知（需要降级处理） |
| `[]` 空数组 | 显式声明无参数（函数应使用默认值） |
| `["a", "b"]` | 有参数 |

**修改点：**

1. **`EventBindingResolver.GetBindingInfo()`**：当用户未配置 `inputs` 时返回 `null`，而不是 `[]`
2. **`PipelineFactory`**：`Parameter = result.Inputs != null ? string.Join(",", result.Inputs) : null`
3. **`HandleClick`**：区分 `null` 和 `""`：
   - `null` → 不传参数（`parameters = null`）
   - `""` → 传空数组（`parameters = Array.Empty<object>()`）

### 优化二：消除 `buildEvents` 的覆盖语义

将 `buildEvents` 从"整体替换"改为"深度合并"：

```
buildEvents: true 的处理逻辑：
  1. 自动生成默认的 events 结构
  2. 将用户显式定义的 events 深度合并到自动生成的 events 上
  3. 用户定义的字段优先，自动生成的字段作为默认值
```

具体实现：

```csharp
// 伪代码
if (buildEvents)
{
    var autoEvents = AutoGenerateEvents(controlConfig);
    var userEvents = controlConfig.Events;
    if (userEvents != null)
    {
        // 深度合并：用户定义优先
        autoEvents = DeepMerge(autoEvents, userEvents);
    }
    controlConfig.Events = autoEvents;
}
```

### 优化三：缩短参数传递链

将 6 层转换缩短为 3 层：

```
JSON 配置中的 bindings.inputs
  → 1. EventBindingResolver.GetBindingInfo()  → string? parameter（逗号分隔的控件名）
    → 2. ExecuteCommandStage.HandleClick  → Split 回数组 → string?[]
      → 3. ArgumentBuilder.BuildFromDescriptor  → 类型转换 → Delegate.DynamicInvoke
```

**具体修改：**

1. **`EventBindingResolver`** 直接返回 `string?` 类型的 `Parameter`（逗号分隔的控件名），不再返回 `List<string>?` 类型的 `Inputs`
2. **`PipelineFactory`** 不再需要 `string.Join` 拼接逻辑
3. **`BindingResult`** 的 `Parameter` 字段直接使用 `EventBindingResolver` 返回的值

### 优化四：修复 `ArgumentBuilder` 的 null 处理

在 `BuildFromDescriptor` 中增加 null 处理：

```csharp
public static object?[]? BuildFromDescriptor(
    IFunctionDescriptor descriptor, object? parameters)
{
    if (descriptor.Parameters.Count == 0)
        return Array.Empty<object>();

    // ★ 新增：null 处理
    if (parameters == null)
    {
        // 尝试使用默认值构建参数
        var args = new object?[descriptor.Parameters.Count];
        bool allOptional = true;
        for (int i = 0; i < descriptor.Parameters.Count; i++)
        {
            if (descriptor.Parameters[i].IsOptional)
                args[i] = descriptor.Parameters[i].DefaultValue;
            else if (descriptor.Parameters[i].Type.IsValueType)
                args[i] = Activator.CreateInstance(descriptor.Parameters[i].Type);
            else
                allOptional = false;
        }
        if (allOptional)
            return args;
        // 有必需参数且未提供 → 返回 null，让上层报错
        return null;
    }

    // ... 原有逻辑
}
```

### 优化五：统一 GUI/CLI 模式的参数来源

在 `HandleClick` 中，将参数来源逻辑抽象为统一接口：

```csharp
private string?[] ReadInputValues(CommandContext context, string[] controlNames)
{
    IParameterSource source;
    if (context.IsGuiMode && context.SharedData.TryGetValue("TargetForm", out var formObj) && formObj != null)
        source = new GuiControlSource(formObj);
    else if (context.IsCliMode && context.VirtualStore != null)
        source = new VirtualStoreSource(new Dictionary<string, string?>(context.VirtualStore));
    else
        return Array.Empty<string?>();  // 没有可用数据源

    var rawValues = new string?[controlNames.Length];
    for (int i = 0; i < controlNames.Length; i++)
        rawValues[i] = source.ReadValue(controlNames[i]);
    return rawValues;
}
```

---

## 推荐修复顺序

### 第一阶段：紧急修复（最小改动，解决当前问题）

1. **修复 `HandleClick` 的空值保护** — 当 `inputControls` 为空时传入空数组而非 null
2. **修复 `ArgumentBuilder` 的 null 处理** — null 参数时尝试使用默认值

### 第二阶段：根本修复（消除混乱根源）

3. **修复 `buildEvents` 的覆盖语义** — 改为深度合并，保留用户显式定义的字段
4. **统一空值语义约定** — 在整个参数传递链中统一 null/[] 的含义

### 第三阶段：架构优化（消除设计缺陷）

5. **缩短参数传递链** — 消除不必要的序列化/反序列化
6. **统一 GUI/CLI 参数来源** — 抽象为统一接口

## 相关文件

| 文件 | 作用 |
|------|------|
| `FileDriven/DrivenConfig/TempletConfig/JsonConfig/composites.json` | 定义 `buildEvents: true` 的模板 |
| `Operation/FunctionStorage/Business/UI/WinFormsEventBinder.cs` | WinForms 事件绑定器 |
| `Operation/FunctionStorage/Framework/FunctionStorageRegistry.cs` | 函数注册和事件绑定解析 |
| `FileDriven/FileDriven.Execution/Command/PipelineFactory.cs` | 管线工厂，构建 BindingResult |
| `Share/CommandFramework/Pipeline/Stages/ExecuteCommandStage.cs` | 命令执行阶段，HandleClick |
| `Share/CommandFramework/Execution/ArgumentBuilder.cs` | 参数构建器 |
| `Share/ParameterResolver/Converter/TypeConverter.cs` | 类型转换器 |
| `Share/UniversalMiddleware/ConfigurationModels/Models/EventConfig.cs` | 事件配置模型 |
