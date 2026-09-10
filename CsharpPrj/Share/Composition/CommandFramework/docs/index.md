# CommandFramework 文档总览

> 版本：v1.0
> 日期：2026-07-04

---

## 目录结构

```
docs/
├── index.md                          ← 本文档：文档总览索引
├── design/                           ← 设计类文档
│   ├── design-document.md            ← 框架核心设计文档
│   ├── control-handling-design.md    ← 通用控件处理方案
│   └── string-processing-plan.md     ← 字符串处理统一方案
├── refactoring/                      ← 重构类文档
│   ├── refactoring-plan.md           ← 与 EventTriggerFramework 重复消除方案
│   └── middleware-pipeline-plan.md   ← 洋葱模型中间件管线设计
├── workflow/                         ← 工作流类文档
│   ├── workflow-refactoring-analysis.md  ← WorkflowExecution 重构分析
│   └── nodeprocess-extraction-analysis.md ← NodeProcess 逻辑提取分析
└── integration/                      ← 集成类文档
    └── integration-analysis.md       ← 两套管线系统整合分析
```

---

## 文档分类说明

### 1. 设计文档 (`design/`)

| 文档 | 状态 | 核心内容 |
|------|------|----------|
| [design-document.md](design/design-document.md) | ✅ 已实施 | 框架设计背景、接口定义、数据模型、Bug修复清单 |
| [control-handling-design.md](design/control-handling-design.md) | 🔄 待实施 | 通用控件查找器 `IControlFinder` 和控件值写入器 `IControlValueWriter` 设计 |
| [string-processing-plan.md](design/string-processing-plan.md) | 🔄 待实施 | 字符串拆分逻辑统一方案（SplitWithQuotesAndEscape） |

### 2. 重构方案 (`refactoring/`)

| 文档 | 状态 | 核心内容 |
|------|------|----------|
| [refactoring-plan.md](refactoring/refactoring-plan.md) | ✅ 已实施 | 移除 `EventTriggerFramework` 中与 `CommandFramework` 重复的接口和实现类 |
| [middleware-pipeline-plan.md](refactoring/middleware-pipeline-plan.md) | 🔄 待实施 | 参考 UniversalJsonMiddleware 设计洋葱模型中间件管线 |

### 3. 工作流文档 (`workflow/`)

| 文档 | 状态 | 核心内容 |
|------|------|----------|
| [workflow-refactoring-analysis.md](workflow/workflow-refactoring-analysis.md) | 🔄 待实施 | WorkflowExecution 基于 ExecutionFramework 的优化重构分析（5个阶段路线图） |
| [nodeprocess-extraction-analysis.md](workflow/nodeprocess-extraction-analysis.md) | 🔄 待实施 | NodeProcess 通用逻辑提取到 WorkflowExecution 的分析报告（12项提取清单） |

### 4. 集成分析 (`integration/`)

| 文档 | 状态 | 核心内容 |
|------|------|----------|
| [integration-analysis.md](integration/integration-analysis.md) | ✅ 部分实施 | CommandFramework 两套管线系统（Pipeline + CentralPipeline）整合分析（Phase 1已完成） |

---

## 实施状态追踪

### 已完成 ✅

| 任务 | 关联文档 | 完成日期 |
|------|----------|----------|
| CommandFramework 框架设计与实现 | [design-document.md](design/design-document.md) | 2026-06-05 |
| EventTriggerFramework 重复类型消除 | [refactoring-plan.md](refactoring/refactoring-plan.md) | 2026-06-05 |
| Phase 1 基础修复（注释行标记、枚举转换、并行取消等） | [integration-analysis.md](integration/integration-analysis.md) | 2026-07-04 |

### 进行中 🔄

| 任务 | 关联文档 | 优先级 |
|------|----------|--------|
| Phase 2 上下文统一 | [integration-analysis.md](integration/integration-analysis.md) | P1 |
| Phase 3 管线统一 | [integration-analysis.md](integration/integration-analysis.md) | P1 |
| 控件处理方案实施 | [control-handling-design.md](design/control-handling-design.md) | P1 |
| 字符串处理统一 | [design/string-processing-plan.md](design/string-processing-plan.md) | P2 |

### 待规划 📋

| 任务 | 关联文档 | 预计时间 |
|------|----------|----------|
| 工作流优化重构 | [workflow-refactoring-analysis.md](workflow/workflow-refactoring-analysis.md) | 后续阶段 |
| NodeProcess 逻辑提取 | [nodeprocess-extraction-analysis.md](workflow/nodeprocess-extraction-analysis.md) | 后续阶段 |
| 中间件管线实现 | [middleware-pipeline-plan.md](refactoring/middleware-pipeline-plan.md) | 后续阶段 |

---

## 核心技术文档推荐阅读顺序

```
1. design-document.md          ← 先了解框架整体设计
       ↓
2. integration-analysis.md     ← 了解当前架构问题和整合方案
       ↓
3. refactoring-plan.md         ← 了解重构历史和重复消除
       ↓
4. control-handling-design.md  ← 了解控件处理方案
       ↓
5. workflow-refactoring-analysis.md  ← 了解工作流优化方向
```

---

## 关键设计决策汇总

### 架构设计
- **双执行路径**：CommandEngine（轻量直接执行）和 Pipeline（阶段化执行）
- **统一入口**：`CommandFrameworkHost` + `CommandFrameworkUsage` 两种集成方式
- **三层架构**：接口抽象层 → 核心实现层 → 管线编排层

### 重构方向
- **管线统一**：以 `CentralPipeline` 为核心框架，所有阶段动态注册
- **上下文统一**：合并三层上下文为统一的 `ExecutionContext<TChainInfo>`
- **消除重复**：统一字符串拆分、命令注册检查、失败查找等逻辑

### 扩展机制
- **环境适配器**：`IEnvironmentAdapter` 支持路由到不同后端环境
- **控件值读写器**：`IControlValueAccessor` 统一读写不同类型控件
- **中间件管线**：洋葱模型支持短路和横切关注点

---

## 相关代码入口

| 组件 | 文件路径 | 说明 |
|------|----------|------|
| 统一入口 | `CommandFrameworkHost.cs` | 框架生命周期管理 |
| 命令引擎 | `Execution/CommandEngine.cs` | 命令注册/执行/撤销/重做 |
| 命令解析器 | `CommandParser.cs` | 命令字符串解析 |
| 管线系统 | `Pipeline/PipelineEntry.cs` | 管线入口和阶段管理 |
| 工作流引擎 | `WorkflowExecution/Engine/WorkflowEngine.cs` | 工作流执行管理 |
| 节点执行器 | `WorkflowExecution/Engine/NodeExecutor.cs` | 节点执行和弹性策略 |
| 中央管线 | `WorkflowExecution/Pipeline/CentralPipeline.cs` | 可扩展管线框架 |

---

## 术语表

| 术语 | 说明 |
|------|------|
| 事件命令 | 需要操作 UI 控件的命令，如 `set`、`click` |
| 非事件命令 | 不需要操作 UI 控件的命令，如 `load`、`echo`、`sleep` |
| 管线阶段 | 命令执行流程中的一个步骤 |
| 洋葱模型 | 中间件管线模式，支持嵌套调用和短路 |
| 命令规范化 | 将简写命令转换为完整格式，如 `set` → `do set` |