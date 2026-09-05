# WinFormsEventBinder 事件触发链重构方案

## 1. 问题背景

### 1.1 日志现象

```
[14:56:35] [OK] domainListView.selectedindexchanged → Math_GetFunctionsInDomain
[14:56:37] [ERROR] domainListView.selectedindexchanged: 所有输入控件值均为 null，跳过函数 'Math_GetFunctionsInDomain' 执行
[14:56:37] [INFO] [OK] domainListView.selectedindexchanged → Math_GetFunctionsInDomain|分析日志中的逻辑错误
```

用户在 `domainListView` 中选择一个数学域时，`SelectedIndexChanged` 事件触发，但 `FireAndForgetTriggerAsync` 中 `FindControlValue("domainListView")` 返回了 null，导致"所有输入控件值均为 null"的误报。

### 1.2 根本原因

`ListView.SelectedIndexChanged` 事件在选中项变化时会触发**两次**：
1. **第一次**：清除旧选中项，`SelectedItems.Count == 0`
2. **第二次**：选中新项，`SelectedItems.Count > 0`

第一次触发时，`ControlValueHelper.GetValue` 对 `ListView` 的适配器返回 null（因为 `SelectedItems.Count == 0`），导致 `inputValues.All(v == null)` 为 true，触发 ERROR 日志。

### 1.3 核心问题

调用链过于分散，导致逻辑分散在多个地方，难以统一处理：

```
BindSingleEvent (第251行)
  └─ 闭包 → FireAndForgetTriggerAsync (第328行)
      ├─ _inputTargetsCache 获取 inputNames (第350行)
      ├─ FindControlValue → FindControlRecursive → ControlValueHelper.GetValue (第523行)
      ├─ inputValues.All(v == null) 检查 (第361行) ← ★ 问题点
      ├─ FunctionStorageRegistry.ExecuteAsync (第378行)
      ├─ _outputTargetCache 获取 outputTarget (第391行)
      ├─ FindControlByName → FindControlRecursive (第652行)
      ├─ _suppressor.SuppressAll (第424行)
      └─ IControlValueWriter.WriteValue (第432行)
```

---

## 2. 重构目标

将"输入值获取 → 空值检查 → 执行决策 → 结果写入"集中到 `FireAndForgetTriggerAsync` 中，消除分散的辅助方法。

### 2.1 具体目标

1. **合并 `FindControlValue` 和 `FindControlByName`**：一次遍历同时获取控件引用和值
2. **集中输入值获取逻辑**：在 `FireAndForgetTriggerAsync` 中统一处理
3. **修复 `ListView` 中间状态误报**：区分"真的没有选中项"和"正在切换中"
4. **消除 `EventSuppressor` 的无效使用**：当前 `SuppressAll` 未实际阻止事件

---

## 3. 重构方案

### 3.1 新增 `FindControl` 方法

合并 `FindControlValue`（第523行）和 `FindControlByName`（第652行），一次遍历同时获取控件引用和值：

```csharp
/// <summary>
/// 查找控件并获取其值（合并 FindControlValue + FindControlByName）
/// <para>一次遍历 Application.OpenForms 同时获取控件引用和值，</para>
/// <para>消除两个方法各自遍历的开销。</para>
/// </summary>
private static (Control? control, string? value) FindControl(string controlName)
{
    try
    {
        foreach (Form form in Application.OpenForms)
        {
            var control = FindControlRecursive(form, controlName);
            if (control != null)
            {
                var value = ControlValueHelper.GetValue(control)?.ToString();
                return (control, value);
            }
        }
    }
    catch (Exception ex)
    {
        Log("CommandLoopSkeleton_ExecuteCore_error",
            new { command = $"FindControl({controlName})", message = $"查找控件异常: {ex.Message}" },
            moduleName: "FileDriven", keyPrefix: "filedriven:");
    }

    return (null, null);
}
```

### 3.2 重构 `FireAndForgetTriggerAsync` 的输入值获取逻辑

将输入值获取、空值检查、ListView 中间状态判断集中处理：

```csharp
// 获取输入控件引用和值（一次遍历）
var inputControls = new List<(Control? control, string? value)>();
foreach (var name in inputNames)
{
    var (ctrl, val) = FindControl(name);
    inputControls.Add((ctrl, val));
}

// 提取输入值数组（供函数执行使用）
var inputValues = inputControls.Select(ic => ic.value).ToArray();

// ★ 修复：区分"真的没有选中项"和"ListView 正在切换中"
var allNull = inputValues.All(v => v == null);
if (allNull)
{
    // 检查是否是 ListView 的中间状态（SelectedItems 正在切换）
    var isListViewTransition = inputControls.Any(ic => 
        ic.control is ListView lv && lv.SelectedItems.Count == 0);
    
    if (isListViewTransition)
    {
        // ListView 清除旧选中项时的中间状态，静默跳过
        return;
    }
    
    // 真的没有有效输入，记录警告
    Log("CommandLoopSkeleton_ExecuteCore_error",
        new { command = $"{controlName}.{eventType}", 
              message = $"所有输入控件值均为 null，跳过函数 '{functionName}' 执行" },
        moduleName: "FileDriven", keyPrefix: "filedriven:");
    return;
}
```

### 3.3 合并结果写入的控件查找

```csharp
// 查找输出控件（复用 FindControl，一次遍历）
var (outputControl, _) = FindControl(outputTarget);
if (outputControl != null)
{
    var writer = ComponentRegistry.Instance.Resolve<IControlValueWriter>();
    if (writer != null)
    {
        writer.WriteValue(outputControl, result.Value);
    }
    else
    {
        var resultStr = result.Value.ToString();
        if (!string.IsNullOrEmpty(resultStr))
            WriteResultToControl(outputControl, resultStr);
    }
}
```

### 3.4 消除 `EventSuppressor` 的无效使用

当前 `_suppressor.SuppressAll(controlsToSuppress)` 只是增加抑制计数，但事件绑定代码没有检查 `IsSuppressed`，所以实际上没有阻止任何事件。

**方案 A**：移除 `SuppressAll` 调用（如果事件抑制不是必需的）
**方案 B**：在 `BindSingleEvent` 的闭包中检查 `_suppressor.IsSuppressed(ctrl)`（如果事件抑制是必需的）

根据讨论，选择**方案 A**，因为：
- `ListView` 的中间状态问题已通过上述逻辑修复
- 事件抑制器当前未实际生效，移除不会改变行为
- 减少不必要的复杂性

---

## 4. 改动范围

只修改 `WinFormsEventBinder.cs` 一个文件：

| 改动 | 位置 | 说明 |
|------|------|------|
| 新增 `FindControl` 方法 | 替换第523-551行 `FindControlValue` | 合并 `FindControlValue` + `FindControlByName` |
| 保留 `FindControlByName` 作为兼容包装 | 第652行 | 内部委托给 `FindControl` |
| 重构 `FireAndForgetTriggerAsync` 输入值获取 | 第350-367行 | 使用 `FindControl` + ListView 中间状态判断 |
| 重构 `FireAndForgetTriggerAsync` 结果写入 | 第406-441行 | 使用 `FindControl` 查找输出控件 |
| 移除 `_suppressor.SuppressAll` 调用 | 第424行 | 消除无效的事件抑制 |

---

## 5. 完整重构后的 `FireAndForgetTriggerAsync` 伪代码

```csharp
private async Task FireAndForgetTriggerAsync(string controlName, string eventType, 
    string value, string functionName, string bindingKey)
{
    try
    {
        if (string.IsNullOrEmpty(functionName))
        {
            var success = _eventPipelineExecutor.Execute(controlName, eventType, value);
            if (!success) { /* 日志 */ }
            return;
        }

        // ================================================================
        // 阶段1：获取输入值（集中处理）
        // ================================================================
        object? actualParameters;
        _inputTargetsCache.TryGetValue(bindingKey, out var inputNames);
        
        if (inputNames is { Count: > 0 })
        {
            // 一次遍历获取所有输入控件的引用和值
            var inputControls = new List<(Control? control, string? value)>();
            foreach (var name in inputNames)
            {
                var (ctrl, val) = FindControl(name);
                inputControls.Add((ctrl, val));
            }

            var inputValues = inputControls.Select(ic => ic.value).ToArray();

            // ★ 修复：区分"真的没有值"和"ListView 正在切换"
            if (inputValues.All(v => v == null))
            {
                var isListViewTransition = inputControls.Any(ic => 
                    ic.control is ListView lv && lv.SelectedItems.Count == 0);
                
                if (isListViewTransition)
                    return; // 静默跳过
                
                Log(/* 所有输入控件值均为 null */);
                return;
            }

            actualParameters = inputValues;
        }
        else
        {
            actualParameters = value;
        }

        // ================================================================
        // 阶段2：执行函数
        // ================================================================
        var result = await FunctionStorageRegistry.ExecuteAsync(functionName, actualParameters);
        if (!result.IsSuccess)
        {
            Log(/* 函数执行失败 */);
            return;
        }

        // ================================================================
        // 阶段3：写回结果
        // ================================================================
        _outputTargetCache.TryGetValue(bindingKey, out var outputTarget);
        if (string.IsNullOrEmpty(outputTarget))
        {
            Log(/* outputTarget 未找到 */);
        }
        else if (result.Value == null)
        {
            Log(/* 返回值为 null */);
        }
        else
        {
            var (outputControl, _) = FindControl(outputTarget);
            if (outputControl != null)
            {
                var writer = ComponentRegistry.Instance.Resolve<IControlValueWriter>();
                if (writer != null)
                {
                    writer.WriteValue(outputControl, result.Value);
                }
                else
                {
                    var resultStr = result.Value.ToString();
                    if (!string.IsNullOrEmpty(resultStr))
                        WriteResultToControl(outputControl, resultStr);
                }
            }
            else
            {
                Log(/* 输出控件未找到 */);
            }
        }

        Log(/* 执行成功 */);
    }
    catch (Exception ex)
    {
        Log(/* 异常 */);
    }
}
```

---

## 6. 保留的兼容方法

```csharp
/// <summary>
/// 保留 FindControlByName 作为兼容包装（内部委托给 FindControl）
/// </summary>
private static Control? FindControlByName(string controlName)
{
    return FindControl(controlName).control;
}

/// <summary>
/// 保留 FindControlValue 作为兼容包装（内部委托给 FindControl）
/// </summary>
private static string? FindControlValue(string controlName)
{
    return FindControl(controlName).value;
}
```
