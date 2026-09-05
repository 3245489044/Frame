# Calculus 模块代码分析报告

> **分析范围**：`CsharpPrj/BasicMethod/Map/Calculus/` 下全部 22 个源文件  
> **分析日期**：2026-07-18  
> **分析维度**：重复逻辑、冗余逻辑、错误逻辑

---

## 一、发现的问题汇总

### 🔴 P0 — 错误逻辑（已修复）

| # | 文件 | 问题 | 修复方式 |
|---|------|------|---------|
| 1 | `Convergers/RetryConverger.cs:23` | `HasConverged` 条件取反：`current != previous` 时返回已收敛，与重试语义完全相反 | 改为 `EqualityComparer<T>.Default.Equals(current, previous)` |
| 2 | `Differentiators/TreeDifferentiator.cs:54-62` | `UpdateChildren` 通过反射写入 `IReadOnlyCollection` 属性，`CanWrite` 通常为 false，子节点更新无效 | 重新设计：添加 `withChildren` 回调参数，支持 `IMutableTreeNode`、record 类型和自定义更新策略 |

### 🟡 P1 — 重复/冗余逻辑（已修复）

| # | 文件 | 问题 | 修复方式 |
|---|------|------|---------|
| 3 | `Evolvers/PipelineEvolver.cs` vs `Evolvers/CompositeEvolver.cs` | 两个类代码完全重复（仅字段名不同） | 标记 `CompositeEvolver.cs` 为已废弃，统一使用 `PipelineEvolver` |
| 4 | `Extensions/IntegratorExtensions.cs:31` | `ComposedIntegrator.Integrate` 第二次调用传原始 `delta` 而非 `firstResult`，语义不清晰 | 添加注释明确链式语义 |
| 5 | `Convergers/TimeoutConverger.cs:14` | `_startTime` 在构造函数中固定，实例复用会立即超时 | 改为 `DateTime?` 惰性初始化，添加 `ResetTimer()` 方法 |

### 🟢 P2 — 设计问题（已评估）

| # | 问题 | 说明 | 处理 |
|---|------|------|------|
| 6 | `StableConverger`/`ThresholdConverger`/`TimeoutConverger` 的 `Converge()` 为空操作 | 直接返回 input，无实际变换逻辑 | 保持现状——`Converge()` 在接口层面有意义（`RetryConverger` 需要），其他实现返回 input 是合法的恒等变换 |
| 7 | 四个抽象基类结构重复 | `DifferentiatorBase`/`IntegratorBase`/`EvolverBase`/`ConvergerBase` 的 Name 属性+构造函数校验重复 | 保持现状——每个 Base 类仅 14-16 行，分属不同子命名空间，职责清晰，引入共享基类会增加不必要的抽象层 |
| 8 | 四个 `Then()` 扩展方法结构重复 | 每个扩展类都创建了私有的 ComposedXxx 内部类 | 保持现状——模式化代码在泛型约束不同的情况下难以统一抽象 |

---

## 二、修复文件清单

| 文件 | 修改类型 | 说明 |
|------|---------|------|
| `Convergers/RetryConverger.cs` | 🐛 Bug 修复 | `HasConverged` 条件取反修正 |
| `Differentiators/TreeDifferentiator.cs` | 🔄 重构 | 重新设计树节点更新机制，添加 `withChildren` 回调 |
| `Evolvers/CompositeEvolver.cs` | 🗑️ 废弃标记 | 标记为已废弃，统一使用 `PipelineEvolver` |
| `Extensions/IntegratorExtensions.cs` | 📝 注释补充 | 明确 `ComposedIntegrator.Integrate` 的链式语义 |
| `Convergers/TimeoutConverger.cs` | 🐛 Bug 修复 | `_startTime` 改为惰性初始化，添加 `ResetTimer()` |

---

## 三、构建验证

```
dotnet build Calculus.csproj → 成功生成，0 错误 0 警告
```
