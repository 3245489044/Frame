# WdUI 基于 CommandFramework 重构分析报告

> **分析范围**：WdUI / CommandFramework / EventTriggerFramework  
> **Git 范围**：4080053 → 19a418d（75 文件变更，6940 新增 / 4214 删除）  
> **分析日期**：2026-06-05

---

## 目录

1. [架构全景图](#1-架构全景图)
2. [Git 提交历史分析](#2-git-提交历史分析)
3. [已完成的 CommandFramework 对齐（无需重构）](#3-已完成的-commandframework-对齐无需重构)
4. [应基于 CommandFramework 重构的逻辑](#4-应基于-commandframework-重构的逻辑)
   - [A. WinFormsCommandEngine 深层重构](#a-winformscommandengine-深层重构)
   - [B. WdUIComposer 命令执行路径](#b-wduicomposer-命令执行路径)
   - [C. EventTriggerFramework 中 WdUI 特有的扩展](#c-eventtriggerframework-中-wdui-特有的扩展)
   - [D. 管线层面的对齐](#d-管线层面的对齐)
5. [重构优先级与建议](#5-重构优先级与建议)
6. [不推荐重构的部分](#6-不推荐重构的部分)
7. [附录：关键文件对照表](#7-附录关键文件对照表)

---

## 1. 架构全景图

### 1.1 当前架构分层

```
┌─────────────────────────────────────────────────────────────┐
│                      WdUI (WinForms UI)                      │
│  ┌─────────────┐ ┌──────────────┐ ┌──────────────────────┐  │
│  │ WinForms     │ │ WdUIComposer │ │ RenderPipeline       │  │
│  │ CommandEngine│ │ (命令入口)    │ │ (渲染管线)           │  │
│  └──────┬───────┘ └──────┬───────┘ └──────────────────────┘  │
│         │                │                                     │
│  ┌──────┴───────┐ ┌──────┴───────┐ ┌──────────────────────┐  │
│  │ ControlValue │ │ EventBus     │ │ ViewManager          │  │
│  │ Helper       │ │ Adapter      │ │ ConfigContainer      │  │
│  └──────────────┘ └──────────────┘ └──────────────────────┘  │
├─────────────────────────────────────────────────────────────┤
│                  CommandFramework (通用命令层)                 │
│  ┌──────────┐ ┌──────────┐ ┌────────────────────────────┐   │
│  │ Command  │ │ Command  │ │ Pipeline (ParseCommandStage │   │
│  │ Engine   │ │ Registry │ │  + PipelineEntry)          │   │
│  └──────────┘ └──────────┘ └────────────────────────────┘   │
│  ┌──────────┐ ┌──────────┐ ┌────────────────────────────┐   │
│  │ ICommand │ │ NonEvent │ │ EnvironmentAdapterBase     │   │
│  │          │ │ Command  │ │ ControlValueAccessorBase   │   │
│  │          │ │ Handler  │ │ DefaultValueExtractor      │   │
│  └──────────┘ └──────────┘ └────────────────────────────┘   │
├─────────────────────────────────────────────────────────────┤
│               EventTriggerFramework (事件触发层)              │
│  ┌──────────────────┐ ┌────────────────────────────────┐    │
│  │ EventBindingStage │ │ WinFormsEventBinder            │    │
│  │ EventBindingInfo  │ │ WinFormsControlValueAccessor   │    │
│  └──────────────────┘ └────────────────────────────────┘    │
└─────────────────────────────────────────────────────────────┘
```

### 1.2 依赖关系

```
WdUI
  ├── 直接依赖 CommandFramework (ICommandEngine, CommandExecutionContext)
  ├── 直接依赖 EventTriggerFramework (IEventBus, IControlValueAccessor)
  └── 间接依赖 BasicMethod.Framework (ConfigLoader, ViewManager, LifecycleManager)

CommandFramework
  └── 独立项目，无 WdUI 依赖

EventTriggerFramework
  ├── 依赖 CommandFramework (ICommand, CommandPipeline, ParseCommandStage)
  └── 依赖 BasicMethod (WinFormsControlValueAccessor)
```

---

## 2. Git 提交历史分析

### 2.1 关键提交概览

| 提交哈希 | 涉及范围 | 关键变更 |
|---------|---------|---------|
| `4080053` | WdUI + EventTriggerFramework | 基线版本，WdUI 使用 `CommandContext`，EventTriggerFramework 有独立管线 |
| `d806b2b` | WdUI | WinFormsCommandEngine 首次适配 CommandFramework |
| `780239b` | WdUI | WinFormsCommandEngine 二次适配：`CommandContext` → `CommandExecutionContext` |
| `19a418d` | WdUI + CommandFramework + EventTriggerFramework | 大规模重构：CommandFramework 独立，EventTriggerFramework 管线化 |

### 2.2 核心变更统计

```
75 files changed, 6940 insertions(+), 4214 deletions(-)

关键变更分类：
├── CommandFramework 新建：21 个文件（~2500 行）
│   ├── ICommand.cs (721 行) — 命令接口体系
│   ├── CommandEngine.cs — 命令引擎
│   ├── CommandParser.cs (280 行) — 命令解析
│   ├── CommandRegistry.cs (122 行) — 命令注册
│   ├── DelegateCommand.cs (160 行) — 委托命令
│   ├── NonEventCommandHandler.cs (128 行) — 非事件命令处理
│   ├── FunctionExecutor.cs — 函数执行器
│   ├── FunctionRegistry.cs (126 行) — 函数注册
│   └── Pipeline 管线系统 (CommandPipeline, PipelineEntry, ParseCommandStage)
│
├── EventTriggerFramework 重构：~30 个文件（大量删除 + 新增）
│   ├── 删除旧管线：MiddlewarePipeline, StagePipeline, PipelineContext
│   ├── 删除旧阶段：ExecuteFunctionStage, ExtractValuesStage, FindBindingStage, WriteResultStage
│   ├── 删除旧抽象：ICommandParser, ICommandPipeline, IPipelineContext, IPipelineStage
│   ├── 删除旧命令：Commands/CommandParser, Commands/ICommand, Commands/NonEventCommandHandler
│   ├── 新增：EventBindingStage.cs (671 行) — 统一事件绑定阶段
│   ├── 新增：EventTriggerFrameworkInitializer.cs (747 行) — 初始化器
│   ├── 新增：EnhancedEventBusCore.cs (284 行) — 增强事件总线
│   ├── 新增：EventBindingInfo.cs (197 行) — 事件绑定信息
│   └── 新增：README.md (1437 行) — 完整文档
│
└── WdUI 适配：2 个文件（~70 行变更）
    ├── WinFormsCommandEngine.cs (61 行变更)
    │   ├── CommandContext → CommandExecutionContext
    │   ├── async Task → Task (同步化)
    │   └── 新增 ctx.Data = value (get 命令返回值)
    └── WdUI.csproj (6 行变更)
        └── 添加 CommandFramework 引用
```

### 2.3 WinFormsCommandEngine 的三次提交演变

**基线 (4080053)**：
```csharp
class WinFormsCommandEngine : ICommandEngine
{
    // 使用 CommandContext（WdUI 自定义上下文）
    RegisterCommand("set", async (ctx, ct) => await ExecuteSetCommand(ctx));
    private async Task ExecuteSetCommand(CommandContext ctx) { ... }
}
```

**第一次适配 (d806b2b)**：
```csharp
// 开始使用 CommandFramework 的 CommandExecutionContext
// 但签名仍为 async Task
```

**第二次适配 (780239b → 19a418d)**：
```csharp
class WinFormsCommandEngine : CommandFramework.Execution.CommandEngine
{
    // 继承 CommandEngine 基类
    // 使用 CommandExecutionContext（CommandFramework 标准上下文）
    // 同步化：async Task → Task + return Task.CompletedTask
    RegisterCommand("set", async (CommandExecutionContext ctx, CancellationToken ct) 
        => await ExecuteSetCommand(ctx));
    private Task ExecuteSetCommand(CommandExecutionContext ctx) { ... }
}
```

---

## 3. 已完成的 CommandFramework 对齐（无需重构）

以下文件已经完成了与 CommandFramework 的对齐，**不需要进一步重构**：

| 文件 | 当前状态 | 对齐方式 | 说明 |
|------|---------|---------|------|
| `UI/WinFormsCommandEngine.cs` | ✅ **已对齐** | 继承 `CommandFramework.Execution.CommandEngine` | 使用 `CommandExecutionContext`，注册 7 个内置命令 |
| `UI/WdUI.Events.cs` (EventBusAdapter) | ✅ **已对齐** | 实现 `IEventBus` | 委托到 `WdUIEventBus`，通过 `EventTriggerFramework` 的事件总线 |
| `UI/WdUI.Container.cs` (WinFormsContainerAdapter) | ✅ **已对齐** | 实现 `ILayoutContainer` / `ILayoutElement` | 适配 WinForms 控件到布局系统 |
| `UI/ControlValueHelper.cs` | ✅ **已对齐** | 委托到 `BasicMethod.Common.WinForms.ControlValueHelper` | 控件值读写已统一 |
| `Framework/ConfigurationFramework.cs` | ✅ **已对齐** | 继承 `BasicMethod.Framework.ConfigLoader/Builder/Validator` | 配置框架已提取到 BasicMethod |
| `Framework/ViewManagerFramework.cs` | ✅ **已对齐** | 组合 `BasicMethod.Framework.ViewManager` + `NavigationService` | 视图管理已提取 |
| `Framework/FrameworkModule.cs` | ✅ **已对齐** | 继承 `BasicMethod.Framework.LifecycleManager` | 生命周期管理已提取 |

---

## 4. 应基于 CommandFramework 重构的逻辑

### A. WinFormsCommandEngine 深层重构

#### A1. 当前问题

WinFormsCommandEngine 虽然已继承 `CommandEngine` 基类，但**命令实现方式仍为委托注册**，未充分利用 CommandFramework 的 `IEnvironmentAdapter` 机制。

**当前代码**（WinFormsCommandEngine.cs）：
```csharp
private void RegisterBuiltInCommands()
{
    RegisterCommand("set", async (CommandExecutionContext ctx, CancellationToken ct) 
        => await ExecuteSetCommand(ctx));
    RegisterCommand("get", async (CommandExecutionContext ctx, CancellationToken ct) 
        => await ExecuteGetCommand(ctx));
    // ... 共 7 个命令
}

// 每个命令方法独立，手动处理参数提取、控件查找、日志
private Task ExecuteSetCommand(CommandExecutionContext ctx)
{
    var controlName = ctx.Parameters?.TryGetValue("controlName", out var cn) == true ? cn?.ToString() : null;
    var value = ctx.Parameters?.TryGetValue("value", out var v) == true ? v?.ToString() : null;
    // ... 手动查找控件、设置值、日志
}
```

#### A2. 重构建议

**方案：实现 `IEnvironmentAdapter` 接口**

CommandFramework 已有 `EnvironmentAdapterBase` 抽象类，提供标准化的环境适配模式：

```csharp
// CommandFramework 现有接口
public abstract class EnvironmentAdapterBase
{
    protected abstract Task<object?> ExecuteCommandAsync(
        string commandName, 
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken);
}
```

**重构后**：
```csharp
public class WinFormsEnvironmentAdapter : EnvironmentAdapterBase
{
    private readonly IControlFinder _controlFinder;
    private readonly IControlValueAccessor _valueAccessor;

    protected override Task<object?> ExecuteCommandAsync(
        string commandName,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken)
    {
        return commandName switch
        {
            "set" => ExecuteSetAsync(parameters),
            "get" => ExecuteGetAsync(parameters),
            "click" => ExecuteClickAsync(parameters),
            "list" => ExecuteListAsync(parameters),
            "show" => ExecuteShowAsync(parameters),
            "hide" => ExecuteHideAsync(parameters),
            "close" => ExecuteCloseAsync(parameters),
            _ => Task.FromResult<object?>(null)
        };
    }
}
```

**优势**：
- 职责分离：命令路由由 `CommandEngine` 处理，环境适配由 `EnvironmentAdapterBase` 处理
- 参数提取可复用 `DefaultValueExtractor`
- 返回值通过 `CommandExecutionResult` 统一管理，替代 `ctx.Data = value` 的手动赋值

#### A3. 影响范围

| 文件 | 变更类型 | 工作量 |
|------|---------|--------|
| `UI/WinFormsCommandEngine.cs` | 新增 `WinFormsEnvironmentAdapter` 类 | 小 |
| `UI/WinFormsCommandEngine.cs` | 移除 `RegisterBuiltInCommands` 中的 7 个委托注册 | 小 |
| `UI/WinFormsCommandEngine.cs` | 移除 7 个 `ExecuteXxxCommand` 私有方法 | 小 |

---

### B. WdUIComposer 命令执行路径

#### B1. 当前问题

`WdUIComposer` 中有一个 `CommandExecutor` 内部类，当前使用 `UniversalJsonMiddleware.Abstractions.ICommandExecutor` 接口：

```csharp
// WdUIComposer.cs（推测结构）
internal class CommandExecutor : ICommandExecutor
{
    public async Task<object?> ExecuteCommandAsync(string command, 
        IReadOnlyDictionary<string, object?> parameters)
    {
        // 手动解析命令字符串，查找 WinFormsCommandEngine
        // 调用 engine.ExecuteCommandAsync(...)
    }
}
```

这与 CommandFramework 的 `ICommandEngine` 接口存在功能重叠。

#### B2. 重构建议

**直接使用 `CommandFramework.Abstractions.ICommandEngine`**：

```csharp
// 重构后
internal class CommandExecutor
{
    private readonly ICommandEngine _commandEngine;

    public CommandExecutor(ICommandEngine commandEngine)
    {
        _commandEngine = commandEngine;
    }

    public async Task<CommandExecutionResult> ExecuteAsync(
        string commandText, 
        CancellationToken cancellationToken = default)
    {
        var context = new CommandExecutionContext
        {
            CommandText = commandText,
            // 参数由 CommandParser 自动解析
        };
        return await _commandEngine.ExecuteAsync(context, cancellationToken);
    }
}
```

#### B3. 影响范围

| 文件 | 变更类型 | 工作量 |
|------|---------|--------|
| `UI/WdUIComposer.cs` | 修改 `CommandExecutor` 实现 | 中 |
| `UI/WdUIComposer.cs` | 依赖注入改为 `ICommandEngine` | 小 |

---

### C. EventTriggerFramework 中 WdUI 特有的扩展

#### C1. 当前问题

`EventTriggerFramework` 的 `NonEventCommandHandler` 注册了占位处理器，与 `WinFormsCommandEngine` 注册的命令存在**功能重叠**：

```csharp
// EventTriggerFramework/Execution/NonEventCommandHandler.cs
// 注册了以下占位处理器：
// - look / discover / assert / list / validate
// - exit / load / close / show

// WinFormsCommandEngine 也注册了：
// - show / hide / close / list
```

两者都注册了 `show`、`close`、`list` 命令，但实现不同：
- `NonEventCommandHandler` 的版本是**占位符**（仅记录日志）
- `WinFormsCommandEngine` 的版本是**实际实现**（操作 WinForms 控件）

#### C2. 重构建议

**统一命令路由**：

1. `NonEventCommandHandler` 移除与 WinForms 相关的占位符（show/close/list）
2. 这些命令统一由 `WinFormsCommandEngine`（或重构后的 `WinFormsEnvironmentAdapter`）处理
3. `NonEventCommandHandler` 只保留通用占位符（look/discover/assert/validate）

**或者反过来**：如果 `NonEventCommandHandler` 是更上层的抽象，则 WinFormsCommandEngine 应委托给它。

#### C3. 影响范围

| 文件 | 变更类型 | 工作量 |
|------|---------|--------|
| `CommandFramework/Execution/NonEventCommandHandler.cs` | 移除 show/close/list 占位符 | 小 |
| `EventTriggerFramework/Execution/NonEventCommandHandler.cs` | 同步移除（如果存在） | 小 |

---

### D. 管线层面的对齐

#### D1. 当前问题

WdUI 拥有**两套独立的管线系统**：

```
WdUI.RenderPipeline.cs          ← 渲染管线（布局计算 + 渲染）
    └── 与 CommandFramework 无关

CommandFramework.Pipeline       ← 命令管线（命令解析 + 执行）
    └── ParseCommandStage → PipelineEntry → 命令执行

EventTriggerFramework.Pipeline  ← 事件绑定管线
    └── EventBindingStage（使用 ParseCommandStage）
```

三者之间**没有集成**：
- 渲染管线不经过命令管线
- 事件绑定管线虽然使用了 `ParseCommandStage`，但执行路径独立
- 没有统一的管线编排机制

#### D2. 重构建议

**短期**：保持独立，不做集成。因为渲染管线与命令管线的职责完全不同：
- 渲染管线：UI 布局计算、控件排列、视觉呈现
- 命令管线：命令解析、参数提取、业务逻辑执行

**长期**：如果出现"渲染由命令驱动"的需求（如 `set control.width=100` 触发重新布局），可考虑：
1. 在 `RenderPipeline` 中嵌入 `CommandPipeline` 作为子管线
2. 或通过事件总线桥接：命令执行 → 触发布局失效 → 重新渲染

#### D3. 影响范围

| 文件 | 变更类型 | 工作量 |
|------|---------|--------|
| 无 | 暂不重构 | 0 |

---

## 5. 重构优先级与建议

### 5.1 优先级矩阵

| 优先级 | 重构项 | 影响范围 | 工作量 | 风险 | 收益 |
|--------|--------|---------|--------|------|------|
| **P0** | WinFormsCommandEngine 实现 IEnvironmentAdapter | WinFormsCommandEngine.cs | ⭐ 小 | 低 | 高 |
| **P0** | WdUIComposer 的 CommandExecutor 改用 ICommandEngine | WdUIComposer.cs | ⭐ 小 | 低 | 高 |
| **P1** | 统一 show/close/list 命令（去重） | NonEventCommandHandler.cs + WinFormsCommandEngine.cs | ⭐⭐ 中 | 中 | 中 |
| **P1** | ControlValueHelper 改用 IControlValueAccessor | ControlValueHelper.cs | ⭐ 小 | 低 | 中 |
| **P2** | 渲染管线与命令管线的集成 | RenderPipeline.cs + CommandPipeline.cs | ⭐⭐⭐ 大 | 高 | 低 |

### 5.2 推荐执行顺序

```
Phase 1（P0）→ Phase 2（P1）→ Phase 3（P2，可选）
```

**Phase 1：环境适配器化**
1. 创建 `WinFormsEnvironmentAdapter : EnvironmentAdapterBase`
2. 将 7 个命令从委托注册迁移到适配器
3. WdUIComposer 的 CommandExecutor 改用 `ICommandEngine`

**Phase 2：命令去重与统一**
1. 分析 `NonEventCommandHandler` 的占位符与 `WinFormsCommandEngine` 的重叠
2. 移除重复的占位符，统一命令路由
3. `ControlValueHelper` 改用 `IControlValueAccessor`

**Phase 3（可选）：管线集成**
1. 评估渲染管线与命令管线的集成需求
2. 如有需要，设计桥接机制

### 5.3 依赖关系图

```
Phase 1: 环境适配器化
  ┌─────────────────────────────────────────────┐
  │ WinFormsEnvironmentAdapter                  │
  │  ├── 依赖: IControlFinder (已有)             │
  │  ├── 依赖: IControlValueAccessor (已有)      │
  │  └── 依赖: EnvironmentAdapterBase (CF 已有)  │
  └─────────────────────────────────────────────┘
           │
           ▼
Phase 2: 命令去重
  ┌─────────────────────────────────────────────┐
  │ NonEventCommandHandler 精简                  │
  │  └── 移除 show/close/list 占位符             │
  └─────────────────────────────────────────────┘
           │
           ▼
Phase 3: 管线集成（可选）
  ┌─────────────────────────────────────────────┐
  │ RenderPipeline + CommandPipeline 桥接        │
  └─────────────────────────────────────────────┘
```

---

## 6. 不推荐重构的部分

以下模块**不应**基于 CommandFramework 重构，原因如下：

| 模块 | 文件 | 原因 |
|------|------|------|
| **ConfigContainer** | `UI/WdUI.Container.cs` | 容器渲染逻辑与命令执行无关，属于 UI 布局职责 |
| **ViewManager** | `UI/WdUI.ViewManager.cs` | 视图管理是独立职责，已基于 BasicMethod.Framework |
| **EventBus** | `UI/WdUI.Events.cs` | 事件总线是独立基础设施，已通过 EventTriggerFramework 对齐 |
| **Layout 管线** | `Layout/*.cs` | 布局计算与命令执行无关，属于渲染管线职责 |
| **Framework 层** | `Framework/*.cs` | 已基于 BasicMethod.Framework，无需再次抽象 |
| **RenderPipeline** | `WdUI.RenderPipeline.cs` | 渲染管线与命令管线职责不同，暂不集成 |
| **Singleton** | `Framework/Singleton.cs` | 通用单例模式，与命令框架无关 |
| **ControlFactory** | `Framework/ControlFactoryFramework.cs` | 控件工厂，与命令执行无关 |

---

## 7. 附录：关键文件对照表

### 7.1 WdUI 文件清单

| 文件路径 | 行数 | 与 CommandFramework 关系 | 重构建议 |
|---------|------|------------------------|---------|
| `UI/WinFormsCommandEngine.cs` | ~280 | ✅ 已继承 CommandEngine | P0: 实现 IEnvironmentAdapter |
| `UI/WdUIComposer.cs` | ~200 | ⚠️ 使用 ICommandExecutor | P0: 改用 ICommandEngine |
| `UI/ControlValueHelper.cs` | ~80 | ✅ 已委托到 BasicMethod | P1: 改用 IControlValueAccessor |
| `UI/WdUI.Events.cs` | ~60 | ✅ 已实现 IEventBus | 无需重构 |
| `UI/WdUI.Container.cs` | ~100 | ✅ 已实现 ILayoutContainer | 无需重构 |
| `UI/WdUI.Framework.cs` | ~50 | 🔗 依赖 BasicMethod.Framework | 无需重构 |
| `UI/WdUI.Management.cs` | ~80 | 🔗 依赖 BasicMethod.Framework | 无需重构 |
| `UI/WdUI.ViewManager.cs` | ~60 | 🔗 依赖 BasicMethod.Framework | 无需重构 |
| `UI/WdUI.Configuration.cs` | ~40 | 🔗 依赖 BasicMethod.Framework | 无需重构 |
| `UI/WdUI.Layout.cs` | ~50 | 🔗 依赖 BasicMethod.Framework | 无需重构 |
| `UI/WdUI.LayoutEngine.cs` | ~60 | 🔗 依赖 BasicMethod.Framework | 无需重构 |
| `UI/WdUI.Utilities.cs` | ~30 | 🔗 依赖 BasicMethod.Framework | 无需重构 |
| `WdUI.RenderPipeline.cs` | ~150 | ❌ 独立管线 | P2: 可选集成 |
| `Framework/ConfigurationFramework.cs` | 118 | ✅ 已基于 BasicMethod.Framework | 无需重构 |
| `Framework/ViewManagerFramework.cs` | 236 | ✅ 已基于 BasicMethod.Framework | 无需重构 |
| `Framework/FrameworkModule.cs` | 93 | ✅ 已基于 BasicMethod.Framework | 无需重构 |
| `Framework/ControlFactoryFramework.cs` | ~60 | 🔗 独立职责 | 无需重构 |
| `Framework/Singleton.cs` | ~30 | 🔗 通用模式 | 无需重构 |

### 7.2 CommandFramework 核心接口

| 接口/类 | 文件 | 用途 |
|---------|------|------|
| `ICommand` | `ICommand.cs` | 命令接口，定义 ExecuteAsync |
| `ICommandEngine` | `ICommand.cs` | 命令引擎接口 |
| `CommandEngine` | `Execution/CommandEngine.cs` | 命令引擎基类实现 |
| `CommandExecutionContext` | `Models/CommandExecutionContext.cs` | 命令执行上下文 |
| `CommandExecutionResult` | `Execution/CommandExecutionResult.cs` | 命令执行结果 |
| `EnvironmentAdapterBase` | `Execution/EnvironmentAdapterBase.cs` | 环境适配器基类 |
| `ControlValueAccessorBase` | `Execution/ControlValueAccessorBase.cs` | 控件值访问器基类 |
| `DefaultValueExtractor` | `Execution/DefaultValueExtractor.cs` | 默认值提取器 |
| `NonEventCommandHandler` | `Execution/NonEventCommandHandler.cs` | 非事件命令处理器 |
| `DelegateCommand` | `Execution/DelegateCommand.cs` | 委托命令包装 |
| `CommandRegistry` | `CommandRegistry.cs` | 命令注册表 |
| `CommandParser` | `CommandParser.cs` | 命令解析器 |
| `FunctionExecutor` | `Execution/FunctionExecutor.cs` | 函数执行器 |
| `FunctionRegistry` | `Execution/FunctionRegistry.cs` | 函数注册表 |
| `InMemoryStateStore` | `Execution/InMemoryStateStore.cs` | 内存状态存储 |
| `CommandPipeline` | `Pipeline/CommandPipeline.cs` | 命令管线 |
| `PipelineEntry` | `Pipeline/PipelineEntry.cs` | 管线条目 |
| `PipelineStageBase` | `Pipeline/PipelineStageBase.cs` | 管线阶段基类 |
| `ParseCommandStage` | `Pipeline/Stages/ParseCommandStage.cs` | 命令解析阶段 |

### 7.3 EventTriggerFramework 关键文件

| 文件 | 用途 | 与 WdUI 关系 |
|------|------|-------------|
| `ICommand.cs` | 命令接口（ETF 版，包装 CF 版） | 桥接层 |
| `ICommandExecutionResult.cs` | 执行结果接口 | 桥接层 |
| `ControlValueAccessorBase.cs` | 控件值访问器基类 | WinFormsControlValueAccessor 继承 |
| `WinFormsControlValueAccessor.cs` | WinForms 控件值访问器 | WdUI 使用 |
| `WinFormsEventBinder.cs` | WinForms 事件绑定器 | WdUI 使用 |
| `EventBindingStage.cs` | 事件绑定管线阶段 | 使用 CF 的 ParseCommandStage |
| `EventTriggerFrameworkInitializer.cs` | 框架初始化器 | 配置 WdUI 相关组件 |
| `NonEventCommandHandler.cs` | 非事件命令处理器（ETF 版） | 与 WdUI 命令重叠 |

---

## 总结

**已完成的对齐**：
- ✅ WinFormsCommandEngine 继承 CommandEngine，使用 CommandExecutionContext
- ✅ ControlValueHelper 委托到 BasicMethod
- ✅ EventBusAdapter 实现 IEventBus
- ✅ Framework 层基于 BasicMethod.Framework

**建议重构（P0）**：
1. 🔄 WinFormsCommandEngine → 实现 IEnvironmentAdapter（替代委托注册）
2. 🔄 WdUIComposer.CommandExecutor → 改用 ICommandEngine

**建议重构（P1）**：
3. 🔄 统一 show/close/list 命令（去重）
4. 🔄 ControlValueHelper → 改用 IControlValueAccessor

**不建议重构**：
- ❌ ConfigContainer / ViewManager / EventBus / Layout 管线 / RenderPipeline
