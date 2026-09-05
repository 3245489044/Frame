# FileDriven 框架使用指南

> 面向使用者的操作指南，聚焦于"怎么用"，不涉及技术实现细节。

---

## 一、概述

**FileDrivenFramework（FDF）** 是一个**文件驱动的 UI 自动化执行框架**。核心设计理念：**一切皆文件配置**——UI 界面由 JSON 定义，自动化流程由脚本文件驱动，业务逻辑由工作流文件编排，函数由注册中心管理。

| 能力 | 说明 | 配置位置 |
|------|------|---------|
| **JSON 驱动 UI** | 通过 JSON 配置文件动态生成 WinForms 窗体 | `DrivenConfig/JsonConfig/` |
| **脚本驱动自动化** | 通过 JSON 脚本或纯文本命令文件批量执行 UI 操作 | `DrivenConfig/Scripts/` |
| **工作流编排** | 通过 JSON 工作流定义多步骤函数调用链，支持变量传递 | `DrivenConfig/WorkFlow/` |
| **函数注册体系** | 统一的函数注册中心，供 UI 事件、工作流、CLI 调用 | `Operation/FunctionStorage/` |
| **模板系统** | 复用 UI 组件模板和样式模板，减少重复配置 | `DrivenConfig/TempletConfig/` |

---

## 二、快速开始

### 环境与构建

- .NET SDK 8.0+，Windows 系统（依赖 WinForms）
- `dotnet build WorkFlowPrj.sln` 或 `dotnet build FileDriven/FileDriven.Launcher/FileDriven.Launcher.csproj`

### 四种运行模式

| 模式 | 命令 | 说明 |
|------|------|------|
| **配置选择器** | `dotnet run --project FileDriven/FileDriven.Launcher` | 启动图形化配置选择器，交互式选择 JSON 配置 |
| **直接加载 UI** | `... -- --config MathFunctions.json --show` | 直接加载指定 JSON 配置并显示窗体 |
| **命令文件** | `... -- --file test_commands.txt` | 批量执行命令文件中的自动化指令 |
| **单命令** | `... -- --exec "load MathFunctions.json show"` | 快速执行单条命令 |

### 第一个示例

```bash
dotnet build WorkFlowPrj.sln
dotnet run --project FileDriven/FileDriven.Launcher -- --config DrivenConfig/JsonConfig/MathFunctions.json --show
```

你会看到一个数学运算测试窗体，包含两个数值输入框、四个运算按钮和一个结果显示框。输入数值点击按钮即可看到运算结果。

---

## 三、JSON UI 配置

配置文件位于 `DrivenConfig/JsonConfig/`，用于声明式定义 WinForms 界面。

### 配置文件结构

```json
{
  "extends": "BaseConfig.json",   // 可选：继承另一个配置
  "form": { ... },                 // 窗体配置
  "views": [ ... ],                // 视图列表（至少一个）
  "theme": { ... },                // 可选：主题配置
  "styles": { ... },               // 可选：共享样式
  "dataLoad": { ... }              // 可选：数据加载配置
}
```

### 窗体配置（form）

```json
"form": {
  "title": "我的应用", "width": 860, "height": 720,
  "maximizeBox": true, "minimizeBox": true,
  "windowState": "Normal", "startPosition": "CenterScreen"
}
```

### 支持的控件类型

| type | 控件 | 说明 |
|------|------|------|
| `label` | 标签 | 显示文本 |
| `button` | 按钮 | 可绑定点击事件 |
| `textbox` | 文本框 | 支持单行/多行/只读 |
| `combobox` | 下拉框 | 支持 items 列表 |
| `checkbox` | 复选框 | 支持 checked 状态 |
| `listview` | 列表视图 | 支持多列显示 |
| `groupbox` | 分组框 | 可嵌套子控件 |
| `panel` | 面板容器 | 可嵌套子控件 |
| `tabcontrol` / `tabpage` | 选项卡 | 多页面切换 |
| `numericupdown` | 数字选择器 | 数值输入 |
| `progressbar` | 进度条 | 进度显示 |
| `picturebox` | 图片框 | 图片显示 |

### 控件通用字段

```json
{
  "type": "button", "name": "myButton", "text": "点击我",
  "toolTip": "提示信息", "placeholder": "请输入...",
  "enabled": true, "readOnly": false, "multiline": false,
  "checked": false, "items": ["选项1", "选项2"], "selectedIndex": 0,
  "backColor": "#2563EB", "foreColor": "White",
  "font": { "name": "Consolas", "size": 14, "bold": true },
  "layout": { ... }, "events": { ... },
  "styleRef": "primary-button", "controls": [ ... ]
}
```

### 比例布局系统

所有位置和尺寸使用 0.0~1.0 的相对值，自动适应窗口大小变化。

| 字段 | 说明 |
|------|------|
| `left` / `top` | 左上角位置比例 |
| `width` / `height` | 宽高比例 |
| `right` / `bottom` | 右下对齐（与 left/top 互斥） |

常见模式：
```json
// 顶部通栏标题
{ "left": 0.0, "top": 0.0, "width": 1.0, "height": 0.06 }
// 底部状态栏
{ "left": 0.02, "bottom": 0.01, "width": 0.96, "height": 0.03 }
// 居中按钮
{ "left": 0.3, "top": 0.4, "width": 0.4, "height": 0.08 }
```

> 比例值在 0~1 之间表示百分比，大于 1 视为固定像素值。

### 事件绑定

控件支持的事件：`click`、`doubleClick`、`textChanged`、`selectedIndexChanged`、`checkedChanged`

事件值对应注册在 `FunctionStorage` 中的函数名：

```json
{
  "type": "button", "name": "addButton", "text": "Add (+)",
  "events": {
    "click": "Plus",
    "bindings": {
      "inputs": ["valueATextBox", "valueBTextBox"],
      "output": "resultTextBox"
    }
  }
}
```

`bindings` 字段说明：
- `inputs`：事件触发时从这些控件读取值作为函数参数
- `output`：函数执行结果写入该控件
- `outputMapping`：多事件时指定不同输出目标

### 主题与样式

**全局主题（theme）：**
```json
"theme": {
  "primaryColor": "#2563EB", "fontFamily": "Consolas", "fontSize": 14.0
}
```

**共享样式（styles）：**
```json
"styles": {
  "primary-button": { "backColor": "#2563EB", "foreColor": "White", "font": { "size": 11, "bold": true } },
  "readonly-bg": { "backColor": "#F8FAFC", "foreColor": "#1F2937" }
}
```

控件通过 `styleRef` 引用：`"styleRef": "primary-button"`

> **优先级**：控件直接属性 > `styleRef` > `theme`

### 配置继承（extends）

```json
// 基配置 BaseConfig.json
{ "form": { "title": "基础应用", "width": 1024, "height": 768 } }

// 派生配置
{ "extends": "BaseConfig.json", "form": { "title": "我的应用" }, "views": [...] }
```

合并规则：标量值覆盖、列表追加、对象深度合并。

### 模板引用（template）

```json
{ "template": "title-bar-with-text", "name": "titleLabel", "text": "我的应用" }
```

模板定义在 `TempletConfig/JsonConfig/composites.json` 中（详见第七章）。

---

## 四、脚本驱动

脚本文件位于 `DrivenConfig/Scripts/`，支持 JSON 脚本和纯文本命令文件两种格式。

### JSON 脚本格式

```json
{
  "description": "脚本描述",
  "script": "脚本名称",
  "commands": [
    { "action": "sideload", "target": "MathFunctions.json" },
    { "template": "math-operation", "valueA": "10", "valueB": "25", "buttonName": "addButton" },
    { "action": "sleep", "target": "500" },
    { "action": "close", "target": "Built-in Math Functions Test" },
    { "action": "exit" }
  ]
}
```

命令字段：`action`（命令动作）、`target`（命令目标）、`template`（引用命令模板）、`value`（设置值）

### 命令模板

JSON 脚本支持 `template` 字段引用命令模板，自动展开为多条命令：

```json
{ "template": "math-operation", "valueA": "10", "valueB": "25", "buttonName": "addButton" }
// 等价于：
// { "action": "set", "target": "valueATextBox", "value": "10" }
// { "action": "set", "target": "valueBTextBox", "value": "25" }
// { "action": "click", "target": "addButton" }
```

| 模板名 | 说明 | 参数 |
|--------|------|------|
| `load-config` / `load-config-show` | 加载 UI 配置（并显示） | `configPath` |
| `echo-section` | 输出标题文本 | `title` |
| `math-operation` | 设置两个数值并点击运算按钮 | `valueA`, `valueB`, `buttonName` |
| `list-forms` | 列出所有已加载的表单 | 无参数 |

### 纯文本命令文件

以 `.txt` 为扩展名，每行一个命令，`#` 开头为注释：

```text
# 数学运算综合测试
load DrivenConfig/JsonConfig/MathFunctions.json show
sleep 500
set valueATextBox 10
set valueBTextBox 25
click addButton
sleep 200
get resultTextBox
close "Built-in Math Functions Test"
```

执行规则：按顺序逐行执行，空行和注释被忽略，命令失败不影响后续执行。

### CLI 命令参考

| 命令 | 语法 | 功能 |
|------|------|------|
| `load` | `load <path> [name] [show]` | 加载 JSON 配置并生成窗体 |
| `show` | `show [formName]` | 显示已注册的窗体 |
| `close` | `close <formName>` | 关闭指定窗体 |
| `list` | `list` | 列出所有已注册的窗体 |
| `set` | `set <control> <value>` | 设置控件值 |
| `click` | `click <controlName>` | 模拟点击控件 |
| `get` | `get <control>` | 获取控件的值 |
| `sleep` | `sleep <ms>` | 等待指定毫秒数 |
| `echo` | `echo <text>` | 回显文本 |
| `log` | `log <subcommand>` | 日志查询/管理 |
| `help` | `help` | 显示帮助信息 |

**log 子命令：** `log category <name>`、`log level <level>`、`log errors`、`log count`、`log clear`、`log categories`、`log query [params]`

### 脚本文件组织

```
Scripts/
├── AI/          deepseek_chat_direct.json, deepseek_chat_workflow.json
├── Demo/        simple_demo.json
├── Math/        math_functions_test.json, math_workflow_test.json
├── UI/          layout_validation_test.json
├── Workflow/    cli_backend_test.json, default_input_test.json
└── *.txt        初始化、日志测试等
```

---

## 五、工作流编排

工作流定义文件位于 `DrivenConfig/WorkFlow/`，通过 JSON 定义多步骤函数调用链。

### 工作流格式

```json
{
  "workflowName": "MathOperationsWorkflow",
  "displayName": "数学运算工作流",
  "defaultInput": { "a": 0, "b": 0, "c": 0 },
  "steps": [
    {
      "stepName": "加法运算",
      "funcName": "Plus",
      "inputKeys": ["a", "b"],
      "outputKey": "addResult",
      "parameters": ["${a}", "${b}"]
    }
  ],
  "outputMapping": { "addResult": "addResult" }
}
```

### 字段说明

| 字段 | 必填 | 说明 |
|------|------|------|
| `workflowName` | ✅ | 工作流唯一名称，用于函数调用标识 |
| `displayName` | 否 | 显示名称 |
| `defaultInput` | 否 | 默认输入参数值 |
| `ioMapping` | 否 | UI 输入输出字段映射 |
| `steps` | ✅ | 工作流步骤列表 |
| `outputMapping` | 否 | 输出结果映射 |

### 步骤定义

| 字段 | 必填 | 说明 |
|------|------|------|
| `stepName` | ✅ | 步骤名称 |
| `funcName` | ✅ | 调用的函数名（必须在 FunctionStorage 中已注册） |
| `inputKeys` | ✅ | 输入参数键名列表 |
| `outputKey` | ✅ | 输出结果键名 |
| `parameters` | 否 | 参数表达式，支持 `${variable}` 变量引用 |

变量来源：`defaultInput` 默认值、前序步骤的 `outputKey` 输出值、调用时传入的参数。

### 完整工作流示例

```json
{
  "workflowName": "MathOperationsWorkflow",
  "defaultInput": { "a": 0, "b": 0, "c": 0, "total": 100, "count": 20 },
  "steps": [
    { "stepName": "加法", "funcName": "Plus", "inputKeys": ["a","b"], "outputKey": "addResult", "parameters": ["${a}","${b}"] },
    { "stepName": "减法", "funcName": "Minus", "inputKeys": ["a","b"], "outputKey": "subtractResult", "parameters": ["${a}","${b}"] },
    { "stepName": "乘法", "funcName": "Multiply", "inputKeys": ["a","b"], "outputKey": "multiplyResult", "parameters": ["${a}","${b}"] },
    { "stepName": "除法", "funcName": "Divide", "inputKeys": ["a","b"], "outputKey": "divideResult", "parameters": ["${a}","${b}"] },
    { "stepName": "复合运算", "funcName": "Multiply", "inputKeys": ["addResult","c"], "outputKey": "compoundResult", "parameters": ["${addResult}","${c}"] },
    { "stepName": "平均值", "funcName": "Divide", "inputKeys": ["total","count"], "outputKey": "average", "parameters": ["${total}","${count}"] }
  ]
}
```

执行流程：步骤 1→Plus(a,b)→addResult → 步骤 2→Minus(a,b)→subtractResult → ... → 步骤 5→Multiply(addResult,c)→compoundResult → 步骤 6→Divide(total,count)→average

### 工作流即函数

工作流注册后可通过 `FunctionStorageRegistry.ExecuteAsync("工作流名称", input)` 调用。

预置工作流：`MathOperationsWorkflow`（数学运算）、`DeepSeekChatWorkflow`（AI 对话）、`BatchRefactorWorkflow`（批量重构）、`CodeReviewWorkflow`（代码审查）、`NLToCodeWorkflow`（自然语言转代码）

---

## 六、函数注册体系

`FunctionStorageRegistry` 是统一的函数注册入口，所有可被 UI 事件、工作流步骤、CLI 命令调用的函数都通过它注册。

### 注册与调用

```csharp
FunctionStorageRegistry.Register("函数名", 函数委托, "描述");
var result = await FunctionStorageRegistry.ExecuteAsync("函数名", 参数);
```

### 内置函数库

**MathLibrary（数学运算）：** `Add/Plus`（加法）、`Subtract/Minus`（减法）、`Multiply`（乘法）、`Divide`（除法）

**UIToolbox（UI 操作）：** `ClearOutput`（清空输出）、`ExecuteWorkflow`（执行工作流）、`ReloadWorkflows`（重载工作流列表）、`SaveResultToFile`（保存结果）、`LoadWorkflowDetails`（加载工作流详情）、`LoadEnvironmentApiKey`（加载 API Key）

**WorkflowLibrary（工作流即函数）：** 自动将 `WorkFlow/` 目录下的 JSON 工作流注册为可调用函数，支持热重载。

### 参数匹配

| 参数格式 | 匹配方式 | 适用场景 |
|---------|---------|---------|
| `Dictionary<string, object>` | 按参数名匹配 | 工作流步骤调用 |
| `List<object>` / `object[]` | 按位置匹配 | UI 事件触发、CLI 命令 |
| 单值（string/number） | 直接传入 | 单参数函数 |

### 注册自定义函数

```csharp
public static class MyLibrary {
    public static void RegisterAll() {
        FunctionStorageRegistry.Register("Hello", (Func<string>)(() => "Hello!"), "问候语");
        FunctionStorageRegistry.Register("Greet", (Func<string, string>)((n) => $"Hello, {n}!"), "向指定名称问候");
        FunctionStorageRegistry.Register("Add", (Func<double, double, double>)((a,b) => a+b), "加法");
    }
}
```

在框架初始化时调用 `MyLibrary.RegisterAll()` 即可。

---

## 七、模板配置

模板系统位于 `DrivenConfig/TempletConfig/`，用于复用 UI 组件和样式。

### 复合组件模板（composites.json）

包含两类模板：**UI 控件模板**（用于 JSON UI 配置的 `template` 字段）和**命令模板**（用于 JSON 脚本的 `template` 字段）。

**UI 控件模板示例：**
```json
"title-bar-with-text": {
  "type": "label",
  "defaults": { "backColor": "#2563EB", "foreColor": "White", "font": { "size": 12, "bold": true }, "layout": { "left": 0, "top": 0, "width": 1, "height": 0.06 } },
  "parameters": { "name": { "required": true }, "text": { "required": true } }
}
```

**命令模板示例：**
```json
"math-operation": {
  "type": "command-group",
  "commands": [
    { "action": "set", "target": "valueATextBox", "value": "${valueA}" },
    { "action": "set", "target": "valueBTextBox", "value": "${valueB}" },
    { "action": "click", "target": "${buttonName}" }
  ],
  "parameters": { "valueA": { "required": true }, "valueB": { "required": true }, "buttonName": { "required": true } }
}
```

### 模板参数系统

| 字段 | 说明 |
|------|------|
| `required` | 是否必填 |
| `default` | 默认值 |
| `suffix` / `prefix` | 自动追加到 name 后的后缀 / 添加到值前的前缀 |
| `from` | 从另一个参数取值 |

### 内置 UI 控件模板

| 模板名 | 说明 | 参数 |
|--------|------|------|
| `title-bar-with-text` | 顶部标题栏 | `name`, `text` |
| `status-bar` | 底部状态栏 | `name`, `text` |
| `labeled-textbox` | 标签+文本框组合 | `name`, `text` |
| `action-button` | 带事件绑定的操作按钮 | `name`, `text`, `functionName`, `inputs`, `output` |
| `toolbar-button` | 工具栏按钮 | `name`, `text`, `functionName`, `layout` |

### 样式模板（styles.json）

```json
{
  "primary-button": { "applyTo": ["button"], "properties": { "backColor": "#2563EB", "foreColor": "White", "font": { "size": 11, "bold": true } } },
  "readonly-bg": { "applyTo": ["textbox"], "properties": { "backColor": "#F8FAFC", "foreColor": "#1F2937" } },
  "terminal-textbox": { "applyTo": ["textbox"], "properties": { "backColor": "#1E1E1E", "foreColor": "#90EE90", "font": { "name": "Consolas", "size": 10 } } }
}
```

控件通过 `"styleRef": "primary-button"` 引用。

---

## 八、完整示例：数学运算工作流

### 链路总览

```
注册函数 → 定义 UI 配置 → 定义工作流 → 编写脚本 → 运行
```

### 1. 注册函数（已预置）

数学运算函数已注册在 `MathLibrary` 中：`Plus`、`Minus`、`Multiply`、`Divide`

### 2. 定义 UI 配置（MathFunctions.json）

```json
{
  "form": { "title": "Built-in Math Functions Test", "width": 500, "height": 480 },
  "views": [{
    "name": "MainView", "type": "Panel",
    "controls": [
      { "template": "title-bar-with-text", "name": "titleLabel", "text": "Built-in Math Functions" },
      { "type": "groupbox", "name": "inputGroupBox", "text": "Input Values",
        "controls": [
          { "type": "label", "name": "valueALabel", "text": "Value A:", "layout": { "left": 0.05, "top": 0.2, "width": 0.15, "height": 0.25 } },
          { "type": "textbox", "name": "valueATextBox", "placeholder": "Enter number", "layout": { "left": 0.22, "top": 0.2, "width": 0.3, "height": 0.3 } },
          { "type": "label", "name": "valueBLabel", "text": "Value B:", "layout": { "left": 0.55, "top": 0.2, "width": 0.15, "height": 0.25 } },
          { "type": "textbox", "name": "valueBTextBox", "placeholder": "Enter number", "layout": { "left": 0.72, "top": 0.2, "width": 0.25, "height": 0.3 } }
        ]
      },
      { "type": "groupbox", "name": "functionGroupBox", "text": "Math Functions",
        "controls": [
          { "template": "action-button", "name": "addButton", "text": "Add (+)", "functionName": "Plus",
            "events": { "click": "Plus", "bindings": { "inputs": ["valueATextBox","valueBTextBox"], "output": "resultTextBox" } },
            "layout": { "left": 0.05, "top": 0.15, "width": 0.2, "height": 0.3 } },
          { "template": "action-button", "name": "subtractButton", "text": "Subtract (-)", "functionName": "Minus",
            "events": { "click": "Minus", "bindings": { "inputs": ["valueATextBox","valueBTextBox"], "output": "resultTextBox" } },
            "layout": { "left": 0.28, "top": 0.15, "width": 0.2, "height": 0.3 } },
          { "template": "action-button", "name": "multiplyButton", "text": "Multiply (*)", "functionName": "Multiply",
            "events": { "click": "Multiply", "bindings": { "inputs": ["valueATextBox","valueBTextBox"], "output": "resultTextBox" } },
            "layout": { "left": 0.51, "top": 0.15, "width": 0.2, "height": 0.3 } },
          { "template": "action-button", "name": "divideButton", "text": "Divide (/)", "functionName": "Divide",
            "events": { "click": "Divide", "bindings": { "inputs": ["valueATextBox","valueBTextBox"], "output": "resultTextBox" } },
            "layout": { "left": 0.74, "top": 0.15, "width": 0.2, "height": 0.3 } }
        ]
      },
      { "type": "groupbox", "name": "resultGroupBox", "text": "Result",
        "controls": [
          { "type": "label", "name": "resultLabel", "text": "Result:", "layout": { "left": 0.05, "top": 0.25, "width": 0.15, "height": 0.3 } },
          { "type": "textbox", "name": "resultTextBox", "styleRef": "readonly-bg", "readOnly": true, "font": { "name": "Consolas", "size": 11, "bold": true }, "layout": { "left": 0.22, "top": 0.25, "width": 0.7, "height": 0.4 } }
        ]
      },
      { "template": "status-bar", "name": "statusLabel", "text": "选择数值并点击运算按钮查看结果" }
    ]
  }]
}
```

### 3. 定义工作流（MathOperationsWorkflow.json）

```json
{
  "workflowName": "MathOperationsWorkflow",
  "defaultInput": { "a": 0, "b": 0, "c": 0, "total": 100, "count": 20 },
  "steps": [
    { "stepName": "加法", "funcName": "Plus", "inputKeys": ["a","b"], "outputKey": "addResult", "parameters": ["${a}","${b}"] },
    { "stepName": "减法", "funcName": "Minus", "inputKeys": ["a","b"], "outputKey": "subtractResult", "parameters": ["${a}","${b}"] },
    { "stepName": "乘法", "funcName": "Multiply", "inputKeys": ["a","b"], "outputKey": "multiplyResult", "parameters": ["${a}","${b}"] },
    { "stepName": "除法", "funcName": "Divide", "inputKeys": ["a","b"], "outputKey": "divideResult", "parameters": ["${a}","${b}"] },
    { "stepName": "复合运算", "funcName": "Multiply", "inputKeys": ["addResult","c"], "outputKey": "compoundResult", "parameters": ["${addResult}","${c}"] },
    { "stepName": "平均值", "funcName": "Divide", "inputKeys": ["total","count"], "outputKey": "average", "parameters": ["${total}","${count}"] }
  ]
}
```

### 4. 编写测试脚本（math_functions_test.json）

```json
{
  "description": "Math Functions 综合测试",
  "script": "math_functions_test",
  "commands": [
    { "action": "sideload", "target": "MathFunctions.json" },
    { "template": "echo-section", "title": "加法运算测试" },
    { "template": "math-operation", "valueA": "10", "valueB": "25", "buttonName": "addButton" },
    { "template": "echo-section", "title": "减法运算测试" },
    { "template": "math-operation", "valueA": "100", "valueB": "30", "buttonName": "subtractButton" },
    { "template": "echo-section", "title": "乘法运算测试" },
    { "template": "math-operation", "valueA": "15", "valueB": "4", "buttonName": "multiplyButton" },
    { "template": "echo-section", "title": "除法运算测试" },
    { "template": "math-operation", "valueA": "100", "valueB": "4", "buttonName": "divideButton" },
    { "action": "sleep", "target": "500" },
    { "action": "close", "target": "Built-in Math Functions Test" },
    { "action": "exit" }
  ]
}
```

### 5. 运行

**GUI 交互模式：**
```bash
dotnet run --project FileDriven/FileDriven.Launcher -- --config DrivenConfig/JsonConfig/MathFunctions.json --show
```

**脚本自动化模式：**
```bash
dotnet run --project FileDriven/FileDriven.Launcher -- --file test_commands.txt
```

**工作流模式：**
```bash
dotnet run --project FileDriven/FileDriven.Launcher -- --config DrivenConfig/JsonConfig/WorkflowRunner.json --show
```
在打开的窗体中选择工作流 → 输入参数 → 执行 → 查看结果。

---

## 附录

### 配置文件目录索引

| 目录 | 用途 |
|------|------|
| `DrivenConfig/JsonConfig/` | UI 配置文件 |
| `DrivenConfig/Scripts/` | 自动化脚本 |
| `DrivenConfig/WorkFlow/` | 工作流定义 |
| `DrivenConfig/TempletConfig/JsonConfig/` | UI 模板和样式 |
| `DrivenConfig/Configs/` | 默认配置 |

### 常用启动命令

```bash
# 配置选择器模式
dotnet run --project FileDriven/FileDriven.Launcher
# 直接加载 UI
dotnet run --project FileDriven/FileDriven.Launcher -- --config MathFunctions.json --show
# 执行命令文件
dotnet run --project FileDriven/FileDriven.Launcher -- --file test_commands.txt
# 单条命令
dotnet run --project FileDriven/FileDriven.Launcher -- --exec "load MathFunctions.json show"
# 查看帮助
dotnet run --project FileDriven/FileDriven.Launcher -- --help
```

### 常见问题

| 问题 | 解决方法 |
|------|---------|
| 配置文件未找到 | 检查文件路径是否正确 |
| 配置格式错误 | 使用 JSON 验证工具检查格式 |
| 控件未找到 | 检查控件的 `name` 属性是否匹配 |
| 命令未识别 | 使用 `help` 命令查看支持的命令列表 |
| 函数未注册 | 检查 `FunctionStorageRegistry` 中注册的函数名 |
| 工作流步骤失败 | 检查 `funcName` 对应的函数是否已注册 |
| 脚本执行异常 | 检查模板引用的参数是否完整 |
