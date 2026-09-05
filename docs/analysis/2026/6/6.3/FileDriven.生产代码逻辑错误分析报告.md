# FileDriven 生产代码逻辑错误分析报告

> 基于 `FileDriven.Tests` 覆盖分析报告，对 `FileDriven` 项目中的生产代码进行逻辑错误分析。
> 分析日期：2026-06-02

---

## 一、概述

通过对 `FileDriven.Tests` 测试套件（共 531 个测试）的覆盖分析，结合对生产代码的深入审查，发现以下逻辑错误。这些错误已通过测试验证确认。

---

## 二、已修复的逻辑错误

### 错误 1：`CommandNormalizer` 缺少 `look`/`wait`/`assert`/`discover` 命令注册

**严重程度**：高

**涉及文件**：`BasicMethod/MetaCommand/Parsing.cs`

**错误描述**：
`CommandNormalizer` 类中的 `ActionCommands` 集合缺少 `look`、`wait`、`assert`、`discover` 四个命令。当用户输入 `look`、`wait`、`assert`、`discover` 等简写命令时，`CommandNormalizer.Normalize()` 无法将其规范化为 `do look`、`do wait`、`do assert`、`do discover` 格式。

**影响范围**：
- `look` 命令：测试 `Look_NoLoadedConfigs_ShouldReturnEmpty`、`Look_WithLoadedConfigs_ShouldListNames` 失败
- `wait` 命令：测试 `Wait_ValidMs_ShouldSucceed`、`Wait_InvalidMs_ShouldUseDefault` 失败
- `assert` 命令：测试 `Assert_ExistingControl_ShouldPass`、`Assert_NonExistentControl_ShouldFail`、`Assert_EmptyTarget_ShouldFail` 失败
- `discover` 命令：测试 `Discover_NoConfigs_ShouldReturnZero`、`Discover_WithConfigs_ShouldReturnCount` 失败

**根本原因**：
`CommandNormalizer` 的 `ActionCommands` 集合是命令规范化的核心配置。当用户输入 `look` 时，规范化器应将其转换为 `do look`，然后 `ParseCommandSkeleton` 才能识别其为非事件命令。缺少注册导致这些命令被当作未知命令处理，`IsNonEventCommand` 为 `false`，后续的 `HandleLook`/`HandleWait`/`HandleAssert`/`HandleDiscover` 方法无法被调用。

**修复方式**：
在 `ActionCommands` 集合中添加 `"look"`、`"wait"`、`"assert"`、`"discover"` 四个命令。

```csharp
private static readonly HashSet<string> ActionCommands = new(StringComparer.OrdinalIgnoreCase)
{
    "load", "set", "click", "echo", "close", "show", "list", "sleep", "validate",
    "log", "look", "wait", "assert", "discover"
};
```

---

### 错误 2：`echo` 命令的 `ParsedTarget` 为 `null` 时返回 `null`

**严重程度**：中

**涉及文件**：`FileDriven/FileDriven.Pipeline/Skeletons/NonEventCommandHandler.cs`

**错误描述**：
当 `echo` 命令没有目标文本时（如 `echo`），`context.ParsedTarget` 为 `null`，但 `context.FunctionResultValue` 直接赋值为 `context.ParsedTarget`，导致返回 `null` 而非空字符串。

**影响范围**：
- 测试 `Echo_EmptyTarget_ShouldReturnEmpty` 期望返回 `""`（空字符串），但实际返回 `null`

**根本原因**：
`Handle` 方法中 `echo` 分支的代码为：
```csharp
context.FunctionResultValue = context.ParsedTarget;
```
当 `ParsedTarget` 为 `null` 时，`FunctionResultValue` 也被设置为 `null`，违反了调用方期望返回空字符串的约定。

**修复方式**：
使用 null 合并运算符确保返回空字符串：
```csharp
context.FunctionResultValue = context.ParsedTarget ?? string.Empty;
```

---

### 错误 3：`echo` 命令的 `ParsedTarget` 只取第一个单词

**严重程度**：中

**涉及文件**：`FileDriven/FileDriven.Pipeline/Skeletons/NonEventCommandHandler.cs`

**错误描述**：
当 `echo` 命令包含多个单词时（如 `echo hello world`），`MetaCommand.MetaCommand.Parse()` 将第一个单词 `hello` 解析为 `ParsedTarget`，将后续单词 `world` 解析为 `ParsedParameters` 中的键（值为 `true`）。但 `echo` 分支只使用了 `ParsedTarget`，忽略了 `ParsedParameters` 中的额外单词。

**影响范围**：
- 测试 `Echo_SimpleText_ShouldReturnTarget` 期望返回 `"hello world"`，但实际返回 `"hello"`

**根本原因**：
`MetaCommand.Parse()` 的 `ParseAsDoCommand` 方法将 `tokens[1]` 作为 `Target`，`tokens[2..]` 中不含 `=` 的 token 被当作参数（值为 `true`）。对于 `echo` 命令，所有后续 token 都应是目标文本的一部分，不应被解析为参数。

**修复方式**：
在 `echo` 分支中，将 `ParsedParameters` 中值为 `true` 的键（即不含 `=` 的 token）合并到目标文本中：
```csharp
var targetText = context.ParsedTarget ?? string.Empty;
if (context.ParsedParameters != null && context.ParsedParameters.Count > 0)
{
    var extraParts = context.ParsedParameters
        .Where(kvp => kvp.Value is bool b && b)
        .Select(kvp => kvp.Key);
    if (extraParts.Any())
    {
        targetText = string.IsNullOrEmpty(targetText)
            ? string.Join(" ", extraParts)
            : targetText + " " + string.Join(" ", extraParts);
    }
}
context.FunctionResultValue = targetText;
```

---

## 三、预先存在的失败测试（非本次修改引入）

以下 2 个测试在修改前就已失败，不属于本次修复范围：

### 测试 1：`ResetCurrentCommand_ShouldResetAllFields`

- **文件**：`FileDrivenPipelineContextTests.cs`
- **错误**：`CurrentCommandLineNumber` 未在 `ResetCurrentCommand()` 中重置为 0
- **原因**：`ResetCurrentCommand()` 方法缺少 `CurrentCommandLineNumber = 0;` 语句

### 测试 2：`Parse_SetCommand_WithoutValue_ShouldFail`

- **文件**：`ParseCommandSkeletonTests.cs`
- **错误**：测试主机进程崩溃
- **原因**：`set` 命令不带值时，`ParseCommandSkeleton` 未正确处理，导致后续 Skeleton 阶段崩溃

---

## 四、修复验证结果

| 测试类别 | 总数 | 通过 | 失败 | 说明 |
|---------|------|------|------|------|
| NonEventCommandHandlerTests | 27 | 27 | 0 | 全部通过 ✅ |
| 全部 FileDriven.Tests | 531 | 529 | 2 | 2 个预先存在的失败 |

修复后，`NonEventCommandHandlerTests` 的 27 个测试全部通过，且未引入新的回归问题。

---

## 五、总结

本次分析发现并修复了 3 个生产代码逻辑错误：

1. **`CommandNormalizer` 命令注册遗漏**：缺少 `look`/`wait`/`assert`/`discover` 四个命令，导致这些非事件命令无法被正确识别和处理。
2. **`echo` 命令 null 处理**：`ParsedTarget` 为 `null` 时返回 `null` 而非空字符串。
3. **`echo` 命令多单词处理**：`ParsedTarget` 只取第一个单词，忽略了后续单词。

这些错误的共同特征是：**生产代码与测试代码对命令解析行为的理解不一致**。测试期望 `echo hello world` 返回完整文本，但生产代码的解析器将多单词目标拆分为 target 和 parameters，导致信息丢失。修复方案在 `NonEventCommandHandler` 层面进行了补偿，将参数中无值的键合并回目标文本。
