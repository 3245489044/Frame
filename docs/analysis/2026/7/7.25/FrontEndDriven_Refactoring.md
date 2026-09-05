# FrontEndDriven 重构手册（更新版）

> 基于 `CsharpPrj/Operation/FrontEndDriven` 源代码分析的 34 项问题。
> 按风险递减排列，每项含问题定位、重构方案、前后对比。
>
> 更新日期：2026-07-25 | 基础分析 + 补充分析 + 新 10 项

---

## 目录

- [FrontEndDriven 重构手册（更新版）](#frontenddriven-重构手册更新版)
  - [目录](#目录)
  - [P0 — 必须修复（可能引起运行时缺陷）](#p0--必须修复可能引起运行时缺陷)
    - [B7. UIReconciler 异常后 `_lastAppliedState` 提前更新](#b7-uireconciler-异常后-_lastappliedstate-提前更新)
    - [B2. ExecuteCommandStage 同步阻塞 async 调用](#b2-executecommandstage-同步阻塞-async-调用)
    - [B4. UnsubscribeSplitterMoved 取消订阅实际未发生](#b4-unsubscribesplittermoved-取消订阅实际未发生)
    - [B5. EventHub 事件字段非线程安全](#b5-eventhub-事件字段非线程安全)
    - [B-NEW5. ApplyValueChange 缺失 Entity 参数导致 ECS 不同步](#b-new5-applyvaluechange-缺失-entity-参数导致-ecs-不同步)
    - [N2. InitializeProtocolLayer 过早 return 导致半初始化](#n2-initializeprotocollayer-过早-return-导致半初始化)
    - [N5. Dispose 中事件清理不完整导致内存泄漏风险](#n5-dispose-中事件清理不完整导致内存泄漏风险)
    - [N6. UIReconciler ToDictionary 缺少重复键保护](#n6-uireconciler-todictionary-缺少重复键保护)
    - [N7. HandleNonEventCommand handler 结果未完全同步回 ctx](#n7-handlenoneventcommand-handler-结果未完全同步回-ctx)
    - [N9. WinFormsOutputPort Apply catch 块无日志记录](#n9-winformsoutputport-apply-catch-块无日志记录)
  - [P1 — 建议修复（代码正确性/可维护性）](#p1--建议修复代码正确性可维护性)
    - [R2. TemplatePreprocessSkeleton 同步包装](#r2-templatepreprocessskeleton-同步包装)
    - [R3. WinFormsRenderBridge 三重冗余保底事件通道](#r3-winformsrenderbridge-三重冗余保底事件通道)
    - [R1. ControlSystemBase 孤类](#r1-controlsystembase-孤类)
    - [R4. ContextKeys 无类型安全](#r4-contextkeys-无类型安全)
    - [B6. 容器类型定义双处不同步](#b6-容器类型定义双处不同步)
    - [B-NEW1. ExecuteCommandStage 嵌套分支结构导致误判风险](#b-new1-executecommandstage-嵌套分支结构导致误判风险)
    - [B-NEW3. GetDiffSummary 跨域比较导致误报](#b-new3-getdiffsummary-跨域比较导致误报)
    - [B-NEW4. 审计报告异常降级后 PipelineCompleted 仍执行](#b-new4-审计报告异常降级后-pipelinecompleted-仍执行)
    - [N1. WinFormsOutputPort 构造函数参数过多（9个）](#n1-winformsoutputport-构造函数参数过多9个)
    - [N3. RenderAsync 与 ExecuteCreateStageInternal 代码重复](#n3-renderasync-与-executecreatestageinternal-代码重复)
  - [P2 — 按需清理（代码整洁度）](#p2--按需清理代码整洁度)
    - [D1. 事件标准类型遍历双重 Contains](#d1-事件标准类型遍历双重-contains)
    - [D2. UIReconciler 中独立的 GetDiffSummary 逻辑](#d2-uireconciler-中独立的-getdiffsummary-逻辑)
    - [D3. RecalculateAll 模式可提取公共方法](#d3-recalculateall-模式可提取公共方法)
    - [D4/D5. 契约工厂抽取公共方法](#d4d5-契约工厂抽取公共方法)
    - [D7. 日志记录器类合并](#d7-日志记录器类合并)
    - [R5. Initialize 三层命名与实际顺序不符](#r5-initialize-三层命名与实际顺序不符)
    - [R6. ApplyChanges 中死代码分支](#r6-applychanges-中死代码分支)
    - [R7. ConfigLoadInput 单属性薄包装](#r7-configloadinput-单属性薄包装)
    - [B-NEW2. EventBindingFormatter.Capitalize 驼峰格式化不一致](#b-new2-eventbindingformattercapitalize-驼峰格式化不一致)
    - [R-NEW1. ExecuteCommandStage SharedData 复制模式重复](#r-new1-executecommandstage-shareddata-复制模式重复)
    - [R-NEW2. EcsRenderPipeline 守卫代码重复](#r-new2-ecsrenderpipeline-守卫代码重复)
    - [R-NEW3. 首次/后续渲染状态双份管理](#r-new3-首次后续渲染状态双份管理)
    - [R-NEW4. Pending 组件四件套重复创建](#r-new4-pending-组件四件套重复创建)
    - [R-NEW5. Dock/Anchor 允许值私有定义重复](#r-new5-dockanchor-允许值私有定义重复)
    - [N4. CsgEntryPoint 重复 Entity/Component 查询](#n4-csgentrypoint-重复-entitycomponent-查询)
    - [N8. CollectEventBindingProps 使用字符串字面量而非 UIProtocol 常量](#n8-collecteventbindingprops-使用字符串字面量而非-uiprotocol-常量)
    - [N10. EcsRenderPipeline 私有字段过多（16个）](#n10-ecsrenderpipeline-私有字段过多16个)
  - [附录：重构优先级汇总](#附录重构优先级汇总)
    - [已实施修复标记](#已实施修复标记)

---

## P0 — 必须修复（可能引起运行时缺陷）

### B7. UIReconciler 异常后 `_lastAppliedState` 提前更新

**状态：已修复**

**定位**：
- `WinFormsOutputPort.cs` L382-402：try-catch 包围 `_reconciler.Apply(targetForm)`
- `UIReconciler.cs` L72：`_lastAppliedState = targetState;` **在 Apply 方法末尾无条件执行**，即使在 Apply 过程中抛出异常

**问题**：
Apply 抛出异常时，`_lastAppliedState` 已被置为 `targetState`，但 `_isFirstApply = false`。重试时走差异路径而非全量路径，可能错过控件变化。

**方案（已实施）**：
将 `_lastAppliedState` 的更新分离到 `ConfirmApplied` 方法，由调用方在确认无异常后调用。

---

### B2. ExecuteCommandStage 同步阻塞 async 调用

**状态：已修复**

**定位**：`ExecuteCommandStage.cs` L94-98

**方案（已实施）**：改为纯 await。

---

### B4. UnsubscribeSplitterMoved 取消订阅实际未发生

**状态：已修复**

**定位**：`WinFormsOutputPort.cs` L303-306

**方案（已实施）**：委托给 `_adapter.UnsubscribeSplitterMoved` 实现。

---

### B5. EventHub 事件字段非线程安全

**状态：已修复**

**定位**：`EventHub.cs` L20-35

**方案（已实施）**：捕获委托副本后调用。

---

### B-NEW5. ApplyValueChange 缺失 Entity 参数导致 ECS 不同步

**定位**：`Ports.cs` L346-351

```csharp
public void ApplyValueChange(object control, object? value)
```

**问题**：
方法签名只接收 `object control` 和 `object? value`，**无法将值变更写回 ECS Component**。调用方在设置控件值后，ECS 侧 `ControlValueComponent` 等保持旧值，下次 Collect() 会重新应用旧值覆盖刚设定的新值。

这在双向绑定的场景中（如 TextBox 文本变化）会导致：用户在 UI 中输入新值 → ApplyValueChange 设置到 WinForms 控件 → 管线下次执行时 Collect() 从 ECS Component 读取旧值 → 生成的 CanonicalForm 包含旧值 → Reconcile 用旧值覆盖用户刚输入的内容。

**方案**：
添加 `Entity entity` 参数，值变更后同步到 ECS Component：

```csharp
// 改前
public void ApplyValueChange(object control, object? value)

// 改后
public void ApplyValueChange(Entity entity, object control, object? value)
{
    if (control == null)
        throw new ArgumentNullException(nameof(control));

    _valueAccessor.SetValue(control, value);

    // 同步回 ECS Component
    if (entity.IsValid)
    {
        _entityManager.AddOrSetComponent(entity, new ControlValueComponent
        {
            Value = value?.ToString()
        });
    }
}
```

---

### N2. InitializeProtocolLayer 过早 return 导致半初始化

**文件**：`EcsRenderPipeline.cs` L246  
**类型**：错误逻辑（静默失败的半初始化）

```csharp
private void InitializeProtocolLayer()
{
    if (_entityManager == null || _controlPool == null || _configDrivenFactory == null)
        return;  // ← 静默返回，_initialized 不会被设置
    // ...
}
```

`Initialize()` L184 在调用该方法后设置 `_initialized = true`，但由于此处的 `return` 跳过了协议层初始化，`_csgEntryPoint`、`_winFormsOutputPort`、`_ecsInputPort` 等关键依赖全部为 null。后续调用 `ExecuteCreationStage()` 或 `ExecuteRenderStage()` 时，`_capabilityApplier?.Update()` 的 null 条件运算符会静默跳过，管线不执行任何操作。

**方案**：将 return 改为抛出 `InvalidOperationException`，或删除此前置检查（由调用方 `Initialize()` 的调用顺序保证）。

---

### N5. Dispose 中事件清理不完整导致内存泄漏风险

**文件**：`EcsRenderPipeline.cs` L768-777  
**类型**：错误逻辑（潜在内存泄漏）

```csharp
public void Dispose()
{
    if (_disposed) return;
    RenderCompleted = null;   // ← 仅清理了这一个事件
    _world?.Dispose();
    _initialized = false;
    _disposed = true;
    GC.SuppressFinalize(this);
}
```

`ConversionCompleted`（L89: `event Action<Entity>?`）和 `PipelineCompleted`（L92: `event Action?`）未置 null。外部订阅者在 Dispose 后仍持有对 EcsRenderPipeline 实例的引用，阻止 GC 回收。

**方案**：在 Dispose 中将三个事件全部置 null。

---

### N6. UIReconciler ToDictionary 缺少重复键保护

**文件**：`UIReconciler.cs` L94-95  
**类型**：错误逻辑（运行时异常风险）

```csharp
var currentNodes = current.Nodes.ToDictionary(n => n.Id);
var targetNodes = target.Nodes.ToDictionary(n => n.Id);
```

如果 `CanonicalForm.Nodes` 中存在重复的 `Id`，`ToDictionary` 会抛出 `ArgumentException`。当前代码没有任何预防措施或保护逻辑。

**方案**：使用 `ToDictionary(n => n.Id, n => n, StringComparer.OrdinalIgnoreCase)` 的重载 + try-catch，或在 CollectAllInOnePass 中确保 Id 唯一性。

---

### N7. HandleNonEventCommand handler 结果未完全同步回 ctx

**文件**：`ExecuteCommandStage.cs` L197-218  
**类型**：错误逻辑

```csharp
handler(context);
// handler 执行后，结果通过 SharedData 同步
ctx.FunctionResultValue = context.FunctionResultValue;
// ← 缺少 FunctionExecuteSuccess 和 FunctionErrorMessage 的同步
```

`CommandContext` 有 `FunctionResultValue` 属性（L218 已同步），但没有同步 `FunctionExecuteSuccess` 和 `FunctionErrorMessage`。如果 handler 设置了错误状态，ctx 不会感知，外层的 `ctx.SetStageResult(Name, ctx.FunctionExecuteSuccess, ctx.FunctionErrorMessage)`（L80）可能错误地报告成功。

**方案**：在调用 handler 后同步全部三个字段。需要额外检查 `CommandContext` 是否具有这两个属性，如果没有，则用返回值机制或异常处理。

---

### N9. WinFormsOutputPort Apply catch 块无日志记录

**文件**：`Ports.cs` L390-395  
**类型**：错误逻辑（调试困难）

```csharp
try
{
    LastAppliedNodeCount = _reconciler.Apply(targetForm);
}
catch
{
    // ← 注释说"不修改状态"，但 catch 块为空
    throw;
}
```

虽然重新抛出异常保留了调用栈，但没有记录任何日志。异常发生时，调用方（EcsRenderPipeline.ExecuteRenderStage L400）会重新包装异常并记录，但无法获取 `_reconciler.Apply` 失败的具体上下文。

**方案**：在 catch 中添加日志记录后再重新抛出。

---

## P1 — 建议修复（代码正确性/可维护性）

### R2. TemplatePreprocessSkeleton 同步包装

**状态：未修复**（需确认基类接口约束）

**定位**：`TemplatePreprocessSkeleton.cs` L29-31

### R3. WinFormsRenderBridge 三重冗余保底事件通道

**状态：已修复**

**定位**：`WinFormsRenderBridge.cs` L107-162

**方案（已实施）**：移除 `Application.Idle` 通道和 5s 超时，仅保留 `HandleCreated` 通道。

---

### R1. ControlSystemBase 孤类

**状态：已修复**

**定位**：`ControlRender/Systems/ControlSystemBase.cs`

**方案（已实施）**：删除该文件。

---

### R4. ContextKeys 无类型安全

**定位**：`CommandExecution/Context/ContextKeys.cs`

17 个 `public const string` 键，使用者通过 `_contextFrame.GetData<T>(ContextKeys.Xxx)` 访问。

**方案**：使用 Typed Key 模式

```csharp
public static class ContextKeys
{
    public static readonly ContextKey<Form> TargetForm = new("TargetForm");
    // ...
}
```

### B6. 容器类型定义双处不同步

**状态：已修复**

**定位**：
- `ConfigDrivenControlFactory.cs` L80-84
- `StandardControlStrategies.RegisterAll()`

**方案（已实施）**：提取到 `FrontEndDrivenConstants.ContainerTypes` 公共常量。

---

### B-NEW1. ExecuteCommandStage 嵌套分支结构导致误判风险

**定位**：`ExecuteCommandStage.cs` L112-143

```csharp
if (_functionRegistry != null && _functionRegistry.Contains(target))
{
    try
    {
        if (_functionRegistry.TryGetDelegate(target, out var del) && del != null)
        {
            var execResult = await _functionExecutor.ExecuteAsync(del, ctx.ParsedParameters);
            // ...
        }
        else
        {
            ctx.FunctionExecuteSuccess = false;       // ← L130-132: 内部 else
            ctx.FunctionErrorMessage = $"Function '{target}' delegate is null";
        }
    }
    catch (Exception ex)
    {
        ctx.FunctionExecuteSuccess = false;            // ← L134-138: 内部 catch
        ctx.FunctionErrorMessage = $"Function '{target}' execution error: {ex.Message}";
    }
}
else
{
    ctx.FunctionExecuteSuccess = false;                // ← L140-144: 外部 else
    ctx.FunctionErrorMessage = $"No binding found for {target}.{action}";
}
```

**问题**：
- 内部 if-else 嵌套在外部 if-else 之内，结构深度达 **4 层**
- 三个错误路径各自独立设置 `FunctionExecuteSuccess` 和 `FunctionErrorMessage`，模式重复
- L130-132 的 `else` 和 L140 的 `else` 在缩进上难以区分，维护者容易误判分支归属

**方案**：将 `_functionRegistry` 分支抽取为独立方法

```csharp
private async Task ExecuteDirectFunctionAsync(
    CommandPipelineContext ctx,
    string target,
    CancellationToken cancellationToken)
{
    if (_functionRegistry == null || !_functionRegistry.Contains(target))
    {
        ctx.FunctionExecuteSuccess = false;
        ctx.FunctionErrorMessage = $"No binding found for {target}.{ctx.ParsedAction}";
        return;
    }

    if (!_functionRegistry.TryGetDelegate(target, out var del) || del == null)
    {
        ctx.FunctionExecuteSuccess = false;
        ctx.FunctionErrorMessage = $"Function '{target}' delegate is null";
        return;
    }

    try
    {
        var execResult = await _functionExecutor.ExecuteAsync(del, ctx.ParsedParameters);
        ctx.FunctionExecuteSuccess = execResult.IsSuccess;
        ctx.FunctionResultValue = execResult.Value?.ToString();
        if (!execResult.IsSuccess)
            ctx.FunctionErrorMessage = execResult.ErrorMessage;
    }
    catch (Exception ex)
    {
        ctx.FunctionExecuteSuccess = false;
        ctx.FunctionErrorMessage = $"Function '{target}' execution error: {ex.Message}";
    }
}
```

### B-NEW3. GetDiffSummary 跨域比较导致误报

**定位**：`UIReconciler.cs` L237

```csharp
public string GetDiffSummary()
{
    if (_lastAppliedState == null) return "尚未应用任何状态";
    var currentState = GetCurrentState();
    var changes = ComputeChanges(_lastAppliedState, currentState);
    // ...
}

public CanonicalForm GetCurrentState()
{
    return _adapter.ToCanonical();  // ← 从 WinForms 控件实时读取
}
```

**问题**：
- `_lastAppliedState` 存储的是 **ECS 侧**上次成功应用的 CanonicalForm
- `GetCurrentState()` 通过 `_adapter.ToCanonical()` 实时读取 **WinForms 控件**的状态
- 如果 ECS Component 和 WinForms 控件属性不同步（例如值已变更但未回写 Component），diff 结果会显示误报的差异

**方案**：
1. 添加跨域比较的注释说明
2. 可选：增加一个 `_csgEntryPoint.Collect()` 的实时 ECS 状态作为中间对比

```csharp
/// <summary>
/// 获取当前 ECS 状态摘要（用于与上次应用状态比较）
/// <para>注意：当前实现通过 WinFormsAdapter 实时读取控件状态，</para>
/// <para>与 ECS Component 中缓存的 _lastAppliedState 可能存在跨域差异。</para>
/// <para>如需精确的 ECS 侧差异，应使用 _csgEntryPoint.Collect() 的输出。</para>
/// </summary>
public string GetDiffSummary()
{
    // ...
}
```

---

### B-NEW4. 审计报告异常降级后 PipelineCompleted 仍执行

**定位**：`EcsRenderPipeline.cs` L420-457

```csharp
PipelineCompleted?.Invoke();  // ← L420: 已执行

if (_csgEntryPoint != null && _winFormsOutputPort != null)
{
    try
    {
        var auditReport = RenderAuditPort.Collect(...);
        // ...
    }
    catch (Exception ex)
    {
        FrontEndDrivenLog.Log("EcsRenderPipeline_AuditReport_Error", ...);
        // ← 异常被吞掉，外部无法感知审计失败
    }
}
```

**问题**：
- `PipelineCompleted` 事件在审计报告之前触发，事件订阅者看到的是"管线已完成"信号，但不知道审计是否成功
- 审计异常被 `catch` 吞掉，仅输出日志，不暴露给调用方

**方案**：
交换顺序，审计先执行再触发 PipelineCompleted：

```csharp
// 改前
PipelineCompleted?.Invoke();
try { /* 审计 */ } catch { /* 吞掉 */ }

// 改后
try { /* 审计 */ } catch
{
    FrontEndDrivenLog.Log("EcsRenderPipeline_AuditReport_Error", ...);
    // 可通过事件通知审计失败
    AuditFailed?.Invoke(this, EventArgs.Empty);
}
PipelineCompleted?.Invoke();
```

### N1. WinFormsOutputPort 构造函数参数过多（9个）

**文件**：`Ports.cs` L231-248  
**类型**：冗余逻辑（违反单一职责 + 构造函数参数过多反模式）

构造函数需要 9 个参数：`winFormsAdapter, reconciler, csg, valueAccessor, entityManager, controlPool, adapter, eventBindHandler`。过多的构造函数参数表明该类的职责边界过宽。

**方案**：将参数分组为值对象（如 `WinFormsOutputPortDependencies`），或将 IControlCreationOutput、IEventSubscriptionOutput、IPropertyApplyOutput、IEventBindingOutput 四个接口拆分为独立的输出处理器。

---

### N3. RenderAsync 与 ExecuteCreateStageInternal 代码重复

**文件**：`EcsRenderPipeline.cs` L516-586  
**类型**：重复逻辑

两个方法都执行了几乎相同的初始化流程：

| 步骤 | RenderAsync | ExecuteCreateStageInternal |
|------|-------------|---------------------------|
| Initialize | ✅ L528 | ✅ L574 |
| CreateRootEntity | ✅ L532 | ✅ L575 |
| RegisterRootContainer | ✅ L533 | ✅ L577-580 |
| ConvertChildren | ✅ L534 | ✅ L581 |
| 执行管线 | ✅ ExecutePipeline() (L536) | ✅ ExecuteCreationStage() (L583) |

差异仅在于 `RenderAsync` 执行完整管线（Create + Render），而 `ExecuteCreateStageInternal` 只执行创建阶段。

**方案**：`ExecuteCreateStageInternal` 应委托给 `RenderAsync` 的前半部分，或将公共流程提取为 `PrepareAndExecute(Action<Entity> stageAction)` 方法。

---

## P2 — 按需清理（代码整洁度）

### D1. 事件标准类型遍历双重 Contains

**状态：已修复**

**定位**：`EventBindingFormatter.cs` L22-34

**方案（已实施）**：使用 `processedTypes` 集合记录已处理事件，消除二次 `Contains` 查询。

---

### D2. UIReconciler 中独立的 GetDiffSummary 逻辑

**状态：已修复**

**定位**：`UIReconciler.cs` L225-249

**方案（已实施）**：委托 `ComputeChanges` 实现。

---

### D3. RecalculateAll 模式可提取公共方法

**定位**：`LayoutSystem.cs` + `SplitContainerSystem.cs`

**方案**：提取公共方法到 EntityManager 扩展。

---

### D4/D5. 契约工厂抽取公共方法

**状态：已修复**

**定位**：`ContractBuilder.cs` L113-149

**方案（已实施）**：抽取 `SimpleTextControl()` 和 `RangeControl()` 公共工厂方法。

---

### D7. 日志记录器类合并

**定位**：`Shared/Diagnostics/` 下 8 个 Logger 文件

**方案**：合并为 1-2 个通用 Logger。

---

### R5. Initialize 三层命名与实际顺序不符

**状态：已修复**

**定位**：`EcsRenderPipeline.cs` L176-275

**方案（已实施）**：`InitializePorts` → `InitializeProtocolLayer`，`InitializeSystems` → `InitializeSystemLayer`。

---

### R6. ApplyChanges 中死代码分支

**状态：已修复**

**定位**：`UIReconciler.cs` L151-180

---

### R7. ConfigLoadInput 单属性薄包装

**定位**：`ConfigLoadInput.cs`

**方案**：评估使用场景，考虑直接使用 `ConcurrentDictionary` 替代。

---

### B-NEW2. EventBindingFormatter.Capitalize 驼峰格式化不一致

**定位**：`EventBindingFormatter.cs` L48-52

```csharp
private static string Capitalize(string name)
{
    if (string.IsNullOrEmpty(name)) return name;
    return char.ToUpper(name[0]) + name[1..];
}
```

**问题**：
- `doubleclick` → `Doubleclick`（首字母大写），而非期望的 `DoubleClick`（驼峰）
- `selectedindexchanged` → `Selectedindexchanged`，而非 `SelectedIndexChanged`
- 如果下游通过格式化后的事件名做字符串匹配，所有多词事件名都会匹配失败

**影响事件**：`doubleclick`, `textchanged`, `selectedindexchanged`, `checkedchanged`, `valuechanged`

**方案**：
使用显式映射表或正则分割驼峰：

```csharp
// 方案A：显式映射表（推荐，无歧义）
private static readonly Dictionary<string, string> EventNameCasingMap = new(StringComparer.OrdinalIgnoreCase)
{
    ["click"] = "Click",
    ["doubleclick"] = "DoubleClick",
    ["textchanged"] = "TextChanged",
    ["selectedindexchanged"] = "SelectedIndexChanged",
    ["checkedchanged"] = "CheckedChanged",
    ["valuechanged"] = "ValueChanged"
};

// 方案B：正则分割（自动适用，新事件类型无需维护）
private static string ToPascalCase(string name)
{
    if (string.IsNullOrEmpty(name)) return name;
    return Regex.Replace(name, @"(^|_|\s+)([a-z])",
        m => m.Groups[2].Value.ToUpper());
}
```

---

### R-NEW1. ExecuteCommandStage SharedData 复制模式重复

**定位**：`ExecuteCommandStage.cs` L195-206

```csharp
// 循环复制所有 SharedData
foreach (var kvp in ctx.SharedData)
{
    context.SharedData[kvp.Key] = kvp.Value;
}
// 3 行独立赋值
if (ctx.TargetForm != null)
    context.SharedData[ContextKeys.TargetForm] = ctx.TargetForm;
if (ctx.FunctionExecutor != null)
    context.SharedData["FunctionExecutor"] = ctx.FunctionExecutor;
if (ctx.BindingResolver != null)
    context.SharedData["BindingResolver"] = ctx.BindingResolver;
```

**问题**：
- 循环复制所有 kvp 后，又用 3 行独立复制个别键，分为"批量复制"和"特定键复制"两段
- 未来新增特定键时需要在两段都关注：是否已被循环覆盖，是否需要单独处理
- 模式重复：3 行独立赋值的结构完全相同

**方案**：
提取为扩展方法：

```csharp
public static class CommandContextExtensions
{
    public static void MergeFrom(this CommandContext target, CommandPipelineContext source,
        params string[] additionalKeys)
    {
        foreach (var kvp in source.SharedData)
            target.SharedData[kvp.Key] = kvp.Value;

        // 补充特定键（SharedData 中缺失的特征数据）
        void CopyIfNotNull<T>(T? value, string key) where T : class
        {
            if (value != null) target.SharedData[key] = value;
        }

        foreach (var key in additionalKeys)
        {
            switch (key)
            {
                case nameof(ContextKeys.TargetForm):
                    CopyIfNotNull(source.TargetForm, ContextKeys.TargetForm);
                    break;
                case "FunctionExecutor":
                    CopyIfNotNull(source.FunctionExecutor, "FunctionExecutor");
                    break;
                case "BindingResolver":
                    CopyIfNotNull(source.BindingResolver, "BindingResolver");
                    break;
            }
        }
    }
}

// 调用处
var context = new CommandContext { ... };
context.MergeFrom(ctx, ContextKeys.TargetForm, "FunctionExecutor", "BindingResolver");
```

### R-NEW2. EcsRenderPipeline 守卫代码重复

**定位**：`EcsRenderPipeline.cs`（涉及 7 个公共方法）

```csharp
public void Clear()
{
    ThrowIfDisposed();       // ← 重复
    if (!_initialized) return;  // ← 重复
    // ...
}

public void RecalculateLayout()
{
    ThrowIfDisposed();       // ← 重复
    EnsureInitialized();     // ← 重复
    // ...
}

public Entity GetEntity(string controlName)
{
    ThrowIfDisposed();       // ← 重复
    EnsureInitialized();     // ← 重复
    // ...
}
```

**涉及方法**：`Clear`, `RecalculateLayout`, `GetEntity`, `GetControlName`, `RegisterControl`, `UnregisterControl`, `ExecuteCreationStage`, `ExecuteRenderStage`

**方案**：
提取为 `Guard()` 方法：

```csharp
// 方案A：条件守卫
private void Guard(bool requireInitialized = true)
{
    ThrowIfDisposed();
    if (requireInitialized)
        EnsureInitialized();
}

// 方案B（激进）：返回 bool 的 TryGuard，方法内提前 return
private bool TryGuard(bool requireInitialized = true)
{
    if (_disposed) return false;
    if (requireInitialized && !_initialized) return false;
    return true;
}
```

### R-NEW3. 首次/后续渲染状态双份管理

**定位**：`WinFormsOutputPort.cs` L213 → `_isFirstApply` / `UIReconciler.cs` L26 → `_initialized`

**问题**：
- `WinFormsOutputPort` 维护了 `_isFirstApply` 标记（决定首次 Apply 后是否执行 `SyncActualContainerSizes`）
- `UIReconciler` 维护了 `_initialized` 标记（决定首次 Apply 是全量还是差异路径）
- 两者必须保持同步：`_isFirstApply = false` 应在 `_initialized = true` 的同时设置
- 当前通过 `WinFormsOutputPort.Apply` 的 try-catch 确保，但两个标记分属不同对象，增加了维护成本

**方案**：
统一由一个组件管理：

```csharp
// WinFormsOutputPort 不再维护 _isFirstApply
// UIReconciler 新增 IsFirstApply 属性，供 WinFormsOutputPort 查询
public bool IsFirstApply => !_initialized;
```

### R-NEW4. Pending 组件四件套重复创建

**定位**：`ConfigDrivenControlFactory.cs` L179-183

```csharp
// 每个 Entity 创建时都要重复这 4 行
_entityManager.AddComponent(entity, new PendingCapabilityComponent());
_entityManager.AddComponent(entity, new PendingLayoutComponent());
_entityManager.AddComponent(entity, new PendingStyleComponent());
_entityManager.AddComponent(entity, new PendingEventBindingComponent());
```

**问题**：
- 当前只在 `ConvertControl` 方法中调用，但后期如果在 `ConvertView` 或 `CreateSplitterPanelEntity` 中也需要创建 Entity，需要重复写这 4 行
- 新增 Pending 组件类型时，需要找到所有创建 Entity 的地方逐一添加

**方案**：
提取为方法：

```csharp
private void AddPendingComponents(Entity entity)
{
    _entityManager.AddComponent(entity, new PendingCapabilityComponent());
    _entityManager.AddComponent(entity, new PendingLayoutComponent());
    _entityManager.AddComponent(entity, new PendingStyleComponent());
    _entityManager.AddComponent(entity, new PendingEventBindingComponent());
}
```

### R-NEW5. Dock/Anchor 允许值私有定义重复

**定位**：`ContractBuilder.cs` L25-32

```csharp
private static readonly HashSet<string> DockAllowedValues = new(StringComparer.OrdinalIgnoreCase)
    { "none", "top", "bottom", "left", "right", "fill" };

private static readonly HashSet<string> AnchorAllowedValues = new(StringComparer.OrdinalIgnoreCase)
    { "top", "left", "right", "bottom",
      "top, left", "top, right", "bottom, left", "bottom, right",
      "top, bottom, left, right" };
```

**问题**：
- 这些允许值是全局通用的规则约束，但声明为 `private static readonly` 仅 `ContractBuilder` 可见
- 其他需要校验 Dock/Anchor 值的模块（如 `Shared/ContractValidator.cs`）必须重复定义

**方案**：
提取到公共常量类（参考 `FrontEndDrivenConstants.ContainerTypes` 的修复模式）：

```csharp
// FrontEndDrivenConstants.cs 追加
public static class LayoutConstants
{
    public static readonly HashSet<string> DockAllowedValues = new(StringComparer.OrdinalIgnoreCase)
        { "none", "top", "bottom", "left", "right", "fill" };

    public static readonly HashSet<string> AnchorAllowedValues = new(StringComparer.OrdinalIgnoreCase)
        { "top", "left", "right", "bottom",
          "top, left", "top, right", "bottom, left", "bottom, right",
          "top, bottom, left, right" };
}
```

### N4. CsgEntryPoint 重复 Entity/Component 查询

**文件**：`Ports.cs` L78-82  
**类型**：冗余逻辑

```csharp
var entities = _entityManager.GetEntities<ControlTypeComponent>();  // 查询①
foreach (var entity in entities)
{
    var typeComp = _entityManager.GetComponent<ControlTypeComponent>(entity);  // 查询②
```

`GetEntities<T>()` 返回所有拥有 Component T 的 Entity 列表，然后循环中又对每个 Entity 执行 `GetComponent<T>()`。如果 EntityManager 内部实现中 `GetEntities` 已经包含了 Component 数据，第二次查找是冗余的。

**方案**：在 EntityManager 层提供 `GetEntitiesWithComponent<T>()` 返回 `(Entity, T)` 元组列表，消除二次查询。

### N8. CollectEventBindingProps 使用字符串字面量而非 UIProtocol 常量

**文件**：`Ports.cs` L188-189  
**类型**：冗余逻辑（编码风格不一致）

```csharp
mergedProps["_eventBindings"] = eventNames;   // ← 字符串字面量
mergedProps["_functionNames"] = functionNames; // ← 字符串字面量
```

而同一个类的其他属性设置都使用 `UIProtocol.Prop_*` 或 `UIProtocol.Meta_*` 常量（L90-91, L96-100, L135-138, L150-159, L174）。

**方案**：在 `UIProtocol` 中添加 `Prop_EventBindings` 和 `Prop_FunctionNames` 常量，并在此处引用。

### N10. EcsRenderPipeline 私有字段过多（16个）

**文件**：`EcsRenderPipeline.cs` L53-84  
**类型**：冗余逻辑

16 个私有字段，按功能可以分为4组：
- **ECS 核心**（4个）：`_world`, `_entityManager`, `_controlPool`, `_controlFactory`
- **配置工厂**（1个）：`_configDrivenFactory`
- **System 实例**（5个）：`_capabilityApplier`, `_layoutSystem`, `_splitContainerSystem`, `_styleSystem`, `_eventBindingSystem`
- **CSG 协议层**（6个）：`_csg`, `_winFormsAdapter`, `_uiReconciler`, `_ecsInputPort`, `_csgEntryPoint`, `_winFormsOutputPort`

**方案**：使用嵌套值对象分组（如 `CoreDependencies`, `SystemDependencies`），减少顶级字段数量，同时使初始化顺序的依赖关系更清晰。

---

## 附录：重构优先级汇总

| 优先级 | 项数 | 新增项 | 估算工时 |
|--------|------|--------|----------|
| **P0** | 10 | B-NEW5 + N2/N5/N6/N7/N9（5项） | 5h |
| **P1** | 10 | B-NEW1/B-NEW3/B-NEW4 + N1/N3（2项） | 7h |
| **P2** | 14 | B-NEW2/R-NEW1~5 + N4/N8/N10（3项） | 8h |
| **合计** | **34** | **10 项新增** | **20h** |

### 已实施修复标记

以下 11 项已在当前代码中修复，无需重复操作：

| 项 | 状态 | 备注 |
|----|------|------|
| B7 | ✅ 已修复 | ConfirmApplied 分离 |
| B2 | ✅ 已修复 | 纯 await |
| B4 | ✅ 已修复 | 委托 Adapter |
| B5 | ✅ 已修复 | Volatile.Read |
| R3 | ✅ 已修复 | 移除冗余通道 |
| R1 | ✅ 已修复 | 删除孤类 |
| B6 | ✅ 已修复 | 公共常量 |
| D1 | ✅ 已修复 | processedTypes |
| D2 | ✅ 已修复 | 委托 ComputeChanges |
| D4/D5 | ✅ 已修复 | 公共工厂方法 |
| R5 | ✅ 已修复 | 重命名 |

---

*生成日期：2026-07-25 | 基础分析 + 补充分析 + 新 10 项*