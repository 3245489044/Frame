# Command ECS 重构方案

> **日期**: 2026-07-27
> **动机**: 测试5 (math_functions_test.json) 执行失败，追踪问题暴露当前命令系统的5层耦合和不可追溯性。
> **核心理念**: 将命令执行 ECS 化，输入输出聚拢为 Port，矩阵打印所有命令状态。

---

## 一、现状分析

### 1.1 当前命令执行链路

执行一条命令（如 `echo "测试"` 或 `exec Math_GetDomains`）经过以下链路：

```
Script JSON
  → ScriptLoadSkeleton (JSON加载 + 模板展开 → commandLines[])
    → ForEachSkeleton (逐条遍历)
      → ParseCommandStage (Modules.Parsing.Parsing.CommandParser → ParsedCommand)
        → ExecuteCommandStage
          │
          ├─ 判断 IsNonEventCommandAction(action) ?
          │   ├─ YES → NonEventCommandCenter.Registry.TryGetHandler(name)
          │   │            → 15+ handler (echo/sleep/close/sideload/list/exec/...)
          │   │            → 结果写回 ctx.FunctionExecuteSuccess/ErrorMessage/ResultValue
          │   │
          │   └─ NO  → _findBindingFunc(target, action)
          │                → IEventBindingResolver.Resolve()
          │                → 没找到则 _functionRegistry.Contains(target)
          │                     → 没找到则 "No binding found for {target}.{ctx.ParsedAction}"
          │
          └→ ResultCollectStage (收集执行结果)
```

### 1.2 涉及文件（共 21 个）

| 职责 | 文件 | 行数 | 说明 |
|------|------|------|------|
| 命令注册 | `NonEventCommandCenter.cs` | 621 | 硬编码 15+ handler，if/else/switch |
| 命令注册名称表 | `NonEventCommands.cs` | 132 | 定义 14 个命令常量 + 关键性映射 |
| 解析阶段 | `ParseCommandStage.cs` | 88 | 委托 Modules.Parsing.Parsing.CommandParser |
| 执行阶段 | `ExecuteCommandStage.cs` | 222 | 核心分发逻辑，3 路分支 |
| 结果收集 | `ResultCollectStage.cs` | ～50 | 收集执行状态 |
| 管线上下文 | `CommandPipelineContext.cs` | 415 | 承载所有状态，20+ 属性 |
| 管线工厂 | `PipelineFactory.cs` | 95 | 构建 3-stage 管线 |
| 查找适配器 | `NonEventCommandLookupAdapter.cs` | ～30 | 桥接 Container 接口 |
| 工具类 | `PipelineContextUtility.cs` | 39 | IsNonEventCommandAction 委托 |
| 执行引擎 | `CommandExecutionEngine.cs` | ～ | 执行调度 |
| 模板预处理 | `TemplatePreprocessSkeleton.cs` | ～ | 模板展开 |
| 命令事件总线 | `EventHub.cs` | ～ | 事件通信 |
| 注册中心 | `FunctionStorageRegistry.cs` | ～ | 函数注册 |
| 执行结果 | `CommandExecutionResult.cs` | ～ | 结果模型 |
| 运行上下文 | `CommandRuntimeContext.cs` | ～ | 运行时状态 |
| 上下文键 | `ContextKeys.cs` | ～ | 上下文常量 |
| 执行选项 | `ExecutionOptions.cs` | ～ | 配置选项 |
| 重入守卫 | `HandlerReentrancyGuard.cs` | ～ | 防重入 |
| 上下文扩展 | `PipelineContextExtensions.cs` | ～ | 扩展方法 |
| 函数执行系统 | `FunctionExecutionSystem.cs` | 154 | **已有 ECS 参考** |
| 输入输出 Ports | `Ports.cs` | 899 | **已有 ECS 参考** |

### 1.3 核心问题

| # | 问题 | 严重 | 描述 |
|---|------|------|------|
| 1 | **三层外部查找** | 高 | NonEventCommandCenter.Registry → IEventBindingResolver → FunctionRegistry，失败时只报 "No binding found" 不说明哪层失败 |
| 2 | **15+ handler 硬编码** | 中 | NonEventCommandCenter.cs 621 行全部是 if/else/switch，新建命令必须改此文件 |
| 3 | **状态分散** | 高 | 执行状态分布在 ctx.FunctionExecuteSuccess / FunctionErrorMessage / FunctionResultValue / NonEventCommandLookup / _functionRegistry 上，无法快照 |
| 4 | **无矩阵输出** | 中 | 无法在任意时刻输出所有命令的执行状态表格，只能靠解析日志 |
| 5 | **初始化时序脆** | 高 | NonEventCommandCenter 必须在管线构建前完成 Initialize()，且与 UnifiedRegistryCenter.Initialize() 存在循环依赖 |
| 6 | **ParseCommandStage 与 ExecuteCommandStage 的 IsNonEventCommand 判断重复** | 中 | 解析阶段和执行阶段都需要判断命令类型，逻辑分散 |

---

## 二、ECS 化设计方案

### 2.1 Command 实体化

将每条命令变为 ECS Entity，组件化描述：

```csharp
// ── 组件定义 ──

// 命令元数据（只读，创建时赋值）
public struct CommandMetaComponent : IComponent
{
    public string Action;       // echo / exec / sleep / sideload / ...
    public string Target;       // 目标控件/函数名
    public Dictionary<string, object>? Parameters;
    public string RawCommand;   // 原始命令文本
    public int Index;           // in script
}

// 命令执行状态（由 System 写入）
public struct CommandStatusComponent : IComponent
{
    public CommandState State;  // Pending | Executing | Success | Failed | ParseError
    public string? ErrorMessage;
    public long DurationMs;
}

// 命令执行结果（由 System 写入）
public struct CommandResultComponent : IComponent
{
    public string? ResultValue;
    public DateTime CompletedAt;
}
```

### 2.2 系统划分

```
CommandInputPort (聚拢入口)
  │
  ├── TemplateExpandSystem    (展开 script JSON + template → commandLines[])
  ├── ParseCommandSystem      (commandLine → CommandMetaComponent)
  │
  ├── EchoSystem              (echo 命令)
  ├── SleepSystem             (sleep 命令)
  ├── CloseSystem             (close 窗口)
  ├── SideloadSystem          (sideload 配置文件)
  ├── ListSystem              (list 表单)
  ├── LoadSystem              (load UI 配置)
  ├── ExecSystem              (exec 函数调用)
  ├── ExitSystem              (exit 框架退出)
  └── ...                     (其他系统)
  │
  └── CommandOutputPort (聚拢出口 + 矩阵打印)
       └─ Snapshot() → List<CommandResultRow>
```

### 2.3 输入 / 输出聚拢

**CommandInputPort** — 所有命令进入 ECS World 的唯一入口：

```csharp
public sealed class CommandInputPort
{
    // 加载脚本 → 展开模板 → 创建 Entity
    public void LoadScript(string scriptPath);

    // 执行所有 Pending 命令
    public void ExecuteAll();

    // 单步执行
    public void ExecuteNext();
}
```

**CommandOutputPort** — 所有命令输出和状态的唯一出口：

```csharp
public sealed class CommandOutputPort
{
    // 快照 → 用于矩阵打印
    public CommandSnapshot Snapshot();

    // 矩阵字符串
    public string PrintMatrix();
}
```

### 2.4 矩阵输出示例

```
┌───────┬──────────────────────────────┬──────────┬────────┬──────────────────┐
│  #001 │ echo "测试 1"                 │ Success  │  0.2ms │                  │
│  #002 │ sideload MathFunctionBrowser  │ ParseErr │  0.0ms │ 路径格式错误      │
│  #003 │ exec Math_GetDomains          │ Failed   │  1.5ms │ 函数未注册        │
│  #004 │ exec Math_GetStats            │ Failed   │  1.0ms │ 函数未注册        │
│  #005 │ sleep 500                     │ Pending  │    -   │                  │
│  ...  │ ...                           │ ...      │  ...   │ ...              │
│  #045 │ echo "全部测试完成"            │ Pending  │    -   │                  │
├───────┼──────────────────────────────┼──────────┼────────┼──────────────────┤
│ 总计  │ 45 命令                       │ 0/45 通过 │        │ 失败率 100%       │
└───────┴──────────────────────────────┴──────────┴────────┴──────────────────┘
```

---

## 三、废弃 / 保留决策

### 3.1 直接废弃（11 个文件）

| 文件 | 替代 |
|------|------|
| `NonEventCommandCenter.cs` (621行) | 各 CommandSystem 分发 |
| `NonEventCommandLookupAdapter.cs` | 移除，不再需要桥接 |
| `ExecuteCommandStage.cs` (222行) | CommandInputPort + 各 System |
| `ParseCommandStage.cs` (88行) | ParseCommandSystem |
| `ResultCollectStage.cs` | CommandOutputPort 集成 |
| `PipelineContextUtility.cs` (39行) | 内联到 CommandSystem |
| `CommandPipelineContext.cs` (415行，精简) | 保留骨架，删除执行字段 |
| `HandlerReentrancyGuard.cs` | 由 ECS 的 Update 节制自然代替 |

### 3.2 保留改造（6 个文件）

| 文件 | 改造方向 |
|------|---------|
| `PipelineFactory.cs` (95行) | 简化为 CommandInputPort 调用 |
| `CommandExecutionEngine.cs` | 精简为 CommandInputPort 封装 |
| `ContextKeys.cs` | 删除命令执行相关键 |
| `ExecutionOptions.cs` | 保留，精简 |
| `EventHub.cs` | 由 ECS DataChanged 事件代替 |
| `TemplatePreprocessSkeleton.cs` | 保留，集成到 TemplateExpandSystem |

### 3.3 保持不动（4 个文件）

| 文件 | 原因 |
|------|------|
| `NonEventCommands.cs` (132行) | 命令名称常量定义，纯数据 |
| `FunctionExecutionSystem.cs` (154行) | 已有 ECS 系统，与 CommandSystem 协作 |
| `Ports.cs` (CsgEntryPoint + WinFormsOutputPort) | 已有 ECS Ports |
| `FunctionStorageRegistry.cs` | 函数注册，exec 命令调用方 |

---

## 四、实施步骤

### Phase 1：新建文件（4 个）

| 步骤 | 文件 | 说明 |
|------|------|------|
| 1.1 | `CommandComponents.cs` | CommandMeta / CommandStatus / CommandResult 组件 |
| 1.2 | `CommandInputPort.cs` | 聚拢入口：LoadScript / ExecuteAll / ExecuteNext |
| 1.3 | `CommandOutputPort.cs` | 聚拢出口：Snapshot / PrintMatrix |
| 1.4 | `CommandSystems.cs` | ParseCommandSystem + 各命令 System (echo/sleep/exec/...) |

### Phase 2：改造（3 个）

| 步骤 | 文件 | 说明 |
|------|------|------|
| 2.1 | `PipelineFactory.cs` | 移除 3-stage，改为 CommandInputPort.LoadScript + ExecuteAll |
| 2.2 | `CommandPipelineContext.cs` | 删除执行状态字段，保留基本骨架 |
| 2.3 | `FrameworkInitializer.cs` | 移除 NonEventCommandCenter.Initialize() 调用 |

### Phase 3：废弃标记（7 个）

| 步骤 | 文件 | 说明 |
|------|------|------|
| 3.1-3.7 | NonEventCommandCenter 等 | 添加 `[Obsolete]` + 注释说明替代路径 |

### Phase 4：测试验证

| 步骤 | 说明 |
|------|------|
| 4.1 | 运行 `echo 5 | 交互启动.bat`，验证矩阵输出 |
| 4.2 | 逐条核对 45 条命令的状态和错误信息 |
| 4.3 | 修复 exec 命令的函数查找路径 |
| 4.4 | 全部通过后清理废弃文件 |

---

## 五、风险与回退

| 风险 | 影响 | 回退方案 |
|------|------|---------|
| ECS 启动时序：CommandSystem 必须在 LoadScript 前注册 | 命令不被执行 | 沿用 PipelineFactory 的构建顺序 |
| PrintMatrix 在 CLI 模式无窗口 | 矩阵无法显示 | 输出到 Console 或日志文件 |
| TemplateExpand 与现有模板展开冲突 | 双倍展开 | 完全替换 ScriptLoadSkeleton |