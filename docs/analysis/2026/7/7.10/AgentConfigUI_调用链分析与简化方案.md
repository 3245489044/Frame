# AgentConfigUI 调用链分析与简化方案

> 分析日期：2026-07-09
> 涉及文件：AgentConfigUI.json、EventBinderTransformer.cs、WorkflowMethodExecutionService.cs、FunctionStorageRegistry.cs、UIToolbox.cs、WorkflowSkeletons.cs、ParameterBuilder.cs、EventBindingHelper.cs、OutputWriter.cs

---

## 一、问题现象

`AgentConfigUI.json` 中 `workflowDetailTextBox`（第 56-64 行）在 `selectedIndexChanged` 事件触发后显示"未找到工作流"，而非工作流详情。

**配置位置**：`FileDriven/DrivenConfig/JsonConfig/AgentConfigUI.json` 第 56-64 行

```json
{
  "type": "textbox",
  "name": "workflowDetailTextBox",
  "style": "readonly-bg",
  "placeholder": "工作流详情将显示在此处...",
  "multiline": true,
  "readOnly": true,
  "scrollBars": "Vertical",
  "layout": { "left": 0.68, "top": 0.1, "width": 0.3, "height": 0.8 }
}
```

**事件绑定配置**：同一文件第 136-148 行

```json
"events": {
  "selectedIndexChanged": ["LoadWorkflowDetails", "LoadDefaultInput"],
  "bindings": {
    "parameters": [
      { "target": "workflowName", "source": "funcListView", "sourceType": "control" }
    ],
    "output": "workflowDetailTextBox",
    "outputMapping": {
      "LoadWorkflowDetails": "workflowDetailTextBox",
      "LoadDefaultInput": "inputTextBox"
    }
  }
}
```

---

## 二、当前完整调用链（9 层间接调用）

### 2.1 调用链全景

```
[1] 用户点击 funcListView 的项
    ↓
[2] selectedIndexChanged 事件触发
    ↓
[3] EventBinderTransformer.CreateEventHandler()          ← Share 层
    文件: CsharpPrj/Share/EventBindingOrchestrator/Parameters/EventBinderTransformer.cs
    第 234-400 行
    ↓ Task.Run
[4] ParameterBuilder.Build(sender, parameterBindings)     ← 从控件读取参数
    文件: CsharpPrj/Share/EventBindingOrchestrator/Parameters/ParameterBuilder.cs
    第 79-117 行
    ↓
[5] _executionServiceResolver?.Invoke()                   ← 延迟解析 IEventExecutionService
    ↓
[6] WorkflowMethodExecutionService.ExecuteFunctionAsync()  ← Operation 层
    文件: CsharpPrj/Operation/WorkFlowMethod/WorkflowMethodExecutionService.cs
    第 68-211 行
    ↓
[7] _registry.TryGetConfig(functionName, out config)      ← 查找函数配置
    ↓
[8] ParameterBuilder.Build(paramDescriptors, namedParameters) ← 参数匹配链（第二次构建）
    ↓
[9] _nodeExecutor.ExecuteNodeAsync(node, wfContext)        ← NodeExecutor
    ↓
[10] FunctionStorageRegistry.ExecuteAsync(functionName)     ← 静态门面
     文件: CsharpPrj/Operation/StateBlock/FunctionStorage/Framework/FunctionStorageRegistry.cs
     第 160-179 行
     ↓
[11] WorkFlowMethodUsage.Executor.ExecuteAsync()            ← 静态门面
     文件: CsharpPrj/Operation/WorkFlowMethod/WorkflowMethodUseage.cs
     第 228-238 行
     ↓
[12] FunctionExecutor.ExecuteAsync()                        ← 实际执行
     ↓
[13] UIToolbox 中注册的 Func<string, string> 委托被执行
     文件: FileDriven/FileDriven.Business/Registrations/UIToolbox.cs
     第 91-96 行
     ↓
[14] LoadWorkflowDetailsSkeleton.GetWorkflowDetails()       ← Skeleton 包装
     文件: FileDriven/FileDriven.Business/Workflow/WorkflowSkeletons.cs
     第 38-42 行
     ↓
[15] WorkflowExecutionLayer.GetWorkflow(workflowName)       ← 查询工作流
     ↓
[16] 返回详情文本
     ↓ (回传路径)
[17] OutputWriter.FindAndWrite(sender, output, result)      ← 写回 workflowDetailTextBox
     文件: CsharpPrj/Share/EventBindingOrchestrator/Parameters/OutputWriter.cs
     第 73-81 行
```

### 2.2 关键日志证据

```
[DEBUG] [WorkFlowMethod] [workflowmethod:WorkflowMethodExecution_Execute_start]
  { "function": "LoadWorkflowDetails", "paramCount": 2 }
                          ↑ 注意：这里 paramCount=2，说明传入了 2 个参数

[DEBUG] [WorkFlowMethod] [workflowmethod:WorkflowMethodExecution_Registry_found]
  { "function": "LoadWorkflowDetails", "source": "IFunctionRegistry", "paramCount": 1 }
                          ↑ 注意：这里 paramCount=1，参数数量发生了变化
```

`paramCount: 2 → 1` 的变化发生在 `WorkflowMethodExecutionService.ExecuteFunctionAsync` 内部：
- 第 42-66 行：`ExecuteFunctionAsync(string functionName, object?[]? parameters)` 将 `object?[]` 转换为 `Dictionary<string, object>`，其中 `parameters[0]` 是 `ListViewItem` 对象
- 第 68 行：委托给 `ExecuteFunctionAsync(string functionName, Dictionary<string, object> namedParameters)`
- 第 139-156 行：`ParameterBuilder.Build` 将 `Dictionary` 转换为位置参数数组
- 第 174-184 行：创建 `NodeDefinition`，`Parameters` 数组长度为 1

---

## 三、问题根因分析

### 3.1 根因：ParameterBuilder 从 ListView 读取参数时返回了错误的值（P0）

**问题文件**：`CsharpPrj/Share/EventBindingOrchestrator/Parameters/ParameterBuilder.cs` 第 68-72 行

```csharp
private string GetControlValue(object control)
{
    return _winFormsStrategy?.GetControlValue(control)?.ToString()
        ?? EventBindingHelper.GetControlValueReflection(control);
}
```

**问题文件**：`CsharpPrj/Share/EventBindingOrchestrator/Parameters/EventBindingHelper.cs` 第 174-185 行

```csharp
public static string GetControlValueReflection(object control)
{
    try
    {
        var textProp = control.GetType().GetProperty("Text");
        return textProp?.GetValue(control)?.ToString() ?? string.Empty;
    }
    catch
    {
        return string.Empty;
    }
}
```

**根因**：当 `sourceType: "control"` 且 `source: "funcListView"` 时，`ParameterBuilder.ResolveControlValue` 找到 `funcListView` 控件后，调用 `GetControlValue` 读取其值。`GetControlValueReflection` 读取的是 `ListView` 的 `Text` 属性。

**对于 `ListView` 控件，`Text` 属性不是选中的项名称，而是空字符串**。`ListView` 的选中项信息存储在 `SelectedItems` 集合中，而不是 `Text` 属性。

因此 `workflowName` 参数值为 `""`（空字符串），`LoadWorkflowDetailsSkeleton.GetWorkflowDetails("")` 返回 `"未找到工作流: "`。

### 3.2 次要问题：参数两次构建（P1）

**问题文件**：
- `CsharpPrj/Share/EventBindingOrchestrator/Parameters/EventBinderTransformer.cs` 第 288 行
- `CsharpPrj/Operation/WorkFlowMethod/WorkflowMethodExecutionService.cs` 第 139 行

参数先在 `EventBinderTransformer` 中通过 `ParameterBuilder.Build(sender, bindings)` 构建一次（从控件读取值），然后在 `WorkflowMethodExecutionService` 中又通过 `ParameterBuilder.Build(paramDescriptors, namedParameters)` 构建第二次（从 Dictionary 匹配参数）。

第一次构建的结果（`Dictionary<string, object>`）直接作为 `namedParameters` 传给第二次构建，第二次构建又将其拆解为位置参数数组。**两次构建之间，参数值没有变化，但增加了不必要的处理开销**。

### 3.3 次要问题：TryWriteWorkflowResult 反射调用（P2）

**问题文件**：`CsharpPrj/Operation/WorkFlowMethod/WorkflowMethodExecutionService.cs` 第 281-348 行

`TryWriteWorkflowResult` 通过 `Type.GetType("WorkFlowMethod.Usage.WorkflowResultWriter, WorkFlowMethod")` 反射调用 `WorkflowResultWriter.WriteResults`。但结果回写已由 `EventBinderTransformer` 中的 `OutputWriter` 负责（第 333-340 行），不需要在 `WorkflowMethodExecutionService` 中重复处理。

---

## 四、整合方案（保持架构理念，减少冗余层）

### 4.1 整合后的调用链（9 层 → 6 层）

```
[1] 用户点击 funcListView 的项
    ↓
[2] selectedIndexChanged 事件触发
    ↓
[3] EventBinderTransformer.CreateEventHandler()
    ↓ Task.Run
[4] ParameterBuilder.Build(sender, parameterBindings)     ← 修复：ListView 提取 SelectedItem.Text
    ↓
[5] WorkflowMethodExecutionService.ExecuteFunctionAsync()
    ↓
[6] _registry.TryGetConfig → NodeExecutor
    ↓
[7] FunctionExecutor（通过 WorkFlowMethodUsage.Executor）
    ↓
[8] UIToolbox 注册的 Func<string, string>（内联 LoadWorkflowDetails）
    ↓
[9] WorkflowExecutionLayer.GetWorkflow(workflowName)
    ↓
[10] OutputWriter.FindAndWrite(output, result)
```

**整合内容**：

| 原层 | 整合方式 | 说明 |
|------|---------|------|
| `FunctionStorageRegistry.ExecuteAsync` | **删除** | 静态门面，仅委托给 `WorkFlowMethodUsage.Executor`，调用方直接使用 `WorkFlowMethodUsage.Executor` |
| `LoadWorkflowDetailsSkeleton` | **内联** | `SkeletonBase<string, string>` 包装冗余，直接内联到 `UIToolbox` 注册的委托中 |
| `ParameterBuilder.Build` 第二次构建 | **跳过** | `WorkflowMethodExecutionService` 中直接使用 `namedParameters` 提取位置参数 |

### 4.2 具体修改步骤

#### Step 1（P0）：修复 ParameterBuilder 的 ListView 参数读取

**文件**：`CsharpPrj/Share/EventBindingOrchestrator/Parameters/ParameterBuilder.cs`

在 `ResolveControlValue` 方法中，当找到的控件是 `ListView` 时，提取 `SelectedItems[0]?.Text` 而非读取 `Text` 属性。

**修改位置**：第 122-147 行 `ResolveControlValue` 方法

```csharp
private object? ResolveControlValue(object? form, ParameterBinding binding)
{
    if (form == null) { ... return null; }

    var sourceControl = FindControlByName(form, binding.Source);
    if (sourceControl == null) { ... return null; }

    // ★ 2026-07-09 修复：当控件是 ListView 时，提取 SelectedItem.Text
    //   ListView.Text 属性始终为空字符串，选中项信息在 SelectedItems 中
    if (sourceControl.GetType().Name == "ListView")
    {
        var selectedItemsProp = sourceControl.GetType().GetProperty("SelectedItems");
        if (selectedItemsProp != null)
        {
            var selectedItems = selectedItemsProp.GetValue(sourceControl) as System.Collections.IList;
            if (selectedItems != null && selectedItems.Count > 0)
            {
                var firstItem = selectedItems[0];
                var textProp = firstItem.GetType().GetProperty("Text");
                return textProp?.GetValue(firstItem)?.ToString() ?? string.Empty;
            }
        }
        return string.Empty;
    }

    return GetControlValue(sourceControl);
}
```

#### Step 2（P1）：消除 WorkflowMethodExecutionService 中的参数二次构建

**文件**：`CsharpPrj/Operation/WorkFlowMethod/WorkflowMethodExecutionService.cs`

修改 `ExecuteFunctionAsync(string functionName, Dictionary<string, object> namedParameters)` 方法，当 `namedParameters` 已经包含正确的参数值时，直接使用它们作为位置参数，跳过第二次 `ParameterBuilder.Build`。

**修改位置**：第 129-156 行

```csharp
// 修改前：
var paramConfigs = config.Parameters ?? Array.Empty<IParamConfig>();
object?[] positionalArgs;

var builder = new ParameterBuilder();
var paramDescriptors = paramConfigs.Select(p => new ParamConfigAdapter(
    p.Name, p.Type, p.IsOptional, p.DefaultValue)).ToArray();
var buildResult = builder.Build(paramDescriptors, namedParameters);
positionalArgs = buildResult.Args;

// 修改后：如果 namedParameters 已经包含正确的参数值，直接提取
// 否则回退到 ParameterBuilder.Build
var paramConfigs = config.Parameters ?? Array.Empty<IParamConfig>();
object?[] positionalArgs;

if (namedParameters.Count > 0 && paramConfigs.Length > 0)
{
    // 按参数配置顺序从 namedParameters 提取值
    positionalArgs = new object?[paramConfigs.Length];
    for (int i = 0; i < paramConfigs.Length; i++)
    {
        var paramName = paramConfigs[i].Name;
        if (namedParameters.TryGetValue(paramName, out var val))
            positionalArgs[i] = val;
        else
            positionalArgs[i] = namedParameters.Values.ElementAtOrDefault(i);
    }
}
else
{
    var builder = new ParameterBuilder();
    var paramDescriptors = paramConfigs.Select(p => new ParamConfigAdapter(
        p.Name, p.Type, p.IsOptional, p.DefaultValue)).ToArray();
    var buildResult = builder.Build(paramDescriptors, namedParameters);
    positionalArgs = buildResult.Args;
}
```

#### Step 3（P1）：删除 FunctionStorageRegistry.ExecuteAsync 静态门面

**文件**：`CsharpPrj/Operation/StateBlock/FunctionStorage/Framework/FunctionStorageRegistry.cs`

删除第 160-207 行的 `ExecuteAsync` 方法（包括 `CancellationToken` 重载和 `Delegate` 重载）。调用方直接使用 `WorkFlowMethodUsage.Executor.ExecuteAsync`。

**注意**：`FunctionStorageRegistry` 的 `Register`、`Contains`、`Clear` 等方法保留，只删除执行相关的方法。

**影响范围**：需检查 `FunctionStorageRegistry.ExecuteAsync` 的调用方，改为直接调用 `WorkFlowMethodUsage.Executor.ExecuteAsync`。

#### Step 4（P2）：内联 LoadWorkflowDetailsSkeleton

**文件**：`FileDriven/FileDriven.Business/Workflow/WorkflowSkeletons.cs`

删除 `LoadWorkflowDetailsSkeleton` 类（第 29-99 行），将其逻辑内联到 `UIToolbox` 注册中。

**文件**：`FileDriven/FileDriven.Business/Registrations/UIToolbox.cs`

修改 `LoadWorkflowDetails` 的注册，直接内联 `WorkflowExecutionLayer.GetWorkflow` 调用：

```csharp
// 修改前：
registry.Register("LoadWorkflowDetails",
    (Func<string, string>)LoadWorkflowDetailsSkeleton.GetWorkflowDetails,
    "获取工作流详情文本");

// 修改后：
registry.Register("LoadWorkflowDetails",
    (Func<string, string>)((workflowName) =>
    {
        var def = WorkflowExecutionLayer.GetWorkflow(workflowName);
        if (def == null) return $"未找到工作流: {workflowName}";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"工作流: {def.DisplayName}");
        sb.AppendLine($"名称: {def.WorkflowName}");
        sb.AppendLine($"描述: {def.Description}");
        sb.AppendLine($"版本: {def.Version}");
        sb.AppendLine($"步骤数: {def.Steps?.Count ?? 0}");
        sb.AppendLine();

        if (def.Steps != null)
        {
            for (int i = 0; i < def.Steps.Count; i++)
            {
                var step = def.Steps[i];
                sb.AppendLine($"步骤{i + 1}: {step.StepName}");
                sb.AppendLine($"  函数: {step.FuncName}");
                sb.AppendLine($"  输入: {string.Join(", ", step.InputKeys)}");
                sb.AppendLine($"  输出: {step.OutputKey}");
            }
        }

        if (def.OutputMapping != null && def.OutputMapping.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("输出映射:");
            foreach (var kvp in def.OutputMapping)
                sb.AppendLine($"  {kvp.Key} → {kvp.Value}");
        }

        return sb.ToString();
    }),
    "获取工作流详情文本");
```

#### Step 5（P2）：删除 TryWriteWorkflowResult 反射调用

**文件**：`CsharpPrj/Operation/WorkFlowMethod/WorkflowMethodExecutionService.cs` 第 281-348 行

删除 `TryWriteWorkflowResult` 方法及其调用（第 200 行）。结果回写已由 `EventBinderTransformer` 中的 `OutputWriter` 负责，不需要在 `WorkflowMethodExecutionService` 中重复处理。

**删除位置**：
- 第 200 行：`TryWriteWorkflowResult(functionName, result.Value);`
- 第 281-348 行：整个 `TryWriteWorkflowResult` 方法

---

## 五、涉及文件清单

| 文件路径 | 角色 | 修改类型 |
|---------|------|---------|
| `FileDriven/DrivenConfig/JsonConfig/AgentConfigUI.json` | UI 配置 | 无需修改 |
| `CsharpPrj/Share/EventBindingOrchestrator/Parameters/EventBinderTransformer.cs` | 事件绑定转换器 | 无需修改 |
| `CsharpPrj/Share/EventBindingOrchestrator/Parameters/ParameterBuilder.cs` | 参数构建器 | **P0 修复：ListView 提取 SelectedItem.Text** |
| `CsharpPrj/Share/EventBindingOrchestrator/Parameters/EventBindingHelper.cs` | 事件绑定辅助 | 无需修改 |
| `CsharpPrj/Share/EventBindingOrchestrator/Parameters/OutputWriter.cs` | 输出写入器 | 无需修改 |
| `CsharpPrj/Operation/WorkFlowMethod/WorkflowMethodExecutionService.cs` | 函数执行服务 | **P1 消除参数二次构建 + P2 删除 TryWriteWorkflowResult** |
| `CsharpPrj/Operation/StateBlock/FunctionStorage/Framework/FunctionStorageRegistry.cs` | 静态注册门面 | **P1 删除 ExecuteAsync 方法（保留 Register/Contains/Clear）** |
| `CsharpPrj/Operation/WorkFlowMethod/WorkflowMethodUseage.cs` | 静态使用入口 | 无需修改 |
| `FileDriven/FileDriven.Business/Registrations/UIToolbox.cs` | UI 函数注册 | **P2 内联 LoadWorkflowDetails 逻辑** |
| `FileDriven/FileDriven.Business/Workflow/WorkflowSkeletons.cs` | 工作流骨架 | **P2 删除 LoadWorkflowDetailsSkeleton 类** |

---

## 六、优先级建议

| 优先级 | 步骤 | 影响 | 风险 |
|--------|------|------|------|
| **P0** | Step 1：修复 ParameterBuilder ListView 参数提取 | 修复"未找到工作流"Bug | 低，仅影响 ListView 控件 |
| **P1** | Step 2：消除参数二次构建 | 减少不必要的参数处理 | 中，需验证参数顺序匹配 |
| **P1** | Step 3：删除 FunctionStorageRegistry.ExecuteAsync | 消除静态门面冗余 | 中，需检查调用方 |
| **P2** | Step 4：内联 LoadWorkflowDetailsSkeleton | 消除 SkeletonBase 包装 | 低，逻辑简单 |
| **P2** | Step 5：删除 TryWriteWorkflowResult 反射调用 | 清理冗余代码 | 低，OutputWriter 已覆盖 |
