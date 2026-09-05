# AgentConfigUI.json `exec` 命令分析与修复方案

## 一、问题概述

`AgentConfigUI.json` 的 `initCommands` 和事件绑定中使用了 `exec` 命令来调用 `AgentConfigWorkflow`，但该命令的 handler 未在 `NonEventCommandRegistry` 中注册，导致功能不可用。

### 涉及的关键代码路径

```
AgentConfigUI.json
  ├── initCommands: ["exec AgentConfigWorkflow _stepSelector=Init"]
  ├── funcListView.selectedIndexChanged → "AgentConfigWorkflow" (事件绑定)
  └── workflowComboBox.selectedIndexChanged → ["LoadWorkflowDetails", "LoadDefaultInput"]
```

---

## 二、当前架构分析

### 2.1 命令处理链路

```
JSON 配置
  → ScriptCommand.ToCommandText()     → 生成文本命令 "exec AgentConfigWorkflow _stepSelector=Init"
  → ExecuteCommandHandler             → 判断 IsNonEventCommandAction("exec") = true
  → HandleNonEventCommandAsync()      → 通过 INonEventCommandLookup.TryGetHandler("exec", ...)
  → NonEventCommandRegistry           → _handlers 字典中查找 "exec"
  → 未找到 → 返回 "未找到非事件命令 handler: exec"
```

### 2.2 注册现状

| 注册点 | 注册内容 | 是否包含 `exec` |
|--------|----------|:---:|
| `NonEventCommandCenter.Initialize()` | 创建空的 `CoreNonEventCommandRegistry` | ❌ |
| `WinFormsCommandEngine.RegisterBuiltInCommands()` | `set`, `get`, `click`, `list`, `show`, `hide`, `close` | ❌ |
| `CommandExecution.RegisterBuiltInCommands()` | `sleep`, `echo`, `exit` | ❌ |
| `UIToolbox` | `ClearOutput`, `ExecuteWorkflow`, `ReloadWorkflows`, `SaveResultToFile`, `LoadWorkflowDetails`, `LoadEnvironmentApiKey`, `SetValue` | ❌ |

### 2.3 关键发现

1. **`exec` 被识别为非事件命令** — `IsNonEventCommandAction("exec")` 返回 `true`
2. **但 handler 从未注册** — `NonEventCommandRegistry._handlers` 中没有 `"exec"` 条目
3. **`AgentConfigWorkflow` 工作流未定义** — 搜索整个项目，只在 JSON 配置中出现，无 C# 代码对应
4. **不会循环调用 AI** — handler 缺失时返回明确的错误信息，管线正常终止

---

## 三、修复方案

### 方案 A：注册 `exec` 命令 handler（推荐）

在 `NonEventCommandCenter.Initialize()` 中注册 `exec` 命令的 handler，使其能够通过 `INonEventCommandLookup` 查找到。

#### 修改文件

**`CsharpPrj/Operation/FunctionStorage/Framework/NonEventCommandCenter.cs`**

```csharp
public static void Initialize()
{
    lock (_lock)
    {
        if (_nonEventRegistry != null) return;

        var registry = new CoreNonEventCommandRegistry();

        // ★ 修复：注册 exec 命令 handler
        //   exec 命令用于执行已注册的工作流函数
        //   格式：exec <functionName> [param1=value1] [param2=value2]
        registry.Register("exec", context =>
        {
            var target = context.Target;  // 函数名（如 AgentConfigWorkflow）
            var parameters = context.Parameters;  // 参数字典

            if (string.IsNullOrEmpty(target))
            {
                context.ErrorMessage = "exec 命令缺少目标函数名";
                context.Success = false;
                return;
            }

            // 通过 FunctionStorageRegistry 查找并执行函数
            var functionRegistry = FunctionStorageRegistry.Instance;
            if (functionRegistry == null)
            {
                context.ErrorMessage = "FunctionStorageRegistry 未初始化";
                context.Success = false;
                return;
            }

            try
            {
                // 构造函数调用参数
                var funcParams = new Dictionary<string, object?>();
                if (parameters != null)
                {
                    foreach (var kvp in parameters)
                    {
                        funcParams[kvp.Key] = kvp.Value;
                    }
                }

                // 执行函数
                var result = functionRegistry.ExecuteFunction(target, funcParams);
                context.ResultValue = result?.ToString() ?? "";
                context.Success = true;
            }
            catch (Exception ex)
            {
                context.ErrorMessage = $"exec 命令执行失败: {ex.Message}";
                context.Success = false;
            }
        }, CommandCriticality.Important, "执行已注册的工作流函数");

        _nonEventRegistry = registry;
    }
}
```

#### 依赖接口

需要确保 `FunctionStorageRegistry` 暴露 `ExecuteFunction` 方法：

```csharp
// FunctionStorageRegistry 中需要的方法
public object? ExecuteFunction(string functionName, Dictionary<string, object?> parameters)
{
    if (_registry.TryGetValue(functionName, out var entry))
    {
        return entry.Execute(parameters);
    }
    throw new KeyNotFoundException($"函数 '{functionName}' 未注册");
}
```

---

### 方案 B：将 `exec` 改为事件绑定（替代方案）

如果 `exec` 命令的语义本身就是"触发一个工作流"，更合适的做法是将 `initCommands` 中的 `exec` 改为事件绑定，通过 `EventBindingOrchestrator` 来路由。

#### 修改 `AgentConfigUI.json`

```json
{
  "initCommands": [
    "fire AgentConfigWorkflow _stepSelector=Init"
  ]
}
```

然后在 `EventBindingOrchestrator` 中注册 `AgentConfigWorkflow` 的事件绑定。

---

### 方案 C：移除未实现的 `exec` 命令（最小改动）

如果 `AgentConfigWorkflow` 尚未实现，最安全的做法是暂时移除 `initCommands` 中的 `exec` 命令，避免初始化时报错。

#### 修改 `AgentConfigUI.json`

```json
{
  "initCommands": []
}
```

---

## 四、推荐方案对比

| 方案 | 改动量 | 风险 | 效果 |
|:----|:-----:|:----:|:----|
| **A: 注册 exec handler** | 中（1 个文件 + 接口调整） | 低 | 完整支持 exec 命令 |
| **B: 改为事件绑定** | 中（JSON + 事件注册） | 中 | 更符合架构设计 |
| **C: 移除 exec** | 小（1 个 JSON 文件） | 低 | 消除报错，功能暂缺 |

**推荐方案 A**，因为：
1. `exec` 已被 `IsNonEventCommandAction` 识别，说明设计上预期它存在
2. 注册 handler 后，所有使用 `exec` 的 JSON 配置都能正常工作
3. 改动集中在 `NonEventCommandCenter`，影响范围可控

---

## 五、验证方法

### 5.1 单元测试

```csharp
[Test]
public async Task ExecCommand_WithRegisteredFunction_ShouldExecuteSuccessfully()
{
    // Arrange
    NonEventCommandCenter.Initialize();
    var registry = NonEventCommandCenter.Registry;

    // Act
    var found = registry.TryGetHandler("exec", out var handler);

    // Assert
    Assert.IsTrue(found);
    Assert.IsNotNull(handler);
}
```

### 5.2 集成测试

1. 启动应用，打开 Agent 控制面板
2. 观察控制台输出，确认 `initCommands` 中的 `exec` 命令执行成功
3. 在函数列表中选择一个函数，确认 `selectedIndexChanged` 事件触发 `AgentConfigWorkflow`

### 5.3 错误日志检查

```
[信息] 执行 initCommand: exec AgentConfigWorkflow _stepSelector=Init
[信息] exec 命令执行成功: AgentConfigWorkflow
```

---

## 六、附录：相关文件清单

| 文件 | 作用 |
|------|------|
| `FileDriven/DrivenConfig/JsonConfig/AgentConfigUI.json` | UI 配置，包含 `exec` 命令引用 |
| `CsharpPrj/Operation/FunctionStorage/Framework/NonEventCommandCenter.cs` | 非事件命令注册中心（需修改） |
| `CsharpPrj/Operation/FunctionStorage/Framework/NonEventCommands.cs` | 非事件命令注册表实现 |
| `CsharpPrj/BasicMethod/Container/Core/ExecuteCommandHandler.cs` | 命令执行 handler，识别 `exec` 为非事件命令 |
| `CsharpPrj/Operation/FunctionStorage/Framework/FunctionStorageRegistry.cs` | 函数注册表（需暴露 ExecuteFunction） |
| `FileDriven/FileDriven.Utilities/ScriptRunner/Core/Models/ScriptCommand.cs` | 脚本命令模型，定义 `exec` 的序列化格式 |
