# MathFunctionBrowser 工作流改造方案

> **目标**：将 MathFunctionBrowser.json 的后端执行从直接注册函数（MathBrowserFunctions.cs）改为工作流驱动，与 MathFunctions.json 使用同一套工作流引擎机制。

---

## 一、现状分析

### 1.1 MathFunctions.json（参考目标）

MathFunctions.json 的事件绑定直接调用已注册的函数名：

```json
"events": {
  "click": "Add",
  "bindings": {
    "parameters": [
      { "target": "a", "source": "valueATextBox", "sourceType": "control" },
      { "target": "b", "source": "valueBTextBox", "sourceType": "control" }
    ],
    "output": "resultTextBox"
  }
}
```

这些函数（`Add`、`Subtract`、`Multiply`、`Divide`）是直接注册在 `FunctionStorageRegistry` 中的数学运算函数。

### 1.2 DeepSeekChat.json（工作流引用模式）

DeepSeekChat.json 的按钮通过 `functionName` 直接引用工作流名称：

```json
"template": "action-button",
"name": "sendButton",
"functionName": "DeepSeekChatWorkflow",
"parameters": [
  { "target": "userInput", "source": "userInputTextBox", "sourceType": "control" }
],
"output": "responseTextBox"
```

工作流名称 `DeepSeekChatWorkflow` 被 `WorkflowLibrary.RegisterAll()` 注册为 `FunctionStorageRegistry` 中的可调用函数，所以事件绑定可以直接使用。

### 1.3 MathFunctionBrowser.json（待改造）

当前通过 `MathBrowserFunctions.cs` 注册了 6 个辅助函数：

| 函数名 | 触发事件 | 说明 |
|---|---|---|
| `Math_GetDomains` | `initCommands` | 获取数学域列表 |
| `Math_GetStats` | `initCommands` | 获取统计信息 |
| `Math_GetFunctionsInDomain` | `domainListView.selectedIndexChanged` | 选择域后获取函数列表 |
| `Math_GetFunctionDetail` | `funcListView.selectedIndexChanged` | 选择函数后获取详情 |
| `Math_Execute` | `execButton.click` | 执行数学函数 |
| `Math_Search` | `searchTextBox.textChanged` | 搜索函数 |

这些函数直接操作 `FunctionStorageRegistry`，**不是通过工作流引擎执行**。

### 1.4 差异对比

| 维度 | MathFunctions.json | MathFunctionBrowser.json |
|---|---|---|
| 交互模式 | 单步（点击按钮→执行） | 多步骤（选择域→选择函数→输入参数→执行） |
| 状态管理 | 无状态 | 需要维护选中域、函数、历史记录 |
| 事件数量 | 6 个事件（4 按钮 + 2 文本框） | 7 个事件（3 按钮 + 2 列表 + 1 文本框 + initCommands） |
| 后端实现 | 直接注册的数学函数 | MathBrowserFunctions.cs 辅助函数 |
| 工作流使用 | 否 | **目标：是** |

---

## 二、改造方案

### 2.1 核心思路

**一个主工作流 + `_stepSelector` 步骤选择机制**

- 创建一个 `MathFunctionBrowserWorkflow` 工作流定义，包含所有步骤
- 在 `WorkflowExecutionLayer` 中增加 `_stepSelector` 支持，允许从指定步骤开始执行
- 每个事件绑定通过 `_stepSelector` 参数指定执行哪个步骤
- 工作流引擎维护 `context` 字典，实现步骤间状态传递

### 2.2 架构变更

```
改造前：
  MathFunctionBrowser.json
    → events.click: "Math_Execute"
    → MathBrowserFunctions.cs (直接注册函数)
    → FunctionStorageRegistry.ExecuteAsync()

改造后：
  MathFunctionBrowser.json
    → events.click: "MathFunctionBrowserWorkflow"
    → parameters: { _stepSelector: "Execute", ... }
    → WorkflowLibrary (工作流注册为函数)
    → WorkflowExecutionLayer.ExecuteWorkflow()
    → ExecuteStepsCore (从 _stepSelector 指定步骤开始)
    → MathBrowserWorkflowFunctions.cs (工作流步骤函数)
```

---

## 三、模块化分类

### 模块 A：工作流引擎层修改

#### A-1. WorkflowDefinition 模型增加 StepSelector

**文件**：`CsharpPrj/Operation/FunctionStorage/Models/WorkflowDefinition.cs`

```csharp
// 新增属性
[JsonPropertyName("stepSelector")]
public string? StepSelector { get; set; }
```

#### A-2. WorkflowExecutionLayer 支持步骤选择

**文件**：`FileDriven/FileDriven.Business/Workflow/WorkflowExecutionLayer.cs`

修改 `ExecuteStepsCore` 方法：

```csharp
private static (Dictionary<string, object> context, List<Dictionary<string, object>> failedSteps) ExecuteStepsCore(
    WorkflowDefinition def, string input,
    Func<string, object[], Task<TConfig.ExecutionResult>> executeFunc)
{
    var context = new Dictionary<string, object>();
    var failedSteps = new List<Dictionary<string, object>>();
    string? stepSelector = null;

    // 解析输入，提取 _stepSelector
    if (!string.IsNullOrEmpty(input))
    {
        try
        {
            var inputObj = JsonSerializer.Deserialize<Dictionary<string, object>>(input);
            if (inputObj != null)
            {
                if (inputObj.TryGetValue("_stepSelector", out var sel))
                    stepSelector = sel?.ToString();
                
                foreach (var kvp in inputObj)
                {
                    if (kvp.Key == "_stepSelector") continue;
                    if (kvp.Value is JsonElement jsonElement)
                        context[kvp.Key] = ConvertJsonElementToPrimitive(jsonElement);
                    else
                        context[kvp.Key] = kvp.Value;
                }
            }
        }
        catch { context["input"] = input; }
    }

    // 确定起始步骤
    var steps = def.Steps ?? new List<WorkflowStep>();
    int startIndex = 0;
    if (!string.IsNullOrEmpty(stepSelector))
    {
        startIndex = steps.FindIndex(s => 
            s.StepName.Equals(stepSelector, StringComparison.OrdinalIgnoreCase));
        if (startIndex < 0) startIndex = 0;
    }

    // 从指定步骤开始执行
    for (int i = startIndex; i < steps.Count; i++)
    {
        var step = steps[i];
        // ... 原有执行逻辑不变
    }
    // ...
}
```

---

### 模块 B：工作流定义文件

#### B-1. 新建 MathFunctionBrowserWorkflow.json

**文件**：`FileDriven/DrivenConfig/WorkFlow/MathWorkflows/MathFunctionBrowserWorkflow.json`

```json
{
  "description": "数学函数浏览器工作流 - 支持步骤选择执行，管理域选择、函数浏览、参数输入、执行计算和历史记录",
  "workflowName": "MathFunctionBrowserWorkflow",
  "displayName": "数学函数浏览器工作流",
  "defaultInput": {
    "domain": "",
    "funcName": "",
    "paramStr": "",
    "keyword": ""
  },
  "steps": [
    {
      "stepName": "Init",
      "funcName": "MathBrowser_Init",
      "inputKeys": [],
      "outputKey": "initResult",
      "parameters": []
    },
    {
      "stepName": "SelectDomain",
      "funcName": "MathBrowser_SelectDomain",
      "inputKeys": ["domain"],
      "outputKey": "funcList",
      "parameters": ["${domain}"]
    },
    {
      "stepName": "SelectFunction",
      "funcName": "MathBrowser_SelectFunction",
      "inputKeys": ["funcName"],
      "outputKey": "detail",
      "parameters": ["${funcName}"]
    },
    {
      "stepName": "Execute",
      "funcName": "MathBrowser_Execute",
      "inputKeys": ["funcName", "paramStr"],
      "outputKey": "result",
      "parameters": ["${funcName}", "${paramStr}"]
    },
    {
      "stepName": "Search",
      "funcName": "MathBrowser_Search",
      "inputKeys": ["keyword"],
      "outputKey": "funcList",
      "parameters": ["${keyword}"]
    },
    {
      "stepName": "GetStats",
      "funcName": "MathBrowser_GetStats",
      "inputKeys": [],
      "outputKey": "stats",
      "parameters": []
    }
  ],
  "outputMapping": {
    "initResult": "initResult",
    "funcList": "funcList",
    "detail": "detail",
    "result": "result",
    "stats": "stats"
  }
}
```

**步骤说明**：

| 步骤名 | 对应原函数 | 触发时机 | 输出 |
|---|---|---|---|
| `Init` | `Math_GetDomains` + `Math_GetStats` | 配置加载时（initCommands） | 域列表 + 统计信息 |
| `SelectDomain` | `Math_GetFunctionsInDomain` | 用户选择域 | 函数列表 |
| `SelectFunction` | `Math_GetFunctionDetail` | 用户选择函数 | 函数详情 |
| `Execute` | `Math_Execute` | 用户点击"执行计算" | 计算结果 |
| `Search` | `Math_Search` | 用户输入搜索关键词 | 过滤后的函数列表 |
| `GetStats` | `Math_GetStats` | 初始化/刷新 | 统计文本 |

---

### 模块 C：工作流步骤函数

#### C-1. 新建 MathBrowserWorkflowFunctions.cs

**文件**：`FileDriven/FileDriven.Business/Registrations/MathBrowserWorkflowFunctions.cs`

将 `MathBrowserFunctions.cs` 中的 6 个函数重构为工作流步骤函数，注册到 `FunctionStorageRegistry`：

```csharp
using System.Text;
using System.Text.Json;
using Modules.Registry;
using TConfig;
using static FunctionStorage.Registrations.MathUtils;

namespace FunctionStorage.Registrations;

/// <summary>
/// 数学函数浏览器工作流步骤函数
/// 替代 MathBrowserFunctions.cs，作为 MathFunctionBrowserWorkflow 的后端执行单元
/// 每个函数对应工作流中的一个步骤
/// </summary>
public static class MathBrowserWorkflowFunctions
{
    public static void RegisterAll(IFunctionRegistry registry)
    {
        var reg = registry;

        // ================================================================
        // 步骤1: Init — 初始化，获取域列表 + 统计信息
        // 返回 JSON: { domains: ListData, stats: string }
        // ================================================================
        reg.Register("MathBrowser_Init",
            (Func<string>)(() =>
            {
                var names = reg.GetRegisteredNames().ToList();
                var domains = names
                    .Select(n => n.Contains('_') ? n.Split('_')[0] : "其他")
                    .Distinct().OrderBy(d => d);
                var stats = $"共 {names.Count} 个数学函数，{domains.Count()} 个数学域";
                
                return JsonSerializer.Serialize(new
                {
                    domains = ListData.From(domains),
                    stats = stats
                });
            }), "初始化数学浏览器（域列表 + 统计）");

        // ================================================================
        // 步骤2: SelectDomain — 选择数学域，获取域下函数列表
        // 参数: domain (string)
        // 返回: ListData
        // ================================================================
        reg.Register("MathBrowser_SelectDomain",
            (Func<string, ListData>)(domain =>
            {
                if (string.IsNullOrEmpty(domain))
                    return ListData.From(Enumerable.Empty<string>());

                var names = reg.GetRegisteredNames()
                    .Where(n => n.StartsWith(domain + "_", StringComparison.OrdinalIgnoreCase)
                             || string.Equals(n, domain, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(n => n);
                return ListData.From(names);
            }), "选择数学域，获取域下函数列表");

        // ================================================================
        // 步骤3: SelectFunction — 选择函数，获取函数详情
        // 参数: funcName (string)
        // 返回: string (详情文本)
        // ================================================================
        reg.Register("MathBrowser_SelectFunction",
            (Func<string, string>)(funcName =>
            {
                if (!reg.TryGetDescriptor(funcName, out var desc))
                    return "函数未找到";

                var sb = new StringBuilder();
                sb.AppendLine($"函数: {desc.Name}");
                sb.AppendLine($"描述: {desc.Description}");
                sb.AppendLine($"返回: {desc.ReturnType.Name}");
                sb.AppendLine($"参数 ({desc.Parameters.Count} 个):");
                foreach (var p in desc.Parameters)
                {
                    var opt = p.IsOptional ? $" (可选, 默认={p.DefaultValue})" : "";
                    sb.AppendLine($"  {p.Name}: {p.Type.Name}{opt}");
                }
                return sb.ToString();
            }), "选择函数，获取函数详细信息");

        // ================================================================
        // 步骤4: Execute — 执行数学函数
        // 参数: funcName (string), paramStr (string)
        // 返回: string (格式化结果)
        // ================================================================
        reg.Register("MathBrowser_Execute",
            (Func<string, string, string>)((funcName, paramStr) =>
            {
                if (!reg.TryGetDescriptor(funcName, out var desc))
                    return $"错误: 函数 '{funcName}' 未找到";

                try
                {
                    var parameters = ParseMathParameters(paramStr, desc.Parameters);
                    if (reg.TryGetDelegate(funcName, out var del) && del != null)
                    {
                        var result = del.DynamicInvoke(parameters);
                        return FormatMathResult(result);
                    }
                    return $"错误: 函数 '{funcName}' 委托不可用";
                }
                catch (Exception ex)
                {
                    var realMessage = ex is System.Reflection.TargetInvocationException tie 
                        && tie.InnerException != null
                        ? tie.InnerException.Message
                        : ex.Message;
                    return $"错误: {realMessage}";
                }
            }), "执行数学函数并返回格式化结果");

        // ================================================================
        // 步骤5: Search — 搜索数学函数
        // 参数: keyword (string)
        // 返回: ListData
        // ================================================================
        reg.Register("MathBrowser_Search",
            (Func<string, ListData>)(keyword =>
            {
                if (string.IsNullOrWhiteSpace(keyword))
                    return ListData.From(reg.GetRegisteredNames());
                
                var names = reg.GetRegisteredNames()
                    .Where(n => n.Contains(keyword, StringComparison.OrdinalIgnoreCase));
                return ListData.From(names);
            }), "按关键字搜索数学函数");

        // ================================================================
        // 步骤6: GetStats — 获取统计信息
        // 参数: 无
        // 返回: string
        // ================================================================
        reg.Register("MathBrowser_GetStats",
            (Func<string>)(() =>
            {
                var names = reg.GetRegisteredNames().ToList();
                var domains = names
                    .Select(n => n.Contains('_') ? n.Split('_')[0] : "其他")
                    .Distinct().Count();
                return $"共 {names.Count} 个数学函数，{domains} 个数学域";
            }), "获取数学函数统计信息");
    }
}
```

#### C-2. 注册到生命周期

**文件**：`FileDriven/FileDriven.Business/FileDrivenLifecycle.cs`

在 `RegisterCore` 方法中注册新的工作流步骤函数：

```csharp
// 注册数学浏览器工作流步骤函数（替代 MathBrowserFunctions）
Registrations.MathBrowserWorkflowFunctions.RegisterAll(_registry);

// 保留旧函数注册（向后兼容），或标记为废弃
Registrations.MathBrowserFunctions.RegisterAll(_registry); // 可删除
```

---

### 模块 D：JSON 配置更新

#### D-1. MathFunctionBrowser.json 完整更新

**文件**：`FileDriven/DrivenConfig/JsonConfig/MathFunctionBrowser.json`

```json
{
  "description": "通用数学函数计算器 - 工作流驱动，自动展示所有已注册数学函数",
  "initCommands": [
    "exec MathFunctionBrowserWorkflow output=domainListView|statsLabel _stepSelector=Init"
  ],
  "form": {
    "title": "数学函数计算器",
    "width": 1100,
    "height": 750
  },
  "views": [{
    "name": "MainView",
    "type": "Panel",
    "controls": [
      {
        "type": "label", "name": "titleLabel",
        "text": "  数学函数计算器",
        "font": { "name": "Microsoft YaHei UI", "size": 14, "bold": true },
        "layout": { "left": 0.02, "top": 0.01, "width": 0.5, "height": 0.04 }
      },

      // ================================================================
      // 搜索面板
      // ================================================================
      {
        "type": "panel", "name": "searchPanel",
        "layout": { "left": 0.02, "top": 0.06, "width": 0.96, "height": 0.05 },
        "controls": [
          {
            "type": "label", "name": "searchLabel", "text": "搜索函数:",
            "layout": { "left": 0.01, "top": 0.15, "width": 0.1, "height": 0.6 }
          },
          {
            "type": "textbox", "name": "searchTextBox",
            "placeholder": "输入函数名搜索（如 ReLU、Euclidean、KMeans）...",
            "events": {
              "textChanged": "MathFunctionBrowserWorkflow",
              "bindings": {
                "parameters": [
                  { "target": "_stepSelector", "value": "Search" },
                  { "target": "keyword", "source": "searchTextBox", "sourceType": "control" }
                ],
                "output": "funcListView"
              }
            },
            "layout": { "left": 0.12, "top": 0.1, "width": 0.4, "height": 0.8 }
          },
          {
            "type": "label", "name": "statsLabel", "text": "加载中...",
            "foreColor": "#6B7280",
            "layout": { "left": 0.55, "top": 0.15, "width": 0.4, "height": 0.6 }
          }
        ]
      },

      // ================================================================
      // 主面板
      // ================================================================
      {
        "type": "panel", "name": "mainPanel",
        "layout": { "left": 0.02, "top": 0.13, "width": 0.96, "height": 0.83 },
        "controls": [
          // --------------------------------------------------------------
          // 数学域列表
          // --------------------------------------------------------------
          {
            "type": "groupbox", "name": "domainGroup", "text": "数学域",
            "layout": { "left": 0.0, "top": 0.0, "width": 0.18, "height": 0.82 },
            "controls": [
              {
                "type": "listview", "name": "domainListView",
                "view": "List", "fullRowSelect": true,
                "events": {
                  "selectedIndexChanged": "MathFunctionBrowserWorkflow",
                  "bindings": {
                    "parameters": [
                      { "target": "_stepSelector", "value": "SelectDomain" },
                      { "target": "domain", "source": "domainListView", "sourceType": "control" }
                    ],
                    "output": "funcListView"
                  }
                },
                "layout": { "left": 0.05, "top": 0.05, "width": 0.9, "height": 0.9 }
              }
            ]
          },

          // --------------------------------------------------------------
          // 函数列表
          // --------------------------------------------------------------
          {
            "type": "groupbox", "name": "funcGroup", "text": "函数",
            "layout": { "left": 0.19, "top": 0.0, "width": 0.22, "height": 0.82 },
            "controls": [
              {
                "type": "listview", "name": "funcListView",
                "view": "Details", "fullRowSelect": true,
                "columns": [
                  { "text": "函数名", "width": 120 },
                  { "text": "返回", "width": 60 }
                ],
                "events": {
                  "selectedIndexChanged": "MathFunctionBrowserWorkflow",
                  "bindings": {
                    "parameters": [
                      { "target": "_stepSelector", "value": "SelectFunction" },
                      { "target": "funcName", "source": "funcListView", "sourceType": "control" }
                    ],
                    "output": "detailTextBox"
                  }
                },
                "layout": { "left": 0.05, "top": 0.05, "width": 0.9, "height": 0.9 }
              }
            ]
          },

          // --------------------------------------------------------------
          // 详情与参数
          // --------------------------------------------------------------
          {
            "type": "groupbox", "name": "detailGroup", "text": "详情与参数",
            "layout": { "left": 0.42, "top": 0.0, "width": 0.33, "height": 0.82 },
            "controls": [
              {
                "type": "textbox", "name": "detailTextBox",
                "multiline": true, "readOnly": true,
                "font": { "name": "Consolas", "size": 9 },
                "layout": { "left": 0.05, "top": 0.03, "width": 0.9, "height": 0.45 }
              },
              {
                "type": "label", "name": "paramLabel", "text": "参数输入:",
                "layout": { "left": 0.05, "top": 0.50, "width": 0.3, "height": 0.05 }
              },
              {
                "type": "textbox", "name": "paramInput",
                "multiline": true,
                "placeholder": "输入参数值，多个参数用逗号分隔\n支持 JSON 数组格式，如 [1.0, 2.0, 3.0]",
                "font": { "name": "Consolas", "size": 10 },
                "layout": { "left": 0.05, "top": 0.56, "width": 0.9, "height": 0.4 }
              }
            ]
          },

          // --------------------------------------------------------------
          // 结果
          // --------------------------------------------------------------
          {
            "type": "groupbox", "name": "resultGroup", "text": "结果",
            "layout": { "left": 0.76, "top": 0.0, "width": 0.24, "height": 0.82 },
            "controls": [
              {
                "type": "textbox", "name": "resultTextBox",
                "multiline": true, "readOnly": true,
                "scrollBars": "Both",
                "font": { "name": "Consolas", "size": 11, "bold": true },
                "layout": { "left": 0.05, "top": 0.03, "width": 0.9, "height": 0.55 }
              },
              {
                "type": "label", "name": "historyLabel", "text": "历史:",
                "layout": { "left": 0.05, "top": 0.60, "width": 0.3, "height": 0.05 }
              },
              {
                "type": "listview", "name": "historyListView",
                "view": "Details", "fullRowSelect": true,
                "columns": [
                  { "text": "函数", "width": 80 },
                  { "text": "结果", "width": 100 }
                ],
                "layout": { "left": 0.05, "top": 0.66, "width": 0.9, "height": 0.3 }
              }
            ]
          },

          // --------------------------------------------------------------
          // 操作按钮
          // --------------------------------------------------------------
          {
            "type": "panel", "name": "actionBar",
            "layout": { "left": 0.0, "top": 0.84, "width": 1.0, "height": 0.14 },
            "controls": [
              {
                "type": "button", "name": "execButton",
                "text": "执行计算",
                "events": {
                  "click": "MathFunctionBrowserWorkflow",
                  "bindings": {
                    "parameters": [
                      { "target": "_stepSelector", "value": "Execute" },
                      { "target": "funcName", "source": "funcListView", "sourceType": "control" },
                      { "target": "paramStr", "source": "paramInput", "sourceType": "control" }
                    ],
                    "output": "resultTextBox"
                  }
                },
                "layout": { "left": 0.42, "top": 0.1, "width": 0.16, "height": 0.7 }
              },
              {
                "type": "button", "name": "clearButton",
                "text": "清空",
                "events": {
                  "click": "ClearOutput",
                  "bindings": { "output": "resultTextBox" }
                },
                "layout": { "left": 0.60, "top": 0.1, "width": 0.1, "height": 0.7 }
              },
              {
                "type": "button", "name": "copyButton",
                "text": "复制",
                "events": {
                  "click": "CopyToClipboard",
                  "bindings": {
                    "parameters": [
                      { "target": "text", "source": "resultTextBox", "sourceType": "control" }
                    ]
                  }
                },
                "layout": { "left": 0.72, "top": 0.1, "width": 0.1, "height": 0.7 }
              }
            ]
          }
        ]
      },

      // ================================================================
      // 状态栏
      // ================================================================
      {
        "type": "label", "name": "statusLabel",
        "text": "就绪 - 选择数学域和函数，输入参数后点击执行",
        "layout": { "left": 0.02, "top": 0.97, "width": 0.96, "height": 0.03 }
      }
    ]
  }]
}
```

#### D-2. 事件绑定对照表

| 控件 | 事件 | 旧函数名 | 新工作流 | _stepSelector |
|---|---|---|---|---|
| `domainListView` | `selectedIndexChanged` | `Math_GetFunctionsInDomain` | `MathFunctionBrowserWorkflow` | `SelectDomain` |
| `funcListView` | `selectedIndexChanged` | `Math_GetFunctionDetail` | `MathFunctionBrowserWorkflow` | `SelectFunction` |
| `execButton` | `click` | `Math_Execute` | `MathFunctionBrowserWorkflow` | `Execute` |
| `searchTextBox` | `textChanged` | `Math_Search` | `MathFunctionBrowserWorkflow` | `Search` |
| `clearButton` | `click` | `ClearOutput` | `ClearOutput`（不变） | — |
| `copyButton` | `click` | `CopyToClipboard` | `CopyToClipboard`（不变） | — |
| `initCommands` | 加载时 | `Math_GetDomains` + `Math_GetStats` | `MathFunctionBrowserWorkflow` | `Init` |

---

### 模块 E：废弃代码处理

#### E-1. MathBrowserFunctions.cs 标记废弃

**文件**：`FileDriven/FileDriven.Business/Registrations/MathBrowserFunctions.cs`

在类上添加 `[Obsolete]` 属性：

```csharp
[Obsolete("已由 MathBrowserWorkflowFunctions 替代，通过 MathFunctionBrowserWorkflow 工作流驱动")]
public static class MathBrowserFunctions
{
    // ... 保留实现，确保向后兼容
}
```

#### E-2. FileDrivenLifecycle.cs 调整

**文件**：`FileDriven/FileDriven.Business/FileDrivenLifecycle.cs`

```csharp
// 注册新的工作流步骤函数
Registrations.MathBrowserWorkflowFunctions.RegisterAll(_registry);

// 旧函数保留（向后兼容），后续版本可删除
// Registrations.MathBrowserFunctions.RegisterAll(_registry);
```

---

## 四、实施步骤清单

### 第1步：修改工作流引擎
- [ ] `WorkflowDefinition.cs` — 增加 `StepSelector` 属性
- [ ] `WorkflowExecutionLayer.cs` — `ExecuteStepsCore` 支持 `_stepSelector`

### 第2步：新建工作流步骤函数
- [ ] `MathBrowserWorkflowFunctions.cs` — 6 个工作流步骤函数

### 第3步：新建工作流定义
- [ ] `MathFunctionBrowserWorkflow.json` — 工作流定义文件

### 第4步：更新 JSON 配置
- [ ] `MathFunctionBrowser.json` — 事件绑定改为工作流调用

### 第5步：更新生命周期
- [ ] `FileDrivenLifecycle.cs` — 注册新函数，标记旧函数废弃

### 第6步：验证
- [ ] 编译通过
- [ ] 初始化加载域列表和统计
- [ ] 选择域 → 显示函数列表
- [ ] 选择函数 → 显示详情
- [ ] 输入参数 → 执行计算 → 显示结果
- [ ] 搜索函数 → 过滤列表
- [ ] 清空/复制功能正常

---

## 五、关键设计决策

### 5.1 为什么选择 `_stepSelector` 而非多个工作流？

| 方案 | 优点 | 缺点 |
|---|---|---|
| **一个主工作流 + stepSelector** | 统一管理、状态共享、减少文件数 | 需要修改引擎 |
| **多个独立工作流** | 无需修改引擎 | 状态无法共享、文件多、管理复杂 |
| **保持现状（直接函数）** | 无需修改 | 无法获得工作流引擎的好处 |

### 5.2 历史记录管理

由于工作流引擎的 `context` 是每次调用新建的，历史记录需要外部存储：

- 在 `MathBrowserWorkflowFunctions.cs` 中增加静态 `ConcurrentQueue` 存储历史
- 每次 `Execute` 步骤执行后自动追加
- 可通过 `MathBrowser_GetHistory` 函数返回

### 5.3 向后兼容

- `MathBrowserFunctions.cs` 保留但标记 `[Obsolete]`
- 旧 JSON 配置仍可使用旧函数名
- 新配置使用工作流名称
