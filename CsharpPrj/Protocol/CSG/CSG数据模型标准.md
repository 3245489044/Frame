# CSG 数据模型标准（CSG Data Model Standard）

> **Configuration-StateMachine-Graph 统一中间表示**
> 版本: v1.0-draft | 状态: 草案 | 最后更新: 2026-06-25

---

## 目录

1. [概述](#1-概述)
2. [设计原则](#2-设计原则)
3. [CanonicalForm 数据模型](#3-canonicalform-数据模型)
4. [类型系统](#4-类型系统)
5. [URI 寻址方案](#5-uri-寻址方案)
6. [变换操作规范](#6-变换操作规范)
7. [一致性验证规则](#7-一致性验证规则)
8. [扩展机制](#8-扩展机制)
9. [与 MCP 的关系](#9-与-mcp-的关系)
10. [附录](#10-附录)

---

## 1. 概述

### 1.1 什么是 CSG？

CSG（Configuration-StateMachine-Graph）是一种**领域模型统一中间表示（Unified Intermediate Representation）**标准。它基于标签转移系统（Labeled Transition System, LTS）的数学基础，将任何领域模型统一表示为**节点-边图结构**，并提供三个标准投影：

| 投影 | 含义 | 类比 |
|------|------|------|
| **配置 (Configuration)** | 静态描述 — What | JSON/XML/YAML 等序列化格式 |
| **状态机 (StateMachine)** | 动态语义 — How | 有限状态机、工作流引擎 |
| **图 (Graph)** | 拓扑结构 — Where | 依赖图、ER 图、控件树 |

### 1.2 核心思想

**任何可计算的过程都可以表示为这三者的统一体：**

- **图**是最底层的数学结构：G = (V, E, L) 其中 V 是顶点集，E 是边集，L 是标签函数
- **状态机**是图的动态解释：状态 = 顶点，转移 = 边，标签 = 转移条件/动作
- **配置**是图的序列化：JSON 表示的邻接表或树结构

三者之间存在**可逆变换**，通过一个统一的中间表示（CanonicalForm）来沟通。

### 1.3 数学基础

三者统一于**标签转移系统 (Labeled Transition System, LTS)**：

```
LTS = (S, Act, →)
  S   = 状态集
  Act = 动作/标签集
  → ⊆ S × Act × S = 转移关系
```

| 视图 | 对应关系 |
|------|---------|
| **图 (Graph)** | LTS 的静态结构：S = 节点集, → = 边集 |
| **状态机 (StateMachine)** | LTS 的动态语义：S = 状态, Act = 触发操作, → = 转移规则 |
| **配置 (Configuration)** | LTS 的序列化表示：JSON/XML 编码的 S, Act, → |

### 1.4 标准定位

CSG 是**纯粹的数据模型标准**，不定义传输协议、不绑定编程语言、不依赖特定运行时。任何系统只要能生成或消费符合 CSG JSON Schema 的数据，就能实现互操作。

```
┌─────────────────────────────────────────────────────────────┐
│                    CSG 数据模型标准                           │
│                                                             │
│  ┌─────────────────────────────────────────────────────┐   │
│  │  核心数据模型 (Core Data Model)                       │   │
│  │  ├── CanonicalForm (JSON Schema)                    │   │
│  │  ├── CanonicalNode                                  │   │
│  │  └── CanonicalEdge                                  │   │
│  └─────────────────────────────────────────────────────┘   │
│                                                             │
│  ┌─────────────────────────────────────────────────────┐   │
│  │  类型系统 (Type System)                              │   │
│  │  ├── 标准类型注册表 (Core + Domain)                  │   │
│  │  └── 自定义类型扩展规则                              │   │
│  └─────────────────────────────────────────────────────┘   │
│                                                             │
│  ┌─────────────────────────────────────────────────────┐   │
│  │  操作规范 (Operation Specification)                   │   │
│  │  ├── 变换操作 (C↔IR, S↔IR, G↔IR)                   │   │
│  │  └── 一致性验证                                      │   │
│  └─────────────────────────────────────────────────────┘   │
│                                                             │
│  ┌─────────────────────────────────────────────────────┐   │
│  │  验证规则 (Validation Rules)                          │   │
│  │  ├── 节点覆盖、边覆盖、可达性、无死锁、类型一致       │   │
│  │  └── 标准错误码                                      │   │
│  └─────────────────────────────────────────────────────┘   │
│                                                             │
│  ┌─────────────────────────────────────────────────────┐   │
│  │  URI 寻址方案 (URI Addressing Scheme)                 │   │
│  │  ├── csg:// 方案定义                                 │   │
│  │  ├── 集合/选择器/查询语法                            │   │
│  │  └── URI 模板                                       │   │
│  └─────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────┘
```

---

## 2. 设计原则

### 2.1 核心原则

| 原则 | 说明 |
|------|------|
| **数据优先** | 只定义数据格式和变换规则，不定义传输方式 |
| **JSON 原生** | 序列化格式为 JSON，任何语言都能读写 |
| **可扩展** | 类型系统支持自定义扩展，不破坏核心兼容性 |
| **数学严谨** | 基于 LTS 理论，变换操作有明确的数学语义 |
| **领域无关** | 核心模型不绑定任何特定领域（UI/Workflow/Database 等） |

### 2.2 设计决策

| 决策 | 选择 | 理由 |
|------|------|------|
| 序列化格式 | JSON | 通用性最强，生态最成熟 |
| ID 类型 | 字符串 | 跨语言兼容，支持 UUID/路径/名称 |
| 类型系统 | 枚举 + 扩展 | 核心类型标准化，领域类型可扩展 |
| 属性存储 | 键值字典 | 灵活，不限制具体属性 |
| 边方向 | 有向 | LTS 的转移关系本质是有向的 |
| URI 方案 | `csg://` | 统一寻址，类似 MCP 的 Resource URI |

---

## 3. CanonicalForm 数据模型

### 3.1 顶层结构

```json
{
  "nodes": [
    { /* CanonicalNode */ }
  ],
  "edges": [
    { /* CanonicalEdge */ }
  ],
  "metadata": {
    "csgVersion": "1.0.0",
    "source": "UIConfiguration",
    "schema": "https://csg-schema.org/v1/canonical-form.json",
    "created": "2026-06-25T00:00:00Z"
  }
}
```

### 3.2 CanonicalNode（规范节点）

```json
{
  "id": "node_001",
  "label": "Idle State",
  "type": "State",
  "properties": {
    "description": "空闲状态",
    "timeout": 30000
  }
}
```

| 字段 | 类型 | 必需 | 默认值 | 说明 |
|------|------|------|--------|------|
| `id` | string | ✅ | — | 全局唯一标识（UUID、路径、名称均可） |
| `label` | string | ❌ | `""` | 显示标签 |
| `type` | string | ❌ | `"Default"` | 节点类型（见类型系统 §4） |
| `properties` | object | ❌ | `{}` | 扩展属性字典 |

### 3.3 CanonicalEdge（规范边）

```json
{
  "id": "edge_001",
  "sourceId": "node_001",
  "targetId": "node_002",
  "label": "onClick",
  "type": "Transition",
  "condition": "validate()",
  "weight": 1.0,
  "properties": {
    "trigger": "click"
  }
}
```

| 字段 | 类型 | 必需 | 默认值 | 说明 |
|------|------|------|--------|------|
| `id` | string | ✅ | — | 边唯一标识 |
| `sourceId` | string | ✅ | — | 源节点 ID |
| `targetId` | string | ✅ | — | 目标节点 ID |
| `label` | string | ❌ | `""` | 边显示标签 |
| `type` | string | ❌ | `"Default"` | 边类型（见类型系统 §4） |
| `condition` | string | ❌ | `null` | 转移条件表达式（状态机用） |
| `weight` | number | ❌ | `1.0` | 边权重（图算法用） |
| `properties` | object | ❌ | `{}` | 扩展属性字典 |

### 3.4 Metadata（元数据）

| 字段 | 类型 | 必需 | 说明 |
|------|------|------|------|
| `csgVersion` | string | ✅ | CSG 协议版本（语义化版本号） |
| `source` | string | ❌ | 来源描述（如 "UIConfiguration"） |
| `schema` | string | ❌ | 使用的 JSON Schema URI |
| `created` | string | ❌ | 创建时间（ISO 8601 格式） |

### 3.5 完整 JSON Schema

正式 JSON Schema 定义见：[schemas/csg-canonical-form.schema.json](./schemas/csg-canonical-form.schema.json)

---

## 4. 类型系统

### 4.1 设计思路

类型系统采用**分层注册表**设计：

- **Core 层**：所有领域通用的基础类型（State, Action, Transition 等）
- **Domain 层**：特定领域的类型（UI 的 View/Control，Database 的 Table/Field 等）
- **Custom 层**：第三方自定义类型（使用 `x-` 前缀避免冲突）

### 4.2 标准节点类型

#### Core 节点类型

| 类型 | 说明 | 适用投影 |
|------|------|---------|
| `Default` | 普通节点 | C/S/G |
| `State` | 状态机状态 | S |
| `Action` | 动作/操作 | C/S |
| `Condition` | 条件分支 | C/S/G |
| `Start` | 起始节点 | S/G |
| `End` | 终止节点 | S/G |
| `Parallel` | 并行网关 | C/G |
| `Merge` | 合并网关 | C/G |
| `Data` | 数据节点 | C/G |

#### UI 领域节点类型

| 类型 | 说明 | 适用投影 |
|------|------|---------|
| `View` | UI 视图 | C/G |
| `Control` | UI 控件 | C/G |

#### Database 领域节点类型

| 类型 | 说明 | 适用投影 |
|------|------|---------|
| `Table` | 数据库表 | C/G |
| `Field` | 数据库字段 | C/G |

#### ECS 领域节点类型

| 类型 | 说明 | 适用投影 |
|------|------|---------|
| `Entity` | ECS 实体 | C/G |
| `Component` | ECS 组件 | C/G |

### 4.3 标准边类型

#### Core 边类型

| 类型 | 说明 | 适用投影 |
|------|------|---------|
| `Default` | 普通边 | C/S/G |
| `Transition` | 状态转移 | S |
| `DataFlow` | 数据流 | C/G |
| `ControlFlow` | 控制流 | C/S/G |
| `Reference` | 引用 | C/G |
| `Composition` | 组合（父子关系） | C/G |
| `Dependency` | 依赖 | C/G |
| `Association` | 关联 | C/G |

#### UI 领域边类型

| 类型 | 说明 | 适用投影 |
|------|------|---------|
| `Navigation` | UI 导航 | C/G |

### 4.4 自定义类型扩展规则

第三方可以通过以下规则扩展类型系统：

1. **命名规则**：自定义类型必须使用 `x-{domain}-{name}` 格式
2. **注册**：建议在类型注册表中注册以避免冲突
3. **兼容性**：不识别的类型应视为 `Default` 处理

```json
{
  "type": "x-iot-sensor",
  "properties": {
    "unit": "celsius",
    "range": "0-100"
  }
}
```

### 4.5 完整类型注册表

正式类型注册表定义见：[schemas/csg-type-registry.json](./schemas/csg-type-registry.json)

---

## 5. URI 寻址方案

### 5.1 方案标识

CSG 使用 `csg://` 作为 URI 方案（Scheme），用于在 CanonicalForm 内部寻址元素。

### 5.2 URI 语法

```
csg://{collection}[/{selector}][?{query}]
```

| 部分 | 说明 | 示例 |
|------|------|------|
| `collection` | 集合名称 | `nodes`, `edges`, `metadata`, `topology` |
| `selector` | 选择器（可选） | 节点 ID、边 ID、元数据键 |
| `query` | 查询参数（可选） | 过滤、排序等 |

### 5.3 标准 URI 路径

#### 节点寻址

| URI | 返回类型 | 说明 |
|-----|---------|------|
| `csg://nodes` | `CanonicalNode[]` | 所有节点列表 |
| `csg://node/{id}` | `CanonicalNode?` | 指定节点 |
| `csg://nodes?type=State` | `CanonicalNode[]` | 按类型过滤节点 |

#### 边寻址

| URI | 返回类型 | 说明 |
|-----|---------|------|
| `csg://edges` | `CanonicalEdge[]` | 所有边列表 |
| `csg://edge/{id}` | `CanonicalEdge?` | 指定边 |
| `csg://edges?type=Transition` | `CanonicalEdge[]` | 按类型过滤边 |

#### 元数据寻址

| URI | 返回类型 | 说明 |
|-----|---------|------|
| `csg://metadata` | `object` | 所有元数据 |
| `csg://metadata/{key}` | `any?` | 指定元数据项 |

#### 投影查询（扩展）

| URI | 返回类型 | 说明 |
|-----|---------|------|
| `csg://projection/config` | `CanonicalForm` | 配置投影 |
| `csg://projection/sm` | `CanonicalForm` | 状态机投影 |
| `csg://projection/graph` | `CanonicalForm` | 图投影 |

### 5.4 URI 模板

| 模板 | 说明 |
|------|------|
| `csg://nodes` | 所有节点列表 |
| `csg://node/{id}` | 指定节点 |
| `csg://edges` | 所有边列表 |
| `csg://edge/{id}` | 指定边 |
| `csg://metadata` | 所有元数据 |
| `csg://metadata/{key}` | 指定元数据 |
| `csg://projection/config` | 配置投影 |
| `csg://projection/sm` | 状态机投影 |
| `csg://projection/graph` | 图投影 |

---

## 6. 变换操作规范

### 6.1 操作分类

| 分类 | 说明 | 示例 |
|------|------|------|
| `transform` | 形式变换（C↔IR, S↔IR, G↔IR） | ConfigToCanonical |
| `validate` | 一致性验证 | ValidateConsistency |
| `build` | 规范形式构建 | BuildFromConfig |

### 6.2 标准操作签名

#### 形式变换操作

| 操作名 | 输入 | 输出 | 可逆 | 反向操作 |
|--------|------|------|------|---------|
| `ConfigToCanonical` | 领域配置对象 | `CanonicalForm` | ✅ | `CanonicalToConfig` |
| `StateMachineToCanonical` | 状态机对象 | `CanonicalForm` | ✅ | `CanonicalToStateMachine` |
| `GraphToCanonical` | 图对象 | `CanonicalForm` | ✅ | `CanonicalToGraph` |

### 6.3 操作元数据格式

```json
{
  "name": "ConfigToCanonical",
  "label": "配置转规范形式",
  "description": "将领域配置对象转换为 CanonicalForm",
  "category": "transform",
  "inputType": "object",
  "outputType": "CanonicalForm",
  "isReversible": true,
  "reverseOperationName": "CanonicalToConfig",
  "version": "1.0",
  "tags": ["config", "canonical", "transform"]
}
```

### 6.4 完整操作规范

正式操作规范定义见：[schemas/csg-operations.json](./schemas/csg-operations.json)

---

## 7. 一致性验证规则

### 7.1 验证规则定义

CSG 定义了五条核心一致性验证规则，用于确保 C/S/G 三个投影之间的一致性：

| 规则 | 描述 | 数学定义 |
|------|------|---------|
| **NodeCoverage** | C 的每个元素在 S 和 G 中都有对应节点 | ∀n∈C.Nodes: n.Id ∈ S.Nodes ∧ n.Id ∈ G.Nodes |
| **EdgeCoverage** | C 的每个关系在 S 和 G 中都有对应边 | ∀e∈C.Edges: ∃e'∈S.Edges, e''∈G.Edges: e.SourceId=e'.SourceId ∧ e.TargetId=e'.TargetId |
| **Reachability** | S 的所有状态在 G 中都是可达的 | ∀s∈S.Nodes: ∃path from Start to s in G |
| **NoDeadlock** | S 的每个非终止状态都有至少一个出转移 | ∀s∈S.Nodes: s.Type≠End → deg⁺(s)≥1 |
| **TypeConsistency** | C/S/G 中对应元素的类型标签一致 | ∀n∈C.Nodes: type_C(n) = type_S(n) = type_G(n) |

### 7.2 验证结果格式

```json
{
  "isValid": false,
  "errors": [
    {
      "code": "NodeCoverage",
      "rule": "NodeCoverage",
      "message": "节点 'btn_submit' 在状态机投影中不存在",
      "sourceId": "btn_submit",
      "targetId": null
    }
  ],
  "warnings": [
    {
      "rule": "NoDeadlock",
      "message": "状态 'state_idle' 没有出转移",
      "sourceId": "state_idle",
      "targetId": null
    }
  ]
}
```

### 7.3 标准错误码

| 错误码 | 说明 |
|--------|------|
| `NodeCoverage` | 节点覆盖不完整 |
| `EdgeCoverage` | 边覆盖不完整 |
| `Reachability` | 存在不可达状态 |
| `NoDeadlock` | 存在死锁状态 |
| `TypeConsistency` | 类型标签不一致 |
| `Unspecified` | 未指定的其他错误 |

---

## 8. 扩展机制

### 8.1 类型扩展

通过类型注册表机制，任何领域都可以注册自己的节点/边类型：

```json
{
  "x-myapp-task": {
    "extends": "Default",
    "description": "自定义任务节点",
    "properties": {
      "priority": { "type": "integer", "default": 0 },
      "assignee": { "type": "string" }
    }
  }
}
```

### 8.2 属性扩展

`properties` 字典支持任意键值对，不限制具体属性名和类型：

```json
{
  "id": "node_001",
  "type": "State",
  "properties": {
    "x-myapp-customProp": "任意值",
    "x-anotherProp": 42
  }
}
```

### 8.3 元数据扩展

`metadata` 字典支持任意自定义元数据：

```json
{
  "metadata": {
    "csgVersion": "1.0.0",
    "source": "MyApp",
    "x-myapp-environment": "production",
    "x-myapp-version": "2.3.1"
  }
}
```

### 8.4 适配器映射规范（参考）

适配器用于将领域特定模型映射到 CanonicalForm，建议的映射描述格式：

```yaml
adapter:
  name: "MyAppAdapter"
  domain: "myapp"
  version: "1.0"
  mappings:
    - source: "MyEntity"
      target: "CanonicalNode"
      type: "x-myapp-entity"
      properties:
        id: "$.entityId"
        label: "$.displayName"
        x-custom: "$.customField"
```

---

## 9. 与 MCP 的关系

### 9.1 定位差异

| 维度 | CSG | MCP |
|------|-----|-----|
| **类型** | 数据模型标准 | 通信协议标准 |
| **层次** | 数据表示层 | 传输/工具调用层 |
| **核心产出** | JSON Schema + 类型注册表 | JSON-RPC 消息 + Transport |
| **适用范围** | 任何领域模型的统一表示 | AI 与工具的标准化交互 |
| **依赖** | 无（纯数据格式） | JSON-RPC 2.0 |

### 9.2 互补关系

CSG 和 MCP 解决不同层次的问题，可以互补使用：

```
MCP 场景：
  AI 通过 MCP 调用工具
    → 工具返回 CSG CanonicalForm
    → AI 理解领域结构
    → AI 执行变换操作
    → AI 验证一致性

CSG 场景：
  系统 A 导出 CSG CanonicalForm
    → 系统 B 导入并解析
    → 自动生成状态机
    → 自动生成图分析
    → 自动验证一致性
```

### 9.3 CSG 对 MCP 的借鉴

CSG 在设计上借鉴了 MCP 的若干设计模式：

| CSG 特性 | 借鉴自 MCP | 说明 |
|----------|-----------|------|
| 协议版本协商 | 版本兼容性检查 | `CsgProtocol.IsCompatible()` |
| URI 寻址 | Resource URI | `csg://node/{id}` 类似 MCP 的 `file:///path` |
| 操作元数据 | Tool Definition | `TransformOperation` 类似 MCP 的 `tools/list` |
| URI 模板 | Resource Templates | `ListUriTemplates()` 类似 MCP 的 ResourceTemplate |
| 错误码 | ErrorCode | `CsgErrorCode` 类似 MCP 的标准错误码 |

---

## 10. 附录

### 10.1 完整示例

#### UI 配置 → CanonicalForm

```json
{
  "nodes": [
    { "id": "form_main", "label": "MainForm", "type": "View", "properties": { "title": "主窗口", "width": 800, "height": 600 } },
    { "id": "btn_submit", "label": "Submit", "type": "Control", "properties": { "text": "提交", "x": 100, "y": 200 } },
    { "id": "btn_cancel", "label": "Cancel", "type": "Control", "properties": { "text": "取消", "x": 200, "y": 200 } },
    { "id": "state_idle", "label": "Idle", "type": "State", "properties": {} },
    { "id": "state_loading", "label": "Loading", "type": "State", "properties": {} },
    { "id": "state_success", "label": "Success", "type": "State", "properties": {} },
    { "id": "state_error", "label": "Error", "type": "State", "properties": {} }
  ],
  "edges": [
    { "id": "e1", "sourceId": "form_main", "targetId": "btn_submit", "type": "Composition", "label": "contains" },
    { "id": "e2", "sourceId": "form_main", "targetId": "btn_cancel", "type": "Composition", "label": "contains" },
    { "id": "e3", "sourceId": "state_idle", "targetId": "state_loading", "type": "Transition", "label": "onSubmit", "condition": "validate()" },
    { "id": "e4", "sourceId": "state_loading", "targetId": "state_success", "type": "Transition", "label": "onSuccess" },
    { "id": "e5", "sourceId": "state_loading", "targetId": "state_error", "type": "Transition", "label": "onError" },
    { "id": "e6", "sourceId": "state_error", "targetId": "state_idle", "type": "Transition", "label": "onRetry" }
  ],
  "metadata": {
    "csgVersion": "1.0.0",
    "source": "UIConfiguration",
    "created": "2026-06-25T00:00:00Z"
  }
}
```

#### Workflow 定义 → CanonicalForm

```json
{
  "nodes": [
    { "id": "start", "label": "Start", "type": "Start" },
    { "id": "step_review", "label": "Review Document", "type": "Action", "properties": { "assignee": "reviewer", "timeout": 86400000 } },
    { "id": "step_approve", "label": "Approve", "type": "Action", "properties": { "assignee": "manager" } },
    { "id": "cond_approved", "label": "Approved?", "type": "Condition" },
    { "id": "step_publish", "label": "Publish", "type": "Action" },
    { "id": "end", "label": "End", "type": "End" }
  ],
  "edges": [
    { "id": "wf1", "sourceId": "start", "targetId": "step_review", "type": "ControlFlow" },
    { "id": "wf2", "sourceId": "step_review", "targetId": "cond_approved", "type": "ControlFlow" },
    { "id": "wf3", "sourceId": "cond_approved", "targetId": "step_publish", "type": "ControlFlow", "condition": "approved == true" },
    { "id": "wf4", "sourceId": "cond_approved", "targetId": "step_review", "type": "ControlFlow", "condition": "approved == false" },
    { "id": "wf5", "sourceId": "step_publish", "targetId": "end", "type": "ControlFlow" }
  ],
  "metadata": {
    "csgVersion": "1.0.0",
    "source": "WorkflowDefinition",
    "created": "2026-06-25T00:00:00Z"
  }
}
```

### 10.2 版本历史

| 版本 | 日期 | 变更说明 |
|------|------|----------|
| v1.0-draft | 2026-06-25 | 初始草案 |

### 10.3 参考资源

| 资源 | 链接 |
|------|------|
| CSG 架构设计文档 | [CSG统一框架_架构设计.md](./CSG统一框架_架构设计.md) |
| JSON Schema 规范 | https://json-schema.org/ |
| MCP 协议规范 | https://modelcontextprotocol.io/ |
| 标签转移系统 (LTS) | https://en.wikipedia.org/wiki/Labeled_transition_system |
