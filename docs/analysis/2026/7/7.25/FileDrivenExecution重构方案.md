# FileDriven.Execution 重构方案

> **版本**：v1.0  
> **日期**：2026-07-23  
> **参考**：`架构重构模式 — 常见架构缺陷与应对策略.md`（v3.0）

---

## 1. 概述

### 1.1 目标

基于 `CsharpPrj/Structuring/` 中的架构重构模式，对 `FileDriven.Execution` 模块进行系统化重构，消除以下问题：

| 问题 | 类型 | 严重度 |
|------|------|--------|
| 重构残留（废弃代码未清理） | 技术债务 | P0 |
| 大文件职责错位（适配器定义在初始化器中） | 职责错位 | P1 |
| 输入收拢点未推广 | 聚合不足 | P2 |
| 文档与代码不同步 | 知识管理 | P3 |

### 1.2 架构定位

`FileDriven.Execution` 属于 **操作层（Layer 4）**，职责是：

- 框架初始化（`Lifecycle/`）
- GUI 模式编排（`Gui/`）
- 管线执行（`Pipeline/`）
- 适配器（`Adapters/`）

---

## 2. 重构项

### 2.1 P0：删除重构残留

**问题**：`FileDrivenDependencyInjector.cs` 已标记 `[Obsolete]`，其功能已迁移到 `CreateAndConfigureBlock.cs`，但文件仍保留在项目中。

**方案**：
1. 将 `FileDrivenConfigRegistrarAdapter` 内部类提取到 `Adapters/FileDrivenConfigRegistrarAdapter.cs`
2. 删除 `FileDrivenDependencyInjector.cs`
3. 更新 `CreateAndConfigureBlock.cs` 中的引用

**状态**：✅ 已完成

### 2.2 P1：拆分大文件 — 适配器提取

**问题**：`FrameworkInitializer.cs`（~900 行）包含 `WinFormsControlFinderToTConfigAdapter` 内部类，职责错位（初始化器不应包含适配器定义）。

**方案**：
1. 将 `WinFormsControlFinderToTConfigAdapter` 提取到 `Adapters/WinFormsControlFinderToTConfigAdapter.cs`
2. 在 `FrameworkInitializer.cs` 中改用 `Adapters.WinFormsControlFinderToTConfigAdapter` 引用

**状态**：✅ 已完成

### 2.3 P2：推广输入收拢点

**问题**：`CommandInputPort` 已创建但未被广泛使用，命令执行入口分散。

**方案**：
1. 评估 `CommandInputPort` 的覆盖范围
2. 将分散的命令执行入口收敛到 `CommandInputPort`
3. 将 `CommandInputPort` 注册到 DI 容器

**状态**：⏳ 待评估

### 2.4 P3：文档同步

**问题**：`FileDriven.Execution/docs/` 中的方案文档与代码不同步。

**方案**：
1. 在 `CsharpPrj/Structuring/` 下创建此文档
2. 后续每次重构后同步更新

**状态**：✅ 已完成

---

## 3. 目录结构变更

### 3.1 新增文件

```
FileDriven.Execution/
├── Adapters/                          ← 新增目录
│   ├── FileDrivenConfigRegistrarAdapter.cs    ← 从 FileDrivenDependencyInjector.cs 提取
│   └── WinFormsControlFinderToTConfigAdapter.cs  ← 从 FrameworkInitializer.cs 提取
```

### 3.2 删除文件

```
FileDriven.Execution/Gui/FileDrivenDependencyInjector.cs  ← 已删除
```

### 3.3 修改文件

| 文件 | 变更 |
|------|------|
| `Lifecycle/FrameworkInitializer.cs` | 删除 `WinFormsControlFinderToTConfigAdapter` 内部类，改用 `Adapters.` 引用 |
| `Gui/Blocks/CreateAndConfigureBlock.cs` | 改用 `Adapters.FileDrivenConfigRegistrarAdapter` |

---

## 4. 架构原则检查

### 4.1 聚合架构

| 原则 | 检查结果 |
|------|----------|
| 单一职责 | ✅ 适配器归 `Adapters/`，初始化器归 `Lifecycle/` |
| 边界内完整 | ✅ `Adapters/` 包含所有 TConfig 接口适配 |
| 边界间无重叠 | ✅ 适配器职责不与其他目录重叠 |

### 4.2 零容错设计

| 规则 | 检查结果 |
|------|----------|
| 废弃代码直接删除 | ✅ `FileDrivenDependencyInjector.cs` 已删除 |
| 无静默吞异常 | ✅ 适配器构造函数不吞异常 |

### 4.3 文件写入策略

| 原则 | 检查结果 |
|------|----------|
| 最小改动 | ✅ 只改目标行，不重写无关部分 |
| 无注释残留 | ✅ 已清理废弃注释 |
| 语义命名 | ✅ `Adapters/` 目录名表达意图 |

---

## 5. 后续计划

| 优先级 | 项 | 说明 |
|--------|----|------|
| P2 | 推广 `CommandInputPort` | 评估覆盖范围，收敛命令执行入口 |
| P3 | 持续文档同步 | 每次重构后更新此文档 |
