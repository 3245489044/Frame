# GUI 模式测试卡住问题分析与日志补充方案

## 一、问题现象

运行 `math_functions_test.json` 测试脚本时，GUI 模式启动后**立即关闭**，没有执行任何实际的测试命令。

关键日志序列：

```
[08:42:36.971] [INFO] [FileDriven] Detected JSON script file, converting to command file...
[08:42:37.014] [INFO] [FileDriven] Shutting down framework...
```

从 `Detected JSON script file` 到 `Shutting down framework` 仅间隔 **43ms**，说明脚本转换后没有任何命令被执行，管线直接结束。

---

## 二、根因分析

### 2.1 执行流程

```
GuiModeRunner.Run()
  → InitializationCompleted 事件触发
  → GuiPipelineRunner.RunAsync()
    → PipelineEntry.CreateFileDrivenPipeline()
      → ScriptLoadSkeleton 调用 ScriptConverter.ConvertJsonScriptToCommandFile()
        → JsonPipelineRegistry.Process() 展开 template 引用
        → 管线处理失败（result.IsSuccess == false）
        → 降级路径：尝试使用原始 JSON 直接转换
        → 降级失败：原始 JSON 包含 template 引用，无法展开
        → 返回 null
      → ScriptLoadSkeleton 设置 ScriptLoadSuccess = false
    → PipelineEntry.itemsSelector 检查 ScriptLoadSuccess == false
    → 返回空数组，没有任何命令被执行
  → GuiModeRunner 中 AutoClose == true
  → 立即调用 Application.Exit()
```

### 2.2 根因

**`ScriptConverter` 的管线处理失败和降级失败** 之前只使用了 `System.Diagnostics.Debug.WriteLine` 输出，**没有通过 `FileDrivenLogPlaceholderHandler.Log` 输出到统一日志系统**，导致用户看不到任何错误信息。

---

## 三、补充的日志点

在以下 **5 个关键位置** 添加了日志输出，确保脚本转换失败的全链路信息可见：

### 位置 1：`ScriptConverter.cs` — 管线处理失败详情

**文件：** `FileDriven/FileDriven.Utilities/CLI/ScriptConverter.cs`（第 85 行）

```csharp
var errorReport = result.Errors?.GetErrorReport() ?? "(无错误详情)";
LogPlaceholderHandlerBase.Log("ScriptConverter_Pipeline_Failed", "FileDriven", "filedriven:", new { errorReport });
```

**输出示例：**
```
[ERROR] [FileDriven] [ScriptConverter] 管线处理失败，尝试降级使用原始 JSON:
  Error Report (2 entries):
    Fatal: 0, Error: 2, Warning: 0, Info: 0
    [Error] STAGE_TEMPLATEEXPAND_FAILED: Stage 'TemplateExpand' 处理失败，返回 null
    [Error] STAGE_STYLEAPPLY_FAILED: Stage 'StyleApply' 处理失败，返回 null
```

### 位置 2：`ScriptConverter.cs` — 降级失败详情

**文件：** `FileDriven/FileDriven.Utilities/CLI/ScriptConverter.cs`（第 106 行）

```csharp
try { LogPlaceholderHandlerBase.Log("ScriptConverter_Fallback_Failed", "FileDriven", "filedriven:", new { errorReport }); } catch { }
```

**输出示例：**
```
[ERROR] [FileDriven] [ScriptConverter] 降级失败：原始 JSON 包含 template 引用，
  降级路径无法展开。请检查管线处理失败原因:
  Error Report (2 entries): ...
```

### 位置 3：`ScriptLoadSkeleton.cs` — 转换失败日志

**文件：** `FileDriven/FileDriven.Pipeline/Skeletons/ScriptLoadSkeleton.cs`（第 51 行）

```csharp
LogPlaceholderHandlerBase.Log("ScriptLoadSkeleton_Convert_Failed", "FileDriven", "filedriven:", new { scriptPath = input.ScriptPath });
```

**输出示例：**
```
[ERROR] [FileDriven] 脚本转换失败: D:\...\math_functions_test.json
```

### 位置 4：`PipelineEntry.cs` — 脚本加载失败日志

**文件：** `FileDriven/FileDriven.Pipeline/PipelineEntry.cs`（第 96-98 行）

```csharp
FileDrivenLogPlaceholderHandler.Log("PipelineEntry_ScriptLoad_Failed",
    new { scriptPath = ctx.ScriptPath ?? "(unknown)", errorMessage = ctx.ScriptLoadErrorMessage ?? "无错误信息" });
```

**输出示例：**
```
[ERROR] [FileDriven] 脚本加载失败，无命令可执行:
  D:\...\math_functions_test.json, 错误: Failed to convert JSON script: ...
```

### 位置 5：`GuiModeRunner.cs` — 脚本执行摘要

**文件：** `FileDriven/FileDriven.Launcher/GuiModeRunner.cs`（第 134-135 行）

```csharp
FileDrivenLogPlaceholderHandler.Log("GuiModeRunner_Run_Summary",
    new { totalCommands = totalResults, successCount = successResults, failCount = failResults });
```

**输出示例：**
```
[INFO] [FileDriven] 脚本执行摘要: 总命令=0, 成功=0, 失败=0
```

---

## 四、修改的文件清单

| 文件 | 修改内容 | 行号 |
|------|---------|------|
| `FileDriven/FileDriven.Utilities/CLI/ScriptConverter.cs` | 管线处理失败时输出 `ScriptConverter_Pipeline_Failed` | 85 |
| `FileDriven/FileDriven.Utilities/CLI/ScriptConverter.cs` | 降级失败时输出 `ScriptConverter_Fallback_Failed`（try-catch 包裹） | 106 |
| `FileDriven/FileDriven.Utilities/CLI/ScriptConverter.cs` | 降级成功时输出 `ScriptConverter_Fallback_Success`（try-catch 包裹） | 99 |
| `FileDriven/FileDriven.Pipeline/Skeletons/ScriptLoadSkeleton.cs` | 转换失败时输出 `ScriptLoadSkeleton_Convert_Failed` | 51 |
| `FileDriven/FileDriven.Pipeline/PipelineEntry.cs` | 脚本加载失败时输出 `PipelineEntry_ScriptLoad_Failed` | 96-98 |
| `FileDriven/FileDriven.Launcher/GuiModeRunner.cs` | AutoClose 前输出执行摘要 `GuiModeRunner_Run_Summary` | 134-135 |
| `FileDriven/filedriven_log_messages.json` | 添加所有新日志消息的定义 | 1089-1138 |

---

## 五、日志消息定义

所有新增的日志消息定义位于 `FileDriven/filedriven_log_messages.json`：

```json
"ScriptConverter_Pipeline_Failed": {
  "level": "Error",
  "category": "FileDriven_General",
  "template": "[ScriptConverter] 管线处理失败，尝试降级使用原始 JSON: {errorReport}"
},
"ScriptConverter_Fallback_Success": {
  "level": "Warning",
  "category": "FileDriven_General",
  "template": "[ScriptConverter] 降级成功，已生成命令文件: {tempFilePath}"
},
"ScriptConverter_Fallback_Failed": {
  "level": "Error",
  "category": "FileDriven_General",
  "template": "[ScriptConverter] 降级失败：原始 JSON 包含 template 引用，降级路径无法展开。请检查管线处理失败原因: {errorReport}"
},
"ScriptLoadSkeleton_Convert_Failed": {
  "level": "Error",
  "category": "FileDriven_General",
  "template": "脚本转换失败: {scriptPath}"
},
"PipelineEntry_ScriptLoad_Failed": {
  "level": "Error",
  "category": "FileDriven_General",
  "template": "脚本加载失败，无命令可执行: {scriptPath}, 错误: {errorMessage}"
},
"GuiModeRunner_Run_Summary": {
  "level": "Info",
  "category": "Launcher_Lifecycle",
  "template": "脚本执行摘要: 总命令={totalCommands}, 成功={successCount}, 失败={failCount}"
}
```

---

## 六、预期效果

添加以上日志后，当再次运行测试时，日志系统会输出类似以下内容：

```
[ERROR] [FileDriven] [ScriptConverter] 管线处理失败，尝试降级使用原始 JSON:
  Error Report (2 entries):
    Fatal: 0, Error: 2, Warning: 0, Info: 0
    [Error] STAGE_TEMPLATEEXPAND_FAILED: Stage 'TemplateExpand' 处理失败，返回 null
    [Error] STAGE_STYLEAPPLY_FAILED: Stage 'StyleApply' 处理失败，返回 null

[ERROR] [FileDriven] [ScriptConverter] 降级失败：原始 JSON 包含 template 引用，
  降级路径无法展开。请检查管线处理失败原因: ...

[ERROR] [FileDriven] 脚本转换失败: D:\...\math_functions_test.json

[ERROR] [FileDriven] 脚本加载失败，无命令可执行:
  D:\...\math_functions_test.json, 错误: Failed to convert JSON script: ...

[INFO] [FileDriven] 脚本执行摘要: 总命令=0, 成功=0, 失败=0
```

这样就能精确定位到**模板展开失败**是导致测试立即关闭的根因。

---

## 七、后续修复方向

当前问题根因是 `math_functions_test.json` 中使用了 `template` 字段（如 `"template": "load-config-show"`、`"template": "math-operation"`），但 `JsonPipelineRegistry` 的模板展开阶段（`TemplateExpand`）处理失败。可能的修复方向：

1. **检查模板注册**：确认 `Scripts` 目录下的模板文件（`styles.json`、`composites.json`）是否正确加载
2. **检查模板引用路径**：确认 `math_functions_test.json` 中引用的模板名称与模板定义中的名称一致
3. **增强降级路径**：在降级路径中实现简单的 template 展开逻辑，而非完全依赖 `JsonPipelineRegistry`
