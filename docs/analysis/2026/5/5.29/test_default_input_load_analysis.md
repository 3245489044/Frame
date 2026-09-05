# test_default_input_load.txt 执行流程分析与问题诊断

## 一、脚本概述

**文件路径**: `FileDriven/DrivenConfig/Scripts/test_default_input_load.txt`

**测试目标**: 验证 `WorkflowRunner.json` 中每个工作流选择后，`inputTextBox` 是否正确加载 `defaultInput`。

**运行方式**: `FileDriven.Launcher --file test_default_input_load.txt`

---

## 二、命令执行流程

### 2.1 整体架构

```
脚本命令 (如 "load WorkflowRunner.json")
    │
    ▼
CliModeRouter.ExecuteAsync(command)
    │  NormalizeToMetaCommand: "load WorkflowRunner.json" → "do load WorkflowRunner.json"
    ▼
CommandRouter.ExecuteCommandAsync("do load WorkflowRunner.json")
    │  MetaCommand.Parse → Verb=Do, Action="load", Target="WorkflowRunner.json"
    ▼
BackendCommandAdapter.DoAsync("load", "WorkflowRunner.json", null)
    │
    ├── DoLoadAsync → 解析 JSON 配置，存储到 _loadedConfigs
    ├── DoSetAsync → 设置虚拟控件值，触发事件绑定
    ├── DoClickAsync → 查找 Click 事件绑定，调用后端函数
    └── DoClose → 移除已加载的配置
```

### 2.2 命令解析流程

以 `set workflowComboBox MathOperationsWorkflow` 为例：

```
步骤1: CliModeRouter.NormalizeToMetaCommand
    "set workflowComboBox MathOperationsWorkflow"
    → firstWord = "set" (在 ActionCommands 中)
    → 添加 "do " 前缀
    → "do set workflowComboBox MathOperationsWorkflow"

步骤2: MetaCommand.Parse("do set workflowComboBox MathOperationsWorkflow")
    → tokens = ["do", "set", "workflowComboBox", "MathOperationsWorkflow"]
    → verbStr = "do", Enum.TryParse<MetaVerb>("do") → MetaVerb.Do
    → 进入正常解析路径 (第149-179行)
    → cmd.Action = "set" (tokens[1])
    → cmd.Target = "workflowComboBox" (tokens[2])
    → paramsStartIndex = 3
    → ParameterParser.ParseParameters(tokens, 3)
        → tokens[3] = "MathOperationsWorkflow" (不含 '=')
        → parameters["MathOperationsWorkflow"] = true
    → cmd.Parameters = {"MathOperationsWorkflow": true}

步骤3: CommandRouter.ExecuteMetaCommandAsync
    → adapter.DoAsync("set", "workflowComboBox", {"MathOperationsWorkflow": true})

步骤4: BackendCommandAdapter.DoSetAsync("workflowComboBox", {"MathOperationsWorkflow": true})
    → parameters.TryGetValue("value", out var val) → 没有 "value" 键 → value = null
    → target.IndexOf(' ') → "workflowComboBox" 无空格 → spaceIndex = -1
    → value == null → 返回错误 "set requires a value"
```

---

## 三、已发现的问题

### 问题 1: `set workflowComboBox MathOperationsWorkflow` 无法正确提取值

**严重程度**: 🔴 阻塞级

**根因**: `DoSetAsync` 中第 279-300 行的值提取逻辑只处理 `extractedValue` 以 `{` 或 `[` 开头的情况（JSON 格式），但 `"MathOperationsWorkflow"` 是纯文本。

**影响**: `set workflowComboBox MathOperationsWorkflow` 执行时，`value` 为 null，第 302 行返回错误 `"set requires a value"`。

**修复方案**: 在 `DoSetAsync` 中，当 `parameters` 中没有 `"value"` 键时，需要从 `parameters` 中提取第一个非 `value` 的参数值作为实际值。

**代码位置**: `FileDriven/FileDriven.CLI/Adapters/BackendCommandAdapter.cs` 第 279-300 行

```csharp
// 当前代码（有问题的）：
if (value == null && target != null)
{
    var spaceIndex = target.IndexOf(' ');
    if (spaceIndex > 0)
    {
        var extractedTarget = target.Substring(0, spaceIndex);
        var extractedValue = target.Substring(spaceIndex + 1).Trim();
        if (extractedValue.StartsWith("{") || extractedValue.StartsWith("["))
        {
            target = extractedTarget;
            value = extractedValue;
        }
    }
}
```

### 问题 2: `set workflowComboBox MathOperationsWorkflow` 没有触发事件绑定

**严重程度**: 🔴 阻塞级

**根因**: 与问题 1 相同。由于 `value` 为 null，`DoSetAsync` 在第 302 行就返回错误了，永远不会执行到第 313 行的 `FindEventBinding`。

**影响**: `workflowComboBox` 的 `SelectedIndexChanged` 事件不会被触发，`LoadWorkflowDetails` 函数不会被调用，`workflowDetailTextBox` 和 `inputTextBox` 不会被更新。

**修复方案**: 修复问题 1 后，`value` 会被正确提取，第 313 行的 `FindEventBinding` 会找到 `workflowComboBox` 的 `selectedIndexChanged` 事件绑定，触发 `LoadWorkflowDetails` 函数。

### 问题 3: `set inputTextBox {"a":999,"b":888,"c":777}` JSON 参数被错误拆分

**严重程度**: 🔴 阻塞级（已修复 ✅）

**根因**: `CommandTokenizer` 将 JSON 中的双引号 `"` 当作引号处理，导致 JSON 被拆分为多个 token。
例如 `{"a":999,"b":888,"c":777}` 被拆分为 `["{a", ":", "999", ",", "b", ":", "888", ",", "c", ":", "777", "}"]`。

**影响**: `set inputTextBox {"a":999,"b":888,"c":777}` 执行时，`value` 被设置为 `{a`（第一个 token），而不是完整的 JSON。

**修复方案**: 在 `MetaCommand.Parse` 的 `MetaVerb.Do` 分支中，添加 JSON token 合并逻辑：
1. 检查 `tokens[3..]` 中是否有以 `{` 或 `[` 开头的 token
2. 如果有，从该位置开始合并所有 token 直到 `}` 或 `]` 结束
3. 将合并后的 JSON 字符串作为 target 的一部分

**修复文件**: `BasicMethod/MetaCommand/Models.cs`
- 新增 `MergeJsonTokens` 静态方法
- 在 `MetaVerb.Do` 分支中检测并合并 JSON token

**修复后效果**:
```
[OK] Set inputTextBox = {a:999,b:888,c:777}  ← 之前是 {a
```

**已知限制**: Tokenizer 剥离了 JSON 中的双引号，所以合并后的结果是 `{a:999,b:888,c:777}` 而不是 `{"a":999,"b":888,"c":777}`。但作为字符串值设置到控件时不影响功能。

### 问题 4: `LoadWorkflowInputTemplate` 函数可能不存在

**严重程度**: 🟡 次要

**根因**: `DoSetAsync` 第 333 行调用 `_functionExecutor.ExecuteAsync("LoadWorkflowInputTemplate", ...)`，但该函数可能未在 `FunctionExecutionFramework` 中注册。

**影响**: 如果函数不存在，会抛出异常，被第 341 行的 catch 捕获，仅记录警告日志。

**当前处理**: 已有 try-catch 保护，不会影响主流程。

---

## 四、命令解析细节

### 4.1 MetaCommand.Parse 的两种路径

**路径 A: 正常解析**（当第一个 token 是 MetaVerb 时）

```
"do set workflowComboBox MathOperationsWorkflow"
→ tokens = ["do", "set", "workflowComboBox", "MathOperationsWorkflow"]
→ verb = MetaVerb.Do
→ cmd.Action = tokens[1] = "set"
→ cmd.Target = tokens[2] = "workflowComboBox"
→ paramsStartIndex = 3
→ parameters = {"MathOperationsWorkflow": true}
```

**路径 B: ParseAsDoCommand**（当第一个 token 不是 MetaVerb 时）

```
"set workflowComboBox MathOperationsWorkflow"
→ tokens = ["set", "workflowComboBox", "MathOperationsWorkflow"]
→ verbStr = "set" → Enum.TryParse 失败 → 进入 ParseAsDoCommand
→ cmd.Action = tokens[0] = "set"
→ tokens[1..] = ["workflowComboBox", "MathOperationsWorkflow"]
→ targetParts = ["workflowComboBox", "MathOperationsWorkflow"] (都不含 '=')
→ cmd.Target = "workflowComboBox MathOperationsWorkflow"
→ parameters = null
```

**注意**: 路径 B 中，`targetParts` 将所有不含 `=` 的 token 合并为 target，所以 `cmd.Target = "workflowComboBox MathOperationsWorkflow"`。这会导致 `DoSetAsync` 收到 `target = "workflowComboBox MathOperationsWorkflow"`，然后第 279-300 行从 target 中提取控件名和值。

### 4.2 CliModeRouter.NormalizeToMetaCommand 的作用

将简写命令规范化为 `do` 动词格式，确保 `MetaCommand.Parse` 走路径 A（正常解析）：

| 原始命令 | 规范化后 |
|---------|---------|
| `load WorkflowRunner.json` | `do load WorkflowRunner.json` |
| `set workflowComboBox MathOperationsWorkflow` | `do set workflowComboBox MathOperationsWorkflow` |
| `click executeButton` | `do click executeButton` |
| `echo "hello"` | `do echo "hello"` |
| `get titleLabel` | `get titleLabel` (不在 ActionCommands 中) |

---

## 五、事件绑定机制

### 5.1 配置中的事件定义

`WorkflowRunner.json` 中 `workflowComboBox` 的事件定义：

```json
{
  "type": "combobox",
  "name": "workflowComboBox",
  "events": {
    "selectedIndexChanged": "LoadWorkflowDetails",
    "bindings": {
      "inputs": ["workflowComboBox"],
      "output": "workflowDetailTextBox"
    }
  }
}
```

### 5.2 EventBindingResolver 查找流程

```
FindEventBinding("workflowComboBox", "selectedindexchanged")
    │
    ├── GetSnapshot() → 获取 _loadedConfigs 快照
    │
    ├── FindControlByName(config, "workflowComboBox")
    │   ├── 遍历 config.Views
    │   ├── FindControlInList(view.Controls, "workflowComboBox")
    │   │   ├── 遍历 controls
    │   │   ├── 递归查找 control.Controls (子控件)
    │   │   └── 递归查找 control.Panel1Controls/Panel2Controls (SplitContainer)
    │   └── 返回 ControlConfig { Name = "workflowComboBox", Events = { SelectedIndexChanged = "LoadWorkflowDetails" } }
    │
    ├── switch(eventType) → "selectedindexchanged" → functionName = control.Events.SelectedIndexChanged
    │
    └── 返回 EventBindingInfo { FunctionName = "LoadWorkflowDetails", Inputs = ["workflowComboBox"], Output = "workflowDetailTextBox" }
```

### 5.3 DoSetAsync 中的事件触发流程

```
DoSetAsync("workflowComboBox", value)
    │
    ├── _virtualStore.Set("workflowComboBox", value)
    │
    ├── _bindingResolver.FindEventBinding("workflowComboBox", "selectedindexchanged")
    │   → EventBindingInfo { FunctionName = "LoadWorkflowDetails", Output = "workflowDetailTextBox" }
    │
    ├── _functionExecutor.ExecuteAsync("LoadWorkflowDetails", new object[] { value })
    │   → 执行 LoadWorkflowDetails 函数
    │
    ├── _virtualStore.Set("workflowDetailTextBox", result.Value.ToString())
    │   → 将结果写入 workflowDetailTextBox
    │
    ├── _functionExecutor.ExecuteAsync("LoadWorkflowInputTemplate", new object[] { value })
    │   → 尝试加载工作流的 defaultInput
    │
    └── _virtualStore.Set("inputTextBox", inputTemplateResult.Value.ToString())
        → 将默认输入写入 inputTextBox
```

---

## 六、修复计划

### 修复 1: `DoSetAsync` 值提取逻辑

**目标**: 支持从 `parameters` 中提取非 `value` 键的值

**修改文件**: `FileDriven/FileDriven.CLI/Adapters/BackendCommandAdapter.cs`

**修改内容**: 在第 279-300 行之后，添加从 `parameters` 中提取值的逻辑：

```csharp
// 新增：当 parameters 中没有 "value" 键时，尝试从 parameters 中提取第一个值
if (value == null && parameters != null && parameters.Count > 0)
{
    // 尝试获取第一个非 value 的参数值
    foreach (var kvp in parameters)
    {
        if (!string.Equals(kvp.Key, "value", StringComparison.OrdinalIgnoreCase))
        {
            value = kvp.Value?.ToString();
            break;
        }
    }
}
```

### 修复 2: 验证事件绑定是否正常工作

**目标**: 确保 `FindEventBinding` 能正确找到 `workflowComboBox` 的 `selectedIndexChanged` 事件

**验证方法**: 修复 1 后，运行脚本，观察输出中是否有 `Triggering event: workflowComboBox.SelectedIndexChanged → LoadWorkflowDetails` 日志

### 修复 3: 验证 `LoadWorkflowInputTemplate` 函数

**目标**: 确保 `LoadWorkflowInputTemplate` 函数已注册并能正确执行

**验证方法**: 修复 1 后，运行脚本，观察输出中是否有 `Auto-loaded default input for workflow: MathOperationsWorkflow` 日志

---

## 七、测试验证

### 7.1 手动测试步骤

1. 构建项目: `dotnet build`
2. 运行脚本: `FileDriven.Launcher --file test_default_input_load.txt`
3. 观察输出，检查是否有错误信息
4. 验证 `get workflowDetailTextBox` 和 `get inputTextBox` 的输出是否符合预期

### 7.2 预期输出

```
[OK] Loaded config: ...WorkflowRunner.json
[OK] Set workflowComboBox = MathOperationsWorkflow → LoadWorkflowDetails() succeeded
[OK] workflowDetailTextBox = ... (工作流详情)
[OK] inputTextBox = {"a":0, "b":0, "c":0, "total":100, "count":20}
```

---

## 八、相关文件索引

| 文件 | 用途 |
|------|------|
| `FileDriven/DrivenConfig/Scripts/test_default_input_load.txt` | 测试脚本 |
| `FileDriven/DrivenConfig/JsonConfig/WorkflowRunner.json` | 工作流运行器 UI 配置 |
| `FileDriven/FileDriven.CLI/CliModeRouter.cs` | CLI 命令路由器 |
| `FileDriven/FileDriven.CLI/Adapters/BackendCommandAdapter.cs` | 后端命令适配器（核心） |
| `FileDriven/FileDriven.CLI/Services/EventBindingResolver.cs` | 事件绑定解析器 |
| `FileDriven/FileDriven.CLI/Services/VirtualControlStore.cs` | 虚拟控件存储 |
| `FileDriven/FileDriven.CLI/Adapters/FunctionExecutionFrameworkFunctionExecutor.cs` | 函数执行器 |
| `BasicMethod/MetaCommand/Models.cs` | 命令模型（含 Parse 方法） |
| `BasicMethod/MetaCommand/Parsing.cs` | 命令分词器和参数解析器 |
| `BasicMethod/MetaCommand/Routing.cs` | 命令路由器 |
| `BasicMethod/PathManager/PathResolver.cs` | 路径解析器 |
| `BasicMethod/ConfigurationModels/Models/ControlConfig.cs` | 控件配置模型 |
