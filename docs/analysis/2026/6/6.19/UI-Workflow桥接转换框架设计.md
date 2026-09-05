# UI-Workflow 桥接转换框架设计

> **版本**：v1.0  
> **日期**：2026-06-17  
> **状态**：设计草案  
> **关联文档**：[事件绑定架构分析与修复方案](./事件绑定架构分析与修复方案.md)

---

## 1. 背景与动机

### 1.1 当前问题

在 `MathFunctionBrowser.json` 和 `MathFunctions.json` 的对比分析中发现：

| 问题 | 描述 | 影响范围 |
|------|------|---------|
| **bindings 信息丢失** | `WinFormsEventBinderAdapter` 获取了 `eventOutput`/`eventInputs` 但未传递给执行层 | 所有 events+bindings 配置 |
| **数据流动无统一模型** | 事件绑定、命令管道、函数执行器之间靠"手递手"传递数据 | 维护和调试成本高 |
| **链式联动脆弱** | MathFunctionBrowser 的三级联动（domain→function→detail）依赖调用链传递数据 | 任一级断裂则整条链路失效 |
| **执行路径不统一** | 脚本命令走 `RunPipeline()`，事件触发走 `WinFormsEventBinderAdapter`，initCommands 又走另一条路径 | 认知负担高 |

### 1.2 解决思路

构建一个 **UI-Workflow Bridge（UWB）** 通用转换框架，作为 UI 配置层和工作流引擎层之间的契约边界：

```
UI 配置层 (events+bindings JSON)
      ↕ 双向转换
┌─────────────────────────┐
│   UI-Workflow Bridge    │  ← 新增：统一的桥接层
└─────────────────────────┘
      ↕ 标准接口
  工作流引擎层 (WorkFlowMethod)
```

### 1.3 设计目标

| 目标 | 说明 |
|------|------|
| **双向转换** | UI 配置 → 工作流节点（正向），工作流定义 → UI 配置（反向） |
| **统一数据流** | 所有事件触发统一为 read→exec→write 三步模型 |
| **消除 bindings 丢失** | 数据通过工作流变量池流动，不依赖调用链传递 |
| **可测试性** | 每个转换组件独立可测 |
| **渐进式迁移** | 与现有系统共存，逐步替换 |

---

## 2. 架构概览

### 2.1 整体架构

```
┌──────────────────────────────────────────────────────────────────┐
│                        UI 配置层 (JSON)                           │
│  MathFunctions.json / MathFunctionBrowser.json / ...              │
│  { events: { click: "Add", bindings: { inputs:[], output:"" } } } │
└──────────────────────────┬───────────────────────────────────────┘
                           │
                           ▼
┌──────────────────────────────────────────────────────────────────┐
│                     UI-Workflow Bridge (UWB)                      │
│                                                                   │
│  ┌──────────────┐   ┌──────────────┐   ┌──────────────────┐      │
│  │  EventMapper │──▶│ NodeBuilder  │──▶│  ResultWriter    │      │
│  │  事件→工作流   │   │  执行管理     │   │  结果→UI控件     │      │
│  └──────────────┘   └──────────────┘   └──────────────────┘      │
│         │                                                         │
│         ▼                                                         │
│  ┌──────────────┐   ┌──────────────┐   ┌──────────────────┐      │
│  │ WorkflowGen  │   │ TemplatePool │   │  ConfigSchema    │      │
│  │ 工作流→UI配置  │   │  模板缓存     │   │  配置校验         │      │
│  └──────────────┘   └──────────────┘   └──────────────────┘      │
└──────────────────────────┬───────────────────────────────────────┘
                           │
                           ▼
┌──────────────────────────────────────────────────────────────────┐
│                     工作流引擎层 (WorkFlowMethod)                   │
│                                                                   │
│  UniversalNode → NodeProcess (四步管线)                            │
│    GetConfig → ExecuteFunc → AddNode → NextNode                   │
│                                                                   │
│  函数注册表: IWorkflowRegistry / WorkflowUseage                    │
│  控件服务: IControlService / IControlFinder                        │
└──────────────────────────────────────────────────────────────────┘
```

### 2.2 核心数据流

#### 正向流（UI 事件 → 工作流执行）

```
用户操作 UI 控件
    ↓
WinFormsEventBinderAdapter 捕获事件
    ↓
EventMapper.MapToUniversalNode(controlName, eventName, functionName, bindings)
    ↓ 输出: UniversalNode
NodeBuilder.ExecuteNodeAsync(universalNode, runtimeValues)
    ↓
NodeProcess.ProcessInputAndAddAICallCell(universalNode)
    ├── GetConfig:  从 input 控件读取值
    ├── ExecuteFunc: 调用注册的函数
    ├── AddNode:     准备输出
    └── NextNode:    后续处理
    ↓
ResultWriter.WriteResult(universalNode, result, container)
    ↓
目标控件显示结果
```

#### 反向流（工作流定义 → UI 配置）

```
ChainInfo / UniversalNode
    ↓
WorkflowGen.GenerateEventConfig(universalNode)
    ↓ 输出: EventConfig { FunctionName, Bindings { Inputs, Output } }
    ↓
WorkflowGen.GenerateUiConfig(chain)
    ↓ 输出: 完整的 UI 配置 JSON 骨架
```

---

## 3. 核心组件设计

### 3.1 EventMapper — 事件映射器

**职责**：将 UI 配置中的 `events + bindings` 映射为工作流引擎可消费的 `UniversalNode`。

```csharp
/// <summary>事件映射器接口</summary>
public interface IEventMapper
{
    /// <summary>将 events+bindings 配置映射为 UniversalNode</summary>
    UniversalNode MapToUniversalNode(
        string controlName,
        string eventName,
        string functionName,
        EventBindings bindings);

    /// <summary>批量映射（用于 initCommands）</summary>
    List<UniversalNode> MapInitCommands(List<InitCommand> commands);
}
```

**映射规则**：

| UI 配置字段 | UniversalNode 字段 | 说明 |
|-------------|-------------------|------|
| `events.{eventName}` 的值 | `Symbol` | 函数名作为节点符号 |
| 触发控件名 + 事件名 | `Label` | 用于调试和追踪 |
| `bindings.inputs[]` | `GetConfig[]` | 每个 input 生成一个 GetInputStrConfig |
| `bindings.inputs[]` | `ExeConfig.ParameterKeys` | 参数键列表 |
| 函数名 | `ExeConfig.FuncPipeline.Calls[0].FuncName` | 要调用的函数 |
| `bindings.output` | `AddConfig.AddConfigAddKey` | 结果写入目标 |
| — | `AddConfig.AddAICallCellInvoke = false` | UI 模式不需要 AICallCell |

**示例映射**：

```json
// UI 配置
{
  "events": {
    "selectedIndexChanged": "Math_GetFunctionsInDomain",
    "bindings": { "inputs": ["domainListView"], "output": "funcListView" }
  }
}
```

```csharp
// 映射后的 UniversalNode
var node = new UniversalNode
{
    Symbol = "Math_GetFunctionsInDomain",
    Label = new List<string> { "domainListView", "selectedIndexChanged" },
    GetConfig = new List<GetInputStrConfig>
    {
        new GetInputStrConfig
        {
            Symbol = "domainListView",
            Getstring = "${domainListView}.Value",
            QuerySteps = null  // 直接值，不需要 AICallFrame 查询
        }
    },
    ExeConfig = new ExecuteConfig
    {
        ParameterKeys = new List<string> { "domainListView" },
        FuncPipeline = new FuncPipelineParameters
        {
            Calls = new List<FuncCall>
            {
                new FuncCall { FuncName = "Math_GetFunctionsInDomain" }
            }
        }
    },
    AddConfig = new AddNodeConfig
    {
        AddConfigAddKey = "funcListView",
        AddAICallCellInvoke = false
    }
};
```

### 3.2 NodeBuilder — 节点构建器

**职责**：管理 `UniversalNode` 的执行生命周期，包括参数准备、执行调度、结果收集。

```csharp
/// <summary>节点构建器接口</summary>
public interface INodeBuilder
{
    /// <summary>执行 UniversalNode</summary>
    Task<object> ExecuteNodeAsync(
        UniversalNode node,
        Dictionary<string, object> runtimeInputValues);

    /// <summary>从 UI 控件读取实时值</summary>
    Dictionary<string, object> ReadControlValues(
        UniversalNode node,
        Control triggerControl);

    /// <summary>快速执行（一步完成 read→exec→write）</summary>
    Task ExecuteFullCycleAsync(
        UniversalNode node,
        Control triggerControl,
        Control containerControl);
}
```

**执行流程**：

```
ExecuteFullCycleAsync:
  1. ReadControlValues(node, triggerControl)
     → 遍历 node.GetConfig，从对应控件读取值
     → 返回 Dictionary<string, object>

  2. ExecuteNodeAsync(node, values)
     → 调用 NodeProcess.ProcessInputAndAddAICallCell(node)
     → 返回执行结果

  3. ResultWriter.WriteResult(node, result, containerControl)
     → 将结果写入 node.AddConfig.AddConfigAddKey 指定的控件
```

### 3.3 ResultWriter — 结果写入器

**职责**：将工作流执行结果写回 UI 控件。

```csharp
/// <summary>结果写入器接口</summary>
public interface IResultWriter
{
    /// <summary>将执行结果写入目标控件</summary>
    void WriteResult(
        UniversalNode node,
        object result,
        Control containerControl);

    /// <summary>将结果写入指定控件</summary>
    void WriteToControl(
        string controlName,
        object value,
        Control containerControl);
}
```

**写入策略**：

| 结果类型 | 目标控件类型 | 写入方式 |
|---------|-------------|---------|
| `string` | TextBox | 直接设置 `.Text` |
| `List<string>` | ListView | 清空并填充 Items |
| `object` | TextBox | `.ToString()` 后设置 `.Text` |
| `null` | 任意 | 跳过写入，记录警告 |

### 3.4 WorkflowGen — 工作流生成器（反向转换）

**职责**：将工作流定义反向生成 UI 配置，支持从现有工作流还原 UI 配置骨架。

```csharp
/// <summary>工作流生成器接口</summary>
public interface IWorkflowGen
{
    /// <summary>从 UniversalNode 生成事件配置</summary>
    EventConfig GenerateEventConfig(UniversalNode node);

    /// <summary>从 ChainInfo 生成完整的 UI 配置</summary>
    UiConfig GenerateUiConfig(ChainInfo chain);

    /// <summary>从 UniversalNode 生成控件定义骨架</summary>
    ControlDefinition GenerateControlDefinition(UniversalNode node);
}
```

**应用场景**：
- 从已有的工作流定义反向生成 UI 配置，实现"配置回读"
- 可视化工作流编辑器的导出功能
- 工作流与 UI 配置的一致性校验

### 3.5 TemplatePool — 模板缓存池

**职责**：缓存常用的工作流节点模板，避免重复构建开销。

```csharp
/// <summary>模板缓存池接口</summary>
public interface ITemplatePool
{
    /// <summary>获取或创建模板</summary>
    UniversalNode GetOrCreate(string key, Func<UniversalNode> factory);

    /// <summary>预置模板</summary>
    UniversalNode GetStandardTemplate();
    UniversalNode GetReadOnlyTemplate();
    UniversalNode GetWriteOnlyTemplate();
}
```

**预置模板**：

| 模板名 | 适用场景 | 示例 |
|--------|---------|------|
| **StandardReadExecWrite** | 标准三步操作 | 按钮点击执行函数 |
| **ReadOnly** | 只执行不写回 | 日志记录、统计更新 |
| **WriteOnly** | 只写回不执行 | 清空、复制到剪贴板 |
| **ChainLink** | 链式联动 | domain→function→detail |

### 3.6 ConfigSchema — 配置校验器

**职责**：校验 UI 配置的完整性和正确性，在转换前发现配置错误。

```csharp
/// <summary>配置校验器接口</summary>
public interface IConfigSchema
{
    /// <summary>校验单个控件的事件配置</summary>
    ValidationResult Validate(ControlConfig control);

    /// <summary>校验整个视图的配置</summary>
    ValidationResult ValidateView(ViewConfig view);

    /// <summary>校验 initCommands</summary>
    ValidationResult ValidateInitCommands(List<InitCommand> commands);
}
```

**校验规则**：

| 规则 | 说明 | 错误级别 |
|------|------|---------|
| 函数名不能为空 | `events.{name}` 的值必须存在 | Error |
| inputs 控件必须存在 | `bindings.inputs` 引用的控件必须在视图中定义 | Error |
| output 控件必须存在 | `bindings.output` 引用的控件必须在视图中定义 | Error |
| 事件名必须支持 | 事件名必须是 WinForms 支持的事件 | Warning |
| 循环依赖检测 | 检测 A→B→A 的循环联动 | Error |

---

## 4. 双向转换流程详解

### 4.1 正向转换：UI 事件 → 工作流执行

```
步骤 1: 事件触发
  WinFormsEventBinderAdapter.BindSingleEvent()
  → 捕获 controlName, eventName, functionName, bindings

步骤 2: EventMapper 转换
  → EventMapper.MapToUniversalNode(controlName, eventName, functionName, bindings)
  → 输出 UniversalNode

步骤 3: NodeBuilder 执行
  → NodeBuilder.ExecuteFullCycleAsync(node, triggerControl, containerControl)
    ├── ReadControlValues: 从 input 控件读取值
    ├── ExecuteNodeAsync: 调用 NodeProcess 四步管线
    └── ResultWriter.WriteResult: 写回 output 控件

步骤 4: 结果呈现
  → 目标控件显示执行结果
```

### 4.2 反向转换：工作流定义 → UI 配置

```
步骤 1: 获取工作流定义
  → ChainInfo / UniversalNode (来自 WorkflowRuntime 或 JSON 文件)

步骤 2: WorkflowGen 转换
  → WorkflowGen.GenerateEventConfig(node)
  → 输出 EventConfig { FunctionName, Bindings }

步骤 3: 生成 UI 配置
  → WorkflowGen.GenerateUiConfig(chain)
  → 输出完整的 UI 配置 JSON

步骤 4: 配置应用
  → 生成的配置可直接用于 FileDriven 框架
```

### 4.3 转换对照表

| UI 配置 | 方向 | 工作流节点 | 说明 |
|---------|------|-----------|------|
| `events.click: "Add"` | → | `UniversalNode.Symbol = "Add"` | 事件映射为节点符号 |
| `bindings.inputs: ["a","b"]` | → | `GetConfig[0..n]` + `ParameterKeys` | 输入映射为读取步骤 |
| `bindings.output: "result"` | → | `AddConfig.AddConfigAddKey = "result"` | 输出映射为写入步骤 |
| `initCommands[0]` | → | `UniversalNode` (无 GetConfig) | 初始化命令映射为只执行节点 |
| `UniversalNode.Symbol` | ← | `events.click: "Symbol"` | 节点符号映射为事件函数名 |
| `GetConfig[].Symbol` | ← | `bindings.inputs[0]` | 读取目标映射为输入绑定 |
| `AddConfig.AddConfigAddKey` | ← | `bindings.output` | 写入目标映射为输出绑定 |

---

## 5. 与现有系统的集成

### 5.1 集成点总览

| 集成点 | 现有组件 | 桥接层对接方式 |
|--------|---------|--------------|
| **事件绑定** | `WinFormsEventBinderAdapter` | 在 `BindSingleEvent` 中调用 `EventMapper` + `NodeBuilder` |
| **控件服务** | `IControlService` | `NodeBuilder.ReadControlValues` 和 `ResultWriter.WriteResult` 通过它读写控件 |
| **控件查找** | `IControlFinder` | 在容器范围内按名称查找控件 |
| **函数注册表** | `IWorkflowRegistry` / `WorkflowUseage` | `ExecuteProcess` 通过它查找并调用函数 |
| **管线执行** | `PipelineExecutor` | 可选：将整个工作流提交给管线执行器 |
| **日志系统** | `FileDrivenLogPlaceholderHandler` | 桥接层执行日志与现有日志系统兼容 |

### 5.2 与 WinFormsEventBinderAdapter 的集成

```csharp
// 修改前：bindings 信息丢失
private void BindSingleEvent(Control control, string eventName, string functionName, EventConfig eventsConfig)
{
    var (eventOutput, eventInputs) = GetBindingsForEvent(eventsConfig, eventName);
    EventHandler handler = (sender, args) =>
    {
        // ★ 问题：只传了 ctrl.Name 和 value，bindings 信息丢失
        _eventPipelineExecutor.Execute(ctrl.Name, eventName.ToLowerInvariant(),
            _controlService.GetValue(ctrl)?.ToString() ?? string.Empty);
    };
}

// 修改后：通过桥接层传递 bindings
private void BindSingleEvent(Control control, string eventName, string functionName, EventConfig eventsConfig)
{
    var (eventOutput, eventInputs) = GetBindingsForEvent(eventsConfig, eventName);
    var bindings = new EventBindings { Inputs = eventInputs, Output = eventOutput };

    EventHandler handler = async (sender, args) =>
    {
        if (IsScriptExecutionMode) return;
        if (sender is not Control ctrl || string.IsNullOrEmpty(ctrl.Name)) return;

        // 通过桥接层执行
        var node = _eventMapper.MapToUniversalNode(ctrl.Name, eventName, functionName, bindings);
        await _nodeBuilder.ExecuteFullCycleAsync(node, ctrl, ctrl.Parent);
    };
}
```

### 5.3 与 NodeProcess 的集成

桥接层直接调用 `NodeProcess.ProcessInputAndAddAICallCell`，复用现有的四步管线：

```
NodeBuilder.ExecuteNodeAsync
  → 准备 ExecutionContext
  → 注入 InputValues（从 UI 控件读取的实时值）
  → NodeProcess.ProcessInputAndAddAICallCell(universalNode)
    ├── GetConfigSkeleton:  从 InputValues 获取参数
    ├── ExecuteFuncSkeleton: 通过 IWorkflowRegistry 调用函数
    ├── AddNodeSkeleton:     准备输出
    └── NextNodeSkeleton:    后续处理
  → 从 ExecutionContext 提取 OutputValues
  → 返回结果
```

### 5.4 与 WorkflowUseage 的集成

桥接层通过 `WorkflowUseage` 访问函数注册表和管线执行器：

```csharp
public class NodeBuilder : INodeBuilder
{
    private readonly IControlService _controlService;
    private readonly IControlFinder _controlFinder;

    public NodeBuilder(IControlService controlService, IControlFinder controlFinder)
    {
        _controlService = controlService;
        _controlFinder = controlFinder;
        // 确保 WorkflowUseage 已初始化
        WorkflowUseage.EnsureInitialized();
    }

    public async Task<object> ExecuteNodeAsync(UniversalNode node, Dictionary<string, object> runtimeInputValues)
    {
        // 将运行时值注入 UniversalNode 的 GetConfig
        foreach (var getConfig in node.GetConfig)
        {
            if (runtimeInputValues.TryGetValue(getConfig.Symbol, out var value))
            {
                getConfig.Getstring = value?.ToString();
            }
        }

        // 调用 NodeProcess 四步管线
        await NodeProcess.ProcessInputAndAddAICallCell(node);

        // 从 WorkflowUseage 获取执行结果
        // 注意：NodeProcess 内部通过 ExecuteProcess 调用 WorkflowUseage 注册的函数
        return node; // 实际结果在 AddNode 步骤中处理
    }
}
```

---

## 6. 文件组织

```
FileDriven/FileDriven.Execution/
  └── WorkflowBridge/                          # 新增：UI-工作流桥接层
      ├── IEventMapper.cs                      # 事件映射器接口
      ├── EventMapper.cs                       # 事件映射器实现
      ├── INodeBuilder.cs                      # 节点构建器接口
      ├── NodeBuilder.cs                       # 节点构建器实现
      ├── IResultWriter.cs                     # 结果写入器接口
      ├── ResultWriter.cs                      # 结果写入器实现
      ├── IWorkflowGen.cs                      # 工作流生成器接口（反向转换）
      ├── WorkflowGen.cs                       # 工作流生成器实现
      ├── ITemplatePool.cs                     # 模板缓存池接口
      ├── TemplatePool.cs                      # 模板缓存池实现
      ├── IConfigSchema.cs                     # 配置校验器接口
      ├── ConfigSchema.cs                      # 配置校验器实现
      ├── Models.cs                            # 桥接层专用模型
      │   ├── EventBindings                    # bindings 配置模型
      │   ├── EventConfig                      # 事件配置模型
      │   ├── InitCommand                      # 初始化命令模型
      │   ├── ValidationResult                 # 校验结果模型
      │   └── UiConfig                         # UI 配置模型
      └── README.md                            # 使用说明
```

---

## 7. 演进路径

### 阶段一：基础桥接（当前 sprint）

| 任务 | 说明 | 优先级 |
|------|------|--------|
| 1. 定义 `Models.cs` | EventBindings、EventConfig、ValidationResult 等基础模型 | P0 |
| 2. 实现 `EventMapper` | events+bindings → UniversalNode 的正向映射 | P0 |
| 3. 实现 `ResultWriter` | 将工作流结果写回 UI 控件 | P0 |
| 4. 实现 `NodeBuilder` | 管理 UniversalNode 的执行生命周期 | P0 |
| 5. 集成到 `WinFormsEventBinderAdapter` | 替换当前的事件处理逻辑 | P0 |

### 阶段二：能力增强（下个 sprint）

| 任务 | 说明 | 优先级 |
|------|------|--------|
| 6. 实现 `TemplatePool` | 缓存常用模板，减少构建开销 | P1 |
| 7. 实现 `ConfigSchema` | 配置校验，提前发现配置错误 | P1 |
| 8. 实现 `WorkflowGen` | 工作流→UI 配置的反向转换 | P2 |
| 9. 单元测试覆盖 | 每个组件独立测试 | P1 |

### 阶段三：全面推广

| 任务 | 说明 | 优先级 |
|------|------|--------|
| 10. 迁移所有现有配置 | 将 FileDriven 下所有 JSON 配置迁移到桥接层 | P2 |
| 11. 性能基准测试 | 对比桥接层与当前实现的性能差异 | P2 |
| 12. 可视化编辑器支持 | 通过 WorkflowGen 支持工作流→UI 配置的可视化编辑 | P3 |

---

## 8. 设计决策记录

| 决策 ID | 决策 | 选项 | 选择理由 |
|---------|------|------|---------|
| ADR-001 | 输入格式选择 `UniversalNode` 而非 `NodeConfig` | UniversalNode / NodeConfig | `NodeProcess` 的四步管线直接消费 `UniversalNode`，无需额外转换层 |
| ADR-002 | AICallCell 模式设为 false | true / false | UI 场景不需要 AICallCell 节点，只关心 read→exec→write |
| ADR-003 | 模板缓存使用 `ConcurrentDictionary` | 字典 / LRU Cache / 无缓存 | 同类型事件共享模板，字典足够且简单 |
| ADR-004 | 反向转换为可选功能 | 必需 / 可选 | 核心路径是正向转换，反向用于可视化编辑和配置导出 |
| ADR-005 | 桥接层放在 `FileDriven.Execution` 下 | 独立项目 / FileDriven.Execution | 与事件绑定、控件服务等紧密耦合，放在同一项目减少跨项目引用 |

---

## 9. 与事件绑定架构修复方案的关系

| 文档 | 关系 | 说明 |
|------|------|------|
| [事件绑定架构分析与修复方案](./事件绑定架构分析与修复方案.md) | **父文档** | 定义了问题现象、根因分析和三阶段演进路径 |
| 本文档 | **子文档/实现方案** | 将父文档的阶段二（工作流替换）具体化为可实现的转换框架 |

**对应关系**：

| 父文档阶段 | 本文档对应 | 说明 |
|-----------|-----------|------|
| 阶段一：短期修复 | 不直接对应 | 父文档的短期修复是独立补丁，桥接层是长期方案 |
| 阶段二：工作流替换 | **核心对应** | EventMapper + NodeBuilder + ResultWriter 实现"将 IFunctionExecutor 替换为 WorkflowRuntime" |
| 阶段三：统一执行模型 | WorkflowGen + TemplatePool | 反向转换和模板缓存支持统一执行模型 |

---

## 10. 附录

### 10.1 术语表

| 术语 | 说明 |
|------|------|
| **UWB** | UI-Workflow Bridge，本文档设计的转换框架 |
| **UniversalNode** | 工作流引擎的通用节点模型，NodeProcess 四步管线的输入 |
| **NodeConfig** | 工作流引擎的节点配置模型，比 UniversalNode 更底层 |
| **EventBindings** | UI 配置中的 bindings 信息（inputs + output） |
| **正向转换** | UI 配置 → 工作流节点的转换方向 |
| **反向转换** | 工作流节点 → UI 配置的转换方向 |

### 10.2 参考文件

| 文件 | 用途 |
|------|------|
| `FileDriven/DrivenConfig/JsonConfig/MathFunctions.json` | 简单场景的 UI 配置示例 |
| `FileDriven/DrivenConfig/JsonConfig/MathFunctionBrowser.json` | 复杂联动场景的 UI 配置示例 |
| `Operation/WorkFlowMethod/Orchestration/Models.cs` | 工作流节点模型定义 |
| `Operation/WorkFlowMethod/ExecutionEngine/NodeProcess.cs` | 四步管线实现 |
| `Operation/WorkFlowMethod/ExecutionEngine/ExecuteProcess.cs` | 函数执行实现 |
| `Operation/WorkFlowMethod/WorkflowUseage.cs` | 工作流前台使用模块 |
| `docs/analysis/事件绑定架构分析与修复方案.md` | 父文档：问题分析与演进路径 |
