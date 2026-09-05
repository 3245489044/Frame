# FileDriven.Pipeline 代码分析报告

## 一、编译错误（已修复）

### 1.1 StagePipelineSkeleton.cs — 缺少 using 指令
- **问题**：第 28 行使用 `CommandContext` 但缺少 `using CommandFramework.Models;`
- **修复**：添加 `using CommandFramework.Models;`

### 1.2 NonEventCommandHandler.cs — 错误的命名空间引用
- **问题**：第 29 行 `using FileDriven.UI.Utils.Helpers;` — 命名空间 `Helpers` 不存在
- **原因**：`ControlTreeWalker` 在 `FileDriven.UI.Utils` 命名空间下，没有 `.Helpers` 子命名空间
- **修复**：改为 `using FileDriven.UI.Utils;`

### 1.3 PipelineEntry.cs — 缺少 using 指令
- **问题**：第 382、606 行使用 `CommandContext` 但缺少 `using CommandFramework.Models;`
- **修复**：添加 `using CommandFramework.Models;`

---

## 二、废弃代码

### 2.1 _Obsolete/ 目录（5 个废弃 Skeleton 文件）
- **文件列表**：
  - `ExecuteFunctionSkeleton.cs`
  - `ExtractValuesSkeleton.cs`
  - `FindBindingSkeleton.cs`
  - `ParseCommandSkeleton.cs`
  - `WriteResultSkeleton.cs`
- **状态**：已被 `StagePipelineSkeleton.cs` 替代，未被任何代码引用
- **建议**：删除整个 `_Obsolete/` 目录

---

## 三、重复逻辑

### 3.1 NonEventCommandHandler.cs — HandleLook 与 HandleDiscover 高度相似
- **HandleLook**：获取配置数量 → 输出 "Loaded configs: ..." 或 "No configurations loaded."
- **HandleDiscover**：获取配置数量 → 输出 "Discovered X configurations" 或 "Discovered 0 configurations"
- **差异**：仅输出文本不同，逻辑完全一致
- **建议**：合并为一个方法，通过参数控制输出文本

### 3.2 NonEventCommandHandler.cs — HandleList 与 HandleValidate 高度相似
- **HandleList**：获取注册 Form 名称 → 输出 "Registered forms: ..." 或 "No forms registered."
- **HandleValidate**：获取注册 Form 名称 → 输出 "Validation passed: X form(s) registered." 或 "Validation: No forms registered."
- **差异**：仅输出文本不同，逻辑完全一致
- **建议**：合并为一个方法，通过参数控制输出文本

### 3.3 NonEventCommandHandler.cs — HandleSleep 与 HandleWait
- **HandleSleep**：解析 ms → Thread.Sleep → 设置结果
- **HandleWait**：调用 HandleSleep → 覆盖结果文本
- **问题**：HandleWait 调用 HandleSleep 后又覆盖 FunctionResultValue，导致 HandleSleep 的原始结果丢失
- **建议**：HandleWait 直接实现逻辑，不调用 HandleSleep

### 3.4 FileDrivenPipelineContext.cs — 便捷属性与 CommandContext 基类字段重复
- **问题**：`ParsedAction`、`ParsedTarget`、`ParsedParameters`、`NormalizedCommand`、`ParseSuccess`、`ParseErrorMessage`、`IsNonEventCommand` 等属性只是对 `ParsedCommand` 的映射
- **建议**：保留便捷属性（提高可读性），但统一使用 `ParsedCommand` 作为数据源

### 3.5 FileDrivenPipelineContext.cs — 执行状态字段与 CommandContext 基类重复
- **问题**：`BindingFound`、`BindingErrorMessage`、`ExtractSuccess`、`ExtractErrorMessage`、`FunctionExecuteSuccess`、`FunctionErrorMessage`、`WriteResultSuccess`、`WriteResultErrorMessage` 等字段在 `CommandContext` 基类中已有对应字段
- **建议**：删除这些冗余字段，直接使用基类字段

---

## 四、错误逻辑

### 4.1 NonEventCommandHandler.HandleClose — VirtualStore 污染
- **问题**：`HandleClose` 在关闭单个配置时，从 `LoadedConfigs` 中移除配置，并从 `VirtualStore` 中移除该配置的所有控件名。但 `context.LoadedConfigs` 是 `CreateContext()` 时从 `PipelineEntry._loadedConfigs` 浅拷贝的副本，而 `context.VirtualStore` 是独立创建的字典。
- **影响**：
  - 关闭配置后，`PipelineEntry._loadedConfigs` 中的配置不会被移除（因为是副本）
  - `VirtualStore` 中的控件名被移除，可能导致后续命令找不到控件
- **建议**：`HandleClose` 应通过 `PipelineEntry` 实例来关闭配置，而不是直接操作上下文的副本

### 4.2 NonEventCommandHandler.HandleClose — 关闭所有配置时清空 VirtualStore
- **问题**：当 `target` 为空时（关闭所有配置），`context.LoadedConfigs?.Clear()` 和 `context.VirtualStore?.Clear()` 被调用
- **影响**：`VirtualStore` 被清空，但 `VirtualStore` 中可能包含非配置相关的数据
- **建议**：关闭所有配置时，只清空 `LoadedConfigs`，不清空 `VirtualStore`

### 4.3 NonEventCommandHandler.HandleWait — 结果覆盖
- **问题**：`HandleWait` 调用 `HandleSleep(context)` 后，又覆盖 `context.FunctionResultValue`，导致 `HandleSleep` 设置的原始结果（如 "Slept 1000ms"）被覆盖为 "Waited 30000ms"
- **建议**：`HandleWait` 应直接实现等待逻辑，不调用 `HandleSleep`

### 4.4 NonEventCommandHandler.Handle — action 回退逻辑
- **问题**：当 `ParsedAction` 为 null 时，回退到 `ParsedVerb.ToString()`。但 `ParsedVerb` 的默认值是 `MetaVerb.Do`，转换为字符串后为 "Do"，这会导致进入 `default` 分支
- **建议**：当 `ParsedAction` 为 null 且 `ParsedVerb` 为默认值时，直接返回错误

### 4.5 PipelineEntry.CreateContext — VirtualStore 创建逻辑
- **问题**：`VirtualStore = targetForm == null ? new Dictionary<string, string?>() : null`
  - CLI 模式（targetForm == null）：创建 VirtualStore
  - GUI 模式（targetForm != null）：VirtualStore 为 null
- **影响**：GUI 模式下 `assert` 命令无法工作，因为它依赖 `VirtualStore`
- **建议**：GUI 模式也应创建 VirtualStore，或明确 GUI 模式下 assert 的行为

---

## 五、代码整理方案

### 阶段 0：修复编译错误 ✓（已完成）
1. `StagePipelineSkeleton.cs` — 添加 `using CommandFramework.Models;`
2. `NonEventCommandHandler.cs` — 修复命名空间引用
3. `PipelineEntry.cs` — 添加 `using CommandFramework.Models;`

### 阶段 1：清理废弃代码
1. 删除 `_Obsolete/` 目录及其所有文件

### 阶段 2：修复重构不完全的逻辑
1. `NonEventCommandHandler.cs` — 删除未使用的 `using` 指令
2. `PipelineEntry.cs` — 删除多余空行

### 阶段 3：消除重复逻辑
1. 合并 `HandleLook` 和 `HandleDiscover` 为 `HandleLookDiscover`
2. 合并 `HandleList` 和 `HandleValidate` 为 `HandleListValidate`
3. `HandleWait` 独立实现，不调用 `HandleSleep`

### 阶段 4：修复错误逻辑
1. `HandleClose` — 通过 `PipelineEntry` 实例关闭配置
2. `HandleClose` — 关闭所有配置时不清空 `VirtualStore`
3. `HandleWait` — 直接实现等待逻辑
4. `Handle` — 改进 action 回退逻辑
