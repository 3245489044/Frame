# RGE 数据模型标准（Reference-Graph Execution Data Model Standard）

> **Reference-Graph Execution 引用图执行统一中间表示**
> 版本: v1.0-draft | 状态: 草案 | 最后更新: 2026-08-01

---

## 目录

1. [概述](#1-概述)
2. [设计原则](#2-设计原则)
3. [RGE 与 ECS 的关系](#3-rge-与-ecs-的关系)
4. [核心数据模型](#4-核心数据模型)
5. [引用传递语义](#5-引用传递语义)
6. [嵌套图结构](#6-嵌套图结构)
7. [System 契约](#7-system-契约)
8. [L0 不变式](#8-l0-不变式)
9. [与 CSG 的关系](#9-与-csg-的关系)
10. [附录](#10-附录)

---

## 1. 概述

### 1.1 什么是 RGE？

RGE（Reference-Graph Execution, 引用图执行）是一种**领域无关的执行模型统一中间表示（Unified Intermediate Representation）**标准。它基于**运行时引用流（Reference Flow）**的数学基础，将任何可执行过程统一表示为**嵌套图结构**，其中：

- **图结构即运算关系**：Entity 之间的依赖边描述了"谁消费谁的引用"
- **运行时引用流是执行本质**：执行 = 沿引用路径解析值 → 触发 Entity → 暴露新引用

### 1.2 核心思想

**执行不是遍历步骤，而是传递引用。**

任何一个可执行过程都可以表示为：

```
ExecutionGraph = (Entities, Edges, RootEntry)
  Entities = { Entity₁, Entity₂, ..., Entityₙ }    // 执行单元实例
  Edges    = { Edge(i→j) | j 消费 i 的输出引用 }
  RootEntry = 图的入口 Entity（可选）
```

执行框架的责任**不是**执行具体逻辑，而是**管理引用的传递**——沿图递归解析 `ParamRef` 路径，找到对应的实例值，交给注入的 System 策略执行，再把结果作为新引用暴露给下游。

### 1.3 数学基础

RGE 统一于**引用图（Reference Graph）**：

```
RG = (N, R, ρ)
  N  = 节点集（Entity 的引用标识）
  R  = 引用关系集（依赖边）
  ρ: N → (R × N)   // 引用解析函数：定位节点的值来源
```

| 概念 | 数学对应 |
|------|---------|
| **Entity** | 图中的顶点 N，携带引用标识（UID） |
| **ParamRef** | 有向边 R，表示"消费某个源实体的引用" |
| **ReferenceResolver** | 解析函数 ρ：路径 → 实例值 |
| **嵌套图** | 顶点可携带子图（递归结构），ρ 递归展开 |

### 1.4 标准定位

RGE 是**纯粹的数据模型标准**（L0），不定义传输协议、不绑定执行引擎、不依赖特定运行时。它只声明：

1. **图长什么样**（核心数据模型）
2. **引用如何定位**（引用传递语义）
3. **执行能力如何转包**（System 契约的边界）

任何执行框架只要能消费符合 RGE 模型的图，就能获得统一的引用传递语义；具体执行逻辑由 IoC 注入的 System 策略提供（L2-L3），与 RGE 模型松耦合。

---

## 2. 设计原则

### 2.1 原则清单

| # | 原则 | 说明 |
|---|------|------|
| P1 | **图即运算关系** | 不单独定义"步骤/工作流/节点"，一切皆 Entity + 边 |
| P2 | **引用即路径** | ParamRef 使用路径语法定位引用，天然支持嵌套 |
| P3 | **执行即转包** | 框架只调度引用流，具体执行交给注入的 System |
| P4 | **粒度由调用方决定** | 嵌套式 Entity，调用方自由决定嵌套深度 |
| P5 | **L0 不可变** | 核心引用流协调逻辑固化，策略可替换 |

### 2.2 与传统 ECS 的差异（详见 §3）

RGE 借鉴 ECS 的"数据与逻辑分离"思想，但**不是**传统 ECS——传统 ECS 是扁平的批量查询模型，RGE 是嵌套的引用传递模型。

---

## 3. RGE 与 ECS 的关系

### 3.1 本质差异

| 维度 | 传统 ECS | RGE |
|------|---------|-----|
| **结构** | 扁平：Entity 是纯 ID，System 批量匹配 Component 集合 | 嵌套：Entity 递归携带 SubGraph |
| **数据流动** | System 按 Component 签名批量查询 | ParamRef 路径逐个解析引用 |
| **执行粒度** | 帧更新，批量遍历 | 图遍历，按依赖边推进 |
| **关注点** | 性能（缓存友好/批处理） | 语义（引用关系/依赖可见） |
| **关系表达** | 通过 Component 数据隐含 | 通过 DependencyEdge 显式 |

### 3.2 借鉴与超越

RGE **借鉴**了 ECS 的核心思想——数据（Component）与逻辑（System）分离——但**超越**了扁平结构的限制：

- ECS 的 Entity 是"无名盒子"，RGE 的 Entity 是"有名引用节点"
- ECS 的 System 靠查询匹配，RGE 的 System 靠 IoC 注入按 StepType 路由
- ECS 的图隐含在 Component 数据中，RGE 的图显式存在于 DependencyEdge

### 3.3 为什么需要新名称

RGE 不是 ECS 的变体，而是**新的执行模型范式**：

- 如果叫 ECS，会误以为 Entity 是纯 ID、System 是批量查询
- RGE 强调"引用图"——Entity 是有名引用，图是显式运算关系

---

## 4. 核心数据模型

### 4.1 Entity（执行单元）

```json
{
  "uid": "entity-001",                  // 全局唯一引用标识
  "name": "加法运算",                   // 人类可读名称
  "components": {                       // 挂载的声明数据（不含逻辑）
    "funcRef": "Plus",                  // 函数引用
    "stepType": "Function",             // 执行类型（路由到 System）
    "outputKey": "addResult"            // 输出引用暴露名
  },
  "paramRefs": [                        // 输入引用声明
    { "target": "param0", "path": "../input/a" }
  ],
  "subGraph": null                      // 嵌套子图（可选）
}
```

**Entity 职责边界**：
- 只持有**引用标识**和**声明数据**（Component）
- 不持有执行逻辑
- 可携带 SubGraph 形成嵌套

### 4.2 Component（声明数据）

Component 是挂载在 Entity 上的**声明数据块**。RGE 预定义基础 Component 类型：

| Component | 语义 | 示例值 |
|-----------|------|--------|
| `FuncRef` | 函数/执行目标引用 | `"Plus"`、`"agent.invoke"` |
| `StepType` | 执行类型（路由键） | `Function/Condition/Loop/Parallel` |
| `OutputKey` | 输出引用暴露名 | `"addResult"` |
| `InputRefs` | 输入引用声明（ParamRef 集合） | `[{target, path}]` |
| `Policy` | 弹性策略声明 | `{retry: 3, timeoutMs: 5000}` |
| `Metadata` | 自由附加元数据 | `{description: "..."}` |

> Component 仅声明，解释由注入的 System 策略完成。

### 4.3 DependencyEdge（依赖边）

```json
{
  "from": "entity-001",
  "to": "entity-002",
  "via": { "target": "param0", "path": "entity-001/addResult" }
}
```

**语义**：`to` 的 `param0` 引用 `from` 的 `addResult` 输出。边本身即运算关系。

### 4.4 ExecutionGraph（执行图）

```json
{
  "graphId": "workflow-001",
  "entryEntity": "entity-001",
  "entities": [ ... ],
  "edges": [ ... ],
  "output": { "addResult": "finalResult" }   // 图级输出映射
}
```

### 4.5 核心不变式

```
∀Entity ∈ Graph: Entity.uid 唯一
∀DependencyEdge ∈ Graph: from/to 必须是图中存在的 Entity
∀ParamRef ∈ Entity: path 必须在执行时能被 ReferenceResolver 解析
∃RootEntry → 图可执行（从入口开始遍历）
```

---

## 5. 引用传递语义

### 5.1 ParamRef 路径语法

引用通过**路径字符串**定位，天然支持嵌套：

| 路径 | 语义 |
|------|------|
| `../input/a` | 父图输入对象的属性 a |
| `./entityA/addResult` | 同图内 Entity A 的输出 addResult |
| `input.b.c` | 根输入对象的嵌套属性 b.c |
| `$env:API_KEY` | 外部环境引用 |
| `$const:0.5` | 常量字面量 |

### 5.2 引用解析作用域

```
Entity 引用解析时按以下作用域顺序（从内到外）：
  1. 自身 OutputKey 缓存（已计算的结果）
  2. 所属 SubGraph 内兄弟 Entity 的输出
  3. 父图（Parent Graph）的输入/输出
  4. 图级根输入对象（RootInput）
  5. 外部作用域（环境变量/常量/注册中心）
```

### 5.3 引用解析函数（ReferenceResolver 契约）

```
ReferenceResolver.Resolve(graph, entity, paramRef) → Value
  1. 解析路径 → 定位作用域
  2. 若路径指向未执行 Entity → 先递归执行该 Entity（懒解析）
  3. 返回值（或 Null/缺失标记）
```

### 5.4 缺失引用处理

| 场景 | 行为 |
|------|------|
| 路径存在但值为 null | 传递 null（类型化时标记） |
| 路径指向不存在 Entity | 缺失标记 + 不入参（不传键名字符串） |
| 循环引用 | 由执行框架的引用图环检测阻断（RGE 只声明契约） |

---

## 6. 嵌套图结构

### 6.1 递归 Entity

**任何 Entity 都可以携带 SubGraph**，形成递归嵌套：

```
ExecutionGraph (根)
└── Entity A (工作流级)          ← 粗粒度
    ├── Component: FuncRef=null（容器）
    └── SubGraph
        ├── Entity A1 (步骤级)   ← 细粒度
        │   ├── Component: FuncRef=Plus
        │   └── ParamRef → "../input/a"
        └── Entity A2 (步骤级)
            ├── Component: FuncRef=Minus
            └── ParamRef[0] → "./A1/addResult"
```

### 6.2 子图执行语义

**子图结果 = 父 Entity 的 Value**：

```
执行 Entity A：
  A 带 SubGraph → 递归执行 A.SubGraph
  → SubGraph 的全部输出映射 = A.Value
  → A.Value 通过 A.OutputKey 暴露给父图下游
  → 父图下游 Entity 的 ParamRef 可引用 A.OutputKey
```

### 6.3 粒度自由

调用方根据场景自由选择嵌套深度：

| 调用方视角 | 嵌套方式 |
|-----------|---------|
| 粗粒度 | 整个工作流 = 1 个 Entity（含子图） |
| 细粒度 | 每个函数调用 = 1 个 Entity |
| 混合 | 父 Entity 携带子图，子图内又是更细 Entity |

> 嵌套深度由调用方决定，RGE 模型本身不限制。

---

## 7. System 契约

### 7.1 三类 System 接口（仅声明边界，实现由执行框架提供）

```
IReferenceSystem        // 引用解析：路径 → 值
  Resolve(graph, entity, paramRef) → Value

IIntentSystem           // 执行意图：按 StepType 执行 Entity
  Execute(entity, resolvedParams, context) → EntityResult

ILifecycleSystem        // 生命周期：图级协调（可选）
  OnGraphStart(graph) / OnEntityComplete(entity, result) / OnGraphEnd(graph)
```

### 7.2 转包边界

```
┌────────────────────────────────────────────────┐
│ RGE 模型层（L0, 本标准）                        │
│  ▸ 图结构 + 引用解析契约                        │
│  ▸ 不实现任何 System                           │
├────────────────────────────────────────────────┤
│ 执行框架（L1-L3, 消费 RGE）                    │
│  ▸ 实现 System 接口                            │
│  ▸ IoC 注入策略（FunctionSystem/Loop/...）     │
│  ▸ 生命周期管理                                │
└────────────────────────────────────────────────┘
```

### 7.3 策略可替换

执行能力完全"外包"：同一张 RGE 图可以被不同 System 策略集消费，产生不同的执行行为（本地函数 / 远程调用 / 模拟执行）。

---

## 8. L0 不变式

### 8.1 不可变规则

| # | 不变式 | 说明 |
|---|--------|------|
| L0-1 | 图结构定义不变 | Entity/Component/Edge/Graph 的模型不再改动 |
| L0-2 | 引用解析逻辑不变 | 路径语法 + 作用域顺序固定 |
| L0-3 | System 契约不变 | 三类 System 接口签名固定 |
| L0-4 | 不渗入执行概念 | RGE 层禁止出现调度/会话/策略等执行概念 |

### 8.2 证明责任外部化

- 引用可解析性由执行框架验证（非 RGE 层）
- 图合法性由构建器校验（非 RGE 层）
- RGE 只提供**结构与契约**，不承诺执行正确性

---

## 9. 与 CSG 的关系

RGE 与 CSG 是 **Protocol 层平级的两个标准**，互补而非重叠：

| 维度 | CSG | RGE |
|------|-----|-----|
| 定位 | 领域模型统一中间表示 | 执行模型统一中间表示 |
| 焦点 | 状态/配置/图三投影 | 引用传递/图运算关系 |
| 用途 | 描述"系统是什么样" | 描述"执行如何流转" |
| 关系 | 可以将 CSG CanonicalForm 作为 RGE Entity 的 Component 挂载 | RGE 图可作为 CSG 的动态语义解释 |

**两者的可能协作**：
- CSG 描述工作流的结构（节点/边/配置）
- RGE 描述该结构执行时的引用流转
- CSG CanonicalForm ↔ RGE ExecutionGraph 可双向转换

---

## 10. 附录

### 10.1 术语表

| 术语 | 定义 |
|------|------|
| Entity | 执行单元引用节点，携带 Component + 可选 SubGraph |
| Component | 挂载在 Entity 上的声明数据块 |
| ParamRef | 输入引用声明（路径定位） |
| DependencyEdge | 依赖边，表达"消费谁的引用" |
| ExecutionGraph | 嵌套图，根级实体集合 |
| ReferenceResolver | 引用解析函数（路径 → 值） |
| System | 执行能力提供方（经 IoC 注入） |

### 10.2 后续演进方向

- `schemas/`：RGE JSON Schema 定义
- `RGE.Unified/`：RGE 通用实现骨架（零依赖模型层）
- 与 CSG 的双向转换工具
- 引用循环检测算法规范