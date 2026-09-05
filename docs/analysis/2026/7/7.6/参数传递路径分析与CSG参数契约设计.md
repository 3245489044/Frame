# CSG 参数契约标准（CSG Parameter Contract Standard）

> **CSG 第四维度 P（Parameter）—— 函数调用的输入输出数据模型标准**
> 版本: v1.0-draft | 状态: 草案 | 最后更新: 2026-07-04

---

## 目录

1. [概述](#1-概述)
2. [设计原则](#2-设计原则)
3. [参数契约数据模型](#3-参数契约数据模型)
4. [在 CanonicalForm 中的表示](#4-在-canonicalform-中的表示)
5. [参数解析规范](#5-参数解析规范)
6. [与现有调用路径的映射](#6-与现有调用路径的映射)
7. [实施建议](#7-实施建议)
附录 A：[适配器实现参考](#附录-a适配器实现参考)
附录 B：[当前问题背景](#附录-b当前问题背景)

---

## 1. 概述

### 1.1 什么是参数契约？

CSG（Configuration-StateMachine-Graph）已定义三个基础投影：

| 投影 | 含义 | 类比 |
|------|------|------|
| **C = Configuration** | 静态描述 — **What** | 配置、属性、元数据 |
| **S = StateMachine** | 动态语义 — **How** | 状态转移、工作流 |
| **G = Graph** | 拓扑结构 — **Where** | 依赖图、控件树、数据流 |

**参数契约（Parameter Contract）** 是 CSG 的第四维度 **P**，定义函数调用的输入输出规范：

| 投影 | 含义 | 类比 |
|------|------|------|
| **P = Parameter** | 输入输出 — **With What** | 函数参数、数据绑定、消息体 |

### 1.2 核心思想

**参数是函数调用的数据契约。** 任何函数调用都可以抽象为：

```
Function(name, parameters) → result
  ↑           ↑              ↑
  函数标识    输入数据契约    输出数据契约
```

参数契约不关心"参数从哪里来"（那是适配器的工作），只关心"参数长什么样"——即参数的数据模型标准。

### 1.3 标准定位

参数契约是 **CSG 数据模型标准的一部分**，与 C/S/G 平级：

```
CSG 数据模型标准
├── C = Configuration  — 静态描述（What）
├── S = StateMachine   — 动态语义（How）
├── G = Graph          — 拓扑结构（Where）
└── P = Parameter      — 输入输出（With What） ← 本文档
```

**参数契约不定义：**
- 参数如何从 UI 控件获取（适配器实现）
- 参数如何从工作流上下文提取（适配器实现）
- 参数如何序列化传输（传输层职责）

**参数契约只定义：**
- 参数声明的数据格式
- 参数绑定的数据格式
- 参数值的表示方式
- 参数在 CanonicalForm 中的表达

---

## 2. 设计原则

| 原则 | 说明 |
|------|------|
| **数据优先** | 只定义数据格式和变换规则，不定义传输方式或实现接口 |
| **JSON 原生** | 序列化格式为 JSON，与 CanonicalForm 一致 |
| **声明式** | 参数绑定是声明式配置，不是命令式代码 |
| **与 CanonicalForm 统一** | 参数声明/绑定自然地融入节点-边结构 |
| **领域无关** | 核心模型不绑定任何特定领域（UI/Workflow/Database 等） |

---

## 3. 参数契约数据模型

### 3.1 三层模型

参数契约由三层数据模型构成，从声明到运行时逐层具体化：

```
参数声明（ParameterDeclaration）  — 编译时：函数形参定义
     ↓ 实例化
参数绑定（ParameterBinding）      — 配置时：参数值来源声明
     ↓ 求值
参数值（ParameterValue）          — 运行时：具体的参数值
```

### 3.2 ParameterDeclaration（参数声明）

描述函数的一个形参——**函数期望接收什么**。

```json
{
  "name": "a",
  "type": "int",
  "required": true,
  "default": 0,
  "description": "第一个加数"
}
```

| 字段 | 类型 | 必需 | 默认值 | 说明 |
|------|------|------|--------|------|
| `name` | string | ✅ | — | 参数名（对应形参名） |
| `type` | string | ❌ | `"any"` | 参数类型（见 §3.5 类型系统） |
| `required` | boolean | ❌ | `true` | 是否必需 |
| `default` | any | ❌ | `null` | 默认值（当非必需且未提供时使用） |
| `description` | string | ❌ | `""` | 参数说明 |

**示例：**

```json
// 无默认值的必需参数
{ "name": "a", "type": "int", "required": true }

// 有默认值的可选参数
{ "name": "format", "type": "string", "required": false, "default": "json" }

// 无类型约束的任意参数
{ "name": "data", "required": true }
```

### 3.3 ParameterBinding（参数绑定）

描述参数值**从哪里来**——**数据如何流入函数**。

```json
{
  "target": "a",
  "source": "valueATextBox",
  "sourceType": "control",
  "default": 0,
  "required": true
}
```

| 字段 | 类型 | 必需 | 默认值 | 说明 |
|------|------|------|--------|------|
| `target` | string | ✅ | — | 目标形参名 |
| `source` | string | ✅ | — | 源标识（控件名/上下文键名/字面值） |
| `sourceType` | string | ❌ | `"direct"` | 源类型：`control` / `context` / `direct` / `literal` |
| `default` | any | ❌ | `null` | 当源不可用时的回退值 |
| `required` | boolean | ❌ | `true` | 是否必需（为 true 且源不可用时抛出错误） |

**sourceType 说明：**

| sourceType | source 含义 | 示例 |
|-----------|-------------|------|
| `control` | UI 控件名 | `"valueATextBox"` |
| `context` | 工作流上下文键名 | `"WebSet_"` |
| `direct` | 调用方传入的字典键名 | `"a"` |
| `literal` | 字面值（source 即为值本身） | `"42"` |

### 3.4 ParameterValue（参数值）

运行时参数值的表示——**实际传递的数据**。

```json
{
  "name": "a",
  "value": 3,
  "type": "int"
}
```

| 字段 | 类型 | 必需 | 说明 |
|------|------|------|------|
| `name` | string | ✅ | 形参名 |
| `value` | any | ✅ | 参数值 |
| `type` | string | ❌ | 值类型（用于运行时类型校验） |

**批量表示：**

```json
{
  "parameters": [
    { "name": "a", "value": 3, "type": "int" },
    { "name": "b", "value": 5, "type": "int" }
  ]
}
```

### 3.5 类型系统

参数类型使用字符串标识，采用与 CSG 类型系统一致的分层设计：

| 类型 | 说明 | 示例值 |
|------|------|--------|
| `any` | 任意类型 | — |
| `string` | 字符串 | `"hello"` |
| `int` | 整数 | `42` |
| `float` | 浮点数 | `3.14` |
| `bool` | 布尔值 | `true` |
| `object` | JSON 对象 | `{"key": "val"}` |
| `array` | JSON 数组 | `[1, 2, 3]` |

自定义类型使用 `x-` 前缀，与 CSG 类型扩展规则一致。

---

## 4. 在 CanonicalForm 中的表示

### 4.1 参数声明作为节点属性

函数的参数声明存储在函数节点的 `properties.parameters` 中：

```json
{
  "nodes": [
    {
      "id": "func_add",
      "label": "Add",
      "type": "Action",
      "properties": {
        "functionName": "Add",
        "parameters": [
          { "name": "a", "type": "int", "required": true },
          { "name": "b", "type": "int", "required": true }
        ]
      }
    }
  ]
}
```

### 4.2 参数绑定作为 DataFlow 边

参数绑定表示为从**数据源节点**到**函数节点**的 `DataFlow` 边，边的 `properties` 中携带绑定信息：

```json
{
  "nodes": [
    {
      "id": "input_a",
      "label": "valueATextBox",
      "type": "Control",
      "properties": {
        "controlName": "valueATextBox"
      }
    },
    {
      "id": "input_b",
      "label": "valueBTextBox",
      "type": "Control",
      "properties": {
        "controlName": "valueBTextBox"
      }
    },
    {
      "id": "func_add",
      "label": "Add",
      "type": "Action",
      "properties": {
        "functionName": "Add",
        "parameters": [
          { "name": "a", "type": "int", "required": true },
          { "name": "b", "type": "int", "required": true }
        ]
      }
    }
  ],
  "edges": [
    {
      "id": "bind_a",
      "sourceId": "input_a",
      "targetId": "func_add",
      "type": "DataFlow",
      "properties": {
        "targetParameter": "a",
        "sourceType": "control"
      }
    },
    {
      "id": "bind_b",
      "sourceId": "input_b",
      "targetId": "func_add",
      "type": "DataFlow",
      "properties": {
        "targetParameter": "b",
        "sourceType": "control"
      }
    }
  ]
}
```

### 4.3 三种绑定模式的 CanonicalForm 表示

#### 模式 1：UI 控件绑定（Control → Function）

```
[Control: valueATextBox] --DataFlow--> [Action: Add]
  properties: { targetParameter: "a" }
```

#### 模式 2：工作流上下文绑定（Context → Function）

```
[Data: WebSet_] --DataFlow--> [Action: WebInvoke]
  properties: { targetParameter: "url", sourceType: "context" }
```

#### 模式 3：直接值绑定（Literal → Function）

```
[Data: _literal_42] --DataFlow--> [Action: Add]
  properties: { targetParameter: "a", sourceType: "literal", literalValue: 42 }
```

### 4.4 完整示例

```json
{
  "nodes": [
    {
      "id": "input_a",
      "label": "数值 A",
      "type": "Control",
      "properties": {
        "controlName": "valueATextBox",
        "uiType": "TextBox"
      }
    },
    {
      "id": "input_b",
      "label": "数值 B",
      "type": "Control",
      "properties": {
        "controlName": "valueBTextBox",
        "uiType": "TextBox"
      }
    },
    {
      "id": "func_add",
      "label": "加法运算",
      "type": "Action",
      "properties": {
        "functionName": "Add",
        "parameters": [
          { "name": "a", "type": "int", "required": true, "description": "第一个加数" },
          { "name": "b", "type": "int", "required": true, "description": "第二个加数" }
        ]
      }
    },
    {
      "id": "result_display",
      "label": "结果显示",
      "type": "Control",
      "properties": {
        "controlName": "resultLabel",
        "uiType": "Label"
      }
    }
  ],
  "edges": [
    {
      "id": "bind_a",
      "sourceId": "input_a",
      "targetId": "func_add",
      "type": "DataFlow",
      "label": "a = valueATextBox",
      "properties": {
        "targetParameter": "a",
        "sourceType": "control"
      }
    },
    {
      "id": "bind_b",
      "sourceId": "input_b",
      "targetId": "func_add",
      "type": "DataFlow",
      "label": "b = valueBTextBox",
      "properties": {
        "targetParameter": "b",
        "sourceType": "control"
      }
    },
    {
      "id": "output_result",
      "sourceId": "func_add",
      "targetId": "result_display",
      "type": "DataFlow",
      "label": "result → display",
      "properties": {
        "sourceParameter": "__result__",
        "sourceType": "return"
      }
    }
  ],
  "metadata": {
    "csgVersion": "1.0.0",
    "source": "MathFunctions.json",
    "created": "2026-07-04T00:00:00Z"
  }
}
```

---

## 5. 参数解析规范

### 5.1 解析流程

参数解析是将 ParameterBinding 求值为 ParameterValue 的过程，定义如下变换操作：

```
操作名：ResolveParameters
输入：FunctionNode（含 parameters 声明）+ DataFlow 边集合 + 数据源上下文
输出：ParameterValue[]
规则：
  1. 遍历 FunctionNode 的 DataFlow 入边
  2. 对每条边，根据 sourceType 从数据源上下文取值
  3. 将值赋给边指定的 targetParameter
  4. 对未绑定的参数，检查 required：
     - required=true → 抛出参数缺失错误
     - required=false → 使用 default 值
  5. 按 parameters 声明顺序排列输出
```

### 5.2 源类型取值规则

| sourceType | 取值规则 |
|-----------|---------|
| `control` | 从 UI 控件集合中按 source 查找控件值 |
| `context` | 从工作流上下文中按 source 键名取值 |
| `direct` | 从传入的 Dictionary 中按 source 键名取值 |
| `literal` | 直接使用 source 或 literalValue 字段的值 |

### 5.3 验证规则

| 规则 | 说明 |
|------|------|
| **ParameterCoverage** | 所有 required=true 的参数必须有对应的 DataFlow 入边 |
| **BindingConsistency** | DataFlow 边的 targetParameter 必须在目标节点的 parameters 中存在 |
| **TypeMatch** | 参数值的运行时类型应与声明类型兼容（可选检查） |

---

## 6. 与现有调用路径的映射

参数契约作为数据模型标准，不关心具体实现路径。以下仅说明三种现有路径如何映射到参数契约：

| 路径 | 参数声明来源 | 参数绑定来源 | 参数值来源 |
|------|-------------|-------------|-----------|
| **UI 事件绑定** | 函数注册表的形参反射 | `inputs` 配置中的控件名→形参名映射 | 运行时控件值 |
| **工作流配置** | 函数注册表的形参反射 | `ParameterKeys` 位置→形参名映射 | `WorkflowContext.Data` |
| **直接代码调用** | 函数注册表的形参反射 | 隐式绑定（键名=形参名） | 调用方传入的 Dictionary |

**核心统一点：** 无论参数从哪条路径来，最终都表示为 `ParameterValue[]`（按形参顺序排列的参数值数组），供函数执行引擎调用。

---

## 7. 实施建议

### 阶段一：数据模型定义（协议层）

| 文件 | 内容 |
|------|------|
| `CsharpPrj/Protocol/CSG/CSG.Unified/Contracts/IParameterContract.cs` | 参数契约数据模型（ParameterDeclaration / ParameterBinding / ParameterValue） |
| `CsharpPrj/Protocol/CSG/CSG.Unified/Contracts/ParameterBindingCollection.cs` | 绑定集合 + 序列化支持 |

### 阶段二：参数解析实现（Operation 层）

| 文件 | 内容 |
|------|------|
| `CsharpPrj/Operation/WorkFlowMethod/ParameterResolution/ParameterResolver.cs` | 参数解析器（实现 §5 的解析规范） |
| `CsharpPrj/Operation/WorkFlowMethod/ParameterResolution/Sources/` | 各源类型的取值实现 |

### 阶段三：整合优化

| 任务 | 说明 |
|------|------|
| 统一 `InvokeDelegateAsync` 入口 | 从多个重载合并为单一入口 |
| 集成 `ParameterResolver` | 替代现有的 `ParameterTransformer.FillFromDict` |
| 移除硬编码的双键映射 | 改为声明式 ParameterBinding |

---

## 附录 A：适配器实现参考

> 以下适配器是参数契约的具体实现示例，**不属于标准本身**，仅作为实现参考。

### A.1 参数解析器

```csharp
/// <summary>
/// 参数解析器 — 将 ParameterBinding 求值为 ParameterValue[]
/// 实现 CSG 参数契约的 ResolveParameters 变换操作
/// </summary>
public class ParameterResolver
{
    private readonly IParameterSourceProvider _sourceProvider;
    
    public ParameterResolver(IParameterSourceProvider sourceProvider)
    {
        _sourceProvider = sourceProvider;
    }
    
    /// <summary>
    /// 解析参数：bindings + 数据源上下文 → ParameterValue[]
    /// </summary>
    public ParameterValue[] Resolve(
        ParameterDeclaration[] declarations,
        ParameterBinding[] bindings,
        string functionName)
    {
        // 1. 构建形参名→声明的查找表
        var declMap = declarations.ToDictionary(d => d.Name);
        
        // 2. 解析每个绑定
        var resolved = new Dictionary<string, object?>();
        foreach (var binding in bindings)
        {
            var source = _sourceProvider.GetSource(binding.SourceType);
            var value = source.GetValue(binding.Source, functionName);
            resolved[binding.Target] = value ?? binding.Default;
        }
        
        // 3. 按声明顺序输出
        return declarations.Select(d => new ParameterValue
        {
            Name = d.Name,
            Value = resolved.TryGetValue(d.Name, out var v) ? v : d.Default,
            Type = d.Type
        }).ToArray();
    }
}
```

### A.2 数据源提供者

```csharp
/// <summary>
/// 参数源提供者 — 根据 sourceType 返回对应的取值器
/// </summary>
public interface IParameterSourceProvider
{
    IParameterSource GetSource(string sourceType);
}

public interface IParameterSource
{
    object? GetValue(string sourceKey, string functionName);
}

// 控件源
public class ControlParameterSource : IParameterSource
{
    private readonly Dictionary<string, object> _controlValues;
    
    public object? GetValue(string sourceKey, string functionName)
    {
        return _controlValues.TryGetValue(sourceKey, out var v) ? v : null;
    }
}

// 上下文源
public class ContextParameterSource : IParameterSource
{
    private readonly WorkflowContext _context;
    
    public object? GetValue(string sourceKey, string functionName)
    {
        return _context.Data.TryGetValue(sourceKey, out var v) ? v : null;
    }
}

// 直接源
public class DirectParameterSource : IParameterSource
{
    private readonly Dictionary<string, object> _parameters;
    
    public object? GetValue(string sourceKey, string functionName)
    {
        return _parameters.TryGetValue(sourceKey, out var v) ? v : null;
    }
}
```

---

## 附录 B：当前问题背景

> 以下内容记录了引入参数契约之前的三条割裂路径，作为设计决策的上下文参考。

### B.1 三条调用路径

| 路径 | 参数来源 | 键名约定 | 匹配方式 | 问题 |
|------|---------|---------|---------|------|
| **A：UI 事件绑定** | `inputs` 控件名 | 控件名（如 `valueATextBox`） | 按形参名匹配 Dictionary | ⚠️ 控件名 ≠ 形参名 → 默认值 |
| **B：工作流配置** | `ParameterKeys` 上下文键名 | 上下文键名（如 `WebSet_`） | 按位置传 `object[]` | ✅ 位置匹配无此问题 |
| **C：直接代码调用** | 调用方传入的 Dictionary | 取决于调用方 | 按形参名匹配 Dictionary | ✅ 调用方负责 |

### B.2 核心问题

1. **参数名不匹配**：UI 控件名（`valueATextBox`）与 C# 形参名（`a`）不一致，导致参数传递静默失败
2. **InvokeDelegateAsync 重载过多**：4 个重载各有独立的参数处理逻辑，存在重复代码
3. **缺少统一的参数抽象**：三条路径各自为政，没有统一的参数数据模型

### B.3 解决方案

引入 **CSG 参数契约（P 维度）**，将参数声明、参数绑定、参数值统一为数据模型标准，从根本上解决参数传递的一致性问题。

---

## 附录 C：关键文件索引

| 文件 | 说明 |
|------|------|
| `CsharpPrj/Protocol/CSG/CSG数据模型标准.md` | CSG 数据模型标准（C/S/G） |
| `CsharpPrj/Protocol/CSG/CSG.Unified/Contracts/` | CSG 契约接口目录 |
| `CsharpPrj/Operation/WorkFlowMethod/` | 工作流执行引擎 |
| `CsharpPrj/Operation/EventTriggerFramework/EventBinding/` | UI 事件绑定框架 |
| `CsharpPrj/Modules/ExecutionFramework/Engine/FunctionExecutor.cs` | 函数执行器 |
| `FileDriven/DrivenConfig/JsonConfig/MathFunctions.json` | UI 配置示例 |
