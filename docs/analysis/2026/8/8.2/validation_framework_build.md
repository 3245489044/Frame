# 完整校验框架构建文档

> **定位**：基于 `CsharpPrj/Modules` 与 `CsharpPrj/BasicMethod/Map` 现有资产，构建面向 WorkFlowPrj 的完整配置校验体系
> **日期**：2026-08-01
> **状态**：架构设计（可落地实施）

---

## 一、现状分析与差距定位

### 1.1 调用链校验缺口全景

```
MainLayout.json (配置声明)
  │  ⚠️ 无 JSON Schema 强制校验（有 schema 引用但无实际 Schema 文件强制校验）
  ▼
RenderPipelineComposition (ECS 渲染)
  │  ⚠️ 控件名/类型/事件合法性——绑定失败仅 Skipped + 日志，不阻断
  ▼
EventBindingOrchestrator (事件绑定)
  │  ⚠️ functionName 解析失败仅 Warning（绑定与解析解耦，但校验也解耦了）
  ▼
FunctionCallFacade → WorkflowMethodExecutionService
  │  ⚠️ 无参数契约——函数签名全 (string)→(string)，无类型/数量校验
  ▼
FunctionHub.Resolve (函数解析)
  │  ⚠️ 找不到返回 null + Warning，仅运行时失败
  ▼
StepExecutor → 业务函数
  │  ⚠️ 工作流步骤合法性（依赖函数存在性、参数映射）运行时才暴露
```

**整条链没有任何"前置确定性校验"环节**——所有错误都是运行时才暴露。

### 1.2 FileDrivenConfigValidator 空壳问题（核心病灶）

| 校验能力 | 存在性 | 实际强度 |
|---------|--------|---------|
| JSON 语法校验 | ✅ 有（JsonDocument.Parse）| 强 |
| 结构校验 | ⚠️ 接口有，`CreateEngine()` 空实现 | **实际缺失** |
| 引用校验 | ⚠️ 有 Provider 接口，未注册引用规则 | **半缺失** |
| 语义校验 | ⚠️ 接口有，空实现 | **缺失** |
| 运行时校验 | ❌ `EnableRuntimeRules=false`（默认关闭）| **缺失** |
| 函数绑定校验 | ⚠️ 仅日志告警不阻断 | **缺失** |
| 参数类型校验 | ❌ 参数全字符串化，无类型契约 | **缺失** |
| 工作流合法性校验 | ❌ 无 | **缺失** |

**根因**：`FileDrivenConfigValidator.CreateEngine()` 中的四个分类注册分支全部为空注释，未调用 `ConfigValidatorFactory.CreateDefaultEngine()`。

### 1.3 公理层 → 实现层落差

| 公理/规则要求 | 代码应实现 | 现状 |
|--------------|-----------|------|
| `[AX] QuantificationConstraint: type(v)=T` | 配置字段类型契约 | ❌ 无 |
| `[AX] ScopeGuard: r.active(c)iff c∈r.scope` | scope 引用合法性 | ❌ 无 |
| `[AX] StateSnapshot` | 函数调用前状态校验 | ⚠️ 部分 |
| `[CR: dsl.md]` 配置 DSL 语法 | 配置结构校验 | ⚠️ 空实现 |
| `[CR: rules.md §5] AmbiguityStrategy` | 配置语义校验 | ⚠️ 空实现 |

---

## 二、总体架构设计

### 2.1 分层校验模型（五阶段管线）

```
┌─────────────────────────────────────────────────────────────────┐
│                    ValidationPipeline (StaticPipeline)          │
│                                                                 │
│  ┌───────────┐ ┌───────────┐ ┌───────────┐ ┌───────────┐        │
│  │ Stage 1   │→│ Stage 2   │→│ Stage 3   │→│ Stage 4   │→ ...  │
│  │ Structure │ │ Reference │ │ Semantic  │ │ Runtime   │        │
│  │(JSON结构)  │ │(引用存在性) │ │(绑定格式)  │ │(运行时存在) │        │
│  └───────────┘ └───────────┘ └───────────┘ └───────────┘        │
│        ↓               ↓               ↓               ↓        │
│  ┌───────────────────────────────────────────────────────────┐  │
│  │              Stage 5: Contract（函数契约/工作流合法性）       │  │
│  │              —— 新增阶段，对齐 axioms QuantificationConstraint│  │
│  └───────────────────────────────────────────────────────────┘  │
│                                                                 │
│  StageCoordinator 管理阶段状态：Pending → Running → Completed   │
│                    （失败 → Failed / 超时 → TimedOut）           │
│  DiagnosticContext 全程埋点                                     │
└─────────────────────────────────────────────────────────────────┘
```

### 2.2 分层校验模型与调用链集成点

| 校验阶段 | 触发时机 | 集成点 | 模式 |
|---------|---------|--------|------|
| Structure | 配置加载 | `JsonConfigService.LoadConfigAsync` | Loose（默认）|
| Reference | 配置加载 | 同上（Provider 注入）| Loose |
| Semantic | 配置加载 | 同上 | Loose |
| Runtime | 渲染阶段 | `RenderPipelineComposition.Execute` 前后 | Silent（默认关）|
| Contract | 事件绑定/函数解析 | `EventBindingSystem` 预验证 + `FunctionHub.Resolve` | Silent（默认关）|

### 2.3 模式开关映射（framework.config.json）

```json
"validation": {
  "enabled": true,
  "configLoadMode": "Loose",       // 配置加载阶段：Loose 不阻断
  "eventBindingMode": "Loose",     // 事件绑定阶段：可升级为 Strict
  "parameterResolveMode": "Silent", // 参数解析阶段：默认关
  "rules": {
    "structure": true,             // 复用已有的 5 条结构规则
    "reference": true,             // 复用已有的 4 条引用规则
    "semantic": true,              // 复用已有的 2 条语义规则
    "runtime": false,              // 复用已有 1 条运行时规则
    "contract": false              // 【新增】函数契约/工作流合法性
  }
}
```

---

## 三、可复用资产映射

### 3.1 来自 `CsharpPrj/Modules`

| 模块 | 具体资产 | 校验框架角色 | 复用方式 |
|------|---------|-------------|---------|
| `ParameterResolver/Validation` | `ValidationEngine` | **规则调度器**（分类排序、Strict 阻断、静默跳过）| 直接实例化 |
| 〃 | `ConfigValidatorFactory` | **引擎工厂**（CreateDefaultEngine 含 11 条规则）| 直接调用 |
| 〃 | `Rules/*`（11 条）| **内置规则集** | 直接注册 |
| 〃 | `Diagnostics/*`（Severity/SourceLocation/ValidationReport/ValidationDiagnostic）| **诊断体系**（含 JSON 序列化、修复建议）| 直接复用 |
| 〃 | `Providers/*`（IControlTree/IFunctionRegistry/ITemplateRegistry）| **数据提供者接口** | 直接实现 |
| 〃 | `ConfigValidationContext` | **校验上下文**（Provider 注入 + CreateScope）| 直接复用 |
| `ParameterResolver/Validation` | `ParameterMappingValidator` | **参数映射约束校验**（Target 唯一性/非空/SourceType 合法性）| 直接复用 |
| `Pipeline/StaticPipeline` | `StaticPipeline` / `PipelineExecutor` / `PipelineResult` | **五阶段管线编排** | 直接复用 |
| `DiagnosticContext` | `DiagnosticContext` / `DiagnosticScope` | **校验过程埋点**（AsyncLocal 作用域）| 直接复用 |
| `Timing` | `StageCoordinator` | **阶段状态机**（Pending/Running/Failed/TimedOut）| 直接复用 |
| `Registry` | `FunctionLibraryLoader` | **函数契约元数据发现**（反射 [FunctionLibrary]）| 扩展复用 |
| `ComponentData` | `Reactor` | **校验结果事件驱动** | 可选复用 |

### 3.2 来自 `CsharpPrj/BasicMethod/Map`

| 模块 | 具体资产 | 校验框架角色 | 复用方式 |
|------|---------|-------------|---------|
| `Declarative` | `ConstraintBase<T>` / `PredicateConstraint` / `CompositeConstraint` / `TypeConstraint` / `RangeConstraint` / `RequiredConstraint` | **声明式约束层**（规则即声明，P(x)→bool）| 直接复用 |
| `Declarative` | `DeclarationValidator<T>` / `ValidationResult` | **CSP 约束求解**（注册约束 + Validate）| 直接复用 |
| `DecisionEngine` | `RuleBasedDecisionEngine<TState,TContext>` / `ConditionEvaluator` | **校验结果→修复动作决策** | 可选复用 |
| `Transformers` | `Validator` / `ConditionalTransformer` / `TryTransformer` | **参数转换前校验** | 可选复用 |
| `SortMent` | `SortMentBase` / `SortMentCollection` | **多阶段校验顺序编排** | 可选复用 |

### 3.3 关键复用决策

```
【引擎装配】直接复用 ConfigValidatorFactory.CreateDefaultEngine()
【规则基类】继承 ValidationRuleBase（Modules）
【约束声明】复用 IConstraint<T> + DeclarationValidator（BasicMethod）
【管线编排】复用 StaticPipeline + StageCoordinator（Modules）
【诊断输出】复用 ValidationReport.ToJson()（含 Severity/JsonPath/RepairSuggestion）
```

---

## 四、核心设计：校验引擎装配

### 4.1 修复 FileDrivenConfigValidator（打通对接）

```csharp
// 修复前（空壳）：
private static ValidationEngine CreateEngine(ValidationConfig config)
{
    var engine = new ValidationEngine("FileDrivenValidationEngine");
    if (config.EnableStructureRules) { /* 空 */ }
    if (config.EnableReferenceRules) { /* 空 */ }
    if (config.EnableSemanticRules)  { /* 空 */ }
    if (config.EnableRuntimeRules)   { /* 空 */ }
    return engine;
}

// 修复后（复用 ConfigValidatorFactory）：
private static ValidationEngine CreateEngine(ValidationConfig config)
{
    // ★ 基础引擎复用：已含 11 条内置规则（结构5 + 引用4 + 语义2 + 运行时1）
    var engine = ConfigValidatorFactory.CreateEngine(
        name: "FileDrivenValidationEngine",
        includeStructure: config.EnableStructureRules,
        includeReference: config.EnableReferenceRules,
        includeSemantic:  config.EnableSemanticRules,
        includeRuntime:   config.EnableRuntimeRules);

    // ★ 新增契约规则（对齐 axioms QuantificationConstraint）
    if (config.EnableContractRules)
    {
        engine.RegisterRule(new FunctionContractRule());
        engine.RegisterRule(new WorkflowLegalityRule());
        engine.RegisterRule(new ScopeGuardRule());
    }

    // ★ 新增必填字段规则（结构规则补充）
    if (config.EnableStructureRules)
    {
        engine.RegisterRule(new RequiredFieldRule("$.views[].controls[]", "控件",
            new[] { "name", "type" }));
        engine.RegisterRule(new RequiredFieldRule("$.views[]", "视图",
            new[] { "name" }));
    }

    return engine;
}
```

> 注：`ConfigValidatorFactory.CreateEngine` 当前为 `private`，需要提升为 `public` 或新增 `CreateEngine(ValidationEngineOptions) → ValidationEngine` 公开 API。

### 4.2 校验配置扩展（ValidationConfig 新增字段）

```csharp
public class ValidationConfig
{
    // ... 现有字段 ...

    /// <summary>契约校验模式（默认静默——新增阶段默认关闭）</summary>
    public bool EnableContractRules { get; set; } = false;

    /// <summary>事件绑定校验模式（严格时阻断绑定）</summary>
    public ValidationMode EventBindingMode { get; set; } = ValidationMode.Loose;
}
```

---

## 五、规则体系设计

### 5.1 复用已有 11 条规则

#### 结构规则（Structure）
| 规则 | 文件 | 职责 |
|------|------|------|
| `EventValueTypeRule` | `Rules/Structure/EventValueTypeRule.cs` | events.xxx 值类型必须为 string[] |
| `LayoutFormatRule` | `Rules/Structure/LayoutFormatRule.cs` | layout 格式规范 |
| `EnumValueRule` | `Rules/Structure/EnumValueRule.cs` | 枚举值合法性 |
| `RequiredFieldRule` | `Rules/Structure/RequiredFieldRule.cs` | 必填字段存在性（可参数化）|
| `RequiredField_*` | 同上（工厂实例化）| 按父路径定制必填字段 |

#### 引用规则（Reference）
| 规则 | 文件 | 职责 |
|------|------|------|
| `TemplateRefRule` | `Rules/Reference/TemplateRefRule.cs` | 模板引用存在性 |
| `ControlRefRule` | `Rules/Reference/ControlRefRule.cs` | bindings.parameters[].source 控件存在性 |
| `FunctionRefRule` | `Rules/Reference/FunctionRefRule.cs` | events.xxx 函数名存在性 |
| `OutputTargetRule` | `Rules/Reference/OutputTargetRule.cs` | bindings.output 目标合法性 |

#### 语义规则（Semantic）
| 规则 | 文件 | 职责 |
|------|------|------|
| `EventBindingFormatRule` | `Rules/Semantic/EventBindingFormatRule.cs` | bindings 格式（object/parameters/target/source）|
| `ParameterMappingFormatRule` | `Rules/Semantic/ParameterMappingFormatRule.cs` | 参数映射格式 |

#### 运行时规则（Runtime）
| 规则 | 文件 | 职责 |
|------|------|------|
| `ControlExistenceRule` | `Rules/Runtime/ControlExistenceRule.cs` | 配置引用控件在运行时 UI 树中存在 |

### 5.2 新增规则（补齐缺口）

#### ① FunctionContractRule（函数契约校验）—— 对齐 QuantificationConstraint

```csharp
/// <summary>
/// 函数契约校验规则 — 校验函数调用参数与注册签名匹配
/// </summary>
public class FunctionContractRule : ValidationRuleBase
{
    public FunctionContractRule()
        : base("FunctionContract",
            "校验 events.xxx 引用的函数参数与注册签名匹配",
            ValidationRuleCategory.Contract)  // 新增分类
    {
    }

    public override Task<IReadOnlyList<ValidationDiagnostic>> ValidateAsync(
        JsonElement config, ConfigValidationContext context,
        CancellationToken ct = default)
    {
        var diagnostics = new List<ValidationDiagnostic>();

        if (context.FunctionRegistry == null)
        {
            // FunctionRegistryProvider 需扩展为提供参数签名
            return Task.FromResult<IReadOnlyList<ValidationDiagnostic>>(diagnostics);
        }

        // ① 查找所有事件函数映射
        var functionArrays = JsonTreeWalker.FindEventFunctionArrays(config, ct);
        foreach (var (eventName, funcArray, eventsPath) in functionArrays)
        {
            // ② 查找同控件的 bindings.parameters 获取参数个数
            var paramCount = GetParameterCountAt(eventsPath);

            // ③ 校验函数参数签名匹配
            foreach (var funcItem in funcArray.EnumerateArray())
            {
                if (funcItem.ValueKind != JsonValueKind.String) continue;
                var functionName = funcItem.GetString();
                if (string.IsNullOrWhiteSpace(functionName)) continue;

                var expectedParams = context.FunctionRegistry
                    .GetFunctionParameterTypes(functionName);
                if (expectedParams != null && expectedParams.Count != paramCount)
                {
                    diagnostics.Add(Error(
                        $"函数 '{functionName}' 期望 {expectedParams.Count} 个参数，" +
                        $"实际绑定 {paramCount} 个")
                        .WithLocation(new SourceLocation(
                            context.FilePath ?? "unknown", eventsPath)));
                }
            }
        }
        return Task.FromResult<IReadOnlyList<ValidationDiagnostic>>(diagnostics);
    }
}
```

> **依赖扩展**：`IFunctionRegistryProvider` 已含 `GetFunctionParameterTypes(string)` 方法，但 `FileDrivenFunctionRegistryProvider` 未填充参数数据——需在 `FunctionLibraryLoader` 反射注册时同步收集函数签名。

#### ② WorkflowLegalityRule（工作流合法性校验）

```csharp
/// <summary>
/// 工作流合法性校验规则 — 校验工作流定义中步骤函数存在性、依赖完整性
/// </summary>
public class WorkflowLegalityRule : ValidationRuleBase
{
    public WorkflowLegalityRule()
        : base("WorkflowLegality",
            "校验工作流定义中步骤函数存在性与依赖完整性",
            ValidationRuleCategory.Contract)
    {
    }

    public override Task<IReadOnlyList<ValidationDiagnostic>> ValidateAsync(
        JsonElement config, ConfigValidationContext context,
        CancellationToken ct = default)
    {
        var diagnostics = new List<ValidationDiagnostic>();

        // ① 查找所有工作流定义（Workflows 字典）
        var workflows = JsonTreeWalker.FindProperties(config, "Workflows", ct);
        foreach (var (wfElement, wfPath) in workflows)
        {
            // ② 遍历步骤节点，校验 FuncName 存在性
            foreach (var step in JsonTreeWalker.FindProperties(wfElement, "FunctionName", ct))
            {
                var funcName = step.value.GetString();
                if (string.IsNullOrWhiteSpace(funcName)) continue;
                if (context.FunctionRegistry != null
                    && !context.FunctionRegistry.HasFunction(funcName))
                {
                    diagnostics.Add(Error(
                        $"工作流步骤引用函数 '{funcName}' 未注册")
                        .WithLocation(new SourceLocation(
                            context.FilePath ?? "unknown", step.path)));
                }
            }
        }
        return Task.FromResult<IReadOnlyList<ValidationDiagnostic>>(diagnostics);
    }
}
```

#### ③ ScopeGuardRule（Scope 引用校验）—— 对齐 ScopeGuard 公理

```csharp
/// <summary>
/// Scope 引用校验规则 — 校验控件声明的作用域与配置载入的作用域匹配
/// </summary>
public class ScopeGuardRule : ValidationRuleBase
{
    public ScopeGuardRule()
        : base("ScopeGuard",
            "校验控件声明作用域与配置载入作用域匹配（对齐 axioms ScopeGuard）",
            ValidationRuleCategory.Contract)
    {
    }

    // 校验：MainLayout scope 的控件不能被 Content scope 引用等
    // 数据源：IControlTreeProvider 扩展 GetControlScope(name)
}
```

### 5.3 规则分类扩展

```csharp
public enum ValidationRuleCategory
{
    Structure,   // 已有
    Reference,   // 已有
    Semantic,    // 已有
    Runtime,     // 已有
    Contract     // 【新增】函数契约/工作流合法性/Scope 守卫
}
```

> 需同步扩展 `ValidationEngine.GetRulesInExecutionOrder()` 的分类顺序数组，将 `Contract` 追加到 `Runtime` 之后。

---

## 六、声明式约束层（复用 BasicMethod.Declarative）

### 6.1 规则声明化

将校验规则定义为 `IConstraint<T>` 声明，实现"规则即声明、声明可 CSP 求解"：

```csharp
using BasicMethod.Declarative;
using BasicMethod.Declarative.Abstractions;
using BasicMethod.Declarative.Constraints;

// 声明：控件引用约束（复用已有组件）
var controlRefConstraint = new PredicateConstraint<UIConfiguration>(
    "ControlRef",
    "所有 bindings.parameters[].source 引用的控件必须存在",
    config => config.Views
        .SelectMany(v => v.Controls)
        .SelectMany(CollectParameters)
        .All(p => controlNames.Contains(p.Source)));

// 声明：Target 唯一性（复用 ParameterMappingValidator 的约束）
var targetUniqueConstraint = new PredicateConstraint<IReadOnlyList<ParameterMapping>>(
    "TargetUnique",
    "Target 不能重复",
    mappings => mappings.Select(p => p.Target)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Count() == mappings.Count);

// 组合约束：AND 组合（复用 CompositeConstraint）
var composite = new CompositeConstraint<UIConfiguration>(
    "ConfigContract",
    "配置契约（控件引用 + 参数映射 + 事件格式）",
    CompositeMode.All,
    controlRefConstraint,
    eventFormatConstraint,
    functionContractConstraint);
```

### 6.2 DeclarationValidator 集成

```csharp
// 声明验证器（复用 DeclarationValidator）
var validator = new DeclarationValidator<UIConfiguration>("FileDrivenContractValidator");
validator.AddConstraint(controlRefConstraint);
validator.AddConstraint(targetUniqueConstraint);
validator.AddConstraint(new TypeConstraint("ConfigType", "配置必须是 UIConfiguration 类型", typeof(UIConfiguration)));
validator.AddConstraint(new RangeConstraint<int>("MaxViews", "视图数量限制", 1, 20));

var result = validator.Validate(config);
if (!result.IsValid)
    throw new ContractValidationException(result.FailureReasons);
```

---

## 七、管线编排（复用 Modules.Pipeline + Timing + DiagnosticContext）

### 7.1 StaticPipeline 五阶段编排

```csharp
using Modules.Pipeline.StaticPipeline;

// 五阶段管线：Structure → Reference → Semantic → Runtime → Contract
var pipeline = new StaticPipeline<ValidationPipelineContext>("ValidationPipeline");

pipeline.RegisterStage("Structure", ctx =>
{
    var report = await ConfigValidatorFactory.ValidateJsonAsync(
        ctx.Json, ctx.FilePath, ctx.ConfigLoadMode,
        ctx.TemplateRegistry, ctx.FunctionRegistry, ctx.ControlTree);
    ctx.MergeReport(report!);
});

pipeline.RegisterStage("Reference", /* 复用 ValidationEngine 的 Reference 分类规则 */);
pipeline.RegisterStage("Semantic", /* 复用 ValidationEngine 的 Semantic 分类规则 */);
pipeline.RegisterStage("Runtime",  /* Runtime 分类规则（默认 Silent）*/);
pipeline.RegisterStage("Contract", /* 新增 FunctionContractRule/WorkflowLegalityRule/ScopeGuardRule */);
```

### 7.2 StageCoordinator 阶段状态管理

```csharp
using Modules.Timing;

var coordinator = new StageCoordinator();
coordinator.RegisterStage(StageId.StructureValidated, "结构校验完成");
coordinator.RegisterStage(StageId.ReferenceValidated, "引用校验完成");
coordinator.RegisterStage(StageId.SemanticValidated, "语义校验完成");
coordinator.RegisterStage(StageId.RuntimeValidated, "运行时校验完成");
coordinator.RegisterStage(StageId.ContractValidated, "契约校验完成");

// 校验管线中完成阶段
coordinator.CompleteStage(StageId.StructureValidated, report.IsValid, report.ToDiagnosticString());
```

### 7.3 DiagnosticContext 埋点

```csharp
using Modules.DiagnosticContext.Core;

using (DiagnosticContext.Current?.CreateScope("validation"))
{
    DiagnosticContext.Current?.Record("validation.stage", new { stage = "Structure" });
    DiagnosticContext.Current?.SetState("filePath", filePath);
    DiagnosticContext.Current?.SetState("errorCount", report.ErrorCount);
}
```

---

## 八、与现有框架集成

### 8.1 配置加载阶段（JsonConfigService.LoadConfigAsync）

```csharp
// 现有入口，插入校验：
var config = JsonSerializer.Deserialize<UIConfiguration>(json, options);

// ★ 新增：加载前校验（复用 ValidationEngine + Provider）
var report = FileDrivenConfigValidator.Validate(
    json, configPath,
    mode: configValidationMode,
    new FileDrivenTemplateRegistryProvider(),
    new FileDrivenFunctionRegistryProvider(GetRegisteredFunctionNames()),
    new FileDrivenControlTreeProvider(config));

if (report is { IsValid: false } && strictMode)
    throw new ConfigValidationException($"配置加载失败: {report.ErrorCount} 个错误", report);
```

### 8.2 事件绑定阶段（EventBindingSystem 预验证）

```csharp
// 现有：LogUnresolvedFunctionsWarning（仅告警）
// ★ 增强：Strict 模式阻断 + 错误定位
private void ValidateBindingsBeforeExecute(
    IReadOnlyList<EventBindingDeclaration<IReadOnlyList<ParameterMapping>>> declarations,
    string? configPath)
{
    if (_validationMode != ValidationMode.Strict) return;

    var unresolved = declarations
        .Where(d => _functionHub?.Resolve(d.FunctionName) == null)
        .ToList();

    if (unresolved.Count > 0)
        throw new EventBindingValidationException(
            $"事件绑定校验失败: {unresolved.Count} 个函数未注册",
            unresolved.Select(d => d.FunctionName).ToList());
}
```

### 8.3 函数解析阶段（FunctionHub.Resolve）

```csharp
// 现有：Resolve 返回 null + Warning
// ★ 增强：解析前参数契约校验（FunctionContractRule）
public WorkflowExecutionPlan? Resolve(string functionName)
{
    // ... 现有逻辑 ...

    // ★ 新增：解析成功后校验参数契约
    if (plan != null && _contractValidator != null)
    {
        _contractValidator.ValidateFunctionSignature(functionName, plan);
    }

    return plan;
}
```

### 8.4 LLM 配置生成纠错回路（最大差异点落地）

```csharp
/// <summary>
/// 校验报告 → LLM 可消费的修复反馈
/// </summary>
public class ValidationFeedbackFormatter
{
    /// <summary>将校验报告转为 LLM 可理解的修复指令</summary>
    public string ToLlmFeedback(ValidationReport report)
    {
        var fixes = report.Diagnostics
            .Where(d => d.Severity == Severity.Error)
            .Select(d => new
            {
                path = d.SourceLocation?.JsonPath,
                rule = d.RuleName,
                issue = d.Message,
                fix = d.RepairSuggestion
            });

        return System.Text.Json.JsonSerializer.Serialize(new
        {
            isValid = report.IsValid,
            errorCount = report.ErrorCount,
            fixes
        }, new JsonSerializerOptions { WriteIndented = true });
    }
}

// LLM 迭代闭环：
// 1. LLM 生成配置 JSON
// 2. Validate(configJson) → report
// 3. ToLlmFeedback(report) → 结构化修复指令
// 4. LLM 依据反馈修正 → 回到 1
```

---

## 九、实施路线（P0/P1/P2）

### P0 — 打通现有框架（最快见效）

| 任务 | 落点 | 工作量 |
|------|------|--------|
| 修复 `FileDrivenConfigValidator.CreateEngine()` 调用 `ConfigValidatorFactory` | `FileDriven.JsonRefer/Validation` | 小 |
| 将 `ConfigValidatorFactory.CreateEngine` 提升为 public | `Modules/ParameterResolver/Validation` | 小 |
| `framework.config.json` 开启 structure/reference/semantic | 配置文件 | 极小 |
| 用 MainLayout.json 验证 11 条规则生效 | 手动验证 | 小 |

### P1 — 补齐契约规则

| 任务 | 落点 | 工作量 |
|------|------|--------|
| 新增 `ValidationRuleCategory.Contract` | `Modules/ParameterResolver/Validation` | 小 |
| 新增 `FunctionContractRule` | 同上 | 中 |
| 新增 `WorkflowLegalityRule` | 同上 | 中 |
| 扩展 `FileDrivenFunctionRegistryProvider` 填充参数签名 | `FileDriven.JsonRefer/Validation/Providers` | 中 |
| `FunctionLibraryLoader` 反射收集函数签名 | `Modules/Registry` | 中 |

### P2 — 声明式约束层 + LLM 回路 + 深度集成

| 任务 | 落点 | 工作量 |
|------|------|--------|
| 声明式约束层（`IConstraint` + `DeclarationValidator`）| `BasicMethod/Declarative` + 新校验项目 | 中 |
| `ValidationFeedbackFormatter`（LLM 回路）| 新校验项目 | 小 |
| EventBindingSystem Strict 阻断 | `FrontEndDriven/ControlInteraction/EventBinding` | 小 |
| FunctionHub 解析前契约校验 | `WorkFlowMethod` | 中 |
| ScopeGuardRule（对齐公理）| 新校验项目 | 中 |

---

## 十、示例与验证

### 10.1 用 MainLayout.json 验证

```bash
# 当前（修复前）：MainLayout.json 加载成功但函数绑定 Silent 失败
# 修复后：
#   Structure   → 校验 5 个按钮的 name/type 必填 ✅
#   Reference   → 校验 LoadConfig/LoadWorkflow/ClearOutput/ShowHelp/LoadSidePanel 已注册 ✅
#   Semantic    → 校验每个按钮 events.click 为 string[] ✅
#   Contract    → 校验 Execute 函数参数绑定契约 ✅
```

### 10.2 预期诊断示例

```
[错误] FunctionRef: 事件 'click' 引用的函数 'LoadConfigXxx' 未在函数注册表中定义
       (MainLayout.json - $.controls[0].events.click[0])
       — 可用函数: ClearOutput, Execute, ExecuteWorkflow, LoadConfig, ...

[警告] EventBindingFormat: events.bindings 中缺少 parameters 数组
       (WorkflowRunner.json - $.controls[1].events.bindings)
       — 添加 parameters 数组，如 "parameters": [{ "target": "...", "source": "..." }]
```

---

## 十一、架构决策记录（ADR）

| # | 决策 | 理由 |
|---|------|------|
| 1 | 复用 `ParameterResolver.Validation` 而非新建 | 引擎/规则/诊断/Provider 已完整，仅需打通 |
| 2 | 新增 `Contract` 分类而非并入 Semantic | 契约校验语义独立（类型/签名/工作流合法性）|
| 3 | 默认 `Loose` 不阻断，可升级 `Strict` | 与现有 `framework.config.json` 行为兼容，渐进增强 |
| 4 | 声明式约束层用 `BasicMethod.Declarative` | 与 `ParameterMappingValidator` 现有用法一致，复用 CSP 求解 |
| 5 | 校验报告 JSON 化供 LLM 消费 | 使"LLM 可驱动"成为真正闭环（生成→校验→反馈→修正）|

---

## 十二、结论

> 本框架构建的 **核心工作量不在"从零实现"**，而在于：
> 1. **打通** `FileDrivenConfigValidator` 与 `ParameterResolver.Validation`（修复空壳）
> 2. **补齐** Contract 分类的 3 条规则（函数契约/工作流合法性/Scope 守卫）
> 3. **声明化** 规则层（复用 `BasicMethod.Declarative`）
> 4. **接入** 调用链 4 个阶段（配置加载/事件绑定/函数解析/运行时）
> 5. **闭环** LLM 配置生成纠错回路

可复用资产已全部就绪，按 P0→P1→P2 路线即可落地。