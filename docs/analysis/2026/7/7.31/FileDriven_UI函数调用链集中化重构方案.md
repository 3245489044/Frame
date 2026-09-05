# FileDriven UI 函数调用链集中化重构方案

> **版本**：v1.0  
> **日期**：2026-07-28  
> **范围**：FileDriven.Execution → FileDriven.Business → MainLayout.json  
> **核心理念**：消除中间层硬编码注册，UI 事件函数统一走 Business 层反射注册路径  

---

## 一、现状分析

### 1.1 当前架构

```
MainLayout.json (events.click 声明)
       ↓ (事件名如 "LoadConfigButton_Click")
事件绑定系统 (ECS ExecuteCreationStage)
       ↓ TryGetDelegate(name)
FunctionStorageRegistry
       ↓ (查找失败,因为注册名不匹配)
       ↓ (查找失败后静默失败 == 点击无效)
       
--- 当前存在两条独立的注册路径 ---

路径 A — Execution 层: UiOperationLibrary (硬编码)
  LoadConfigButton_Click   → CommandExecutionService.ExecuteSingle("load")
  LoadWorkflowButton_Click → CommandExecutionService.ExecuteSingle("show workflow")
  ClearButton_Click        → CommandExecutionService.ExecuteSingle("clear")
  HelpButton_Click         → CommandExecutionService.ExecuteSingle("help")
  LoadSideButton_Click     → CommandExecutionService.ExecuteSingle("sideload")
  ExecuteButton_Click      → ❌ 未注册 (实际运行无效的根因)

路径 B — Business 层: UIToolbox [FunctionLibrary] (反射注册)
  LoadConfig              → 反射调用 AppService.LoadUIConfigAsync()
  LoadWorkflow            → WorkflowLibrary.ReloadAll()
  ClearOutput             → 纯数据操作
  ShowHelp                → MessageBox.Show()
  LoadSidePanel           → 反射调用 AppService.LoadUIConfigAsync()
  ExecuteWorkflow         → WorkflowCommandCenter.ExecuteWorkflow()
  ... (共 18 个函数)
```

### 1.2 核心问题

| # | 问题 | 严重度 | 说明 |
|---|------|--------|------|
| 1 | **事件名与注册名不匹配** | P0 | JSON 的 `"LoadConfigButton_Click"` 不对应 Business 层的 `"LoadConfig"`，需要中间层做名字映射 |
| 2 | **ExecuteButton_Click 未注册** | P0 | UiOperationLibrary 中缺少此函数，点击 Execute 按钮无任何响应 |
| 3 | **中间层维护成本** | P1 | UiOperationLibrary 是硬编码的桥接层，新增按钮需要改 JSON + 改代码两处 |
| 4 | **Business 层通过反射调用 Execution 层** | P2 | `UIToolbox.LoadConfigViaReflectionAsync()` 通过反射跨程序集调用，但这是 Business 层不引用 Execution 层时的必要模式 |

### 1.3 已有的跨层反射模式

`UIToolbox` 中的 `LoadConfigViaReflectionAsync()` 方法（第 76-143 行）已确立了 Business 层调用 Execution 层的标准模式：

```csharp
// 1. 遍历 OpenForms 找到 MainForm
foreach (Form form in Application.OpenForms)
{
    if (form.GetType().Name != "MainForm") continue;
    
    // 2. 反射获取 AppService 属性
    var appService = formType.GetProperty("AppService")?.GetValue(form);
    
    // 3. 反射调用 AppService 方法
    var loadMethod = appService.GetType().GetMethod("LoadUIConfigAsync");
    var task = (Task)loadMethod.Invoke(appService, new object[] { ... });
    await task;
}
```

**此模式是重构将遵循的核心模式**——UI 操作的执行入口是 `FileDrivenApplicationService`，Business 层通过反射调用它。

---

## 二、重构目标

### 2.1 目标架构

```
MainLayout.json events.click
       ↓ (事件名直接匹配 Business 层注册名)
事件绑定系统 (ECS ExecuteCreationStage)
       ↓ TryGetDelegate("LoadConfig")
FunctionStorageRegistry ← UIToolbox [FunctionLibrary]
       ↓ (反射)
UIToolbox 内部方法
       ↓ (反射调用 AppService)
FileDrivenApplicationService (Execution 层唯一入口)
       ↓
ECS 命令管线 / 业务逻辑
```

**关键变化**：
- ❌ **删除** `UiOperationLibrary`（Execution 层中间层）
- ✅ **对齐** `MainLayout.json` 事件名到 `UIToolbox` 注册名
- ✅ **补齐** `UIToolbox.RegisterAll()` 中 `Execute` 的注册
- ✅ **增强** `FileDrivenApplicationService.ExecuteCommandAsync()` 使其真正可执行

### 2.2 预期收益

| 维度 | 现状 | 重构后 |
|------|------|--------|
| 注册路径数量 | 2 条（Execution + Business） | 1 条（仅 Business） |
| 维护点 | JSON + UiOperationLibrary + UIToolbox | JSON + UIToolbox |
| 新按钮成本 | 改 2 个文件 | 改 1 个文件 |
| 中间死代码 | UiOperationLibrary 85 行 | 0 行 |

---

## 三、详细重构步骤

### 步骤 1：修改 `MainLayout.json` 的 events.click 值

**文件**：`FileDriven/DrivenConfig/JsonConfig/MainLayout.json`

将 5 个工具栏按钮的 `events.click` 值改为直接对应 `UIToolbox` 的注册名：

| 按钮 name | 当前 events.click | 改为 | 对应 UIToolbox 函数 |
|-----------|-------------------|------|-------------------|
| loadConfigButton | `LoadConfigButton_Click` | `LoadConfig` | 打开文件对话框 → LoadConfigViaReflectionAsync |
| loadWorkflowButton | `LoadWorkflowButton_Click` | `LoadWorkflow` | 重新扫描并加载工作流列表 |
| clearButton | `ClearButton_Click` | `ClearOutput` | 清空输出文本框 |
| helpButton | `HelpButton_Click` | `ShowHelp` | 显示帮助对话框 |
| loadSideButton | `LoadSideButton_Click` | `LoadSidePanel` | 打开文件对话框 → LoadConfigViaReflectionAsync |
| executeButton | `ExecuteButton_Click` | `Execute` | 执行 commandTextBox 中的命令 |

> **注意**：`ClearOutput` 和 `LoadSidePanel` 在 UIToolbox 中未定义，需要注册。见"待注册函数"部分。

**JSON 修改示例**（loadConfigButton）：

```json
{
  "type": "button",
  "name": "loadConfigButton",
  "text": "Load Config",
  "events": {
    "click": "LoadConfig"     // ← 原为 "LoadConfigButton_Click"
  }
}
```

---

### 步骤 2：删除 `UiOperationLibrary.cs`

**文件**：`FileDriven/FileDriven.Execution/Services/UiOperationLibrary.cs`

**操作**：**删除整个文件**（85 行，含类声明和所有注册逻辑）。

**依据**：该类的所有功能都已由 Business 层的 `UIToolbox` 承载。删除后：
- 无编译错误（检查 `UiOperationLibrary` 引用的来源）
- 无运行时错误（事件绑定改为直接查找 `UIToolbox` 的注册名）
- 需要确保 `AppServiceProvider` 不再引用 `UiOperationLibrary`

**关联影响**：检查 `UiOperationLibrary` 是否被以下位置引用：

```csharp
// 搜索所有 *.cs 文件中的 "UiOperationLibrary"
// 如果 FrameworkInitializer 中有调用 UiOperationLibrary.RegisterAll()，删除该调用
```

---

### 步骤 3：在 `UIToolbox` 中补齐未注册函数

**文件**：`FileDriven/FileDriven.Business/Registrations/UI/UIToolbox.cs`

#### 3.1 新增 `Execute` 注册

`Execute` 的功能是：读取 `commandTextBox` 的文本，通过反射调用 `FileDrivenApplicationService.ExecuteCommandAsync()` 执行。

```csharp
// ★ 2026-07-28 新增：Execute — 执行 commandTextBox 中的命令
registrar.Register("Execute",
    (Func<string>)(() =>
    {
        try
        {
            FsLog.Log("UIToolbox_Execute_start");

            // 通过反射调用 AppService 执行命令
            var result = ExecuteCommandViaReflection();
            FsLog.Log("UIToolbox_Execute_completed", new { result });
            return result;
        }
        catch (Exception ex)
        {
            FsLog.Log("UIToolbox_Execute_error", new { error = ex.Message });
            return $"执行失败: {ex.Message}";
        }
    }), "执行 commandTextBox 中的命令");
```

**反射执行方法**（可复用 `LoadConfigViaReflectionAsync` 模式）：

```csharp
/// <summary>
/// 通过反射调用 MainForm.AppService.ExecuteCommandAsync(commandText)
/// <para>复用 LoadConfigViaReflectionAsync 的反射模式。</para>
/// </summary>
private static string ExecuteCommandViaReflection()
{
    foreach (System.Windows.Forms.Form form in System.Windows.Forms.Application.OpenForms)
    {
        var formType = form.GetType();
        if (formType.Name != "MainForm") continue;

        try
        {
            // 1. 获取 AppService
            var appServiceProp = formType.GetProperty("AppService",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
            var appService = appServiceProp?.GetValue(form);
            if (appService == null) break;

            // 2. 获取 commandTextBox.Text
            var textBox = FindControlRecursive(form, "commandTextBox");
            if (textBox == null) return "未找到 commandTextBox";
            var textBoxProp = textBox.GetType().GetProperty("Text");
            var commandText = textBoxProp?.GetValue(textBox) as string ?? "";
            if (string.IsNullOrWhiteSpace(commandText))
                return "命令文本为空";

            // 3. 调用 AppService.ExecuteCommandAsync(commandText)
            var executeMethod = appService.GetType().GetMethod("ExecuteCommandAsync",
                new[] { typeof(string) });
            if (executeMethod == null) return "ExecuteCommandAsync 方法未找到";

            var task = (Task<bool>?)executeMethod.Invoke(appService, new object[] { commandText });
            if (task == null) return "ExecuteCommandAsync 返回 null";

            var success = task.GetAwaiter().GetResult();
            return success ? $"命令执行成功" : "命令执行失败";
        }
        catch (Exception ex)
        {
            return $"执行异常: {ex.Message}";
        }
    }
    return "未找到 MainForm";
}
```

#### 3.2 新增 `ClearOutput` 注册（如不存在）

检查 `UIToolbox` 中是否已注册 `ClearOutput`（第 157-163 行），确认存在则无需重复新增。

#### 3.3 新增 `LoadSidePanel` 注册（如不存在）

检查 `UIToolbox` 中是否已注册 `LoadSidePanel`（第 400-421 行），确认存在则无需重复新增。

---

### 步骤 4：增强 `FileDrivenApplicationService.ExecuteCommandAsync()`

**文件**：`FileDriven/FileDriven.Execution/Services/FileDrivenApplicationService.cs`

#### 4.1 当前实现（存根）

```csharp
public async Task<bool> ExecuteCommandAsync(string command)
{
    return await _dispatcher.InvokeAsync(async () =>
    {
        var parts = command.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var commandName = parts.Length > 0 ? parts[0].ToLowerInvariant() : "";

        try
        {
            // 简化：仅处理 help/clear，外部命令由 CommandExecutor 委托执行
            return commandName == "help" || commandName == "clear";
        }
        catch
        {
            return false;
        }
    });
}
```

#### 4.2 重构后实现

```csharp
/// <summary>
/// 执行 UI 命令（重构：替代 CommandHandler）
/// <para>命令类型：help / clear / load / show / close / click / set / get / list / sleep / sideload 等</para>
/// <para>★ 2026-07-28 重构：去除存根实现，真正委托给 ECS 命令管线执行。</para>
/// </summary>
public async Task<bool> ExecuteCommandAsync(string command)
{
    if (string.IsNullOrWhiteSpace(command))
        return false;

    // help/clear 走快捷路径
    var parts = command.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
    var commandName = parts.Length > 0 ? parts[0].ToLowerInvariant() : "";

    if (commandName == "help" || commandName == "clear")
        return true;

    // 其他命令委托给 CommandExecutionService
    try
    {
        var (success, errorMessage, _) = CommandExecutionService.ExecuteSingle(command);
        if (!success)
            ExecutionLog.Log("AppService_ExecuteCommand_failed",
                new { command, errorMessage });
        return success;
    }
    catch (Exception ex)
    {
        ExecutionLog.Log("AppService_ExecuteCommand_exception",
            new { command, error = ex.Message });
        return false;
    }
}
```

> **注意**：此方法现在从 UI 线程（_dispatcher.InvokeAsync 包装）调用 `CommandExecutionService.ExecuteSingle`。需要确保 `ExecuteSingle` 的内部操作不阻塞 UI 线程（它目前使用 `EntityManager` 同步执行）。

---

### 步骤 5：清理引用

#### 5.1 搜索 `UiOperationLibrary` 的引用点

```csharp
// 可能在 FrameworkInitializer.cs 或 FunctionLibraryLifecycle 中有：
// UiOperationLibrary.RegisterAll(registry);
// 
// 删除此调用，因为 UIToolbox 的注册由 FunctionLibraryLifecycle 统一管理
```

#### 5.2 验证 FunctionLibraryLifecycle 已包含 UIToolbox

确认 `FunctionLibraryLifecycle` 已通过反射扫描或显式调用的方式执行了 `UIToolbox.RegisterAll()`。

---

## 四、执行后架构

### 4.1 注册体系总览

```
[FunctionLibrary] 标记的静态类
  ├─ UIToolbox (Business/Registrations/UI/)
  │     ├─ LoadConfig         → 反射 → AppService.LoadUIConfigAsync()
  │     ├─ LoadWorkflow       → 直接 → WorkflowLibrary.ReloadAll()
  │     ├─ ClearOutput        → 直接 → 清空逻辑
  │     ├─ ShowHelp           → 直接 → MessageBox.Show()
  │     ├─ LoadSidePanel      → 反射 → AppService.LoadUIConfigAsync()
  │     ├─ Execute            → 反射 → AppService.ExecuteCommandAsync()  ★ 新增
  │     └─ ... (其余 13 个)
  ├─ BuiltIn 目录下的函数库
  ├─ Math 目录下的函数库
  └─ Workflow 目录下的函数库

FunctionStorageRegistry (注册中心)
       ↓ TryGetDelegate(name)  — 事件绑定系统通过此方法查找函数
       ↓ DynamicInvoke(args)   — 找到后通过反射调用
```

### 4.2 调用链对比

#### 重构前（以 LoadConfig 为例）

```
点击 "Load Config" 按钮
  → events.click = "LoadConfigButton_Click"
  → FunctionStorageRegistry.TryGetDelegate("LoadConfigButton_Click")
  → 命中 UiOperationLibrary 的注册
  → CommandExecutionService.ExecuteSingle("load")
  → CommandInputPort.ExecuteSingle("load")
  → ECS 命令管线
```

#### 重构后（以 LoadConfig 为例）

```
点击 "Load Config" 按钮
  → events.click = "LoadConfig"
  → FunctionStorageRegistry.TryGetDelegate("LoadConfig")
  → 命中 UIToolbox 的注册
  → UIToolbox.LoadConfig() 内部
  → LoadConfigViaReflectionAsync("选中文件", "Content", "contentContainer")
  → 反射调用 AppService.LoadUIConfigAsync()
  → ECS 渲染管线
```

#### 重构后（以 Execute 为例）

```
点击 "Execute" 按钮
  → events.click = "Execute"
  → FunctionStorageRegistry.TryGetDelegate("Execute")
  → 命中 UIToolbox 的注册
  → UIToolbox.Execute() 内部
  → ExecuteCommandViaReflection()
  → 反射获取 commandTextBox.Text
  → 反射调用 AppService.ExecuteCommandAsync(commandText)
  → CommandExecutionService.ExecuteSingle(commandText)
  → ECS 命令管线
```

---

## 五、错误处理与边界情况

### 5.1 反射调用失败时的回退

所有通过反射调用 `FileDrivenApplicationService` 的方法，在反射失败时应：

1. 记录详细错误日志（包含反射目标类型名和方法名）
2. 返回友好的错误消息字符串给调用方（按钮不崩溃）
3. 不抛出未捕获异常

```csharp
// 标准错误处理模板
try { /* 反射调用 */ }
catch (MissingMethodException ex)
{
    FsLog.Log("Reflection_MethodNotFound", new { method = "ExecuteCommandAsync", error = ex.Message });
    return "内部错误: 方法未找到";
}
catch (InvalidCastException ex)
{
    FsLog.Log("Reflection_TypeMismatch", new { error = ex.Message });
    return "内部错误: 类型不匹配";
}
catch (Exception ex)
{
    FsLog.Log("Reflection_GenericError", new { error = ex.Message });
    return $"执行异常: {ex.Message}";
}
```

### 5.2 Window 未初始化时的状态

当 `MainForm` 尚未加载完成时：
- `Application.OpenForms` 可能为空或包含其他 Form
- `FindControlRecursive` 可能找不到 `commandTextBox` 等控件
- 所有反射调用应检查 null 并返回明确的状态消息

### 5.3 并发点击防护

工具栏按钮可被用户快速多次点击。建议在 `FunctionExecutionSystem` 层面确保同一事件的串行执行：

```csharp
// FunctionExecutionSystem.Update() 中对同一函数增加 Executing 标志
// 如果同一函数正在执行中，新的触发请求排队或丢弃
```

---

## 六、回滚方案

### 6.1 回滚条件

- 重构后任意工具栏按钮点击无响应
- 反射调用持续抛出异常
- 性能明显劣化（按钮响应时间 > 500ms）

### 6.2 回滚步骤

1. **恢复 `MainLayout.json`**：将 events.click 值改回原名
2. **恢复 `UiOperationLibrary.cs`**：还原已删除文件
3. **恢复 `FileDrivenApplicationService.ExecuteCommandAsync()`**：回退到存根实现
4. **删除 `UIToolbox` 中新增的 `Execute` 注册**：还原到重构前状态

---

## 七、测试验证清单

| # | 验证项 | 预期 | 方式 |
|---|--------|------|------|
| 1 | 点击 Load Config | 打开文件选择对话框 | 手动测试 |
| 2 | 点击 Load Workflow | 加载工作流列表 | 手动测试 |
| 3 | 点击 Clear | 输出面板内容清空 | 手动测试 |
| 4 | 点击 Help | 弹出帮助对话框 | 手动测试 |
| 5 | 点击 Load Side | 打开文件选择对话框 | 手动测试 |
| 6 | 点击 Execute | 执行 commandTextBox 中的命令 | 手动测试 |
| 7 | 删除 UiOperationLibrary 后项目可编译 | 无编译错误 | dotnet build |
| 8 | 无 `UiOperationLibrary` 引用残留 | 搜索结果为空 | 全文搜索 |
| 9 | UIToolbox.Execute 输入空格命令 | 返回"命令文本为空" | 手动测试 |
| 10 | UIToolbox.Execute 在 MainForm 未加载时 | 返回"未找到 MainForm" | 手动测试 |

---

## 八、时间规划

| 步骤 | 内容 | 预估时长 |
|------|------|---------|
| 1 | 修改 MainLayout.json（6 个 events.click） | 10 min |
| 2 | 删除 UiOperationLibrary.cs | 5 min |
| 3 | 新增 Execute 注册 + ExecuteCommandViaReflection | 30 min |
| 4 | 增强 FileDrivenApplicationService.ExecuteCommandAsync | 15 min |
| 5 | 清理引用（搜索残留引用点） | 10 min |
| 6 | 编译验证 + 手动测试 | 15 min |
| **合计** | | **约 85 min** |