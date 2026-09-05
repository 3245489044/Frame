# CallFrame — 调用帧追踪系统

## 概述

`CallFrame` 是工作流执行引擎的**调用链追踪系统**，以树形结构记录一次工作流执行中所有函数的调用关系。

替代 `ExecutionEngine/CallFrame.cs` 中的旧系统（`AICallFrame`、`AICallLinqSearch`、`AICallFrameService`、`CallFrameRebuilder`）。

## 设计目标

1. **消除静态依赖**：旧系统使用静态方法（`AICallFrameService.QueryNodes`），新系统使用实例化对象
2. **类型安全**：用强类型枚举替代字符串 DSL（`QueryStepType` 替代 `"WhereHasNextNode"` 字符串）
3. **线程安全**：使用 `ConcurrentDictionary` + `ReaderWriterLockSlim` 保证并发安全
4. **与 ExecutionSession 集成**：通过 `Properties` 字典作为可选扩展，无需修改 `ExecutionSession` 本身
5. **可测试**：所有依赖通过接口注入，无全局状态

## 目录结构

```
CallFrame/
├── Abstractions/
│   ├── ICallFrame.cs              # 调用帧接口
│   ├── ICallFrameNode.cs          # 调用帧节点接口
│   ├── ICallFrameQueryEngine.cs   # 查询引擎接口
│   └── ICallFrameService.cs       # 服务接口（含 AddNodeConfig）
├── Models/
│   ├── CallFrameQuery.cs          # 强类型查询定义（替代字符串 DSL）
│   └── CallFrameResult.cs         # 操作结果封装
├── Core/
│   ├── CallFrame.cs               # 调用帧实现
│   ├── CallFrameNode.cs           # 节点实现
│   ├── CallFrameQueryEngine.cs    # 查询引擎实现
│   └── CallFrameService.cs        # 服务实现
├── Serialization/
│   ├── CallFrameSerializer.cs     # JSON 序列化
│   └── CallFrameRebuilder.cs      # 从记录重建
└── Extensions/
    └── ExecutionSessionExtensions.cs  # 与 ExecutionSession 集成
```

## 与旧系统的对比

| 旧系统 (ExecutionEngine) | 新系统 (CallFrame) | 改进 |
|--------------------------|-------------------|------|
| `AICallFrame` | `ICallFrame` / `Core.CallFrame` | 接口分离，实例化设计 |
| `AICallFrame.CallNode` | `ICallFrameNode` / `Core.CallFrameNode` | 接口分离，线程安全 |
| `AICallLinqSearch` (字符串 DSL) | `CallFrameQuery` + `CallFrameQueryEngine` | 强类型枚举，编译时检查 |
| `AICallFrameService` (静态) | `ICallFrameService` / `Core.CallFrameService` | 实例化，可注入 |
| `CallFrameRebuilder` (静态) | `CallFrameSerializer` + `CallFrameRebuilder` | 实例化，可扩展 |
| `WorkflowRuntimeContextManager` (隐式) | `ExecutionSession.Properties` (显式) | 显式传递，无隐式依赖 |

## 使用示例

### 创建调用帧并添加节点

```csharp
// 创建调用帧
var callFrame = new Core.CallFrame("session-001");

// 创建根节点
var root = callFrame.CreateRoot("MainWorkflow", input: "start");

// 添加子节点
var step1 = root.AddChild("Step1", input: "data1", output: "result1");
var step2 = root.AddChild("Step2", input: "data2", output: "result2");
var subStep = step1.AddChild("SubStep1", input: "subdata", output: "subresult");
```

### 查询调用帧

```csharp
// 使用强类型查询
var query = new CallFrameQuery()
    .WhereHasOutputs()
    .WhereSymbol("Step1")
    .SelectChildren()
    .WhereHasOutputs();

var results = callFrame.Query(query);
```

### 与 ExecutionSession 集成

```csharp
// 通过扩展方法集成
var session = new ExecutionSession("session-001", graphSnapshot);

// 记录节点调用
session.RecordNodeCall("MainWorkflow", input: "start");
session.RecordNodeCall("Step1", input: "data1", output: "result1");

// 查询调用帧
var query = new CallFrameQuery().WhereHasOutputs();
var nodes = session.QueryCallFrame(query);
```

### 序列化

```csharp
var serializer = new CallFrameSerializer();

// 序列化为 JSON
string json = serializer.Serialize(callFrame);

// 反序列化
var restored = serializer.Deserialize(json);
```

## 依赖关系

- `CallFrame` 项目依赖：无（纯 .NET Standard 2.0+）
- `Extensions/ExecutionSessionExtensions.cs` 依赖：`WorkFlowMethod.Execution.Session`
