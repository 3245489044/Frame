# FunctionStorage 与 FunctionExecution — 工作流执行层与 UI 执行层介绍

## 一、概述

**FunctionExecution**（`BasicMethod/FunctionExecution`）是 WorkFlow 体系的**基础函数执行框架**，提供函数注册、参数解析、委托调用等核心能力，是所有业务函数执行的底层引擎。

**FunctionStorage**（`FunctionStorage`）是**函数存储与 UI 执行桥接层**，位于 FunctionExecution 之上，承担三项职责：函数库注册、JSON 工作流执行、UI 事件桥接。

```
WdUI (UI 框架层)           → 动态渲染界面
     ↑ 事件/数据
FunctionStorage (桥接层)   → 函数库、UI 桥接、JSON 工作流执行
     ↑ 依赖
FunctionExecution (执行层) → 函数注册、参数解析、委托调用
     ↑ 依赖
WorkFlowMethod (引擎层)    → 工作流编排、管线执行
```

---

## 二、FunctionExecution — 基础函数执行框架

### 2.1 核心架构

围绕四个核心概念构建：

| 组件 | 说明 |
|------|------|
| **IFunctionRegistry** | 函数注册表接口，支持 Register / Unregister / Contains / Call |
| **FunctionDescriptor** | 函数元数据：名称、参数列表、返回值类型、异步标记、编译后执行器 |
| **ExecutionResult** | 统一执行结果：IsSuccess / Value / ErrorMessage / Duration |
| **ExecutionContext** | 执行上下文：ExecutionId / Items(共享数据) / CancellationToken |

两种注册表实现：

- **FunctionRegistry** — 通用委托注册表，基于 `ConcurrentDictionary` 存储委托和 IFunctionUnit
- **DefaultWorkflowRegistry** — 工作流函数注册表，继承 IFunctionRegistry，额外支持 `Func<WorkflowContext, object>` 签名

### 2.2 函数注册

两种注册方式：

```csharp
// 方式一：委托注册（灵活轻量）
FunctionExecutionFramework.Register("Add", (Func<double, double, double>)Add, "Addition: a + b");
FunctionExecutionFramework.RegisterAction<string>("SetApiKey", key => SetKey(key));

// 方式二：IFunctionUnit 注册（强类型可扩展）
public class AddFunction : FunctionUnitBase<(double a, double b), double>
{
    public override string Name => "Add";
    public override double Execute((double a, double b) input, ExecutionContext? context) => input.a + input.b;
}
```

### 2.3 执行流程

```
ExecuteAsync(funcName, parameters)
  → TryGetDescriptor(funcName)          // 查找函数
  → DefaultParameterProcessor.Process() // 类型转换（如 "3.14" → 3.14）
  → CompiledExecutor / DynamicInvoke    // 执行
  → ExecutionResult                     // 返回结果
```

### 2.4 工作流函数注册表（IWorkflowRegistry）

继承 IFunctionRegistry，提供基于 WorkflowContext 的注册与调用：

```csharp
public interface IWorkflowRegistry : IFunctionRegistry
{
    void Register(string name, Func<WorkflowContext, object> func);
    object Call(string name, WorkflowContext context);
}

public class WorkflowContext : ExecutionContext
{
    public Dictionary<string, object> Parameters { get; }  // 函数参数
    public string NodeKey { get; }                          // 当前节点
    public string WorkflowId { get; }                       // 工作流标识

    T GetParameter<T>(int index);       // 按索引取参数
    T GetParameter<T>(string key);      // 按名称取参数
    T GetData<T>(string key);           // 取上下文数据
    WorkflowContext CreateSubContext(string? nodeKey = null);
}
```

### 2.5 桥接机制

WorkflowBridge 实现工作流函数与通用函数的双向桥接：

```
IWorkflowRegistry → WorkflowFunctionUnitAdapter → IFunctionUnit (通用函数单元)
```

---

## 三、FunctionStorage — 函数存储与 UI 执行桥接层

### 3.1 函数库注册

将多领域函数集中注册到 FunctionExecutionFramework：

| 函数库 | 注册函数 | 说明 |
|--------|---------|------|
| **MathLibrary** | Add/Subtract/Multiply/Divide | 基础数学运算（含别名 Plus/Minus） |
| **ModelLibrary** | ChatComplete/ChatCompleteAsync/ChatCompleteRaw | DeepSeek 模型调用 |
| **ModelLibrary** | ExecuteWorkflow/LoadWorkflowDetails/SaveResultToFile | WorkflowRunner 界面函数 |
| **WorkflowLibrary** | 动态注册所有 JSON 工作流 | 将 JSON 工作流注册为可调用函数 |

WorkflowLibrary 的核心逻辑：

```csharp
// 遍历 JSON 工作流，同时注册到两个体系
WorkflowUseage.RegisterFunction(name, context => ExecuteWorkflow(def, input));  // 工作流函数
WorkflowUseage.RegisterDelegate(name, (Func<string, string>)(input => ...));    // 委托函数
```

### 3.2 JSON 驱动的工作流执行（WorkflowExecutionLayer）

从 `FileDriven/DrivenConfig/WorkFlow/` 加载 JSON 定义并执行，实现**配置即工作流**。

#### JSON 格式

```json
{
  "workflowName": "CodeReviewWorkflow",
  "displayName": "代码审查工作流",
  "steps": [
    {
      "stepName": "分析代码",
      "funcName": "ChatComplete",
      "outputKey": "reviewResult",
      "parameters": [
        "你是一个代码审查专家，请分析以下代码：\n${userInput}",
        "${userInput}"
      ]
    }
  ],
  "outputMapping": { "response": "reviewResult" }
}
```

#### 执行流程

```
ExecuteWorkflow(def, input)
  → 初始化上下文（合并 input + 注入 apiKey）
  → 遍历 steps：
      解析参数（${key} 变量替换）
      → FunctionExecutionFramework.ExecuteAsync() [优先]
      → 失败回退 WorkflowUseage.CallBridgedFunction() [回退]
      → 存储结果到 context
  → outputMapping 映射返回
      → 有 "response" 字段 → 直接返回文本
      → 单字段 → 直接返回值
      → 多字段 → 返回 JSON
```

#### 变量替换

| 语法 | 示例 | 说明 |
|------|------|------|
| `${key}` | `${userInput}` | 替换为 context 中的值 |
| `文本${key}文本` | `请分析：${userInput}` | 字符串中嵌入变量 |
| `${apiKey}` | 自动注入 | 从环境变量读取 DeepSeekAPI |

### 3.3 UI 事件桥接（UIConnector）

连接 WdUI 控件与函数执行引擎，实现**配置即交互**。

#### 支持的事件

| 事件 | 控件 | 参数来源 | 结果回写 |
|------|------|---------|---------|
| Click | Button | Bindings.Inputs 指定控件 | Bindings.Output 指定控件 |
| TextChanged | TextBox | 文本框文本 | 同上 |
| SelectedIndexChanged | ComboBox | 选中项 | 同上 |
| CheckedChanged | CheckBox | 选中状态 | 同上 |
| ValueChanged | TrackBar/NumericUpDown | 当前值 | 同上 |

#### 绑定流程

```
控件事件触发
  → ExtractParameters() 从输入控件取值（支持 7 种控件类型）
  → FunctionExecutionFramework.ExecuteAsync(funcName, parameters)
  → WriteResult() 将结果写回输出控件（TextBox/Label/ProgressBar/TrackBar/NumericUpDown）
```

#### 配置示例

```json
{
  "controls": [{
    "name": "btnExecute", "type": "Button", "text": "执行工作流",
    "events": {
      "click": "ExecuteWorkflow",
      "bindings": { "inputs": ["txtInput"], "output": "txtResult" }
    }
  }]
}
```

---

## 四、执行链路全景

### UI 事件链路

```
用户点击 → UIConnector 提取参数 → FunctionExecution 查找并执行函数 → UIConnector 写回结果
```

### JSON 工作流链路

```
外部调用 → 加载 JSON → 遍历步骤（变量替换 → 执行函数 → 存结果）→ outputMapping 返回
```

### 完整示例：AI 代码审查

```
用户点击按钮 → UIConnector 提取代码
  → ExecuteWorkflow("CodeReview", code)
    → 步骤1: ChatComplete(systemPrompt, userInput) → AI 审查
    → 步骤2: FormatReview(reviewResult) → 格式化报告
  → outputMapping 返回文本 → UIConnector 显示在 txtResult
```

---

## 五、与 WdUI 协同

| 协同场景 | 实现方式 |
|---------|---------|
| **事件绑定** | UIConnector 通过控件名称匹配绑定事件 |
| **参数提取** | 从 WdUI 控件自动提取值，支持 7 种类型 |
| **结果回写** | 执行结果自动写回 WdUI 控件 |
| **配置驱动** | 同一份 JSON 同时驱动 UI 渲染和函数执行 |
| **工作流执行** | WdUI 通过 ExecuteWorkflow 函数执行 JSON 工作流 |

---

## 六、架构定位

```
WdUI (UI 框架层)              → JSON 驱动 UI 渲染
     ↑ UIConnector
FunctionStorage (桥接层)      → 函数库、UI 桥接、JSON 工作流
     ↑ IFunctionRegistry
FunctionExecution (执行层)    → 函数注册、参数解析、委托调用
     ↑ IWorkflowRegistry
WorkFlowMethod (引擎层)       → 工作流编排、管线执行
```

| 层级 | 组件 | 核心职责 |
|------|------|---------|
| UI 层 | WdUI | JSON 驱动 UI 渲染，控件事件触发 |
| 桥接层 | FunctionStorage | 连接 UI 事件与函数执行，管理 JSON 工作流 |
| 执行层 | FunctionExecution | 函数注册、参数解析、委托调用 |
| 引擎层 | WorkFlowMethod | 工作流编排、管线执行、动态更新 |

FunctionExecution 提供底层函数注册与调用能力，FunctionStorage 在其上构建函数库、JSON 工作流执行和 UI 桥接，通过 UIConnector 与 WdUI 集成，实现**配置即工作流、界面即交互**的开发体验。
