# Output 调用链 — 重构后架构

> **最后更新**：2026-07-15
> **重构目标**：完全管线化，消除旧体系（OutputWriter / OutputControlFinder / OutputFindContext 等）

---

## 一、架构概览

```
调用方（EventBindingStageBase / 其他）
         │
         ▼
  IOutputWriter (接口)
         │
         ▼
  OutputPipelineWriterAdapter (适配器)
         │
         ▼
  OutputPipelineRunner.Execute(sender, outputTarget, value)
         │
         ▼
    Pipeline (5 个 Stage 按 Order 排序)
         │
    ┌────┼────┬────┬────┬────┐
    ▼    ▼    ▼    ▼    ▼    ▼
  S10  S20  S30  S40  S50
  Ctrl Adpt Strat Val  Exec
```

---

## 二、文件清单（重构后）

### 2.1 管线核心（新体系）

| 文件 | 说明 |
|------|------|
| `OutputPipelineRunner.cs` | **管线运行器** — 入口，替代旧 `OutputWriter` |
| `OutputPipelineFactory.cs` | **管线工厂** — 创建 Pipeline 实例 |
| `OutputPipelineServiceExtensions.cs` | **DI 扩展** — 注册管线服务 |
| `OutputPipelineWriterAdapter.cs` | **IOutputWriter 适配器** — 桥接旧接口 |
| `OutputWriteContext.cs` | **管线上下文** — 承载所有 Stage 共享数据 |

### 2.2 管线 Stage（5 个标准 Stage）

| 文件 | Order | 说明 |
|------|-------|------|
| `Stages/ControlResolveStage.cs` | 10 | **控件解析** — 查找目标控件（合并旧查找管线） |
| `Stages/AdapterResolveStage.cs` | 20 | **适配器解析** — 获取 unified.json 配置 |
| `Stages/StrategyResolveStage.cs` | 30 | **策略解析** — 根据 Mode 选择写入策略 |
| `Stages/ValueFormatStage.cs` | 40 | **值格式化** — 格式化输出值 |
| `Stages/StrategyExecuteStage.cs` | 50 | **策略执行** — 写入目标控件 |

### 2.3 横切 Stage（可选）

| 文件 | 说明 |
|------|------|
| `Stages/SnapshotStage.cs` | **写入前快照** — 备份控件原始值 |
| `Stages/RollbackStage.cs` | **失败回滚** — 写入失败时恢复原始值 |

### 2.4 保留的工具类

| 文件 | 说明 |
|------|------|
| `ControlAdapterRegistry.cs` | **控件适配器注册表** — 从 unified.json 加载配置 |
| `StrategyResolver.cs` | **策略解析器泛型基类** |
| `WriteStrategyResolver.cs` | **写入策略解析器** |
| `ReadStrategyResolver.cs` | **读取策略解析器** |
| `OutputValueFormatter.cs` | **输出值格式化器** |

### 2.5 策略实现

| 文件 | 说明 |
|------|------|
| `Strategies/TextWriteStrategy.cs` | 文本写入策略 |
| `Strategies/ListWriteStrategy.cs` | 列表写入策略 |
| `Strategies/TextReadStrategy.cs` | 文本读取策略 |
| `Strategies/SelectedItemReadStrategy.cs` | 选中项读取策略 |
| `Strategies/WriteStrategyBase.cs` | 写入策略基类 |

### 2.6 抽象定义

| 文件 | 说明 |
|------|------|
| `Abstractions/ControlAdapter.cs` | 控件适配器配置模型 |
| `Abstractions/IWriteStrategy.cs` | 写入策略接口 |
| `Abstractions/IReadStrategy.cs` | 读取策略接口 |

---

## 三、调用流程详解

### 3.1 标准写入流程

```
EventBindingStageBase.HandleExecutionResult()
  → _outputWriter.WriteOutput(sender, outputTarget, value)    // IOutputWriter 接口
    → OutputPipelineWriterAdapter.WriteOutput()               // 适配器
      → OutputPipelineRunner.Execute()                        // 管线运行器
        → 创建 OutputWriteContext
        → 创建临时 StorageCell
        → Pipeline.Execute(context)
          ├─ ControlResolveStage.Process()     // 查找目标控件
          ├─ AdapterResolveStage.Process()     // 获取适配器配置
          ├─ StrategyResolveStage.Process()    // 解析写入策略
          ├─ ValueFormatStage.Process()        // 格式化值
          └─ StrategyExecuteStage.Process()    // 执行写入
        → 构建 OutputWriteResult
        → 清理 StorageCell
```

### 3.2 带快照的写入流程

```
OutputPipelineRunner.Execute()
  → Pipeline.Execute(context)
    ├─ SnapshotStage.Process()                // 备份控件原始值
    ├─ ControlResolveStage.Process()
    ├─ AdapterResolveStage.Process()
    ├─ StrategyResolveStage.Process()
    ├─ ValueFormatStage.Process()
    ├─ StrategyExecuteStage.Process()
    └─ RollbackStage.Process()                // 失败时恢复原始值
```

---

## 四、删除的旧文件

| 文件 | 原因 |
|------|------|
| `OutputWriter.cs` | 旧体系核心，由 `OutputPipelineRunner` 完全替代 |
| `Abstractions/IOutputWriter.cs` | 接口已迁移到 `Share.EventBindingOrchestrator.Abstractions` |
| `ServiceCollectionExtensions.cs` | 由 `OutputPipelineServiceExtensions.cs` 替代 |
| `OutputControlFinder.cs` | 查找逻辑内联到 `ControlResolveStage` |
| `OutputFindContext.cs` | 合并到 `OutputWriteContext` |
| `FindFromParentFormStage.cs` | 查找逻辑内联到 `ControlResolveStage` |
| `FindByParentChainStage.cs` | 查找逻辑内联到 `ControlResolveStage` |

---

## 五、DI 注册方式

```csharp
// 标准注册（无快照）
services.AddOutputPipeline();

// 带快照的注册
services.AddOutputPipelineWithSnapshot();

// 手动注册（需要同时注册 ControlAdapterRegistry 和 WriteStrategyResolver）
services.AddSingleton<ControlAdapterRegistry>();
services.AddSingleton<WriteStrategyResolver>();
services.AddOutputPipeline();
```

---

## 六、OutputWriteContext 数据结构

```csharp
public class OutputWriteContext
{
    // 输入
    public object Sender { get; set; }           // 事件触发控件
    public string? OutputTarget { get; set; }    // 目标控件名称
    public object? RawValue { get; set; }        // 原始值
    public IStorageCell Storage { get; set; }    // 分层存储

    // Stage 1: ControlResolve
    public object? TargetControl { get; set; }   // 找到的目标控件

    // Stage 2: AdapterResolve
    public OutputAdapter? Adapter { get; set; }  // 适配器配置

    // Stage 3: StrategyResolve
    public IWriteStrategy? Strategy { get; set; } // 写入策略
    public bool HasExplicitConfig { get; set; }   // 是否有显式配置

    // Stage 4: ValueFormat
    public string? FormattedValue { get; set; }   // 格式化后的值

    // Stage 5: StrategyExecute
    public bool WriteSuccess { get; set; }        // 写入是否成功
    public string? ErrorMessage { get; set; }     // 错误消息

    // 诊断
    public Dictionary<string, bool>? StageSuccess { get; set; }
    public Dictionary<string, List<string>>? StageErrors { get; set; }
}
```

---

## 七、OutputWriteResult 数据结构

```csharp
public class OutputWriteResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public object? TargetControl { get; set; }
    public string? FormattedValue { get; set; }
    public bool HasExplicitConfig { get; set; }
    public Dictionary<string, bool>? StageSuccess { get; set; }
    public Dictionary<string, List<string>>? StageErrors { get; set; }
}
```

---

## 八、设计决策记录

### 2026-07-15：完全重构，不保留旧体系

**问题**：Output 调用链存在两套体系并行（旧 OutputWriter 直接调用路径 + 新管线路径），
导致维护成本高、调用路径不清晰。

**决策**：
1. 删除所有旧体系文件（OutputWriter、OutputControlFinder、OutputFindContext 等）
2. 查找逻辑内联到 ControlResolveStage，不再使用独立的 IStagePipeline<OutputFindContext>
3. 添加 OutputPipelineWriterAdapter 适配 IOutputWriter 接口，保持 EventBindingStageBase 兼容
4. OutputPipelineRunner 完全替代 OutputWriter

**理由**：
- 单一职责：每个 Stage 只做一件事
- 可编排：Stage 可组合、可替换、可横切
- 可测试：每个 Stage 可独立测试
- 与 EventBinding 管线风格统一
