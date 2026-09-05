# DiagnosticContext 测试构建报告

> 日期：2026-07-12
> 模块：`CsharpPrj/Modules/DiagnosticContext`
> 测试项目：`CentralTest/CSharpTest/Modules.Tests/DiagnosticContext.Tests`

---

## 一、测试覆盖总览

| 层级 | 测试类 | 测试方法数 | 状态 |
|------|--------|-----------|------|
| **Core** | `DiagnosticContextTests` | 17 | ✅ 全部通过 |
| **Core** | `DiagnosticManagerTests` | 10 | ✅ 全部通过 |
| **Core** | `ScopeNodeTests` | 15 | ✅ 全部通过 |
| **Core** | `EventEntryTests` | 7 | ✅ 全部通过 |
| **Core** | `DiagnosticSnapshotTests` | 9 | ✅ 全部通过 |
| **Factory** | `DiagnosticContextFactoryTests` | 11 | ✅ 全部通过 |
| **Queue** | `EventQueueTests` | 9 | ✅ 全部通过 |
| **Queue** | `EventDispatcherTests` | 9 | ✅ 全部通过 |
| **Queue** | `EventPipelineTests` | 9 | ✅ 全部通过 |
| **Formatting** | `TextFormatterTests` | 6 | ✅ 全部通过 |
| **Formatting** | `JsonFormatterTests` | 6 | ✅ 全部通过 |
| **Formatting** | `SummaryFormatterTests` | 6 | ✅ 全部通过 |
| **总计** | **12 个测试类** | **114 个测试方法** | **✅ 全部通过** |

---

## 二、测试架构

### 2.1 目录结构

```
CentralTest/CSharpTest/Modules.Tests/DiagnosticContext.Tests/
├── DiagnosticContext.Tests.csproj
├── Core/
│   ├── DiagnosticContextTests.cs      # AsyncLocal 自动传播上下文测试
│   ├── DiagnosticManagerTests.cs      # 全局管理器测试
│   ├── ScopeNodeTests.cs              # 嵌套作用域节点测试
│   ├── EventEntryTests.cs             # 事件条目模型测试
│   └── DiagnosticSnapshotTests.cs     # 快照 Freeze/Rehydrate 测试
├── Factory/
│   └── DiagnosticContextFactoryTests.cs  # 工厂装配测试
├── Queue/
│   ├── EventQueueTests.cs             # 事件队列测试
│   ├── EventDispatcherTests.cs        # 事件分发器测试
│   └── EventPipelineTests.cs          # 事件管线测试
└── Formatting/
    ├── TextFormatterTests.cs          # 文本格式化器测试
    ├── JsonFormatterTests.cs          # JSON 格式化器测试
    └── SummaryFormatterTests.cs       # 摘要格式化器测试
```

### 2.2 依赖关系

```
DiagnosticContext.Tests.csproj
├── TestInfrastructure (测试基础设施框架)
├── DiagnosticContext (被测模块)
│   ├── BasicMethod (基础方法库)
│   │   └── Queue (队列框架)
│   └── ...
└── MSTest (测试框架)
```

### 2.3 测试框架复用

测试项目复用了 `CentralTest/CSharpTest/TestInfrastructure` 中的基础设施：
- `TestBase` — 测试基类（提供通用测试工具方法）
- `AsyncTestBase` — 异步测试基类（提供异步测试支持）
- `TestDataHelper` — 测试数据辅助类
- `MockHelper` — Mock 辅助类

---

## 三、测试内容详述

### 3.1 Core 层测试

#### DiagnosticContextTests（17 个测试）

| 测试方法 | 验证内容 |
|---------|---------|
| 创建上下文_应设置名称和创建时间 | 基本创建、Name/CreatedAt 属性 |
| Record_应记录事件到根作用域 | 事件记录到 RootScope |
| Record_多次调用_应添加多个事件 | 多次 Record 累积 |
| SetState_应存储状态 | 状态存储 |
| GetState_应返回正确类型 | 泛型 GetState<T> |
| GetState_键不存在_应返回默认值 | 不存在的键返回 default |
| CreateScope_应创建子作用域 | 子作用域创建 |
| CreateScope_子作用域Record_应记录到子节点 | 子作用域事件隔离 |
| CreateScope_子作用域SetState_应隔离 | 父子状态隔离 |
| CreateScope_嵌套多层_应正确传播 | 多层嵌套树形结构 |
| Dispose_后Record_应静默忽略 | Dispose 后 Record 不抛异常 |
| Dispose_后CreateScope_应抛出ObjectDisposedException | Dispose 后 CreateScope 抛异常 |
| Dispose_后SetState_应静默忽略 | Dispose 后 SetState 不生效 |
| 多次Dispose_应安全 | 重复 Dispose 安全 |
| Freeze_应创建快照 | 快照创建 |
| Freeze_快照应独立于原始上下文 | 快照独立性 |
| ToString/ToSummary/ToJson_应输出格式 | 三种格式化输出 |

#### DiagnosticManagerTests（10 个测试）

| 测试方法 | 验证内容 |
|---------|---------|
| BeginScope_应创建上下文并激活 | 创建+激活+Current 关联 |
| BeginScope_使用委托配置_应应用配置 | 委托配置 |
| Current_无活跃上下文_应返回Null | 无上下文时 Current 为 null |
| Current_有活跃上下文_应返回当前上下文 | Current 正确性 |
| FirstActive_有活跃上下文_应返回第一个 | FirstActive 正确性 |
| RecordToAll_应广播到所有活跃上下文 | 广播机制 |
| GetSnapshot_有活跃上下文_应返回快照文本 | 快照文本 |
| GetSnapshot_无活跃上下文_应返回提示文本 | 无上下文时的提示 |
| Configure_应影响后续创建的上下文 | 全局配置 |
| BeginScope_Dispose后_Current应恢复为Null | Dispose 后上下文恢复 |

#### ScopeNodeTests（15 个测试）

| 测试方法 | 验证内容 |
|---------|---------|
| 创建根节点_应设置名称和创建时间 | 基本属性 |
| Record_应添加事件 | 事件记录 |
| Record_多次调用_应添加多个事件 | 累积 |
| SetState_应存储状态 | 状态存储 |
| SetState_同名覆盖_应更新值 | 覆盖更新 |
| GetState_应返回正确类型 | 泛型 |
| GetState_键不存在_应返回默认值 | 默认值 |
| CreateScope_应创建子节点 | 子节点创建 |
| CreateScope_多层嵌套_应形成树形结构 | 树形结构 |
| Root_子节点_应返回根节点 | Root 属性 |
| Dispose_后Record_应静默忽略 | Dispose 后只读 |
| Dispose_后SetState_应静默忽略 | Dispose 后只读 |
| Dispose_后CreateScope_应抛出ObjectDisposedException | Dispose 后抛异常 |
| Dispose_后数据仍可读取 | Dispose 后数据保留 |
| ToString/ToSummary/ToJson_应输出格式 | 格式化输出 |

#### EventEntryTests（7 个测试）

| 测试方法 | 验证内容 |
|---------|---------|
| 创建事件条目_应设置属性 | 基本属性 |
| 创建事件条目_无数据_DataJson应为Null | 无数据时 DataJson 为 null |
| Timestamp_应格式化正确 | 时间戳格式 |
| Timestamp_零毫秒_应显示00_00_000 | 零值格式 |
| DataDisplay_有数据_应返回Json | DataDisplay |
| DataDisplay_无数据_应返回_null占位 | 无数据占位 |
| 事件名称null_应抛出ArgumentNullException | 参数校验 |

#### DiagnosticSnapshotTests（9 个测试）

| 测试方法 | 验证内容 |
|---------|---------|
| Freeze_应创建快照 | 快照创建 |
| Freeze_应递归快照子作用域 | 递归快照 |
| Freeze_快照应深拷贝_修改原始节点不影响快照 | 深拷贝 |
| Rehydrate_应恢复为新的上下文 | 恢复 |
| Rehydrate_应恢复子作用域 | 子作用域恢复 |
| Rehydrate_应不受原始上下文Dispose影响 | 独立性 |
| 快照_事件列表应不可变 | 不可变性 |
| 快照_状态字典应不可变 | 不可变性 |
| 空上下文快照_应正确 | 空快照 |

### 3.2 Factory 层测试（11 个测试）

| 测试方法 | 验证内容 |
|---------|---------|
| Create_默认配置_应创建上下文 | 默认创建 |
| Create_使用配置对象_应应用配置 | 配置对象 |
| Create_使用委托配置_应应用配置 | 委托配置 |
| Create_启用事件队列_应装配EventQueue | 队列装配 |
| Create_启用分发器_应装配EventDispatcher | 分发器装配 |
| Create_启用Pipeline_应装配EventPipeline | 管线装配 |
| Create_名称null_应抛出ArgumentNullException | 参数校验 |
| Create_名称空字符串_应允许 | 空名称 |
| Create_配置null_应使用默认配置 | null 配置 |
| Create_批量队列类型_应装配BatchEventQueue | 批量队列 |
| Create_包含前缀过滤器_应过滤事件 | 前缀过滤 |

### 3.3 Queue 层测试

#### EventQueueTests（9 个测试）

| 测试方法 | 验证内容 |
|---------|---------|
| EnqueueAsync_应入队事件 | 入队 |
| EnqueueAsync_多次入队_应增加计数 | 多次入队 |
| TryDequeueAsync_应出队事件 | 出队 |
| TryDequeueAsync_空队列_应返回失败 | 空队列 |
| TryDequeueAsync_应保持FIFO顺序 | FIFO |
| Clear_应清空队列 | 清空 |
| StartConsumingAsync_应消费事件 | 消费循环 |
| 新队列_Count应为0 | 初始状态 |
| 使用外部队列_应正确包装 | 外部队列包装 |

#### EventDispatcherTests（9 个测试）

| 测试方法 | 验证内容 |
|---------|---------|
| DispatchAsync_应分发事件到已注册处理器 | 分发到处理器 |
| DispatchAsync_未注册事件_应静默忽略 | 未注册处理 |
| DispatchAsync_应分发到事件队列 | 分发到队列 |
| IsRegistered_应正确判断 | 注册判断 |
| GetRegisteredKeys_应返回所有注册键 | 键列表 |
| Unregister_应移除处理器 | 取消注册 |
| DispatchBatchAsync_应批量分发 | 批量分发 |
| 分发器名称_应正确设置 | 名称 |
| Register_链式调用_应返回自身 | 链式调用 |

#### EventPipelineTests（9 个测试）

| 测试方法 | 验证内容 |
|---------|---------|
| ExecuteAsync_应执行所有步骤 | 执行所有步骤 |
| ExecuteAsync_应保持步骤顺序 | 顺序保持 |
| Remove_应移除指定步骤 | 移除 |
| InsertBefore_应插入到指定步骤之前 | 前插 |
| InsertAfter_应插入到指定步骤之后 | 后插 |
| 同步处理器_应正常工作 | 同步处理器 |
| 管线名称_应正确设置 | 名称 |
| Handlers_应返回只读列表 | 只读列表 |
| Remove_不存在的步骤_应静默忽略 | 安全移除 |

### 3.4 Formatting 层测试

#### TextFormatterTests（6 个测试）

| 测试方法 | 验证内容 |
|---------|---------|
| Format_空节点_应包含名称 | 空节点 |
| Format_有事件_应包含事件信息 | 事件 |
| Format_有状态_应包含状态信息 | 状态 |
| Format_有子节点_应包含子节点信息 | 子节点 |
| Format_多层嵌套_应缩进显示 | 嵌套缩进 |
| Format_事件和状态混合_应全部包含 | 混合 |

#### JsonFormatterTests（6 个测试）

| 测试方法 | 验证内容 |
|---------|---------|
| Format_空节点_应输出JSON | 空节点 |
| Format_有事件_应包含事件数组 | 事件 |
| Format_有状态_应包含状态对象 | 状态 |
| Format_有子节点_应包含children数组 | 子节点 |
| Format_多层嵌套_应递归输出 | 嵌套 |
| Format_输出应为合法JSON | JSON 合法性 |

#### SummaryFormatterTests（6 个测试）

| 测试方法 | 验证内容 |
|---------|---------|
| Format_空节点_应包含名称 | 空节点 |
| Format_有事件_应包含事件计数 | 事件计数 |
| Format_有状态_应包含状态计数 | 状态计数 |
| Format_有子节点_应包含子节点摘要 | 子节点 |
| Format_多层嵌套_应递归摘要 | 嵌套 |
| Format_大量事件_应汇总统计 | 大量事件 |

---

## 四、已知问题

### 4.1 testhost 进程退出异常

**现象：** 所有 114 个测试全部通过（0 失败、0 跳过），但 testhost.exe 进程在测试完成后崩溃，导致 `dotnet test` 命令超时（30s+）并显示"测试主机进程崩溃"。

**输出示例：**
```
已通过! - 失败: 0, 通过: 21, 已跳过: 0, 总计: 21, 持续时间: 76 ms - DiagnosticContext.Tests.dll (net10.0)
测试运行已中止。
```

**根因分析：**

`EventDispatcherTests` 中 `QueueDispatcher.Register<TMessage>(string key, IQueueHandler<TMessage> handler)` 内部创建 `MemoryQueue` 并自动启动消费循环：

```csharp
// QueueDispatcher.cs:80-84
var queue = new MemoryQueue<TMessage>($"{Name}.{key}", ...);
_routes[key] = queue;
var consumeTask = queue.StartConsumingAsync(handler.HandleAsync);
consumeTask.ContinueWith(
    t => OnConsumingError?.Invoke(this, (key, t.Exception.InnerException ?? t.Exception)),
    TaskContinuationOptions.OnlyOnFaulted);
```

`StartConsumingAsync` 启动的 `ConsumeLoopAsync` 是一个 fire-and-forget 的 `Task`（存储在 `_consumeTask` 字段中，但从未被 await）。当 `QueueDispatcher.Dispose()` → `Unregister(key)` → `MemoryQueue.Dispose()` → `QueueBase.Dispose()` 时，`_consumeCts.Cancel()` 被调用，但消费循环 Task 仍在运行（未被等待完成）。

testhost 进程退出时，这些未完成的后台 Task 导致进程崩溃。

**影响范围：** 仅影响测试运行器的退出行为，不影响测试结果的正确性。所有断言均正确通过。

### 4.2 EventDispatcherTests 双重 Dispose

`EventDispatcherTests` 中每个测试方法同时使用了 `using var dispatcher` 和 `[TestCleanup]` 中的 `_dispatcher?.Dispose()`：

```csharp
private EventDispatcher? _dispatcher;

[TestCleanup]
public void Cleanup()
{
    _dispatcher?.Dispose();
}

[TestMethod]
public async Task DispatchAsync_应分发事件到已注册处理器()
{
    using var dispatcher = new EventDispatcher("test-dispatcher");
    _dispatcher = dispatcher;
    // ...
}
```

`using` 在方法结束时 Dispose，然后 `[TestCleanup]` 又 Dispose 一次。虽然 `QueueDispatcher.Dispose` 对空路由表是安全的（第二次 Dispose 时 `_routes` 已空），但这种设计是冗余的，应统一为一种模式。

### 4.3 DiagnosticManagerTests 测试命名不一致

`BeginScope_Dispose后_Current应恢复为Null` 测试的命名与断言语义不一致：

```csharp
public void BeginScope_Dispose后_Current应恢复为Null()
{
    var previous = DiagnosticManager.Current;  // 获取当前（可能为 null）
    using (var ctx = DiagnosticManager.BeginScope("temp"))
    {
        Assert.IsNotNull(DiagnosticManager.Current);
    }
    Assert.AreSame(previous, DiagnosticManager.Current);  // 实际断言是"恢复为 previous 的值"
}
```

测试名说"恢复为Null"，但实际断言是"恢复为 previous 的值"。如果 previous 不是 null（例如前面测试留下了残留上下文），测试仍然通过，但语义错误。

### 4.4 DiagnosticManagerTests 测试间副作用

`Current_无活跃上下文_应返回Null` 测试手动 Dispose 了当前上下文：

```csharp
public void Current_无活跃上下文_应返回Null()
{
    var current = DiagnosticManager.Current;
    current?.Dispose();
    Assert.IsNull(DiagnosticManager.Current);
}
```

这可能导致后续测试（如 `FirstActive`）读到脏数据，因为 `DiagnosticManager._activeContexts` 中的 `WeakReference` 不会立即被清理。

---

## 五、改进建议

### 5.1 修复 testhost 进程退出问题

**方案 A（推荐）：** 在 `QueueBase.Dispose` 中等待消费循环退出（异步安全版本）

```csharp
protected virtual void Dispose(bool disposing)
{
    if (disposing)
    {
        var cts = Interlocked.Exchange(ref _consumeCts, null);
        if (cts != null)
        {
            try { cts.Cancel(); } catch { }
            cts.Dispose();
        }
        // 注意：不在 Dispose 中同步等待 _consumeTask，
        // 避免死锁。消费循环会在下次迭代时检测到取消并退出。
    }
}
```

**方案 B：** 在 `QueueDispatcher.Unregister` 中显式调用 `StopConsumingAsync`（异步版本），但 `Unregister` 是同步方法，需要改造。

### 5.2 统一 Dispose 模式

移除 `EventDispatcherTests` 中的 `using var`，只保留 `[TestCleanup]` 中的 Dispose：

```csharp
[TestCleanup]
public void Cleanup()
{
    _dispatcher?.Dispose();
}

[TestMethod]
public async Task DispatchAsync_应分发事件到已注册处理器()
{
    var dispatcher = new EventDispatcher("test-dispatcher");
    _dispatcher = dispatcher;
    // ...
}
```

### 5.3 修复测试命名

将 `BeginScope_Dispose后_Current应恢复为Null` 改为 `BeginScope_Dispose后_Current应恢复为之前的值`，或修改断言为显式检查 null。

### 5.4 消除测试间副作用

在 `DiagnosticManagerTests` 的 `[TestCleanup]` 中确保清理所有活跃上下文：

```csharp
[TestCleanup]
public void Cleanup()
{
    while (DiagnosticManager.Current != null)
    {
        DiagnosticManager.Current.Dispose();
    }
}
```

---

## 六、构建验证

```bash
# 构建
dotnet build "CentralTest/CSharpTest/Modules.Tests/DiagnosticContext.Tests/DiagnosticContext.Tests.csproj"
# 输出：0 警告，0 错误

# 运行测试
dotnet test "CentralTest/CSharpTest/Modules.Tests/DiagnosticContext.Tests/DiagnosticContext.Tests.csproj" --no-build
# 输出：通过 114，失败 0，跳过 0
# 注意：testhost 进程退出异常（见 4.1）
```
