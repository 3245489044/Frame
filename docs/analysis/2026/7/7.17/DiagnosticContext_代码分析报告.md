# DiagnosticContext 模块代码分析报告

> 分析日期：2026-07-16
> 分析范围：`CsharpPrj/Modules/DiagnosticContext/` + `CsharpPrj/Share/*/Diagnostics/`
> 分析文件数：26 个源文件

---

## 一、重复逻辑（8 处）

### 1. 🔴 工厂模式与直接构造重复

**涉及文件：**
- `Factory/DiagnosticContextFactory.cs`（第 23-25 行）
- `Core/DiagnosticManager.cs`（第 78-82 行）

**问题：** `DiagnosticContextFactory.Create()` 和 `DiagnosticManager.BeginScope()` 做完全相同的事：

```csharp
// DiagnosticContextFactory.Create()
var scope = new DiagnosticScope(scopeName);
return new DiagnosticContext(scope);

// DiagnosticManager.BeginScope()
var scope = new DiagnosticScope(scopeName);
return new DiagnosticContext(scope);
```

此外，`DiagnosticContextFactory` 内部：
- `CreateAndActivate()`（第 28-32 行）与 `Create()` 完全相同（`Activate()` 是空操作）
- `CreateLazy()`（第 35-39 行）与 `Create()` 完全相同

### 2. 🔴 三个配置重载全部忽略参数

**涉及文件：**
- `Core/DiagnosticManager.cs`（第 87-99 行）
- `Factory/DiagnosticContextFactory.cs`（第 42-51 行）

**问题：** 两个类各有三个重载，带配置参数的后两个重载完全忽略配置，直接委托给无参版本：

```csharp
// DiagnosticManager.cs 第 87-91 行
public static DiagnosticContext BeginScope(string scopeName, Action<DiagnosticContextConfiguration> configure)
{
    return BeginScope(scopeName);  // configure 被忽略！
}

// DiagnosticManager.cs 第 96-99 行
public static DiagnosticContext BeginScope(string scopeName, DiagnosticContextConfiguration config)
{
    return BeginScope(scopeName);  // config 被忽略！
}
```

### 3. 🔴 DiagnosticContext 是 DiagnosticScope 的薄包装

**涉及文件：**
- `Core/DiagnosticContext.cs`
- `Core/DiagnosticScope.cs`

**问题：** `DiagnosticContext` 的以下方法全部委托给 `_scope`，没有额外逻辑：

| DiagnosticContext 方法 | 委托目标 |
|----------------------|---------|
| `Record()` | `_scope.Record()` |
| `SetState()` | `_scope.SetState()` |
| `Freeze()` | `_scope.Freeze()` |
| `ToString()` | `_scope.ToString()` |
| `ToSummary()` | `_scope.ToSummary()` |
| `ToJson()` | `_scope.ToJson()` |

### 4. 🔴 ScopeGuard 与 DiagnosticContext 的方法重复

**涉及文件：**
- `Core/DiagnosticContext.cs`（第 162-210 行，ScopeGuard 内部类）

**问题：** `ScopeGuard` 的 `Record()`、`SetState()`、`GetState()`、`CreateScope()`、`ToString()`、`ToSummary()`、`ToJson()` 与 `DiagnosticContext` 几乎完全重复。

### 5. 🔴 Share 层 6 个诊断上下文文件完全重复

**涉及文件（6 个，内容完全相同）：**
- `Share/FrameworkAbstractions/Diagnostics/FrameworkAbstractionsDiagnosticContext.cs`
- `Share/StateManagement/Diagnostics/StateManagementDiagnosticContext.cs`
- `Share/GraphFramework/Diagnostics/GraphFrameworkDiagnosticContext.cs`
- `Share/PlaceholderFramework/Diagnostics/PlaceholderFrameworkDiagnosticContext.cs`
- `Share/CommandFramework/Diagnostics/CommandFrameworkDiagnosticContext.cs`
- `Share/UniversalMiddleware/Diagnostics/UniversalMiddlewareDiagnosticContext.cs`

**问题：** 每个都是 23 行，`Record` 方法完全一样（仅命名空间和类名不同）：

```csharp
public static void Record(string eventName, object? data = null)
{
    var scope = DiagnosticScopeStore.GetCurrent();
    scope?.Record(eventName, data);
}
```

### 6. 🟡 LogCategoryHelper 是纯委托

**涉及文件：**
- `Query/LogQueryFS.cs`（第 360-370 行）

**问题：** `LogCategoryHelper.ToDisplayName()` 完全委托给 `LogCategoryRegistry.GetDisplayName()`，没有额外逻辑，属于纯转发层。

### 7. 🟡 格式化器的 Default 单例模式重复

**涉及文件：**
- `Formatting/TextFormatter.cs`（第 30 行）
- `Formatting/SummaryFormatter.cs`（第 29 行）
- `Formatting/JsonFormatter.cs`（第 30 行）

**问题：** 三个格式化器都使用相同的单例模式，代码结构完全一致。

### 8. 🟡 FrameworkErrors 的 GetErrorLevel 与 Report 中的 severity switch 重复

**涉及文件：**
- `ErrorHandling/FrameworkErrors.cs`

**问题：** `GetErrorLevel()`（第 161-193 行）将错误码映射为级别字符串，`Report()`（第 72-80 行）中的 `severity switch` 将 `ErrorSeverity` 枚举映射为级别字符串，两套映射逻辑但 `GetErrorLevel()` 从未被 `Report()` 调用。

---

## 二、冗余逻辑（5 处）

### 1. 🔴 旧体系残留代码（3 个文件 + 1 个配置类）

**涉及文件：**
- `Queue/EventQueue.cs`（106 行）
- `Queue/EventDispatcher.cs`（123 行）
- `Queue/EventPipeline.cs`（116 行）
- `Configuration/DiagnosticContextConfiguration.cs`（178 行）

**问题：**
- `EventQueue`、`EventDispatcher`、`EventPipeline` 是旧体系的事件队列/分发器/管线实现，新体系（`DiagnosticScope`）完全不使用它们
- `DiagnosticContextConfiguration` 包含大量从未使用的配置项：
  - `EnableEventQueue`、`QueueType`、`BatchSize`、`FlushInterval`、`QueueCapacity`
  - `EnableDispatcher`、`DispatcherName`
  - `EnablePipeline`、`PipelineName`、`IncludeEventPrefix`、`ExcludeEventPrefix`
  - `OutputTarget`、`OutputFilePath`
  - `FormatterType`

### 2. 🟡 空操作/死代码

**涉及文件：**
- `Core/DiagnosticContext.cs`（第 152 行）：`Activate()` — 空方法
- `Core/DiagnosticManager.cs`（第 55 行）：`Configure(Action<configure>)` — 空方法
- `Factory/DiagnosticContextFactory.cs`（第 28-32 行）：`CreateAndActivate()` — 与 `Create()` 完全相同
- `Bootstrapping/LoggingBootstrapper.cs`：`InitializeLogMessages`、`LoadLogFilter` 委托从未被赋值

### 3. 🟡 双重检查锁模式过度使用

**涉及文件：**
- `Categories/CategoryResolver.cs`（第 103-140 行）：`LoadAliases()`
- `Config/DiagnosticsConfigReader.cs`（第 141-169 行）：`GetJsonDocument()`
- `Categories/LogCategoryRegistry.cs`（第 153-162 行）：`EnsureInitialized()`

**问题：** 三个方法都使用相同的双重检查锁模式，代码结构高度相似，可以提取公共模式。

### 4. 🟡 路径查找逻辑重复

**涉及文件：**
- `Categories/CategoryResolver.cs`（第 34-57 行）：`GetConfigPath()`
- `Config/DiagnosticsConfigReader.cs`（第 49-91 行）：`GetSettingsPath()`
- `Categories/LogCategoryLoader.cs`（第 23-45 行）：`GetDefaultConfigPath()`

**问题：** 三个方法都实现了类似的"程序集目录→工作目录"回退查找逻辑。

### 5. 🟢 SummaryFormatter 的 CountAllChildren 递归与 FormatSummary 递归重复

**涉及文件：**
- `Formatting/SummaryFormatter.cs`

**问题：** `FormatSummary` 递归遍历子节点，`CountAllChildren` 又递归遍历一次，可以在一次遍历中同时完成。

---

## 三、错误逻辑（7 处）

### 1. 🔴 DiagnosticContext.Current 每次创建新包装

**涉及文件：**
- `Core/DiagnosticContext.cs`（第 33-40 行）

**问题：** 每次调用都 `new DiagnosticContext(scope)`，导致 `Current != Current`（同一作用域返回不同实例），引用比较会失败，且增加 GC 压力。

```csharp
public static DiagnosticContext? Current
{
    get {
        var scope = DiagnosticScopeStore.GetCurrent();
        return scope != null ? new DiagnosticContext(scope) : null;
    }
}
```

### 2. 🔴 DiagnosticContext.GetOrCreateLazy 可能创建孤立作用域

**涉及文件：**
- `Core/DiagnosticContext.cs`（第 130-138 行）

**问题：** 当 `DiagnosticScopeStore.GetCurrent()` 返回 null 时，创建新的 `DiagnosticScope` 但不设置到 AsyncLocal，调用方无法 Dispose 这个作用域，可能导致内存泄漏。

```csharp
internal static DiagnosticContext GetOrCreateLazy(string scopeName = "Lazy")
{
    var current = DiagnosticScopeStore.GetCurrent();
    if (current != null)
        return new DiagnosticContext(current);

    var scope = new DiagnosticScope(scopeName);  // 创建但不注册到 AsyncLocal
    return new DiagnosticContext(scope);          // 调用方无法 Dispose
}
```

### 3. 🔴 DiagnosticSnapshot.RestoreToScope 子作用域恢复逻辑脆弱

**涉及文件：**
- `Core/DiagnosticSnapshot.cs`（第 114 行）

**问题：** 如果快照的子作用域数量与当前 ScopeNode 的子作用域数量不一致，部分数据会静默丢失，没有日志或异常提示。

```csharp
for (int i = 0; i < snapshot.Children.Count && i < scope.Children.Count; i++)
{
    RestoreToScope(scope.Children[i], snapshot.Children[i]);
}
```

### 4. 🔴 ScopeNode 的 _disposed 守卫不一致

**涉及文件：**
- `Core/ScopeNode.cs`

**问题：** 四种不同的 Dispose 后行为：

| 方法 | Dispose 后行为 |
|------|---------------|
| `Record()`（第 108 行） | 静默忽略 |
| `SetState()`（第 131 行） | 静默忽略 |
| `CreateScope()`（第 153 行） | 抛出 `ObjectDisposedException` |
| `AddEntry()`（第 122 行） | `Debug.Assert` 断言 |

### 5. 🔴 DiagnosticScope 的 Dispose 不恢复 AsyncLocal 到父作用域

**涉及文件：**
- `Core/DiagnosticScope.cs`（第 115-124 行）

**问题：** `Dispose()` 将 `CurrentCallChainId` 设为 null，但如果作用域是嵌套的（通过 `PushScope`），Dispose 后 AsyncLocal 不会恢复到父作用域的 CallChainId，导致后续代码无法找到正确的当前作用域。

```csharp
public void Dispose()
{
    if (_disposed) return;
    _disposed = true;
    DiagnosticScopeStore.Unregister(CallChainId);
    if (DiagnosticScopeStore.CurrentCallChainId == CallChainId)
        DiagnosticScopeStore.CurrentCallChainId = null;  // 不恢复父作用域
    _rootNode.Dispose();
    _scopeStack.Clear();
}
```

### 6. 🟡 ScopeGuard 的 Record 委托给 _owner 而非 _scopeNode

**涉及文件：**
- `Core/DiagnosticContext.cs`（第 179-183 行）

**问题：** `Record` 记录到 `_owner` 的当前节点（可能已被 PushScope 改变），而 `SetState` 直接操作创建 ScopeGuard 时的节点，两者行为不一致。

```csharp
// ScopeGuard.Record() — 委托给 _owner
_owner.Record(eventName, data);  // 可能记录到不同的当前节点

// ScopeGuard.SetState() — 直接操作 _scopeNode
_scopeNode.SetState(key, value); // 固定在创建时的节点
```

### 7. 🟡 LogCategoryRegistry 静态构造函数注册到 LoggingSetup

**涉及文件：**
- `Categories/LogCategoryRegistry.cs`（第 127-132 行）

**问题：** 如果 `LoggingSetup` 尚未加载，注册可能不生效（程序集加载顺序问题），且静态构造函数无法取消注册，导致测试间污染。

```csharp
static LogCategoryRegistry()
{
    LoggingSetup.OnBeforeInitialize += InitializeDefaults;
    LoggingSetup.ResolveCategoryDisplayName += GetDisplayName;
    LoggingSetup.ResolveEnumName += ResolveEnumName;
}
```

---

## 严重程度说明

| 标记 | 含义 | 数量 |
|------|------|------|
| 🔴 **严重** | 需要立即修复（Bug 风险或架构问题） | 10 |
| 🟡 **中等** | 建议优化（重复/冗余，影响可维护性） | 8 |
| 🟢 **轻微** | 可优化（微重构或清理） | 2 |

---

## 修复建议优先级

### P0 — 必须修复（Bug）
1. `DiagnosticContext.Current` 每次创建新包装 → 改为缓存或静态实例
2. `GetOrCreateLazy` 创建孤立作用域 → 注册到 AsyncLocal
3. `RestoreToScope` 子作用域数量不匹配静默丢失 → 添加日志/异常
4. `ScopeNode._disposed` 守卫不一致 → 统一为抛出异常
5. `DiagnosticScope.Dispose` 不恢复父作用域 → 保存并恢复父 CallChainId
6. `ScopeGuard.Record` 委托给 `_owner` → 改为委托给 `_scopeNode`

### P1 — 应该修复（架构）
7. Share 层 6 个重复文件 → 合并为通用类或扩展方法
8. 旧体系残留代码（Queue 目录 + Configuration）→ 删除
9. 工厂模式与直接构造重复 → 统一入口

### P2 — 建议修复（可维护性）
10. 三个配置重载忽略参数 → 删除无用重载
11. 空操作/死代码 → 删除
12. 路径查找逻辑重复 → 提取公共方法
13. LogCategoryHelper 纯委托 → 删除
14. 双重检查锁模式 → 提取公共模式
