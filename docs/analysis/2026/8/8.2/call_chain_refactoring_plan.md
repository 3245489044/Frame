# 调用链四大重构方向设计方案

> **定位**：针对调用链分析中发现的四大架构问题，提供系统性重构方案
> **关联文档**：`validation_framework_build.md`（校验体系）、`refactoring_plan_l2.md`（L2 批次）
> **日期**：2026-08-01
> **状态**：设计方案（可落地实施）
> **状态图例**：⬜ = 未开始，🔄 = 部分完成/进行中，✅ = 已完成

---

## 总览

基于对 `MainLayout.json` → `EcsGate` → `EventBindingSystem` → `FunctionCallFacade` → `FunctionHub` → `StepExecutor` 的完整调用链分析，本方案处理 **4 项架构级重构**：

| # | 方向 | 性质 | 目标 |
|---|------|------|------|
| R1 | 消灭双门面 | 架构调整 | `FunctionCallFacade` 唯一统一入口，废弃 `EcsGate.ExecuteFunctionOperation` |
| R2 | 强类型函数注册 | 类型安全 | 高频函数强类型注册 API，仅编排函数走工作流包装 |
| R3 | 消除反射注册脆弱性 | 编译期安全 | `[FunctionLibrary]` 编译期扫描替代 JSON 类型字符串 |
| R4 | 参数模型收敛 | 类型安全 | 结构化参数（ParameterBuilder）替代字符串路径 |

---

## 第一部分：R1 — 消灭双门面

### R1.1 问题速览

`EcsGate`（`ExecuteFunctionOperation` + `DoExecuteFunc` + Reactor）与 `FunctionCallFacade`（Async/Sync 双路径）并存，两者都提供"函数执行"能力：

```csharp
// EcsGate 路径（保留为备用）
EcsGate.ExecuteAsync(new ExecuteFunctionOperation {
    FunctionName = ..., Parameters = ..., TimeoutMs = ...
}) → 六层中转：EnqueueExecution → FunctionExecutionSystem.Update(Idle) → Matrix → Router → Handler → Reactor

// FunctionCallFacade 路径（主路径，2026-08-01 新增）
FunctionCallFacade.ExecuteAsync(new FunctionCallRequest { ... }) → Task.Run → WorkflowMethodExecutionService
```

**冲突点**：
- 代码注释自相矛盾：`FunctionCallRequest.cs` L44 说 "Async：通过 EcsGate.ExecuteFunctionOperation"，而 `FunctionCallFacade.cs` L170 说 "移除 EcsGate → Enqueue → Idle 轮询 → Matrix → Router → Handler 六层中转"
- 两条路径产生**双重审计**（EcsGate.AuditLogInterceptor + FunctionCallFacade 门面级审计）
- 调用方无法判断该走哪条路径，心智负担高

### R1.2 目标设计

**核心原则**：`FunctionCallFacade` 为唯一统一入口，`EcsGate.ExecuteFunctionOperation` 废弃。

```
[所有调用源] → FunctionCallFacade（唯一门面）
    │
    ├─ Async 路径（默认）：Task.Run → WorkflowMethodExecutionService（直达）
    │         → FunctionHub.Resolve → StepExecutor
    │
    └─ Sync 路径（脚本/InitCommand）：直接 → WorkflowMethodExecutionService
              → FunctionHub.Resolve → StepExecutor

[废弃] EcsGate.ExecuteFunctionOperation / DoExecuteFunc / ExecutionResultReactor
        （如需 ECS 编排/重试，由 FunctionCallFacade 增加 INeedRetry 策略扩展，而非走 EcsGate）
```

### R1.3 实施步骤

| 步骤 | 动作 | 落点 |
|------|------|------|
| 1 | `grep "ExecuteFunctionOperation"` 全仓，确认外部消费点（预计 `EcsGate` 内部 + 少数测试）| 全仓 |
| 2 | 在 `EcsGate.ExecuteAsync` 的 `DispatchAsync` 分支中，对 `ExecuteFunctionOperation` 改为 `[Obsolete]` 标记 + 委托 `FunctionCallFacade.ExecuteAsync`（保留 ECS 上下文的 `SubmitToMatrixOperation` 能力）| `EcsGate.cs` |
| 3 | 标记 `ExecuteFunctionOperation` record 为 `[Obsolete("请使用 FunctionCallFacade.ExecuteAsync", false)]` | `EcsOperation.cs` |
| 4 | 更新 `FunctionCallRequest.cs` L44 注释：Async 路径改为"Task.Run 直达 WorkflowMethodExecutionService" | `FunctionCallRequest.cs` |
| 5 | 删除 `DoExecuteFunc` + `ExecutionResultReactor` + `_pendingResults` + `EnsureReactorRegistered`（除 Matrix 编排场景外）| `EcsGate.cs` |
| 6 | 统一审计：删除 EcsGate 中对函数执行的 AuditLogInterceptor 记录（保留 ECS 操作审计），函数执行审计仅由 FunctionCallFacade 负责 | `EcsGate.cs` |
| 7 | 构建验证 `dotnet build FileDriven.sln` | 全仓 |

### R1.4 风险与回滚

| 风险 | 缓解 | 回滚 |
|------|------|------|
| 外部依赖 EcsGate.ExecuteFunctionOperation 的 Reactor 等待语义 | 步骤 3 标 Obsolete 且 **保留实现**（仅告警），步骤 5 删除前确认零调用 | 恢复 `DoExecuteFunc` + Reactor 代码 |
| Matrix 编排场景（重试/编排）丢失 | 在 `FunctionCallFacade` 增加 `ExecutionMode.Retry`（内部指数退避重试）替代 EcsGate-Matrix 路径 | 保留 `SubmitToMatrixOperation` 不动，仅弃用函数执行分支 |

---

## 第二部分：R2 — 强类型函数注册

### R2.1 问题速览

当前所有函数注册为 `Func<string, string>` 或 `(string) → (string)`，参数全部字符串化：

```csharp
// UIToolbox（现状）
registrar.Register("ExecuteWorkflow",
    (Func<string, string, string>)((workflowName, input) => {
        var result = WorkflowCommandCenter.ExecuteWorkflow(workflowName, input);
        return result.Output;
    }), "执行指定工作流");
```

**问题**：
- 编译期无法校验参数类型/数量（`workflowName` 可能被误传为 int）
- 参数契约靠运行时 `DynamicInvoke` 才暴露错误
- 无法针对不同函数提供差异化参数类型（全部 `string`）

### R2.2 目标设计

**核心原则**：高频函数走**强类型注册 API**（保留类型信息），仅真正需要编排的函数走工作流包装。

```
【新】强类型注册 API（R2）：
  registrar.Register("ExecuteWorkflow",
      (string workflowName, string input) => { ... },      // 强类型委托
      new ParameterDescriptor[] {                           // 参数契约
          new("workflowName", typeof(string)),
          new("input", typeof(string))
      },
      "执行指定工作流");

【旧】字符串注册（维持兼容）：
  registrar.Register("ExecuteWorkflow",
      (Func<string, string>)(input => { ... }),             // 字符串化
      "执行指定工作流");
```

### R2.3 关键实现：强类型委托 + 参数契约

复用 `IFunctionDescriptor` / `FunctionDescriptor.FromContract`（已存在于 `TConfig/Interfaces/IFunctionDescriptor.cs`）：

```csharp
// 强类型注册入口（新 API）
public static void Register<TDelegate>(
    this ITrConfigRegistry registry,
    string name,
    TDelegate function,
    string? description = null) where TDelegate : Delegate
{
    // ★ 从委托自动提取强类型签名（FunctionDescriptor.FromDelegate 已存在）
    var descriptor = FunctionDescriptor.FromDelegate(function, description);
    descriptor.Name = name;

    // 注册为 ITrConfig（含强类型元数据）
    registry.Register(name, descriptor);
}

// 带参数契约的注册（新 API）
public static void Register<TDelegate>(
    this ITrConfigRegistry registry,
    string name,
    TDelegate function,
    IReadOnlyList<IParamConfig> parameters,
    string? description = null) where TDelegate : Delegate
{
    var descriptor = FunctionDescriptor.FromContract(function, parameters, description);
    descriptor.Name = name;
    registry.Register(name, descriptor);
}
```

### R2.4 区分"强类型函数"与"工作流函数"

```
FunctionHub.Resolve(functionName) 增强：
  ① 工作流字典（多节点编排）→ WorkflowExecutionPlan（多节点）
  ② 强类型函数（IFunctionDescriptor 且有 Delegate）→ WorkflowExecutionPlan（单节点，保留类型元数据）
  ③ 字符串函数（Func<string,string>）→ WorkflowExecutionPlan（单节点，类型回退 string）
```

### R2.5 改造范围（优先清单）

| 函数 | 现状签名 | 强类型签名 |
|------|---------|-----------|
| `ExecuteWorkflow` | `(string, string) → string` | `ExecuteWorkflow(string workflowName, string input) → string` |
| `LoadWorkflowDetails` | `string → string` | `LoadWorkflowDetails(string workflowName) → string` |
| `LoadDefaultInput` | `string → string` | `LoadDefaultInput(string workflowName) → string` |
| `SaveResultToFile` | `string → string` | `SaveResultToFile(string content) → string` |
| `MathLibrary` 数学函数 | `string → string` | 例如 `Add(double a, double b) → double`（类型升级）|

### R2.6 实施步骤

| 步骤 | 动作 | 落点 |
|------|------|------|
| 1 | 新增 `StrongTypedRegistrationExtensions.cs`（强类型注册 API）| `Modules/Registry` |
| 2 | `WorkflowMethodExecutionService.ExecuteFunctionAsync` 增强：解析 `IFunctionDescriptor` 的 `Parameters` 做参数映射（命名参数→按签名过滤/转换）| `WorkFlowMethod/WorkflowMethodExecutionService.cs` |
| 3 | `FunctionCallFacade` 增加 `StructuredFunctionCallRequest<TArgs>` 强类型请求模型 | `FrontEndDriven/Gate` |
| 4 | 优先改造高频 UI 函数（UIToolbox 5 个 + MathLibrary 数学函数）| `FileDriven.Business` |
| 5 | 字符串函数保留兼容（`TryGetDescriptor` 无参数元数据时回退 `string`）| `WorkFlowMethod` |
| 6 | 构建 + 冒烟（ExecuteWorkflow 用类型化参数调用）| 全仓 |

### R2.7 风险与回滚

| 风险 | 缓解 | 回滚 |
|------|------|------|
| 现有 `(string)→(string)` 调用方（ParameterBuilder 构建的参数字典）可能不匹配强类型签名 | 保留 `Func<string,string>` 注册路径兼容；`ExecuteFunctionAsync` 对无 `IParameterDescriptor` 的注册仍走字符串回退 | 保留旧注册 API，仅新增不删除 |
| `FunctionDescriptor.FromDelegate` 对 lambda 参数名提取可能为 `param` | 用 `FromContract` 显式传参数名；或反射 `Invoke` 方法（已有兜底逻辑）| 恢复旧签名 |

---

## 第三部分：R3 — 消除反射注册脆弱性

### R3.1 问题速览

`function-libraries.json` 中硬编码类型字符串，运行期反射加载：

```json
{
  "name": "MathLibrary",
  "type": "FunctionStorage.MathLibrary, FileDriven.Business"
}
```

```csharp
// FunctionLibraryLoader.LoadFromConfig 现状
var type = Type.GetType(typeStr, throwOnError: false);
if (type == null) → 运行时失败
```

**问题**：
- 类型字符串在 JSON 中，**编译器无法校验**。类重命名/移命名空间 → 启动时静默失败或 TypeLoadException 难定位
- `Type.GetType(typeStr)` 依赖程序集加载时序，程序集未加载时返回 null
- JSON 与代码双维护，新增库需改 JSON

### R3.2 目标设计

**核心原则**：用编译期扫描（`[FunctionLibrary]` 自动发现）替代 JSON 类型字符串。

```
【现状】JSON 类型字符串 → Type.GetType（运行期脆弱）
【目标】编译期扫描 + 程序集自动发现（双层保障）
```

关键发现：`FunctionLibraryLoader` **已有编译期扫描能力**（`LoadFromAutoDiscovery`，`autoDiscover: true` 时启用）：

```csharp
// FunctionLibraryLoader.LoadFromAutoDiscovery（已有实现）
var libraryTypes = AppDomain.CurrentDomain.GetAssemblies()
    .SelectMany(asm => asm.GetExportedTypes())
    .Where(t => t is { IsClass: true, IsAbstract: true, IsPublic: true }
                && t.GetCustomAttribute<FunctionLibraryAttribute>() != null)
    .Where(t => attr.AutoDiscover)   // ← 仅 AutoDiscover=true 的库
    .ToList();
```

**核心问题**：`[FunctionLibrary]` 属性有 `AutoDiscover` 选项，但 `function-libraries.json` 的 7 个库**未设置**（默认 false），且 `FrameworkInitializer` 调用 `Load(functions, configPath)` 时 `autoDiscover: false`。

### R3.3 目标改造

#### 方案 A：启用已有的 AutoDiscovery（最小改动）

```csharp
// FrameworkInitializer.cs 改造前：
FunctionLibraryLoader.Load(functions, configPath);

// 改造后：
FunctionLibraryLoader.Load(functions, configPath, autoDiscover: true);
```

- `[FunctionLibrary]` 默认 `AutoDiscover = true`（需改造属性默认值）
- JSON 中的 `type` 字段变为**可选**（存在则验证，不存在则靠扫描）
- 保留 `enabled=false` 禁用能力（JSON 中显式禁用）

```csharp
// FunctionLibraryAttribute 改造
public class FunctionLibraryAttribute : Attribute
{
    public bool AutoDiscover { get; set; } = true;  // ★ 默认改为 true
    public string? SourceName { get; set; }
    public FunctionLibraryAttribute(string? sourceName = null) { SourceName = sourceName; }
}
```

#### 方案 B：彻底移除 JSON 类型注册（激进）

```
function-libraries.json 仅保留元信息（enabled / 排序 / 描述），不再承载类型字符串
[FunctionLibrary] 属性成为唯一注册源（编译期可查）
1. FunctionLibraryLoader 只做扫描（autoDiscover 恒 true）
2. 删除 LoadFromConfig 的 Type.GetType 逻辑（或保留 enabled 过滤）
```

> **建议**：先实施**方案 A**（零破坏、快速见效），观察稳定后过渡到**方案 B**。

### R3.4 实施步骤

| 步骤 | 动作 | 落点 |
|------|------|------|
| 1 | `FunctionLibraryAttribute.AutoDiscover` 默认值改为 `true` | `Modules/Registry/Attributes` |
| 2 | 7 个函数库类确认 `[FunctionLibrary]` 标记（已有）且 AutoDiscover 可继承默认 true | `FileDriven.Business/Registrations/**` |
| 3 | `FrameworkInitializer` 调用 `Load(functions, configPath, autoDiscover: true)` | `FileDriven.Execution/Initial/FrameworkInitializer.cs` |
| 4 | `LoadFromConfig` 中 `type` 字段改为可选：存在则校验（重复检测），缺失则靠扫描注册 | `Modules/Registry/FunctionLibraryLoader.cs` |
| 5 | 新增 `LibraryScanReport`（诊断：每个库的来源 = JSON/扫描/重复）| `Modules/Registry` |
| 6 | 构建 + 启动验证（确认 7 库全部注册，无 Type.GetType 失败）| 全仓 |

### R3.5 风险与回滚

| 风险 | 缓解 | 回滚 |
|------|------|------|
| `AppDomain.CurrentDomain.GetAssemblies()` 可能漏掉未加载的程序集 | `asm.GetExportedTypes()` 前 `Assembly.LoadFrom` 预加载关键程序集（FileDriven.Business 等）| 恢复 `autoDiscover: false` |
| `AutoDiscover=true` 默认值导致已有 `[FunctionLibrary(AutoDiscover=false)]` 的库意外加载 | 属性显式回退：`AutoDiscover` 三态（True/False/未设置），未设置时按 JSON 决定 | 恢复默认 false |
| 扫描顺序不稳定（GetAssemblies 顺序不确定）| 注册后按名称排序输出报告 | 保留 JSON 顺序优先 |

---

## 第四部分：R4 — 参数模型收敛

### R4.1 问题速览

参数传递存在**双轨制**：

```
轨道 A（结构化）：ParameterBuilder（ParameterBinding → Dictionary<string, object>）
  - WorkFlowMethod/Parameters/ParameterBuilder.cs（已有，支持 control/literal/expression + 类型转换）
  - 支持 IParameterDescriptor 参数签名过滤（注释提到但当前跳过）

轨道 B（字符串化）：函数注册 Func<string, string> + 工作流输入 input 字符串
  - WorkflowLibrary.RegisterAll：`(Func<string, string>)(input => ExecuteWorkflow(def, input))`
  - WorkflowMethodExecutionService.ExecuteWorkflowAsync 中 JsonElement.GetRawText() → 字符串
  - WorkflowRunner.json：workflowName/input/apiKey 全字符串
```

**问题**：
- `ExecuteWorkflowAsync` 中 `node.Parameters` 反序列化为 `JsonElement` 后用 `GetRawText()` 丢类型信息
- 字符串化丢失所有结构信息（数组/对象/数值）
- `ParameterBuilder` 已有结构化能力但未彻底接管 `ExecuteWorkflowAsync` 的参数构建

### R4.2 目标设计

**核心原则**：`ParameterBuilder` 为唯一参数构建路径，彻底替代字符串化路径。

```
【现状】字符串化：
  JSON 参数 → JsonElement.GetRawText() → string → 业务函数解析

【目标】结构化：
  JSON 参数 → ParameterBinding[] → ParameterBuilder.Build() → Dictionary<string, object>（强类型）
  业务函数直接消费强类型参数
```

### R4.3 关键改造点

#### ① ExecuteWorkflowAsync 参数结构化

```csharp
// WorkflowMethodExecutionService.ExecuteWorkflowAsync 改造前（字符串化）：
else if (node.Parameters is System.Text.Json.JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Object)
{
    foreach (var prop in jsonElement.EnumerateObject())
        nodeParams[prop.Name] = prop.Value.GetRawText();   // ← 丢类型
}

// 改造后（结构化）：
else if (node.Parameters is System.Text.Json.JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Object)
{
    foreach (var prop in jsonElement.EnumerateObject())
        nodeParams[prop.Name] = ConvertJsonToObject(prop.Value);   // ← 保留类型
}

private static object? ConvertJsonToObject(JsonElement element)
{
    return element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonToObject).ToList(),
        JsonValueKind.Object => element.EnumerateObject()
            .ToDictionary(p => p.Name, p => ConvertJsonToObject(p.Value)),
        _ => element.GetRawText()
    };
}
```

#### ② ParameterBuilder 接管工作流参数构建

```csharp
// WorkflowMethodExecutionService 注入 ParameterBuilder
private readonly ParameterBuilder _parameterBuilder;

// ExecuteWorkflowAsync 中：
var stepDef = new TConfig.StepDefinition
{
    // ...
    FunctionName = node.FunctionName,
    // ★ 结构化参数：复用 ParameterBuilder 的类型转换能力
    Parameters = nodeParams,
};

// 但 ParameterBuilder 目前接收 ParameterBinding（控件绑定），
// 需扩展一个重载：从 Dictionary<string, object> 纯映射构建（无控件依赖）
```

#### ③ 函数调用请求强类型化（与 R2 联动）

```csharp
// FunctionCallFacade 增加强类型请求模型（与 R2 联动）
public sealed record StructuredFunctionCallRequest<TArgs>(
    string FunctionName,
    TArgs Arguments,
    FunctionCallSource Source = FunctionCallSource.Other,
    ExecutionMode Mode = ExecutionMode.Auto);
```

### R4.4 改造范围

| 位置 | 现状 | 目标 |
|------|------|------|
| `WorkflowMethodExecutionService.ExecuteWorkflowAsync` | `JsonElement.GetRawText()` 字符串化 | `ConvertJsonToObject` 结构化 |
| `WorkflowLibrary.RegisterAll` | `(Func<string,string>)` 字符串 | `(Func<string, Dictionary<string,object>>)` 结构化（兼容保留）|
| `WorkflowCommandCenter.ExecuteWorkflow` | `string input` | `Dictionary<string, object> input` |
| `ParameterBuilder` | 仅控件绑定 | 增加纯映射重载（无控件）|
| `WorkflowRunner.json` | `input` 为字符串 | 支持对象/数组 JSON 输入 |

### R4.5 实施步骤

| 步骤 | 动作 | 落点 |
|------|------|------|
| 1 | `ExecuteWorkflowAsync` 的 `ConvertJsonToObject` 结构化转换 | `WorkFlowMethod/WorkflowMethodExecutionService.cs` |
| 2 | `ParameterBuilder` 新增 `BuildFromDictionary(Dictionary<string, object>)` 重载 | `WorkFlowMethod/Parameters/ParameterBuilder.cs` |
| 3 | `WorkflowCommandCenter.ExecuteWorkflow` 增加结构化重载（保留字符串兼容）| `FileDriven.Business/Workflow` |
| 4 | `WorkflowRunner.json` 的 `ExecuteWorkflow` 参数：`input` 支持 JSON 对象 | `FileDriven/DrivenConfig/JsonConfig` |
| 5 | `FunctionCallFacade` 增加 `StructuredFunctionCallRequest<TArgs>` | `FrontEndDriven/Gate` |
| 6 | 构建 + 冒烟（ExecuteWorkflow 传结构化参数）| 全仓 |

### R4.6 风险与回滚

| 风险 | 缓解 | 回滚 |
|------|------|------|
| 现有工作流定义（JSON）步骤参数均为字符串，结构化转换可能改变语义 | `ConvertJsonToObject` 对 String 保留原类型（string → string 无异）；仅数组/对象/数值升级类型 | 恢复 `GetRawText()` |
| `ParameterBuilder.BuildFromDictionary` 无控件依赖需验证 | 新增重载不影响原 `Build(sender, bindings)` | 删除重载 |

---

## 实施批次建议

| 批次 | 方向 | 预估改动量 | 风险 | 依赖 |
|------|------|-----------|------|------|
| **R-1（小批量先行）** | R1 步骤 1-4（标 Obsolete + 委托 Facade）| 中小 | 低 | 无 |
| **R-2（类型安全）** | R2 强类型注册 API + R4 参数结构化 | 中 | 中 | R-1（Facade 支撑）|
| **R-3（编译期安全）** | R3 AutoDiscovery 启用 | 小 | 中 | 无 |
| **R-4（收尾）** | R1 步骤 5-7（删 EcsGate 函数路径）+ R2/R4 兼容清理 | 中 | 中 | 依赖 R-1/R-2/R-3 |

> 建议按 R-1 → R-3（低风险先行）→ R-2 → R-4 执行，每批次独立 commit，构建验证通过后再进入下一批。

## 重构成果预期

| 指标 | 现状 | 重构后 |
|------|------|--------|
| 函数执行入口数 | 2（EcsGate + FunctionCallFacade）| 1（FunctionCallFacade）|
| 审计通道数 | 2（AuditLogInterceptor + 门面级）| 1（门面级）|
| 函数签名类型安全性 | 全字符串化 | 强类型元数据（IFunctionDescriptor）|
| 反射注册脆弱性 | JSON 类型字符串 Type.GetType | 编译期 [FunctionLibrary] 扫描 |
| 参数传递类型保真 | JsonElement.GetRawText 字符串化 | ConvertJsonToObject 结构化 |
| 轻量函数调用链深度 | 6 层（EcsGate 路径）| 3 层（Facade → ExecutionService → StepExecutor）|