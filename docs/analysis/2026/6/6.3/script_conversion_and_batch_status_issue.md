# JSON 脚本转换与批次状态问题分析

## 问题现象

运行 `math_functions_test.json` 测试脚本时：

1. **只执行了 3 条命令**（sleep, close, exit），正常应该是 20+ 条
2. **批次状态显示"运行中: 1"**，没有正确更新为"已完成"
3. 日志中只看到 3 条 `[OK]` 记录，前面的 template 命令全部丢失

## 调用链

```
math_functions_test.json
  → GuiModeRunner 检测到 .json 文件
    → ScriptConverter.ConvertJsonScriptToCommandFile()
      → FileDrivenPipelineFactory.Process(jsonContent)    ← 管线处理（展开 template）
        → ReferenceExpansionStage.Process()
          → CommandsHandler.Process()
            → ProcessItems() → ExpandItemTemplate()
              → TemplateResolver.Resolve()
                → "load-config-show" (type=command)     → 展开为 {action, modifier, target}
                → "echo-section" (type=command)         → 展开为 {action, target}
                → "math-operation" (type=command-group) → 展开为 __multi_command[]
                → "list-forms" (type=command)           → 展开为 {action}
      → ScriptToCommandConverter.Convert(processedJson)  ← 转换为纯文本命令
        → JsonSerializer.Deserialize<ScriptDefinition>()
        → ScriptDefinition.ToCommandText()
    → 返回临时命令文件路径
  → 执行临时命令文件中的命令
```

## 问题根因分析

### 问题一：命令丢失（只执行 3 条）

**直接原因**：`ScriptConverter.ConvertJsonScriptToCommandFile()` 中（第 76-79 行）：

```csharp
if (!result.IsSuccess || result.Data == null)
{
    System.Diagnostics.Debug.WriteLine($"[ScriptConverter] 处理失败: {result.Errors.GetErrorReport()}");
    return null;  // ← 返回 null，导致整个脚本转换失败
}
```

当 `FileDrivenPipelineFactory.Process()` 返回 `!IsSuccess` 时，`ScriptConverter` 返回 null，`ScriptLoadSkeleton` 标记加载失败，后续命令执行器只能执行原始的纯文本命令（sleep/close/exit）。

**管线失败的原因**：`TemplateResolver.Resolve()` 对未注册模板抛出 `KeyNotFoundException`，该异常被 `ReferenceExpansionStage.Process` 的 catch 块捕获后添加 `ErrorLevel.Error`，导致 `HasRealErrors = true`，管线返回 `Failed` 状态。

但 `math_functions_test.json` 中使用的模板（`load-config-show`、`echo-section`、`math-operation`、`list-forms`）都在 `composites.json` 中注册了，所以正常情况下管线应该成功。

**需要进一步排查**：管线处理 `math_functions_test.json` 时具体在哪个环节失败，以及 `result.Errors.GetErrorReport()` 输出的具体错误信息。

### 问题二：批次状态未更新（"运行中: 1"）

**直接原因**：`BatchManager` 没有正确标记批次为"已完成"。

**可能的原因**：
1. 脚本转换失败后，命令执行流程被中断，没有走到批次完成的更新逻辑
2. `BatchManager` 的批次状态更新逻辑存在条件遗漏
3. 异常路径下没有正确清理/更新批次状态

### 问题三：框架层面的设计问题

#### 3.1 `__multi_command` / `__multi_control` 内部标记

`TemplateResolver.Resolve()` 在展开 `command-group` 时使用 `__multi_command` 作为内部标记，这个标记只在 `ExpandItemTemplate()` 中被消费。这种"标记传递"模式增加了框架的耦合度：

- `TemplateResolver`（引用解析层）产生标记
- `ExpandItemTemplate`（管线处理层）消费标记
- 其他消费方（如 `ScriptToCommandConverter`）不知道这个标记的存在

#### 3.2 `ScriptConverter` 与 `FileDrivenPipelineFactory` 的职责边界

`ScriptConverter` 调用 `FileDrivenPipelineFactory.Process()` 处理 JSON 脚本，但管线处理后的 JSON 格式与 `ScriptToCommandConverter` 期望的格式不完全一致：

- 管线处理后的 `commands` 数组中，`command-group` 展开后的子项是 `{action, target, value}` 格式
- `ScriptCommand` 模型能识别这种格式
- 但如果管线失败，整个转换流程中断

#### 3.3 错误处理策略不一致

- `TemplateResolver.Resolve()`：未注册模板 → 抛 `KeyNotFoundException`
- `ProcessItems()`：未注册模板 → 添加 `Warning`（第 102-108 行）
- `ReferenceExpansionStage.Process()`：Handler 异常 → 添加 `Error`
- `ValidationStage`：未注册模板 → 根据 `StrictMode` 决定 Warning/Error

这种不一致导致同样的错误在不同阶段产生不同级别的错误，影响管线的最终状态。

## 需要进一步确认的信息

1. `FileDrivenPipelineFactory.Process()` 处理 `math_functions_test.json` 时 `result.Errors.GetErrorReport()` 的具体输出
2. `BatchManager` 的批次状态更新逻辑
3. 命令执行管线在脚本加载失败后的行为

## 相关文件

| 文件 | 作用 |
|------|------|
| `FileDriven/DrivenConfig/Scripts/Math/math_functions_test.json` | 测试脚本 |
| `FileDriven/DrivenConfig/TempletConfig/JsonConfig/composites.json` | 模板定义 |
| `FileDriven/FileDriven.Utilities/CLI/ScriptConverter.cs` | JSON 脚本转换器 |
| `FileDriven/FileDriven.Utilities/ScriptRunner/Processors/ScriptToCommandConverter.cs` | 命令文本转换器 |
| `FileDriven/FileDriven.Utilities/ScriptRunner/Core/Models/ScriptCommand.cs` | 命令模型 |
| `FileDriven/FileDriven.Utilities/ScriptRunner/Core/Models/ScriptDefinition.cs` | 脚本定义模型 |
| `FileDriven/FileDriven.JsonRefer/ReferenceProcessing/Pipeline.cs` | 管线 + Handler |
| `FileDriven/FileDriven.JsonRefer/ReferenceProcessing/Resolvers.cs` | 模板解析器 |
| `FileDriven/FileDriven.Pipeline/Skeletons/ScriptLoadSkeleton.cs` | 脚本加载骨架 |
| `FileDriven/FileDriven.Pipeline/Skeletons/NonEventCommandHandler.cs` | 命令处理器 |
| `FileDriven/FileDriven.Pipeline/Skeletons/ParseCommandSkeleton.cs` | 命令解析骨架 |
