# FrontEndDriven 剩余问题清单

> **分析日期**：2026-07-31
> **项目**：`CsharpPrj/Operation/FrontEndDriven`
> **状态**：已完成第一批修复（编译通过，0 错误 0 警告）；本文档记录剩余未解决问题，供后续迭代修复。
> **设计约束**：遵循 `.clinerules/` 分层准则——L1 修改需 ≥2 验证者签名共识；L3 参数运行时可变。

---

## 一、已完成修复记录（2026-07-31）

| 编号 | 修复项 | 变更文件 | 状态 |
|------|--------|----------|------|
| B2 | `ControlConfigTarget.GetFieldValue` 强制要求 ExtensionData 导致 dock/anchor 契约校验失效 | `Shared/ControlConfigTarget.cs` | ✅ 已修复 |
| R1 | `UnifiedControlService.ResolveDependency<T>` 零调用方死代码 | `WinForms/Services/UnifiedControlService.cs` | ✅ 已删除 |
| R2 | `LoadAndRenderOperation` 零调用方且 EcsGate 无 dispatch case | `Gate/EcsOperation.cs` | ✅ 已删除 |
| R3 | `ApplyDataChangeOperation` 零调用方且 EcsGate 无 dispatch case | `Gate/EcsOperation.cs` | ✅ 已删除 |
| R4 | `ExecuteFunctionOperation.PollIntervalMs` 废弃属性（Reactor 推送已零轮询） | `Gate/EcsOperation.cs` | ✅ 已删除 |
| B4 | `ExecuteFunctionOperation` XML 注释与实现不一致（声称轮询，实际 Reactor 推送） | `Gate/EcsOperation.cs` | ✅ 注释已更新 |
| R6 | `HandlerReentrancyGuard` [Obsolete] + 零引用死代码 | `CommandExecution/Context/HandlerReentrancyGuard.cs` | ✅ 已删除文件 |
| B7 | `RenderPipelineComposition.MarkDirty()` 空 lock 块 + `_layoutLock` 未使用字段 | `Initial/RenderPipeline/RenderPipelineComposition.cs` | ✅ 已删除 |
| R7 | `WinFormsUIAdapter.DetermineNodeType` 未使用 private 方法 | `WinForms/Adapter/WinFormsUIAdapter.cs` | ✅ 已删除 |
| D1 | `EcsInputPort.EnqueueExecution` / `EnqueueExecutionWithResult` 重复实现 | `Ports.cs` | ✅ 已合并（WithResult 委托至 EnqueueExecution，返回 Entity） |
| R5 | `PipelineContextUtility` [Obsolete] 中间层 | `CommandExecution/Context/PipelineContextUtility.cs` | ✅ 已内联至 `CommandPipelineContext` 并删除文件 |

## 二、剩余问题详细清单

### P0 — 错误逻辑（建议优先修复）

#### B1：12 处跨项目命名空间错位（架构隐患）

**位置**：以下文件声明在 `FileDriven.*` / `FileDrivenFramework.*` 命名空间下，但物理位于 FrontEndDriven 项目：

| 文件 | 当前命名空间 | 应属命名空间 |
|------|-------------|-------------|
| `Shared/ContractValidator.cs` | `FileDriven.Pipeline.Contracts` | `FrontEndDriven.Shared` |
| `Shared/ControlConfigTarget.cs` | `FileDriven.Pipeline.Contracts` | `FrontEndDriven.Shared` |
| `Shared/ConfigLoadInput.cs` | `FileDriven.Pipeline.Core` | `FrontEndDriven.Shared` |
| `Shared/LoadedConfigInfo.cs` | `FileDriven.Pipeline.Core` | `FrontEndDriven.Shared` |
| `Shared/EventHelper.cs` | `FileDriven.Abstractions` | `FrontEndDriven.Shared` |
| `Shared/Diagnostics/PipelineLog.cs` | `FileDriven.Pipeline.Diagnostics` | `FrontEndDriven.Diagnostics` |
| `Shared/JsonProcessingOptions.cs` | `FileDriven.JsonRefer.JsonProcessing` | `FrontEndDriven.Shared` |
| `Shared/Utils/JsonNodeExtensions.cs` | `FileDriven.Utilities.Extensions` | `FrontEndDriven.Shared.Utils` |
| `CommandExecution/Diagnostics/ExecutionLog.cs` | `FileDriven.Execution.Diagnostics` | `FrontEndDriven.Execution.Diagnostics` |
| `CommandExecution/Core/TemplatePreprocessSkeleton.cs` | `FileDriven.UI.Layout.Skeletons` | `FrontEndDriven.CommandExecution.Core` |
| `WinForms/UIInspector.cs` | `FileDriven.UI.WinForms.Inspection` | `FrontEndDriven.WinForms.Inspection` |
| `WinForms/LayoutJsonExporter.cs` | `FileDriven.UI.WinForms.Export` | `FrontEndDriven.WinForms.Export` |
| `Shared/Abstractions/CommonAbstractions.cs` | `FileDrivenFramework.Abstractions` | `FrontEndDriven.Abstractions` |
| `Shared/Abstractions/JsonProcessingAbstractions.cs` | `FileDrivenFramework.Abstractions` | `FrontEndDriven.Abstractions` |
| `Shared/Abstractions/WorkflowExecutionAbstractions.cs` | `FileDrivenFramework.Abstractions` | `FrontEndDriven.Abstractions` |

**现状**：编译通过（0 错误），但造成跨项目命名空间污染——引用方难以判断类型归属，且与 FileDriven 项目类型混淆。

**风险**：
1. `FileDriven` 项目后续若在同命名空间声明同类型名 → 编译冲突（ambiguous reference）
2. 维护者 `using FileDriven.Pipeline.Contracts` 可能意外引用到 FrontEndDriven 的类型
3. `EventHelper.CommonOptionalFieldsWithEvents` 是契约体系核心常量，却声明在 `FileDriven.Abstractions` —— 语义严重错位

**建议方案**：
1. 逐个文件修改 `namespace` 声明为 FrontEndDriven 对应命名空间
2. 跨仓库搜索所有 `using` 引用方（编译器的 CS0246/CS0103 错误会列出全部受影响位置）
3. 对引用方补充/修正 `using`（可先加别名 `using X = FileDriven.Abstractions.EventHelper` 过渡）
4. 删减 `FrontEndDriven.csproj` 中多余的 `using FileDriven.*` 引用（如有）

**工作量**：中等（约 15 处命名空间声明 + 若干引用方同步）

---

### P0 — 错误逻辑

#### B3：`EcsConfigContainer.GetRootEntity()` 恒返 `Entity.Invalid`

**位置**：`ControlLookup/Container/EcsConfigContainer.cs`（第 94-97 行）

**现状**：
```csharp
public class EcsConfigContainer : EcsContainer, IEntityTreeRoot
{
    public override Entity GetRootEntity()
    {
        return Entity.Invalid;   // 恒返 Invalid！
    }
}
```

**根因**：2026-07-25 重构将管线职责剥离给 `EcsContainerCore` 后，`EcsConfigContainer` 不再维护 root Entity；
但 `ContainerView`（`WinForms/Container/ContainerControl.cs`）未实现 `IEntityTreeRoot` 接口，
导致 `ControlRegistryAwareFinder` 中 `form is IEntityTreeRoot root` 匹配到 `EcsConfigContainer` 时，
`root.GetRootEntity()` 返回 Invalid → Entity 树备用路径必然失败。

**风险**：
1. **接口契约被破坏**——实现 `IEntityTreeRoot` 却永远返回 Invalid，违反接口语义
2. 依赖 `GetRootEntity()` 的调用链（`ControlRegistryAwareFinder.FindByName` / `FindInForm`）在备用路径静默失败
3. 当前仅靠 `EntityTreeService` 的 ControlPool O(1) 主路径兜底，掩盖了问题

**建议方案**：
- **选项 A（推荐）**：让 `ContainerView` 实现 `IEntityTreeRoot`，持有真实 root Entity（由 `EcsContainerCore` 注入）
- **选项 B**：`EcsConfigContainer` 移除 `IEntityTreeRoot` 实现，并让 `ControlRegistryAwareFinder` 改为从 `EcsContainerCore` 获取 root Entity
- **选项 C（最小改动）**：`EcsConfigContainer.GetRootEntity()` 改为接口显式实现（`Entity IEntityTreeRoot.GetRootEntity()`），并注释说明该实现仅占位；调用方在 root Invalid 时直接走主路径

**工作量**：中等（涉及 `EcsContainerCore` / `ContainerView` / `ControlRegistryAwareFinder` 联动）

---

### P1 — 冗余逻辑

#### B6：`ConfigLoadInput` 同名类结构不一致

**位置**：
- `FrontEndDriven/Shared/ConfigLoadInput.cs` — `namespace FileDriven.Pipeline.Core`，含 `LoadedConfigs` 属性
- `FileDriven/FileDriven.JsonRefer/LayoutJson/Models/ConfigLoadInput.cs` — `namespace FileDriven.JsonRefer.LayoutJson.Models`，含 `Target` 属性

**现状**：两个同名类在不同命名空间下结构完全不同（`LoadedConfigs` vs `Target`）。引用方稍不留意就会 `using` 错命名空间使用错误类型。

**风险**：
1. 编译期不报错（命名空间不同），但运行时可能序列化字段缺失
2. 维护者认知混乱——搜索 `ConfigLoadInput` 出现两个不相关定义

**建议方案**：
- 将 FrontEndDriven 的 `ConfigLoadInput` 重命名为 `FrontEndDrivenConfigLoadInput`（或随 B1 统一命名空间后保留原名但与 FileDriven 区分）
- 验证 `CommandInputPort.LoadScript` 等引用方

**工作量**：小

---

### P2 — 重复逻辑

#### D2：三个日志类样板重复

**位置**：
- `Shared/Diagnostics/FrontEndDrivenLog.cs`（`FrontEndDriven.Diagnostics`）
- `Shared/Diagnostics/PipelineLog.cs`（`FileDriven.Pipeline.Diagnostics`）
- `CommandExecution/Diagnostics/ExecutionLog.cs`（`FileDriven.Execution.Diagnostics`）

**现状**：三个类结构完全相同——均为 `LogPlaceholderHandlerBase` 的静态包装，仅 ModuleName/KeyPrefix 不同：

| 类 | ModuleName | KeyPrefix |
|----|-----------|-----------|
| FrontEndDrivenLog | "FrontEndDriven" | "fde:" |
| PipelineLog | "FileDriven.Pipeline" | "pipe:" |
| ExecutionLog | "FileDriven.Execution" | "exec:" |

**建议方案**：
- 提取统一泛型门面：`public static class PlaceholderLog<TModule>` 或单例工厂
- 三个类简化为一行别名：`public static class FrontEndDrivenLog => PlaceholderLog.FrontEndDriven`（或保留静态转发）
- 注意 `FrontEndDrivenLog.CleanMessageId` 的特殊逻辑（去前缀）需保持一致

**工作量**：中等

---

#### D3：两套 Entity 树查找机制并存 ✅ 已修复（2026-07-31）

**位置**：
- `WinForms/Services/EntityTreeService.cs` — 字典缓存 O(1) 查找（主链，`ControlRegistryAwareFinder` 使用）
- ~~`ControlLookup/EcsEntityTreeWalker.cs`~~ — 已删除

**修复方案**：
- 调研确认 `IEntityTreeWalker` / `EcsEntityTreeWalker` **零调用方**（仅接口定义 + 实现），属于死代码而非重复逻辑
- **直接删除** `ControlLookup/IEntityTreeWalker.cs` 与 `ControlLookup/EcsEntityTreeWalker.cs` 两个文件
- `EntityTreeService` + `ControlPool` 已覆盖全部查找场景（名称→Entity→Control），无需备用链
- 若未来需要"按 Entity 层级遍历"，可重新引入简单实现（Entity 数量少，全量扫描即可）

**工作量**：小（死代码删除）

---

#### D4：`EcsContainerCore` 与 `EcsConfigContainer` 双重容器列表 ✅ 已修复（2026-07-31）

**位置**：
- `ControlLookup/Container/EcsContainerCore.cs` — `_subContainers` 列表 + `RegisterSubContainer/UnregisterSubContainer/GetAllContainers`
- `ControlLookup/Container/EcsConfigContainer.cs` — ~~`_subContainers` 列表~~（已删除）

**修复方案**：
- 调研确认 `EcsConfigContainer.SubContainers` / `RegisterSubContainer` / `UnregisterSubContainer` **零调用方**
- **删除 `EcsConfigContainer` 中重复的子容器列表**：`_subContainers` 字段、`SubContainers` 属性、`RegisterSubContainer` / `UnregisterSubContainer` 方法、`ClearContainerContent` / `Dispose` 中的 `_subContainers.Clear()`
- `EcsConfigContainer` 仅保留 WinForms `Controls.Add` / `Controls.Clear` 原生控件树能力
- `EcsContainerCore` 的子容器列表保留（纯业务 API，未来可能复用）

**工作量**：小（死代码删除）

---

#### D5：`EcsGate.AuditLogInterceptor` 三方法重复 try-catch 模式

**位置**：`Gate/EcsGate.cs`（第 249-308 行）

**现状**：`OnBefore` / `OnAfter` / `OnError` 三个方法中均含：
```csharp
try { File.AppendAllText(_path, JsonSerializer.Serialize(new { ... }) + Environment.NewLine); }
catch { }
```
仅序列化对象字段不同。

**建议方案**：
```csharp
private void WriteEntry(string stage, object payload)
{
    try
    {
        var e = new { s = Interlocked.Increment(ref _seq), n = ..., p = stage, ... };
        File.AppendAllText(_path, JsonSerializer.Serialize(e) + Environment.NewLine);
    }
    catch { }
}
```
三个入口方法各自组装 payload 后调用 `WriteEntry`。

**工作量**：小

---

#### D6：`TemplatePreprocessSkeleton` 同步/异步重复 + 死锁风险

**位置**：`CommandExecution/Core/TemplatePreprocessSkeleton.cs`

**现状**：
- `ExecuteCore`（同步）与 `ExecuteCoreAsync`（异步）代码几乎相同
- 同步版本调用 `_stage.ExecuteAsync(session).GetAwaiter().GetResult()` —— 在 UI 上下文或同步上下文中调用时无 `ConfigureAwait(false)` 保护，存在死锁风险

**建议方案**：
- 同步版本应加 `ConfigureAwait(false)`（`_stage.ExecuteAsync(session).ConfigureAwait(false).GetAwaiter().GetResult()`）
- 或让 `ExecuteCore` 直接委托缓存同步执行路径
- 或标记同步版本 `[Obsolete]` 引导使用异步版本

**工作量**：小

---

### P3 — 冗余清理

#### R8：运行时数据文件存在于源码目录

**位置**：`CommandExecution/Diagnostics/execution_log_messages.json`

**现状**：运行时生成的日志消息映射文件被提交到源码目录。

**建议方案**：
- 移入 `.gitignore` 或 `DiagnosticsConfig/` 模板目录
- 确保 `ExecutionLog.Initialize` 从配置目录读取

**工作量**：小

---

#### R9：`ValidateSystem.ConvertToInt` 与 `TypeConversionHelper` 功能重叠

**位置**：
- `CommandExecution/ECS/CommandSystems.cs`（`ValidateSystem.ConvertToInt`，第 842-850 行）
- `Shared/Utils/TypeConversionHelper.cs`（`TryConvert<int>`）

**现状**：`ConvertToInt` 手动处理 int/long/double，`TypeConversionHelper.TryConvert<int>` 使用 `TypeDescriptor.GetConverter` 实现相同目标，但 TryConvert 的转换器回退路径对 WinForms 值更健壮。

**建议方案**：
- `ValidateSystem` 改为调用 `TypeConversionHelper.TryConvert<int>(value, out var parsed)`
- 若转换器路径不支持 double 直接转 int（走 `CanConvertFrom`），保留本地 fallback 并注释说明差异

**工作量**：小

---

## 三、修复优先级汇总

| 优先级 | 编号 | 问题 | 工作量 | 影响面 |
|--------|------|------|--------|--------|
| **P0** | B1 | 15 处跨项目命名空间错位 | 中等 | 架构级，需同步所有引用方 |
| **P0** | B3 | `GetRootEntity()` 恒返 Invalid | 中等 | `ControlRegistryAwareFinder` 备用查找路径 |
| **P1** | B6 | `ConfigLoadInput` 同名类冲突 | 小 | 维护认知 |
| **P2** | D2 | 三个日志类样板重复 | 中等 | 全部日志调用 |
| **P2** | D3 | 两套 Entity 树查找 | 较大 | 查找链路 |
| **P2** | D4 | 双重容器列表 | 中等 | 子容器管理 |
| **P2** | D5 | `AuditLogInterceptor` 三方法重复 | 小 | ✅ 已修复（2026-07-31 提取 WriteEntry） |
| **P2** | D6 | `TemplatePreprocessSkeleton` 同步/异步重复 | 小 | 模板预处理 |
| **P3** | R8 | 运行时 JSON 在源码目录 | 小 | 仓库整洁 |
| **P3** | R9 | `ConvertToInt` 与类型转换工具重叠 | 小 | ValidateSystem |

---

## 四、参考资料

### 涉及源文件索引

| 问题 | 文件路径 |
|------|----------|
| B1 | `Shared/ContractValidator.cs`、`Shared/ControlConfigTarget.cs`、`Shared/ConfigLoadInput.cs`、`Shared/LoadedConfigInfo.cs`、`Shared/EventHelper.cs`、`Shared/Diagnostics/PipelineLog.cs`、`Shared/JsonProcessingOptions.cs`、`Shared/Utils/JsonNodeExtensions.cs`、`CommandExecution/Diagnostics/ExecutionLog.cs`、`CommandExecution/Core/TemplatePreprocessSkeleton.cs`、`WinForms/UIInspector.cs`、`WinForms/LayoutJsonExporter.cs`、`Shared/Abstractions/*.cs` |
| B3 | `ControlLookup/Container/EcsConfigContainer.cs`、`WinForms/Container/ContainerControl.cs`、`WinForms/Finder/ControlRegistryAwareFinder.cs` |
| B6 | `Shared/ConfigLoadInput.cs`、`FileDriven/FileDriven.JsonRefer/LayoutJson/Models/ConfigLoadInput.cs` |
| D2 | `Shared/Diagnostics/FrontEndDrivenLog.cs`、`Shared/Diagnostics/PipelineLog.cs`、`CommandExecution/Diagnostics/ExecutionLog.cs` |
| D3 | `WinForms/Services/EntityTreeService.cs`、`ControlLookup/EcsEntityTreeWalker.cs`、`ControlLookup/IEntityTreeWalker.cs` |
| D4 | `ControlLookup/Container/EcsContainerCore.cs`、`ControlLookup/Container/EcsConfigContainer.cs` |
| D5 | `Gate/EcsGate.cs` |
| D6 | `CommandExecution/Core/TemplatePreprocessSkeleton.cs` |
| R8 | `CommandExecution/Diagnostics/execution_log_messages.json` |
| R9 | `CommandExecution/ECS/CommandSystems.cs`、`Shared/Utils/TypeConversionHelper.cs` |

### 相关架构文档

- `.clinerules/axioms.md`：AxiomHierarchyStructure（L1 修改需 ≥2 验证者签名共识）
- `.clinerules/engine.md`：引擎状态机与迭代循环
- `docs/Refactoring/`：既有重构记录目录

---

## 五、实施建议

1. **P0 优先**：先修 B1（命名空间错位）再修 B3（GetRootEntity）——两者都影响 `ControlRegistryAwareFinder` 链路，可联动验证
2. **B1 修复步骤**：
   - 从最内层开始（`Shared/Abstractions/*` → `Shared/Utils/*` → `Shared/*` → `WinForms/*` → `CommandExecution/*`）
   - 每修改一个文件立即编译，利用编译器错误定位引用方
   - 最后全局搜索残留的 `using FileDriven.Pipeline.*` 修正
3. **验证方式**：`dotnet build CsharpPrj/Operation/FrontEndDriven/FrontEndDriven.csproj` + `dotnet build FileDriven/FileDriven.sln`
4. **回归关注点**：契约校验（ContractValidator）、事件绑定（EventHelper）、SplitContainer 面板映射（SplitterPanelRegistry）三条链路