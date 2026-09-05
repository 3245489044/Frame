# 基于 Pipeline 的函数执行链集中化重构方案

> **层级**：L2-L3（架构层） | **当前状态**：设计阶段 | **优先级**：高
>
> **核心思想**：用已有 `Modules.Pipeline` 模块代替当前分散三层的函数注册-执行链，
> 消除所有冗余接口、桥接适配器和 `(object)` 强制转换。

---

## 目录

1. [问题分析](#一问题分析)
2. [目标架构](#二目标架构)
3. [核心契约](#三核心契约)
4. [PipelineStage 实现示例](#四pipelinestage-实现示例)
5. [修改清单](#五修改清单)
6. [删除清单](#六删除清单)
7. [约束保证](#七约束保证)
8. [实施步骤](#八实施步骤)
9. [风险与回退](#九风险与回退)

---

## 一、问题分析

### 1.1 当前架构（冗余路径）

```
                    ┌─────────────────────────────────────────────────────┐
                    │                 用户点击按钮                          │
                    │   MainLayout.json → EventBindingSystem               │
                    │   → ExecutionRequestComponent                        │
                    └──────────────────────┬──────────────────────────────┘
                                           │
                    ┌──────────────────────▼──────────────────────────────┐
      注册侧(C)     │  UIToolbox.RegisterAll(registrar)                    │
       FileDriven   │   registrar = UnifiedRegistryCenter.Functions        │
       .Business    │   ↓ 写入 27+ 个函数                                  │
                    └──────────────────────┬──────────────────────────────┘
                                           │ 但注册到哪个实例？
                                           │
                    ┌──────────────────────▼──────────────────────────────┐
      桥接侧(B)     │  FunctionExecutionSystem._executor                   │
       FrontEnd     │   ↓ DI 解析 IWorkflowFunctionExecutor                │
       Driven       │   ↓ (IWorkflowFunctionExecutor)(object)IFunctionExecutor│
                    │   ↓ FunctionExecutor(UnifiedRegistryCenter.Functions)│
                    └──────────────────────┬──────────────────────────────┘
                                           │ 桥接用的是 (object) 强制转换
                                           │ 无法保证实例与注册侧一致
                    ┌──────────────────────▼──────────────────────────────┐
      消费侧(A)     │  _executor.HasFunction("LoadConfig")                 │
                    │  → 可能 false（即使已注册）                            │
                    └─────────────────────────────────────────────────────┘
```

### 1.2 三条明确的冗余

| # | 冗余项 | 所在文件 | 危害 |
|---|--------|----------|------|
| 1 | `IWorkflowFunctionExecutor` 接口 | `CsharpPrj/Plate/TConfig/Abstractions/IFunctionExecutor.cs` | `IFunctionExecutor` 已有完全相同的方法，多一个接口就多一条接线路径 |
| 2 | `(IWorkflowFunctionExecutor)(object)IFunctionExecutor` 强制转换 | `FileDriven/FileDriven.Execution/Initial/FrameworkInitializer.cs:402` | 编译器无法验证，运行时可能 ClassCastException；绕过类型系统 |
| 3 | `FunctionStorageFunctionExecutor` 桥接适配器 | `FileDriven/FileDriven.Utilities/CLI/FunctionStorageFunctionExecutor.cs` | 实现了但未被 ECS 路径使用，纯无用代码 |

### 1.3 根因

**没有结构约束将注册和执行绑定到同一条路径。** 注册写入 `FunctionRegistry`，消费从 `DI → FunctionExecutor`，两者是否指向同一个注册表实例完全靠运气。

如果 Pipeline 模块（`Modules.Pipeline`）能约束所有执行路径，则这种接线失误不可能发生——**因为 Pipeline 的 `RegisterStage` 和 `Execute` 天然在同一个实例上**。

---

## 二、目标架构

### 2.1 重构后结构

```
                    ┌─────────────────────────────────────────────────────┐
                    │                 用户点击按钮                          │
                    │   MainLayout.json → EventBindingSystem               │
                    │   → 创建 FunctionContext{Name="LoadConfig"}          │
                    └──────────────────────┬──────────────────────────────┘
                                           │
                    ┌──────────────────────▼──────────────────────────────┐
      统一入口       │  FunctionExecutionSystem                              │
                    │   pipeline.Execute(FunctionContext)                   │
                    │   ↓                                                  │
                    │   Pipeline（Modules.Pipeline）                         │
                    │   ├─ Stage 1: LoadConfigStage   (Order=10)           │
                    │   ├─ Stage 2: ClearOutputStage  (Order=20)           │
                    │   ├─ Stage 3: LoadWorkflowStage (Order=30)           │
                    │   ├─ Stage 4: ShowHelpStage     (Order=40)           │
                    │   ├─ Stage 5: LoadSidePanelStage(Order=50)           │
                    │   └─ Stage 6: ExecuteStage      (Order=60)           │
                    └─────────────────────────────────────────────────────┘
```

### 2.2 关键变化

| 变化 | 前 | 后 |
|------|----|----|
| 注册方式 | `UIToolbox.RegisterAll(registrar)` → `FunctionRegistry` | `pipeline.RegisterStage(new XxxStage())` |
| 消费方式 | `IWorkflowFunctionExecutor.HasFunction()` + `ExecuteFunctionAsync()` | `pipeline.Execute(FunctionContext)` |
| 实例一致性 | ❌ 依赖 DI 接线 | ✅ `RegisterStage` 和 `Execute` 在同一个 `Pipeline` 实例 |
| 数据类型 | `ExecutionRequestComponent` + 参数字典 | 统一为 `FunctionContext` |

---

## 三、核心契约

### 3.1 `FunctionContext`（新增）

```csharp
// CsharpPrj/Modules/Pipeline/FunctionContext.cs
// 唯一的函数执行上下文，注册侧和消费侧共用

public class FunctionContext
{
    // ── 输入 ──
    public string FunctionName { get; init; } = string.Empty;
    public Dictionary<string, object?> Parameters { get; init; } = new();

    // ── ECS 路径专有（可选）──
    public int? SourceEntityId { get; set; }

    // ── 输出（由 PipelineStage 填充）──
    public bool IsSuccess { get; set; }
    public object? ResultValue { get; set; }
    public string? ErrorMessage { get; set; }

    // ── 元数据 ──
    public DateTime Timestamp { get; } = DateTime.UtcNow;
    public DateTime? CompletedTimestamp { get; set; }
}
```

### 3.2 `PipelineStageBase<FunctionContext>`（已有，复用）

`PipelineStageBase<TContext>` 已在 `Modules.Pipeline.Core.cs:999` 中定义，提供：

- `CanExecute` 前置检查
- 异常自动捕获
- `SetStageResult` 自动更新上下文
- 子类只需重写 `ExecuteCoreAsync`

```csharp
public class LoadConfigStage : PipelineStageBase<FunctionContext>
{
    public override string Name => "LoadConfig";
    public override int Order => 10;

    protected override Task ExecuteCoreAsync(FunctionContext context)
    {
        // 原有 UIToolbox 中 LoadConfig 的逻辑移到此处
        context.ResultValue = "正在加载: ...";
        context.IsSuccess = true;
        return Task.CompletedTask;
    }
}
```

### 3.3 `IPipelineStage<TInput, TOutput>`（已有，复用）

如果不需要 `PipelineStageBase` 的基类能力，可以直接实现接口：

```csharp
public class LoadConfigStage : IPipelineStage<FunctionContext, FunctionContext>
{
    public string Name => "LoadConfig";
    public int Order => 10;
    public bool IsFatal => false;

    public FunctionContext? Process(FunctionContext input, ErrorCollector errors, PipelineContext context)
    {
        // 执行逻辑
        input.ResultValue = "LoadConfig 执行成功";
        input.IsSuccess = true;
        return input;
    }
}
```

---

## 四、PipelineStage 实现示例

### 4.1 ClearOutputStage

```csharp
public class ClearOutputStage : PipelineStageBase<FunctionContext>
{
    public override string Name => "ClearOutput";
    public override int Order => 20;
    public override bool IsFatal => false;

    protected override Task ExecuteCoreAsync(FunctionContext context)
    {
        // 原有 ClearOutput 逻辑：清空输出文本框
        var outputBox = FindControlRecursive(Form.ActiveForm, "outputTextBox");
        if (outputBox != null)
            outputBox.Text = "";
        context.ResultValue = string.Empty;
        context.IsSuccess = true;
        return Task.CompletedTask;
    }

    private static Control? FindControlRecursive(Control? parent, string name)
    {
        if (parent?.Name == name) return parent;
        foreach (Control child in parent?.Controls ?? [])
        {
            var found = FindControlRecursive(child, name);
            if (found != null) return found;
        }
        return null;
    }
}
```

### 4.2 无 UI 操作函数（纯逻辑）

对于无需操作控件的函数（如 `GetWorkflowNames`），结构更简单：

```csharp
public class GetWorkflowNamesStage : PipelineStageBase<FunctionContext>
{
    public override string Name => "GetWorkflowNames";
    public override int Order => 70;
    public override bool IsFatal => false;

    protected override Task ExecuteCoreAsync(FunctionContext context)
    {
        var names = WorkflowExecutionLayer.GetAllWorkflowNames().ToList();
        context.ResultValue = string.Join(",", names);
        context.IsSuccess = true;
        return Task.CompletedTask;
    }
}
```

---

## 五、修改清单

### 5.1 新增文件

| # | 文件 | 说明 |
|---|------|------|
| 1 | `CsharpPrj/Modules/Pipeline/FunctionContext.cs` | 统一的函数执行上下文 |

### 5.2 新增或替换文件

| # | 文件 | 操作 | 说明 |
|---|------|------|------|
| 2 | `FileDriven/FileDriven.Business/PipelineFunctions/` | 新建目录 | 存放所有 PipelineStage 实现 |
| 3 | `FileDriven/FileDriven.Business/PipelineFunctions/LoadConfigStage.cs` | 新建 | 替换 `UIToolbox.LoadConfig` |
| 4 | `FileDriven/FileDriven.Business/PipelineFunctions/ClearOutputStage.cs` | 新建 | 替换 `UIToolbox.ClearOutput` |
| 5 | `FileDriven/FileDriven.Business/PipelineFunctions/LoadWorkflowStage.cs` | 新建 | 替换 `UIToolbox.LoadWorkflow` |
| 6 | `FileDriven/FileDriven.Business/PipelineFunctions/ShowHelpStage.cs` | 新建 | 替换 `UIToolbox.ShowHelp` |
| 7 | `FileDriven/FileDriven.Business/PipelineFunctions/LoadSidePanelStage.cs` | 新建 | 替换 `UIToolbox.LoadSidePanel` |
| 8 | `FileDriven/FileDriven.Business/PipelineFunctions/ExecuteStage.cs` | 新建 | 替换 `UIToolbox.Execute` |
| 9 | `FileDriven/FileDriven.Business/PipelineFunctions/WorkflowQueryStages.cs` | 新建 | 归并 `GetWorkflowNames`、`ReloadWorkflows`、`LoadWorkflowDetails` 等纯查询函数 |
| 10 | `FileDriven/FileDriven.Business/PipelineFunctions/DialogStages.cs` | 新建 | 归并 `SaveResultToFile`、`LoadEnvironmentApiKey`、`LoadDefaultInput` 等简单函数 |

### 5.3 改造文件

| # | 文件 | 改造内容 |
|---|------|----------|
| 11 | `CsharpPrj/Operation/FrontEndDriven/ECS/Systems/FunctionExecutionSystem.cs` | `_executor: IWorkflowFunctionExecutor` → `_pipeline: Pipeline`；`Update()` 内 `_executor.HasFunction()` + `_executor.ExecuteFunctionAsync()` → `_pipeline.Execute(FunctionContext)` |
| 12 | `FileDriven/FileDriven.Execution/Initial/FrameworkInitializer.cs` | 步骤"Registry"中删除 `(object)` 强制转换；新增步骤"PipelineFunctions"注册所有 Stage 到 Pipeline |

### 5.4 `FunctionExecutionSystem.cs` 改造后结构

```csharp
public class FunctionExecutionSystem
{
    private readonly Pipeline _pipeline;
    private readonly EntityManager _entityManager;

    public void Update()
    {
        var pending = _entityManager.GetEntities<ExecutionRequestComponent>();
        if (!pending.Any()) return;

        foreach (var entity in pending)
        {
            var req = _entityManager.GetComponent<ExecutionRequestComponent>(entity);
            var ctx = new FunctionContext
            {
                FunctionName = req.FunctionName,
                Parameters = req.Parameters ?? new(),
                SourceEntityId = entity.Id,
            };

            _pipeline.Execute(ctx);

            _entityManager.AddComponent(
                _entityManager.CreateEntity(),
                new ExecutionResultComponent
                {
                    FunctionName = ctx.FunctionName,
                    SourceEntityId = ctx.SourceEntityId ?? 0,
                    Result = ctx.ResultValue,
                    Success = ctx.IsSuccess,
                    ErrorMessage = ctx.ErrorMessage,
                    RequestTimestamp = ctx.Timestamp,
                    CompletedTimestamp = DateTime.UtcNow
                });
        }

        // 清除已处理请求
        foreach (var e in pending.ToList())
            _entityManager.RemoveComponent<ExecutionRequestComponent>(e);
    }
}
```

### 5.5 `FrameworkInitializer.cs` 改造后注册逻辑

```csharp
// ★ 新增步骤：PipelineFunctions
orchestrator.Register("PipelineFunctions", (IInitializationContext context) =>
{
    var pipeline = new Pipeline("FunctionExecution");
    pipeline
        .RegisterStage(new LoadConfigStage())
        .RegisterStage(new ClearOutputStage())
        .RegisterStage(new LoadWorkflowStage())
        .RegisterStage(new ShowHelpStage())
        .RegisterStage(new LoadSidePanelStage())
        .RegisterStage(new ExecuteStage())
        .RegisterStage(new GetWorkflowNamesStage())
        .RegisterStage(new ReloadWorkflowsStage())
        // ... 继续注册所有 18+ 个 Stage

    AppServiceProvider.Register(pipeline);

    ExecutionLog.Log("PipelineFunctions_registered",
        new { stageCount = pipeline.Count });
});
```

---

## 六、删除清单

| # | 文件/代码 | 原因 | 影响范围 |
|---|-----------|------|----------|
| 1 | `interface IWorkflowFunctionExecutor`（`IFunctionExecutor.cs:66-78`） | 不再被引用 | `FunctionExecutionSystem` 已改为依赖 `Pipeline` |
| 2 | `FunctionStorageFunctionExecutor.cs`（完整文件） | 桥接逻辑不再需要 | ECS 路径不再走 `IWorkflowFunctionExecutor` 桥接 |
| 3 | `FrameworkInitializer.cs:402` `(object)` 强制转换 | 不再注册 `IWorkflowFunctionExecutor` | DI 容器中删除此行 |
| 4 | `UIToolbox.cs`（完整文件） | 所有函数已迁移为 PipelineStage | `function-libraries.json` 中对应条目也删除 |
| 5 | `function-libraries.json` 中 `"UIToolbox"` 条目 | 不再通过反射注册 | `FunctionLibraryLifecycle` 加载列表变短 |
| 6 | `FrontEndDriven.csproj` 中 `TConfig` 对 `IFunctionExecutor` 的间接依赖 | `FunctionExecutionSystem` 不再依赖执行器接口 | 编译依赖简化 |

**删除后的正向效果**：项目减少约 700 行代码（`UIToolbox.cs` ~500 行 + `FunctionStorageFunctionExecutor.cs` ~94 行 + 接口声明 ~13 行 + 桥接代码 ~3 行 + 配置条目）。

---

## 七、约束保证

重构后，以下冗余不可能再出现：

| 冗余场景 | 约束机制 | 违反后果 |
|----------|----------|----------|
| 出现第二条注册路径 | 所有函数必须实现 `IPipelineStage<FunctionContext>`，注册唯一 API：`pipeline.RegisterStage()` | 编译错误（类型不匹配） |
| 注册表和消费表不一致 | 只有一个 `Pipeline` 实例，`RegisterStage()` 和 `Execute()` 在同一个对象上 | 无法不一致 |
| 出现中介接口（类似 `IWorkflowFunctionExecutor`） | `FunctionExecutionSystem` 唯一依赖是 `Pipeline`，无接口可绕 | 无依赖点可插入 |
| 出现 `(object)` 强制转换 | 类型系统不参与桥接，全靠 Pipeline 实例传递 | 无法写出等价代码 |
| 新函数忘记注册 | `Pipeline` 的 `Execute()` 设计为对未注册函数返回失败结果（而非抛异常） | 运行时日志可追踪 |

---

## 八、实施步骤

| 步骤 | 内容 | 预计文件数 |
|------|------|-----------|
| 1 | 新增 `FunctionContext.cs`（统一数据契约） | 1 |
| 2 | 创建 `PipelineFunctions/` 目录，逐个迁移 UIToolbox 函数为 PipelineStage | ~10 |
| 3 | 改造 `FunctionExecutionSystem.cs`（简化 ECS 消费侧） | 1 |
| 4 | 改造 `FrameworkInitializer.cs`（新增 PipelineFunctions 注册步骤，删除 `(object)` 桥接） | 1 |
| 5 | 删除冗余文件 | 3 |
| 6 | 编译验证 | — |

**依赖关系**：步骤 1→2→3→5，步骤 4 可与 2 并行。全部完成后统一编译验证。

---

## 九、风险与回退

| 风险 | 概率 | 影响 | 缓解措施 |
|------|------|------|----------|
| `PipelineStageBase` 对 `CanExecute` 处理与原有逻辑不符 | 中 | 部分函数执行条件变化 | 逐个 Stage UT 覆盖；保留旧 `UIToolbox.cs` 作为对照直至全面验证 |
| ECS 路径的 `ExecutionResultComponent` 输出格式变化 | 低 | Collect() 结果不同 | `FunctionExecutionSystem` 改造时保持 `WriteResult` 输出格式一致 |
| 重构期间编译阻断 | 低 | 无法并行开发 | 一次性完成所有修改后编译，不进行增量编译 |

**回退策略**：保留 `UIToolbox.cs` 和 `FunctionStorageFunctionExecutor.cs` 不移除，仅在注册侧增加条件开关。但由于当前系统已无法正常运行，回退不是优先考虑。

---

> **参考**：[Modules.Pipeline 模块文档](CsharpPrj/Modules/Pipeline/Readme.md) | [PipelineStageBase 接口](CsharpPrj/Modules/Pipeline/Core.cs)