# 函数注册体系完整指南

## 概述

将一个新功能注册到框架的统一函数注册体系，需完成 **C# 注册 →（可选）WorkFlow 定义 →（可选）JSON UI 配置 → Bat 启动** 四个步骤。

### 两种注册模式

| 模式 | 适用场景 | 步骤 |
|------|---------|------|
| **直接注册为函数** | 简单逻辑、基础设施功能、无状态计算 | C# 注册 → Bat 启动 |
| **注册为工作流** | 复杂逻辑、多步骤链式调用、需要 UI 交互 | C# 注册 → WorkFlow 定义 → JSON UI 配置 → Bat 启动 |

**选择原则：**
- **简单逻辑**（单步、无状态、无 UI 需求）→ 直接注册为函数，Bat 脚本通过 `--exec` 调用
- **复杂逻辑**（多步骤、需要 UI 输入/输出、链式调用）→ 注册为工作流，走完整四步

### 架构总览

```
① C# 函数注册层                   ② WorkFlow JSON 定义层（可选）
FunctionStorage/*Library.cs        FileDriven/DrivenConfig/WorkFlow/*.json
  → Register("Fn", delegate)         → workflowName, steps[].funcName
  → 注册到统一注册表                  → 引用已注册的函数名

③ JSON UI 配置层（可选）           ④ Bat 启动脚本层
FileDriven/DrivenConfig/JsonConfig/*.json  EnableMent/*.bat
  → events.click/bindings            → 调用 PowerShell → 加载 UI
  → 绑定工作流执行事件                → 启动交互界面

⑤ 执行链路
UI 事件 → FunctionExecutionFramework.ExecuteAsync()
       → WorkflowExecutionLayer.ExecuteWorkflow()
       → 按 steps 依次执行 funcName 对应的注册函数
       → 结果通过 outputMapping 返回 UI
```

### 直接调用链路（无 WorkFlow）

```
Bat 脚本 → --exec "FnName(args)" → CliOptions 解析
        → FunctionExecutionFramework.ExecuteAsync("FnName", args)
        → 直接执行注册函数 → 返回结果
```

---

## 第一步：C# 函数注册

> **所有功能无论简单复杂，都先注册为 C# 函数。** 简单逻辑到此为止，复杂逻辑继续走 WorkFlow 定义。

### 直接注册为函数（简单逻辑）

对于**不需要 UI、不需要多步骤链式调用**的简单功能，只需完成 C# 注册，Bat 脚本通过 `--exec` 直接调用。

#### 注册示例

```csharp
// FunctionStorage/LoggingLibrary.cs
namespace FunctionStorage
{
    public static class LoggingLibrary
    {
        private static bool _registered;

        public static void RegisterAll()
        {
            if (_registered) return;
            _registered = true;

            // 基础设施功能：注册日志批次（替代环境变量传递）
            FunctionExecutionFramework.Register("RegisterBatch",
                (Func<string, string, string, string, string>)((id, name, source, desc) =>
                {
                    BatchLoggerBridge.RegisterBatch(id, name, source, desc);
                    return $"Batch registered: {id} ({name})";
                }), "注册日志批次");

            // 简单功能：获取当前时间
            FunctionExecutionFramework.Register("GetCurrentTime",
                (Func<string>)(() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")),
                "获取当前时间");
        }
    }
}
```

#### Bat 脚本通过 `--exec` 调用

```batch
@echo off
chcp 65001 >nul
title 功能名称 - FileDrivenFramework

call "%~dp0..\BatTool\Tools\Utils\BatToolConfig.bat"

:: ★ 直接调用注册的函数，无需 WorkFlow JSON 定义
dotnet run --project "%DIR_FILEDRIVEN%\FileDriven.Launcher" -- ^
  --exec "RegisterBatch(Run_20260526, 功能测试, 启动.bat, 描述)" ^
  --exec "GetCurrentTime()"

if %ERRORLEVEL% NEQ 0 ( echo. & echo 执行出错，错误码: %ERRORLEVEL% & pause )
exit /b %ERRORLEVEL%
```

#### 调用规则

| 委托签名 | `--exec` 参数格式 | 示例 |
|---------|------------------|------|
| `Func<string>` | `FnName()` | `GetCurrentTime()` |
| `Func<string, string>` | `FnName(arg)` | `ProcessData("input")` |
| `Func<string, string, string>` | `FnName(arg1, arg2)` | `CompareFiles("a.txt", "b.txt")` |
| `Func<string, string, string, string>` | `FnName(arg1, arg2, arg3)` | `RegisterBatch("id", "name", "src", "desc")` |
| `Func<double, double, double>` | `FnName(n1, n2)` | `Add(1.5, 2.3)` |

> **注意：** `--exec` 参数中的逗号用于分隔参数，如果参数本身包含逗号，请用引号包裹。

---

### 注册为工作流（复杂逻辑）

对于需要 UI 交互、多步骤链式调用的复杂功能，在 C# 注册后，继续走 WorkFlow 定义 → JSON UI 配置 → Bat 启动的完整流程。

在 `FunctionStorage/` 下创建 `YourLibrary.cs`，注册函数到 `FunctionExecutionFramework`。

### 基础模板

```csharp
namespace FunctionStorage
{
    public static class YourLibrary
    {
        private static bool _registered;

        public static void RegisterAll()
        {
            if (_registered) return;
            _registered = true;

            FunctionExecutionFramework.Register("FnName", 委托, "描述");
        }
    }
}
```

### 委托签名对照

| 签名 | 说明 | 典型场景 |
|------|------|---------|
| `Func<string, string>` | 单入参 → 返回字符串 | 处理、分析、转换 |
| `Func<string, string, string>` | 双入参 → 返回字符串 | 比较、拼接 |
| `Func<string>` | 无参 → 返回字符串 | 获取状态 |
| `Func<double, double, double>` | 双数值 → 返回数值 | 数值计算 |
| `Action` / `Action<T>` | 无返回值 | 写日志、触发 |

### 多函数注册示例

```csharp
public static void RegisterAll()
{
    if (_registered) return;
    _registered = true;

    // 无参
    FunctionExecutionFramework.Register("FnA",
        (Func<string>)(() => "结果"), "描述");

    // 单参
    FunctionExecutionFramework.Register("FnB",
        (Func<string, string>)((x) => $"处理: {x}"), "描述");

    // 双参
    FunctionExecutionFramework.Register("FnC",
        (Func<string, string, string>)((a, b) => $"{a}+{b}"), "描述");

    // 数值
    FunctionExecutionFramework.Register("FnD",
        (Func<double, double, double>)((a, b) => a + b), "描述");
}
```

---

## 第二步：WorkFlow JSON 定义

在 `FileDriven/DrivenConfig/WorkFlow/` 下创建 `YourWorkflow.json`，定义步骤链。

### 基础模板

```json
{
  "description": "描述",
  "workflowName": "YourWorkflow",
  "displayName": "显示名称",
  "defaultInput": { "param1": "默认值" },
  "ioMapping": {
    "inputFields": { "param1": "inputTextBox" },
    "outputFields": { "result1": "outputTextBox" }
  },
  "steps": [
    {
      "stepName": "步骤名",
      "funcName": "FnName",
      "inputKeys": ["param1"],
      "outputKey": "result1",
      "parameters": ["${param1}"]
    }
  ],
  "outputMapping": { "result1": "result1" }
}
```

### 关键字段

| 字段 | 必填 | 说明 |
|------|------|------|
| `workflowName` | ✅ | 唯一标识，代码引用用 |
| `steps[].funcName` | ✅ | 必须与 C# 注册的函数名一致 |
| `steps[].parameters` | 否 | 参数列表，`${变量}` 引用上下文 |
| `defaultInput` | 否 | 默认输入，预填充 UI 输入框 |
| `ioMapping` | 否 | 输入/输出字段 → UI 控件映射 |
| `outputMapping` | 否 | 上下文键 → 输出键映射 |

### 按委托签名选择模板

**单入参 `Func<string, string>`：**
```json
{ "stepName": "步骤", "funcName": "FnName", "inputKeys": ["input"], "outputKey": "output", "parameters": ["${input}"] }
```

**双入参 `Func<string, string, string>`：**
```json
{ "stepName": "步骤", "funcName": "FnName", "inputKeys": ["a", "b"], "outputKey": "output", "parameters": ["${a}", "${b}"] }
```

**无参 `Func<string>`：**
```json
{ "stepName": "步骤", "funcName": "FnName", "inputKeys": [], "outputKey": "output" }
```

**数值 `Func<double, double, double>`：**
```json
{ "stepName": "步骤", "funcName": "FnName", "inputKeys": ["a", "b"], "outputKey": "output", "parameters": ["${a}", "${b}"] }
```

### 多步骤链式调用

前一步的输出作为后一步的输入：

```json
{
  "workflowName": "ChainWorkflow",
  "defaultInput": { "initial": "初始值" },
  "steps": [
    { "stepName": "步骤1", "funcName": "FnA", "inputKeys": ["initial"], "outputKey": "mid1", "parameters": ["${initial}"] },
    { "stepName": "步骤2", "funcName": "FnB", "inputKeys": ["mid1"], "outputKey": "mid2", "parameters": ["${mid1}"] },
    { "stepName": "步骤3", "funcName": "FnC", "inputKeys": ["mid2"], "outputKey": "final", "parameters": ["${mid2}"] }
  ],
  "outputMapping": { "result": "final" }
}
```

---

## 第三步：JSON UI 配置

在 `FileDriven/DrivenConfig/JsonConfig/` 下创建 `YourUI.json`，定义界面布局和事件绑定。

### 基础模板（输入 → 执行 → 输出）

```json
{
  "description": "界面描述",
  "form": { "title": "标题", "width": 860, "height": 720 },
  "views": [{
    "name": "MainView", "type": "Panel",
    "controls": [
      {
        "type": "groupbox", "name": "inputGroup", "text": "输入",
        "layout": { "left": 0.02, "top": 0.08, "width": 0.96, "height": 0.25 },
        "controls": [
          { "type": "label", "name": "inputLabel", "text": "输入:", "layout": { "left": 0.02, "top": 0.2, "width": 0.1, "height": 0.15 } },
          { "type": "textbox", "name": "inputBox", "placeholder": "请输入...", "multiline": true, "scrollBars": "Both",
            "layout": { "left": 0.14, "top": 0.15, "width": 0.84, "height": 0.7 } }
        ]
      },
      {
        "type": "groupbox", "name": "controlGroup", "text": "操作",
        "layout": { "left": 0.02, "top": 0.35, "width": 0.96, "height": 0.08 },
        "controls": [
          { "type": "button", "name": "executeBtn", "text": "▶ 执行", "style": "primary-button",
            "events": { "click": "ExecuteWorkflow", "bindings": { "inputs": ["inputBox"], "output": "outputBox" } },
            "layout": { "left": 0.02, "top": 0.2, "width": 0.12, "height": 0.6 } }
        ]
      },
      {
        "type": "groupbox", "name": "outputGroup", "text": "结果",
        "layout": { "left": 0.02, "top": 0.45, "width": 0.96, "height": 0.5 },
        "controls": [
          { "type": "textbox", "name": "outputBox", "style": "readonly-bg", "placeholder": "结果将显示在此处...",
            "multiline": true, "readOnly": true, "scrollBars": "Both", "font": { "name": "Consolas", "size": 10 },
            "layout": { "left": 0.02, "top": 0.1, "width": 0.96, "height": 0.85 } }
        ]
      }
    ]
  }]
}
```

### 事件绑定

```json
"events": {
  "click": "ExecuteWorkflow",         // 触发的工作流/函数名
  "bindings": {
    "inputs": ["控件1", "控件2"],      // 输入源
    "output": "输出控件"               // 输出目标
  }
}
```

**ComboBox 多事件绑定：**
```json
{
  "type": "combobox", "name": "selectorCombo",
  "items": ["选项1", "选项2"],
  "events": {
    "selectedIndexChanged": ["FnA", "FnB"],
    "bindings": {
      "inputs": ["selectorCombo"],
      "output": "detailBox",
      "outputMapping": { "FnA": "detailBox", "FnB": "inputBox" }
    }
  }
}
```

### 布局（比例 0.0~1.0）

```json
"layout": { "left": 0.02, "top": 0.08, "width": 0.96, "height": 0.25 }
```

| 方式 | left | top | right | bottom | width | height |
|------|------|-----|-------|--------|-------|--------|
| 左上角 | ✅ | ✅ | - | - | ✅ | ✅ |
| 右上角 | - | ✅ | ✅ | - | ✅ | ✅ |
| 底部通栏 | ✅ | - | - | ✅ | ✅ | ✅ |

### 样式

```json
"styles": {
  "primary-button": { "backColor": "#2563EB", "foreColor": "White", "font": { "size": 11, "bold": true } },
  "readonly-bg": { "backColor": "#F8FAFC", "foreColor": "#374151" }
}
// 控件引用: "style": "primary-button"
```

---

## 第四步：Bat 启动脚本

在 `EnableMent/` 下创建启动脚本。

### 基础模板（直接注册为函数 — 推荐）

> **不再使用环境变量传递配置信息**，改为通过 `--exec` 直接调用注册的函数。

```batch
@echo off
chcp 65001 >nul
title 功能名称 - FileDrivenFramework

call "%~dp0..\BatTool\Tools\Utils\BatToolConfig.bat"

:: ★ 生成批次 ID
for /f "usebackq delims=" %%i in (`powershell -NoProfile -Command "Get-Date -Format 'yyyyMMdd_HHmmss'"`) do set "CLEAN_TIMESTAMP=%%i"

:: ★ 通过 --exec 直接调用注册的函数，替代环境变量
dotnet run --project "%DIR_FILEDRIVEN%\FileDriven.Launcher" -- ^
  --exec "RegisterBatch(Run_%CLEAN_TIMESTAMP%, 功能启动, 启动.bat, 功能模式)" ^
  --exec "SetLogProjectRoot(%DIR_FILEDRIVEN%)"

if %ERRORLEVEL% NEQ 0 ( echo. & echo 执行出错，错误码: %ERRORLEVEL% & pause )
exit /b %ERRORLEVEL%
```

### 基础模板（注册为工作流 — 传统方式）

```batch
@echo off
chcp 65001 >nul
title 功能名称 - FileDrivenFramework

call "%~dp0..\BatTool\Tools\Utils\BatToolConfig.bat"
set "PS_SCRIPT=%DIR_SCRIPT%\FileDriven\FileDriven\运行测试GUI模式.ps1"

if not exist "%PS_SCRIPT%" ( echo [错误] 找不到脚本 & pause & exit /b 1 )
powershell -NoProfile -ExecutionPolicy Bypass -File "%PS_SCRIPT%" %*
if %ERRORLEVEL% NEQ 0 ( echo. & echo 执行出错，错误码: %ERRORLEVEL% & pause )
exit /b %ERRORLEVEL%
```

### 带参数处理

```batch
:parse_args
if "%~1"=="--batch" ( shift & dotnet run --project "%DIR_FILEDRIVEN%\FileDriven.Launcher" -- --workflow "YourWorkflow" --input "{}" & exit /b %ERRORLEVEL% )
if "%~1"=="--help" ( echo 用法: %~nx0 [--batch] [--help] & exit /b 0 )
if not "%~1"=="" ( shift & goto :parse_args )

powershell -NoProfile -ExecutionPolicy Bypass -File "%PS_SCRIPT%" %*
```

---

## 快速对照表

| 委托签名 | C# 注册 | WorkFlow step | 说明 |
|---------|---------|---------------|------|
| `Func<string, string>` | `(input) => result` | `parameters: ["${input}"]` | 单入单出 |
| `Func<string, string, string>` | `(a, b) => result` | `parameters: ["${a}", "${b}"]` | 双入单出 |
| `Func<string>` | `() => result` | `inputKeys: [], 无 parameters` | 无参 |
| `Func<double, double, double>` | `(a, b) => result` | `parameters: ["${a}", "${b}"]` | 数值运算 |
| 多步骤链式 | 注册多个 Fn | 多个 step 串联 | 前序输出→后续输入 |
| ComboBox 事件 | 注册 FnA, FnB | 无 | `selectedIndexChanged: ["FnA","FnB"]` |

---

## 最佳实践

### 命名规范

| 项目 | 规范 | 示例 |
|------|------|------|
| Library 类 | `{功能}Library` | `YourLibrary` |
| 函数名 | PascalCase，动词开头 | `ProcessData` |
| WorkFlow 文件 | `{功能}Workflow.json` | `YourWorkflow.json` |
| WorkFlow 名称 | PascalCase + `Workflow` | `YourWorkflow` |
| JSON UI 文件 | `{功能}UI.json` | `YourUI.json` |
| 控件名 | camelCase | `inputBox`, `executeBtn` |

### 调试

```bash
# 查看已注册函数
dotnet run --project "FileDriven\FileDriven.Launcher" -- --list-functions

# 测试工作流
dotnet run --project "FileDriven\FileDriven.Launcher" -- --workflow "YourWorkflow" --input "{}"
```

### 常见错误

| 错误 | 原因 |
|------|------|
| `Function 'xxx' not found` | 函数未注册或函数名拼写错误 |
| `未找到工作流定义: xxx` | JSON 文件缺失或 workflowName 不匹配 |
| `步骤 'xxx' 执行失败` | steps[].funcName 与注册的函数名不一致 |
| `Input type mismatch` | 函数参数类型与传入类型不匹配 |

---

## 附录：现有参考

| Library | 注册函数 | 用途 | 注册模式 |
|---------|---------|------|---------|
| `MathLibrary.cs` | `Add`, `Subtract`, `Multiply`, `Divide` | 数值运算 | 工作流 |
| `UIToolbox.cs` | `ClearOutput`, `ExecuteWorkflow`, `ReloadWorkflows`, `SaveResultToFile`, `LoadWorkflowDetails`, `LoadEnvironmentApiKey` | UI 操作 | 工作流 |
| `WorkflowLibrary.cs` | 动态注册所有 WorkFlow JSON | 工作流即函数 | 工作流 |
| `LoggingLibrary.cs` | `RegisterBatch`, `SetLogProjectRoot`, `EnableWduiLogging`, `RegisterPathAlias` | 日志基础设施 | **直接注册** |

| WorkFlow JSON | workflowName | 用途 |
|--------------|-------------|------|
| `MathOperationsWorkflow.json` | `MathOperationsWorkflow` | 数值运算 |
| `CodeReviewWorkflow.json` | `CodeReviewWorkflow` | 代码审查 |
| `DeepSeekChatWorkflow.json` | `DeepSeekChatWorkflow` | AI 对话 |

| JSON UI | 用途 |
|---------|------|
| `WorkflowRunner.json` | 通用工作流运行器 |
| `WorkflowInputUI.json` | 工作流输入配置 |
| `ConfigSelector.json` | 配置文件选择器 |

---

> **版本**: v1.0 | **更新**: 2026-05-26 | **项目**: WorkFlowPrj - FileDrivenFramework
