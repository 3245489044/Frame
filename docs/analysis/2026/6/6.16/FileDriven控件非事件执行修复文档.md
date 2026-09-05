## 修复方案文档

---

### 修复概览

| 优先级 | 问题 | 影响范围 | 修改文件 |
|--------|------|---------|---------|
| **P0** | NonEvent handler 成功路径缺少 `FunctionExecuteSuccess = true` | 所有非事件命令（sideload/sleep/close/exit/exec）被误判为失败 | `FileDriven/FileDriven.Execution/FileDrivenExecutor.cs` |
| **P1** | `RegisterSideContainerControls` 只注册顶层控件，子控件依赖递归查找 | exec 命令的 output 写入依赖 `WinFormsControlFinder` 递归查找，增加查找失败风险 | `FileDriven/FileDriven.UI/Core/MainForm.cs` |
| **P2** | `filedriven_log_messages.json` 缺失 `UnifiedControlService_*` 系列日志键 | 日志系统输出 ERROR 级别"key not found"警告 | `FileDriven/filedriven_log_messages.json` |

---

### P0 修复：NonEvent handler 成功状态管理

**文件：** `FileDriven/FileDriven.Execution/FileDrivenExecutor.cs`

**问题：** `RegisterNonEventCommands` 方法中注册的 5 个 handler（sideload/sleep/close/exit/exec），在成功路径上只设置了 `context.FunctionResultValue`，没有设置 `context.FunctionExecuteSuccess = true`。

**数据流：**
```
handler 执行完毕
  → HandleNonEvent 检查 context.HasError
    → GetFirstFailure() 遍历 ["ParseCommand", "ExecuteCommand"]
      → GetStageSuccess("ExecuteCommand") → false（handler 没设置）
        → HasError = true
          → HandleNonEvent 直接 return，不调用 SetStageResult(Name, true)
            → 命令被标记为失败
```

**修改内容：** 在 5 个 handler 的成功路径末尾添加 `context.FunctionExecuteSuccess = true;`

**具体修改点：**

**① sideload handler（第877行附近）：**
```csharp
// 修改前：
if (config != null)
{
    context.FunctionResultValue = $"Sideloaded '{target}'";
}

// 修改后：
if (config != null)
{
    context.FunctionResultValue = $"Sideloaded '{target}'";
    context.FunctionExecuteSuccess = true;  // ★ 修复：标记成功
}
```

**② sleep handler（第896-904行）：**
```csharp
// 修改前：
registry.Register("sleep", context =>
{
    var target = context.ParsedCommand?.Target ?? "0";
    if (int.TryParse(target, out var ms) && ms > 0)
    {
        System.Threading.Thread.Sleep(ms);
        context.FunctionResultValue = $"Slept for {ms}ms";
    }
    else
    {
        context.FunctionResultValue = "Slept for 0ms";
    }
}, ...);

// 修改后：
registry.Register("sleep", context =>
{
    var target = context.ParsedCommand?.Target ?? "0";
    if (int.TryParse(target, out var ms) && ms > 0)
    {
        System.Threading.Thread.Sleep(ms);
        context.FunctionResultValue = $"Slept for {ms}ms";
    }
    else
    {
        context.FunctionResultValue = "Slept for 0ms";
    }
    context.FunctionExecuteSuccess = true;  // ★ 修复：标记成功
}, ...);
```

**③ close handler（第926-928行附近）：**
```csharp
// 修改前：
CommandFrameworkUsage.CloseConfig(target);
context.FunctionResultValue = $"Closed '{target}'.";

// 修改后：
CommandFrameworkUsage.CloseConfig(target);
context.FunctionResultValue = $"Closed '{target}'.";
context.FunctionExecuteSuccess = true;  // ★ 修复：标记成功
```

**④ exit handler（第940-943行）：**
```csharp
// 修改前：
registry.Register("exit", context =>
{
    context.FunctionResultValue = "Exiting...";
    System.Windows.Forms.Application.Exit();
}, ...);

// 修改后：
registry.Register("exit", context =>
{
    context.FunctionResultValue = "Exiting...";
    context.FunctionExecuteSuccess = true;  // ★ 修复：标记成功
    System.Windows.Forms.Application.Exit();
}, ...);
```

**⑤ exec handler（第1058-1088行）：**
```csharp
// 修改前：
if (result.IsSuccess)
{
    context.FunctionResultValue = result.Value;
    // ... output 写入逻辑
}

// 修改后：
if (result.IsSuccess)
{
    context.FunctionResultValue = result.Value;
    context.FunctionExecuteSuccess = true;  // ★ 修复：标记成功
    // ... output 写入逻辑
}
```

---

### P1 修复：递归注册子控件到 ControlRegistry

**文件：** `FileDriven/FileDriven.UI/Core/MainForm.cs`

**问题：** `RegisterSideContainerControls` 方法只遍历 `view.Controls` 中的顶层控件，没有递归遍历子控件。虽然 `WinFormsControlFinder` 的递归查找能作为回退，但主动注册更可靠。

**修改内容：** 增加递归遍历控件树的逻辑，将所有层级的控件都注册到 `ControlRegistry`。

**具体修改：**

```csharp
// 在 RegisterSideContainerControls 方法中，遍历 view.Controls 时增加递归：

private void RegisterSideContainerControls(UIConfiguration config)
{
    // ... 现有代码 ...

    foreach (var view in config.Views)
    {
        // ... 现有代码 ...

        foreach (var ctrl in view.Controls)
        {
            if (string.IsNullOrWhiteSpace(ctrl.Name))
                continue;

            // 1. 注册当前控件（现有逻辑）
            RegisterSingleControl(ctrl.Name);

            // 2. ★ 新增：递归注册子控件
            if (ctrl.Controls != null && ctrl.Controls.Count > 0)
            {
                RegisterControlsRecursive(ctrl.Controls);
            }
        }
    }
}

/// <summary>
/// ★ 新增：递归注册控件及其子控件
/// </summary>
private void RegisterControlsRecursive(List<ControlDefinition> controls)
{
    foreach (var ctrl in controls)
    {
        if (string.IsNullOrWhiteSpace(ctrl.Name))
            continue;

        RegisterSingleControl(ctrl.Name);

        if (ctrl.Controls != null && ctrl.Controls.Count > 0)
        {
            RegisterControlsRecursive(ctrl.Controls);
        }
    }
}

/// <summary>
/// ★ 提取：注册单个控件到 ControlRegistry
/// </summary>
private void RegisterSingleControl(string controlName)
{
    // 1. 优先从 _sideContainer.FindControl 查找
    var actualControl = _sideContainer.FindControl<Control>(controlName);
    if (actualControl != null)
    {
        ControlRegistry.Register(controlName, actualControl);
        FileDrivenLogPlaceholderHandler.Log("MainForm_RegisterSideContainerControl",
            new { controlName, controlType = actualControl.GetType().Name });
        return;
    }

    // 2. 回退到 WinForms 标准 Controls.Find 递归查找
    var foundControls = _sideContainer.Controls.Find(controlName, true);
    if (foundControls.Length > 0)
    {
        actualControl = foundControls[0];
        ControlRegistry.Register(controlName, actualControl);
        FileDrivenLogPlaceholderHandler.Log("MainForm_RegisterSideContainerControl_fallback_controls_find",
            new { controlName, controlType = actualControl.GetType().Name });
        return;
    }

    // 3. 最后手段：通过 IControlFinder 跨 Form 查找
    // ... 现有逻辑 ...
}
```

---

### P2 修复：补充缺失的日志消息键

**文件：** `FileDriven/filedriven_log_messages.json`

**问题：** 日志中反复出现 `Log message 'UnifiedControlService_value_set' not found for keyPrefix 'filedriven:'` 错误，说明 `filedriven_log_messages.json` 中缺少 `UnifiedControlService_*` 系列键。

**需要补充的键（从 `ControlServices.cs` 中提取）：**

| 键名 | 出现位置 | 建议消息模板 |
|------|---------|------------|
| `UnifiedControlService_value_set` | `TrySetControlValue` 第614行 | `已设置控件值: {controlName} = {value}` |
| `UnifiedControlService_value_ok` | `TryGetControlValue` 第648行 | `已读取控件值: {controlName} = {value}` |
| `UnifiedControlService_value_empty` | `TryGetControlValue` 第642行 | `控件值为空: {controlName}` |
| `UnifiedControlService_control_not_found` | `TrySetControlValue` 第606行 | `控件未找到: {controlName}` |
| `UnifiedControlService_empty_controlName` | `TrySetControlValue` 第578行 | `控件名为空` |
| `UnifiedControlService_TrySetControlValue_container_info` | `TrySetControlValue` 第595行 | `容器信息: controlName={controlName}, containerType={containerType}, containerName={containerName}, hasRegistry={hasRegistry}, registryCount={registryCount}` |
| `ControlRegistryAwareFinder_FindByName_registry_miss` | `ControlRegistryAwareFinder` 第720行 | `Registry 未命中: controlName={controlName}, registryCount={registryCount}, rootType={rootType}, rootName={rootName}` |
| `ControlRegistryAwareFinder_FindByName_registry_null` | `ControlRegistryAwareFinder` 第728行 | `Registry 为 null: controlName={controlName}, rootType={rootType}, rootName={rootName}` |
| `ControlRegistryAwareFinder_FindByName_inner_miss` | `ControlRegistryAwareFinder` 第737行 | `内部查找器未命中: controlName={controlName}, rootType={rootType}` |

---

### 修复后验证方案

修复完成后，运行以下两个测试脚本验证：

```powershell
# 测试 1：math_browser_simple_test
.\运行测试GUI模式.ps1 math_browser_simple_test.json

# 测试 2：math_functions_test
.\运行测试GUI模式.ps1 math_functions_test.json
```

**预期结果：**
- sideload 命令显示 `[OK]` 而非 `[ERROR]`
- exec 命令的 output 写入成功，控件显示函数执行结果
- 不再出现 `Log message '...' not found for keyPrefix` 错误
- 测试通过率 > 0%

---

### 文件修改清单

| 文件 | 修改类型 | 修改行数 | 风险等级 |
|------|---------|---------|---------|
| `FileDriven/FileDriven.Execution/FileDrivenExecutor.cs` | 5 处添加 `FunctionExecuteSuccess = true` | +5 行 | **低**（仅添加成功标记，不改变逻辑） |
| `FileDriven/FileDriven.UI/Core/MainForm.cs` | 重构 `RegisterSideContainerControls`，增加递归注册 | +40~60 行 | **中**（需验证递归逻辑正确性） |
| `FileDriven/filedriven_log_messages.json` | 补充 9 个缺失的日志消息键 | +9 条记录 | **低**（仅配置变更） |