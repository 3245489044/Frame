# FrontEndDriven CSG/Ports 绕开分析报告

> **分析日期：** 2026-07-23
> **分析范围：** `CsharpPrj/Operation/FrontEndDriven/`
> **架构基准：** 三收拢点架构（EcsInputPort → CsgEntryPoint → WinFormsOutputPort）

---

## 架构回顾：标准数据流

按照三收拢点架构设计，标准数据流应为：

```
配置数据
  → [EcsInputPort]     → ECS Component（标准化输入）
  → [CsgEntryPoint]    → CanonicalForm（标准化收集）
  → [WinFormsOutputPort] → WinFormsUIAdapter（标准化输出）
  → WinForms 控件操作
```

**任何不经过 CsgEntryPoint 收集、不通过 WinFormsOutputPort 输出的控件操作，均属于"绕开"。**

---

## 发现的绕开点

---

### ① WinFormsControlValueWriter — 值同步绕开（🔴 严重）

| 项目 | 内容 |
|------|------|
| **文件** | `WinForms/ValueSync/WinFormsControlValueWriter.cs` |
| **类型** | 值读写 → 直接操作控件 |
| **严重程度** | 🔴 严重 |

**绕开路径：**
```
ControlInteraction/ValueSync/Systems/ 中的 ECS System
  → WinFormsControlValueWriter（实现 IControlValueAccessor）
    → 直接操作 17+ 种 WinForms 控件的原生属性
    → ctrl.Text = value / comboBox.SelectedItem = matched / ...
```

**证据：**
- 第 18-20 行：`using System.Windows.Forms;`、`using System.Drawing;`
- 第 39 行：`public class WinFormsControlValueWriter : TConfig.Controls.IControlValueAccessor`
- 第 104-111 行：`GetValue()` 通过适配器直接读取控件属性
- 第 118-126 行：`SetValue()` 通过适配器直接写入控件属性
- 第 307-312 行：`RegisterTextControlAdapter` 中 `ctrl.Text = value ?? string.Empty`
- 第 528-555 行：`RegisterComboBoxAdapter` 中 `comboBox.SelectedItem = matched`
- 第 561-573 行：`RegisterDateTimePickerAdapter` 中 `dateTimePicker.Value = dateVal`

**影响：** 值的变化不经过 CanonicalForm，无法被管线追踪和拦截。ECS System 输出的值变更意图无法通过 CSG 统一收集。

**应该走的路径：**
```
ECS ValueSyncSystem
  → 输出 CanonicalForm（节点属性变更）
  → WinFormsOutputPort 接收
  → WinFormsUIAdapter 执行控件操作
```

---

### ② UIEventBinder — 事件绑定绕开（🔴 严重）

| 项目 | 内容 |
|------|------|
| **文件** | `WinForms/EventBinding/UIEventBinder.cs` |
| **类型** | 事件注册 → 直接绑定 WinForms 事件 |
| **严重程度** | 🔴 严重 |

**绕开路径：**
```
UIEventBinder
  → IWinFormsEventRegistrar（直接注册 WinForms 原生事件）
    → _commandTextBox.KeyPress += handler（直接 += 事件处理器）
    → _eventRegistrar.RegisterClick / RegisterTextChanged / ...
```

**证据：**
- 第 18-19 行：`using System.Windows.Forms;`
- 第 112 行：`_commandTextBox.KeyPress += handler;`
- 第 136-196 行：大量直接注册 WinForms 事件（`RegisterTextChanged`、`RegisterSelectedIndexChanged`、`RegisterDoubleClick`、`RegisterClick`、`RegisterCheckedChanged`）
- 第 37 行：`private readonly IWinFormsEventRegistrar _eventRegistrar;` — 事件注册器直接操作 WinForms

**影响：** 事件绑定逻辑绕过 ECS 的 `EventBindingSystem`，不经过 CanonicalForm。事件绑定信息无法通过 CSG 统一收集和管理。

**应该走的路径：**
```
ECS EventBindingSystem
  → 读取 EventBindingsComponent
  → 输出 CanonicalForm（事件绑定边）
  → WinFormsOutputPort 接收
  → WinFormsEventAdapter 执行事件绑定
```

---

### ③ CommandHandler — 命令处理绕开（🟡 中等）

| 项目 | 内容 |
|------|------|
| **文件** | `WinForms/CommandHandler.cs` |
| **类型** | 命令处理 → 直接操作控件 |
| **严重程度** | 🟡 中等 |

**绕开路径：**
```
CommandHandler
  → 直接持有 _outputTextBox / _commandTextBox 引用
    → _commandTextBox.Clear() 直接清空输入框
    → _outputTextBox.AppendText(...) 直接追加输出文本
```

**证据：**
- 第 10 行：`using System.Windows.Forms;`
- 第 23-24 行：`private readonly TextBox _outputTextBox;` 和 `_commandTextBox;`
- 第 69 行：`_commandTextBox.Clear();`
- 第 155 行：`_outputTextBox.Clear();`
- 第 175 行：`_outputTextBox.AppendText(message + Environment.NewLine);`

**影响：** 命令处理直接操作 WinForms 控件，不经过 Ports 接口。命令的输入和输出都无法通过 CanonicalForm 标准化。

**应该走的路径：**
```
命令输入
  → 通过 EcsInputPort 进入 ECS
  → 命令执行
  → 输出 CanonicalForm（输出文本变更）
  → WinFormsOutputPort 接收
  → WinFormsUIAdapter 更新输出文本框
```

---

### ④ EventSubscriptionManager — 全局事件订阅绕开（🟡 中等）

| 项目 | 内容 |
|------|------|
| **文件** | `ControlInteraction/EventBinding/Implementation/EventSubscriptionManager.cs` |
| **类型** | 事件订阅 → 直接引用全局静态事件 |
| **严重程度** | 🟡 中等 |

**绕开路径：**
```
EventSubscriptionManager
  → 直接订阅 LogRepository.LogWritten（全局静态事件）
  → 直接订阅 FrameworkErrors.ErrorOccurred（全局静态事件）
```

**证据：**
- 第 12-13 行：`using Common.Diagnostics;` 和 `using Modules.DiagnosticContext;`
- 第 133 行：`LogRepository.LogWritten += h`
- 第 150 行：`FrameworkErrors.ErrorOccurred += h`

**影响：** 全局静态事件的订阅不经过 CSG 收集。这些事件流没有通过 CanonicalForm 标准化，无法被管线统一管理。

**应该走的路径：**
```
全局事件
  → 通过 EcsInputPort 转换为 ECS Component
  → 由 ECS System 处理
  → 输出 CanonicalForm
  → WinFormsOutputPort 接收
```

---

### ⑤ CompositeValueWriter — 值访问组合器绕开（🟡 中等）

| 项目 | 内容 |
|------|------|
| **文件** | `ControlInteraction/ValueSync/Implementation/CompositeValueWriter.cs` |
| **类型** | 值访问 → 委托链末端直接操作控件 |
| **严重程度** | 🟡 中等（随 ① 一起整改） |

**绕开路径：**
```
CompositeValueWriter（实现 IControlValueAccessor）
  → 内部持有多个 IControlValueAccessor 实例
    → 遍历调用 writer.GetValue / SetValue
      → 最终落到 WinFormsControlValueWriter → 直接操作控件
```

**证据：**
- 第 25 行：`public class CompositeValueWriter : TConfig.Controls.IControlValueAccessor`
- 第 69-81 行：`GetValue()` 遍历 writer 直接读取控件值
- 第 84-103 行：`SetValue()` 遍历 writer 直接写入控件值

**影响：** 虽然 CompositeValueWriter 本身是合理的组合模式，但它作为值访问的入口，最终委托链的末端仍然是直接操作 WinForms 控件的 `WinFormsControlValueWriter`，整个值读写路径都不经过 CSG。

---

## 绕开点全景图

```
┌─────────────────────────────────────────────────────────────────┐
│                       三收拢点标准架构                            │
│                                                                 │
│  配置 → EcsInputPort → ECS Comp → CsgEntryPoint → CF → OutputPort │
└─────────────────────────────────────────────────────────────────┘
                              ▲                    │
                              │ 应该走这里          │ 应该走这里
                              │                    ▼
┌─────────────────────────────┴──────────────────────────────────┐
│                    绕开路径（实际现状）                          │
│                                                                 │
│  ① WinFormsControlValueWriter ──────────────────────────→ 控件 │
│     值同步 System → IControlValueAccessor → 直接读写属性        │
│                                                                 │
│  ② UIEventBinder ──────────────────────────────────────→ 控件 │
│     IWinFormsEventRegistrar → += 事件处理器 → 直接绑定事件      │
│                                                                 │
│  ③ CommandHandler ─────────────────────────────────────→ 控件 │
│     直接持有 TextBox 引用 → .Clear() / .AppendText()           │
│                                                                 │
│  ④ EventSubscriptionManager ───────────────────────────→ 事件 │
│     直接 += LogRepository.LogWritten / FrameworkErrors         │
│                                                                 │
│  ⑤ CompositeValueWriter ──→ ① WinFormsControlValueWriter → 控件│
│     组合模式包装，末端仍直接操作控件                              │
└─────────────────────────────────────────────────────────────────┘
```

---

## 整改建议

### 优先级 P0（严重 — 必须整改）

| # | 模块 | 整改方案 |
|---|------|---------|
| ① | WinFormsControlValueWriter | 改造为通过 WinFormsOutputPort 输出 CanonicalForm，由 WinFormsUIAdapter 执行控件操作。消除对 `System.Windows.Forms` 的直接依赖。 |
| ② | UIEventBinder | 事件注册信息改为通过 ECS EventBindingSystem 管理，输出 CanonicalForm 事件绑定边，由 WinFormsEventAdapter 执行绑定。 |

### 优先级 P1（中等 — 建议整改）

| # | 模块 | 整改方案 |
|---|------|---------|
| ③ | CommandHandler | 通过 Ports 接口操作控件，不直接持有 TextBox 引用。输出文本通过 CanonicalForm 传递。 |
| ④ | EventSubscriptionManager | 全局事件通过 EcsInputPort 转换为 ECS Component，由 ECS System 处理。 |
| ⑤ | CompositeValueWriter | 随 ① 一起整改，作为值访问的统一入口保留，但末端改为通过 WinFormsOutputPort。 |

---

## 附录：已检查的文件清单

| 文件 | 状态 | 说明 |
|------|------|------|
| `WinForms/ValueSync/WinFormsControlValueWriter.cs` | 🔴 绕开 | 直接操作 17+ 种控件 |
| `WinForms/EventBinding/UIEventBinder.cs` | 🔴 绕开 | 直接注册 WinForms 事件 |
| `WinForms/CommandHandler.cs` | 🟡 绕开 | 直接持有 TextBox 引用 |
| `ControlInteraction/EventBinding/Implementation/EventSubscriptionManager.cs` | 🟡 绕开 | 直接订阅全局静态事件 |
| `ControlInteraction/ValueSync/Implementation/CompositeValueWriter.cs` | 🟡 绕开 | 委托链末端直接操作控件 |
| `ControlInteraction/EventBinding/Systems/EventBindingSystem.cs` | ✅ 合规 | 通过 ControlPool 和委托解耦 |
| `ControlRender/Creation/Systems/ControlCreateSystem.cs` | ✅ 合规 | 通过 ControlPool 创建控件 |
| `ControlRender/Layout/Systems/LayoutSystem.cs` | ✅ 合规 | 通过 Adapter 操作控件 |
| `ControlRender/Style/Systems/StyleSystem.cs` | ✅ 合规 | 通过 Adapter 操作控件 |
| `WinForms/Adapter/WinFormsUIAdapter.cs` | ✅ 合规 | 标准 WinFormsOutputPort 实现 |
| `WinForms/Adapter/WinFormsEventAdapter.cs` | ✅ 合规 | 标准事件适配器 |
| `WinForms/Adapter/ControlMapAdapter.cs` | ✅ 合规 | 标准控件映射适配器 |
| `Protocol/UIProtocol.cs` | ✅ 合规 | 协议常量定义 |
| `Ports.cs` | ✅ 合规 | 端口定义 |
