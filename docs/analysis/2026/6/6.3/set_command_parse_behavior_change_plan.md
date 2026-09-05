# `set` 命令解析行为变更 — 基于方案 B 的源代码更新方案

## 概述

本文档分析 `set` 命令在 `ParseCommandSkeleton` 中的解析行为变更，并基于**方案 B**（set 命令走完整管线流程）给出具体的源代码更新方案。

---

## 1. 背景：方案 B 的设计意图

方案 B 的核心思想是：**`set` 命令不再在 `ParseCommandSkeleton` 阶段做特殊处理（不再调用 `HandleSetValue`），而是走完整管线流程**。

### 管线流程对比

| 阶段 | 旧方案（方案 A） | 新方案（方案 B） |
|------|-----------------|-----------------|
| `ParseCommandSkeleton` | 解析命令 + 调用 `HandleSetValue` 验证/执行 set | 只解析命令，不处理 set 特定逻辑 |
| `FindBindingSkeleton` | 跳过（set 已处理） | 查找 set 的事件绑定 |
| `ExtractValuesSkeleton` | 跳过（set 已处理） | 提取控件值 |
| `ExecuteFunctionSkeleton` | 跳过（set 已处理） | 执行 set 函数（设置控件值） |
| `WriteResultSkeleton` | 跳过（set 已处理） | 写入结果 |

### 设计优势

1. **架构一致性**：所有 Do 动词命令统一走完整管线，set 不再有特殊路径
2. **关注点分离**：Parse 只负责解析，执行逻辑下沉到 `ExecuteFunctionSkeleton`
3. **可扩展性**：新增命令无需修改 `ParseCommandSkeleton`，只需在 `FunctionStorage` 注册函数
4. **消除重复**：`HandleSetValue` 中的逻辑与 `FunctionStorage` 中注册的 set 函数重复

---

## 2. 解析流程追踪

### 2.1 命令规范化

`set valueATextBox 42` 经过 `CommandNormalizer.Normalize()`：

```csharp
// Parsing.cs 第 240-243 行
if (ActionCommands.Contains(firstWord))  // "set" 在 ActionCommands 列表中
{
    return "do " + trimmed;  // → "do set valueATextBox 42"
}
```

### 2.2 MetaCommand 解析

`CommandParser.Parse("do set valueATextBox 42")` 的解析逻辑（Parsing.cs 第 346-353 行）：

```csharp
if (verb == MetaVerb.Do)
{
    cmd.Action = tokens[1];           // "set"
    if (tokens.Count > 2)
    {
        ParseDoTargetAndParameters(tokens, cmd);  // 处理 tokens[2..]
    }
}
```

### 2.3 Target/Parameters 分离逻辑

`ParseDoTargetAndParameters`（Parsing.cs 第 366-421 行）遍历 tokens[2..]：

| Token | 包含 `=`？ | 处理方式 |
|-------|-----------|---------|
| `"valueATextBox"` | ❌ | 加入 `targetParts` |
| `"42"` | ❌ | 加入 `targetParts` |

最终结果：
- `Target = "valueATextBox 42"`（整个剩余部分合并为 target）
- `Parameters = null`（没有 `key=value` 格式的 token）

### 2.4 关键发现

**`CommandParser.ParseDoTargetAndParameters` 的 target 合并逻辑**（第 399-400 行）：

```csharp
if (targetParts.Count > 0)
    cmd.Target = string.Join(" ", targetParts);
```

这意味着所有不含 `=` 的 token 都会被合并到 `Target` 中。对于 `set valueATextBox 42`：
- `targetParts = ["valueATextBox", "42"]`
- `Target = "valueATextBox 42"`

---

## 3. 源代码更新方案

### 3.1 `ParseCommandSkeleton.cs` — 移除 set 特殊处理

**当前状态**（第 100-111 行）：`set` 命令已不在 `ParseCommandSkeleton` 中特殊处理，注释已说明重构意图。

**需要做的**：确认 `HandleSetValue` 方法已完全移除，且 `set` 命令不再被 `IsNonEventCommand` 拦截。

**检查点**：
- ✅ `set` 不在 `IsNonEventCommandAction` 列表中（`FileDrivenPipelineContext.cs` 第 297-299 行）
- ✅ `HandleSetValue` 已从 `ParseCommandSkeleton` 中移除
- ✅ `set` 命令走完整管线（`FindBindingSkeleton` → `ExtractValuesSkeleton` → `ExecuteFunctionSkeleton`）

**无需修改**，当前代码已符合方案 B 的设计。

### 3.2 `CommandNormalizer.cs` — 确认 set 在 ActionCommands 列表中

**当前状态**（Parsing.cs 第 194-198 行）：

```csharp
private static readonly HashSet<string> ActionCommands = new(StringComparer.OrdinalIgnoreCase)
{
    "load", "set", "click", "echo", "close", "show", "list", "sleep", "validate",
    "log"
};
```

✅ `"set"` 已在 `ActionCommands` 列表中，`set valueATextBox 42` 会被规范化为 `do set valueATextBox 42`。

**无需修改**。

### 3.3 `CommandParser.ParseDoTargetAndParameters` — 优化 target 分离逻辑（可选优化）

**当前问题**：对于 `set valueATextBox 42`，`Target = "valueATextBox 42"`（整个剩余部分）。这在 Parse 阶段是合理的，但下游 `FindBindingSkeleton` 需要从 `"valueATextBox 42"` 中提取控件名 `"valueATextBox"`。

**可选优化方案**：在 `ParseDoTargetAndParameters` 中，对于 `Do` 动词的 `set` action，将第一个 token 作为 target，后续 token 作为参数。

**修改位置**：`Parsing.cs` 第 366-421 行 `ParseDoTargetAndParameters` 方法。

**修改内容**：

```csharp
private static void ParseDoTargetAndParameters(List<string> tokens, MetaCommand cmd)
{
    var targetParts = new List<string>();
    var paramParts = new List<string>();
    bool jsonFound = false;

    for (int i = 2; i < tokens.Count; i++)
    {
        var token = tokens[i];

        // 检测 JSON 开始
        if (token.StartsWith("{") || token.StartsWith("["))
        {
            var (jsonStr, endIndex) = MergeJsonTokens(tokens, i);
            if (!string.IsNullOrEmpty(jsonStr))
            {
                if (cmd.Parameters == null)
                    cmd.Parameters = new Dictionary<string, object>();
                cmd.Parameters["_json"] = jsonStr;
                jsonFound = true;
                i = endIndex;
                continue;
            }
        }

        // ★ 方案 B 优化：对于 set 命令，第一个 token 作为 target，后续 token 作为参数
        //   例如 "set valueATextBox 42" → Target="valueATextBox", Parameters={"42": true}
        //   这样下游 FindBindingSkeleton 可以直接使用 ParsedTarget 查找控件绑定
        if (cmd.Action == "set" && targetParts.Count == 0)
        {
            targetParts.Add(token);
        }
        else if (token.Contains('='))
        {
            paramParts.Add(token);
        }
        else
        {
            // ★ 方案 B 优化：对于 set 命令，非 target 的 token 作为参数
            //   例如 "set valueATextBox 42" → "42" 作为参数键（值为 true）
            if (cmd.Action == "set")
            {
                if (cmd.Parameters == null)
                    cmd.Parameters = new Dictionary<string, object>();
                cmd.Parameters[token] = true;
            }
            else
            {
                targetParts.Add(token);
            }
        }
    }

    if (targetParts.Count > 0)
        cmd.Target = string.Join(" ", targetParts);

    if (paramParts.Count > 0)
    {
        var parameters = ParameterParser.ParseParameters(paramParts, 0);
        if (parameters.Count > 0)
        {
            if (cmd.Parameters == null)
                cmd.Parameters = new Dictionary<string, object>();
            foreach (var kv in parameters)
            {
                cmd.Parameters[kv.Key] = kv.Value;
            }
        }
    }

    // 如果没有找到任何 target 和参数，但 tokens[2] 存在，将其作为 target
    if (cmd.Target == null && !jsonFound && tokens.Count > 2)
    {
        cmd.Target = tokens[2];
    }
}
```

**优化效果**：

| 命令 | 旧行为 | 新行为（优化后） |
|------|--------|----------------|
| `set valueATextBox 42` | `Target="valueATextBox 42"`, `Parameters=null` | `Target="valueATextBox"`, `Parameters={"42": true}` |
| `set workflowComboBox MathOps` | `Target="workflowComboBox MathOps"`, `Parameters=null` | `Target="workflowComboBox"`, `Parameters={"MathOps": true}` |
| `set valueATextBox` | `Target="valueATextBox"`, `Parameters=null` | `Target="valueATextBox"`, `Parameters=null` |

### 3.4 `FindBindingSkeleton.cs` — 确保 set 命令能正确查找绑定

**当前状态**（第 95-96 行）：

```csharp
var eventType = resolver.ResolveEventType(
    input.ParsedTarget ?? "", input.ParsedAction ?? "click");
```

✅ `FindBindingSkeleton` 已通过 `IEventBindingResolver.ResolveEventType` 动态解析事件类型，对 `set` 命令会根据控件类型（ComboBox → `selectedindexchanged`, TextBox → `textchanged`）选择合适的事件类型。

**无需修改**，前提是 `ParsedTarget` 能正确解析为控件名（如果采用 3.3 节的优化，则 `ParsedTarget` 为 `"valueATextBox"`，可以直接用于查找绑定）。

### 3.5 `ExecuteFunctionSkeleton.cs` — 确保 set 函数能正确执行

**当前状态**（第 47-55 行）：

```csharp
if (input.IsNonEventCommand)
{
    return input;
}
```

✅ `set` 命令的 `IsNonEventCommand` 为 `false`，不会在此被跳过，会正常执行绑定的 set 函数。

**无需修改**。

### 3.6 `FunctionStorage` — 注册 set 函数（如尚未注册）

**需要确认**：`FunctionStorage` 中是否已注册了 `set` 函数。

**检查位置**：`Share/FunctionStorage/MathLibrary.cs` 或 `Share/FunctionStorage/UIToolbox.cs`。

**如果未注册**，需要添加：

```csharp
// 在 UIToolbox.RegisterAll() 或 MathLibrary.RegisterAll() 中
FunctionStorageRegistry.Register("set", async (args, context) =>
{
    // set 命令的执行逻辑：设置控件值
    // args[0] = 控件名, args[1] = 值
    // 具体实现取决于 GUI/CLI 模式
    return new FunctionResult { IsSuccess = true, Value = $"Set {args[0]} = {args[1]}" };
});
```

---

## 4. 方案 B 的两种子方案对比

### 方案 B1（不优化 ParseDoTargetAndParameters）

| 命令 | `ParsedTarget` | `ParsedParameters` |
|------|---------------|-------------------|
| `set valueATextBox 42` | `"valueATextBox 42"` | `null` |
| `set workflowComboBox MathOps` | `"workflowComboBox MathOps"` | `null` |
| `set valueATextBox` | `"valueATextBox"` | `null` |

**下游影响**：
- `FindBindingSkeleton` 需要从 `"valueATextBox 42"` 中提取控件名（通过空格分割或正则）
- 或者 `FindBindingSkeleton` 使用 `ParsedAction`（`"set"`）+ `ParsedTarget` 的第一个 token 查找绑定

### 方案 B2（优化 ParseDoTargetAndParameters）

| 命令 | `ParsedTarget` | `ParsedParameters` |
|------|---------------|-------------------|
| `set valueATextBox 42` | `"valueATextBox"` | `{"42": true}` |
| `set workflowComboBox MathOps` | `"workflowComboBox"` | `{"MathOps": true}` |
| `set valueATextBox` | `"valueATextBox"` | `null` |

**下游影响**：
- `FindBindingSkeleton` 可以直接使用 `ParsedTarget` 查找控件绑定
- `ExtractValuesSkeleton` 可以从 `ParsedParameters` 中提取值参数
- 更符合"关注点分离"原则

### 推荐方案

**推荐方案 B2**，原因：
1. `ParsedTarget` 保持为纯控件名，下游无需额外解析
2. 值参数在 `ParsedParameters` 中，`ExtractValuesSkeleton` 可以直接使用
3. 与其他 Do 动词命令（如 `click`）的行为一致：`click addButton` → `Target="addButton"`

---

## 5. 修改清单

| 文件 | 修改内容 | 优先级 | 影响范围 |
|------|---------|--------|---------|
| `BasicMethod/MetaCommand/Parsing.cs` | 优化 `ParseDoTargetAndParameters`，对 set 命令分离 target 和参数 | **高** | 所有 set 命令的解析结果 |
| `FileDriven/FileDriven.Pipeline/Skeletons/ParseCommandSkeleton.cs` | 确认已移除 `HandleSetValue`（当前已符合） | 低 | 无 |
| `FileDriven/FileDriven.Pipeline/Skeletons/FindBindingSkeleton.cs` | 确认 set 命令能正确查找绑定（当前已符合） | 低 | 无 |
| `FileDriven/FileDriven.Pipeline/Skeletons/ExecuteFunctionSkeleton.cs` | 确认 set 命令能正确执行（当前已符合） | 低 | 无 |
| `Share/FunctionStorage/UIToolbox.cs` 或 `MathLibrary.cs` | 确认 set 函数已注册 | 中 | set 命令的执行 |

---

## 6. 问题 3 补充：`IsNonEventCommand` 动词检查的设计修复

### 6.1 设计矛盾

`ParseCommandSkeleton.IsNonEventCommand()` 的原始实现（第 139-145 行）：

```csharp
private static bool IsNonEventCommand(MetaVerb verb, string? action)
{
    if (verb != MetaVerb.Do) return false;  // ★ 设计矛盾：非 Do 动词直接返回 false
    return FileDrivenPipelineContext.IsNonEventCommandAction(action);
}
```

**设计矛盾**：`look`/`wait`/`assert`/`discover` 命令的动词分别是 `MetaVerb.Look`/`MetaVerb.Wait`/`MetaVerb.Assert`，不是 `MetaVerb.Do`。但它们的 action 名称（`"look"`/`"wait"`/`"assert"`/`"discover"`）却在 `IsNonEventCommandAction` 列表中（`FileDrivenPipelineContext.cs` 第 297-299 行）。

这意味着：
- `look window` → `verb=Look, action=null` → `IsNonEventCommand` 返回 `false` ❌（应为 true）
- `wait 1000` → `verb=Wait, action=null` → `IsNonEventCommand` 返回 `false` ❌（应为 true）
- `assert controlName` → `verb=Assert, action=null` → `IsNonEventCommand` 返回 `false` ❌（应为 true）

### 6.2 根本修复

**已在 `ParseCommandSkeleton.cs` 第 139-150 行修复**：放宽 `IsNonEventCommand()` 的检查，对非 `Do` 动词也检查 verb 名称是否在非事件命令列表中。

```csharp
private static bool IsNonEventCommand(MetaVerb verb, string? action)
{
    if (verb == MetaVerb.Do)
    {
        // Do 动词：检查 action 是否在非事件命令列表中
        return FileDrivenPipelineContext.IsNonEventCommandAction(action);
    }

    // ★ 修复：非 Do 动词（Look/Wait/Assert）也检查是否在非事件命令列表中
    //   look/wait/assert/discover 的 verb 名称本身就是 action 名称
    return FileDrivenPipelineContext.IsNonEventCommandAction(verb.ToString().ToLowerInvariant());
}
```

### 6.3 修复验证

| 命令 | `verb` | `action` | 旧行为 | 新行为 |
|------|--------|---------|--------|--------|
| `look window` | `Look` | `null` | `false` ❌ | `IsNonEventCommandAction("look")` → `true` ✅ |
| `wait 1000` | `Wait` | `null` | `false` ❌ | `IsNonEventCommandAction("wait")` → `true` ✅ |
| `assert controlName` | `Assert` | `null` | `false` ❌ | `IsNonEventCommandAction("assert")` → `true` ✅ |
| `do click addButton` | `Do` | `"click"` | `IsNonEventCommandAction("click")` → `false` ✅ | `false` ✅ |
| `do set valueATextBox` | `Do` | `"set"` | `IsNonEventCommandAction("set")` → `false` ✅ | `false` ✅ |

### 6.4 `NonEventCommandHandler.Handle()` 的兼容性确认

`NonEventCommandHandler.Handle()` 中的 `switch(action)` 分支（第 90-155 行）已包含 `look`/`wait`/`assert`/`discover` 的处理：

```csharp
switch (action)
{
    case "look":
        HandleLook(context);
        break;
    case "wait":
        HandleWait(context);
        break;
    case "assert":
        HandleAssert(context);
        break;
    case "discover":
        HandleDiscover(context);
        break;
    // ... 其他命令
}
```

**注意**：`look`/`wait`/`assert`/`discover` 命令的 `ParsedAction` 为 `null`（因为非 Do 动词的 action 字段不会被设置），但 `NonEventCommandHandler.Handle()` 第 73 行检查 `action` 是否为空：

```csharp
var action = context.ParsedAction?.ToLowerInvariant();
if (string.IsNullOrEmpty(action))
{
    context.FunctionExecuteSuccess = false;
    context.FunctionErrorMessage = "Non-event command action is required";
    return;
}
```

**这会导致问题**：`look`/`wait`/`assert`/`discover` 的 `ParsedAction` 为 `null`，进入 `Handle()` 后立即返回错误。

### 6.5 配套修复：`NonEventCommandHandler.Handle()` 的 action 回退逻辑

**需要修改 `NonEventCommandHandler.Handle()`**，当 `ParsedAction` 为 `null` 时，回退到使用 `ParsedVerb` 的名称作为 action：

```csharp
public static void Handle(FileDrivenPipelineContext context)
{
    // ★ 修复：当 ParsedAction 为 null 时，回退到使用 ParsedVerb 的名称
    //   look/wait/assert/discover 等非 Do 动词的 ParsedAction 为 null
    var action = context.ParsedAction?.ToLowerInvariant()
        ?? context.ParsedVerb.ToString().ToLowerInvariant();
    
    if (string.IsNullOrEmpty(action))
    {
        context.FunctionExecuteSuccess = false;
        context.FunctionErrorMessage = "Non-event command action is required";
        return;
    }
    // ... 后续 switch 逻辑不变
}
```

### 6.6 修复总结

| 文件 | 修改内容 | 状态 |
|------|---------|------|
| `FileDriven/FileDriven.Pipeline/Skeletons/ParseCommandSkeleton.cs` | 放宽 `IsNonEventCommand()` 动词检查 | ✅ 已修复 |
| `FileDriven/FileDriven.Pipeline/Skeletons/NonEventCommandHandler.cs` | `Handle()` 中 `ParsedAction` 为 null 时回退到 `ParsedVerb` | ⚠️ 需要修复 |

---

## 7. 测试验证


修改后需要验证以下场景：

| 测试场景 | 命令 | 预期 `ParsedTarget` | 预期 `ParsedParameters` |
|---------|------|-------------------|----------------------|
| set 带数字值 | `set valueATextBox 42` | `"valueATextBox"` | `{"42": true}` |
| set 带字符串值 | `set workflowComboBox MathOps` | `"workflowComboBox"` | `{"MathOps": true}` |
| set 无值 | `set valueATextBox` | `"valueATextBox"` | `null` |
| set 带 key=value 参数 | `set valueATextBox value=42` | `"valueATextBox"` | `{"value": 42}` |
| set 非 Do 动词 | `do set valueATextBox 42` | `"valueATextBox"` | `{"42": true}` |
