# Parameters 与 Output 工作流参数处理分析报告

> 分析日期：2026-07-15
> 分析范围：`CsharpPrj/Operation/WorkFlowMethod/Parameters/` 和 `CsharpPrj/Operation/WorkFlowMethod/Output/`

---

## 一、重复逻辑（Duplication）

### 1.1 🔴 ListView 选中项读取逻辑 — 3 处重复

| 位置 | 方法 | 行数 |
|------|------|------|
| `Parameters/ParameterBuilderStages.cs:220-241` | `ReadListViewSelectedValue()` | 22 行 |
| `Parameters/ParameterValueResolver.cs:120-140` | `ReadListViewSelectedValue()` | 21 行 |
| `Output/Strategies/SelectedItemReadStrategy.cs:27-51` | `Read()` | 25 行 |

三处代码几乎完全一致：都通过反射读取 `SelectedItems` → `get_Item(0)` → `Text`，且都用了 `try/catch` 静默吞异常。

### 1.2 🟡 控件值读取逻辑 — 3 处重复

| 位置 | 方法 |
|------|------|
| `Parameters/ParameterBuilderStages.cs:213` | `GetControlValue()` — 内联 |
| `Parameters/ParameterValueResolver.cs:188-207` | `GetControlValue()` — 私有方法 |
| `Output/Strategies/TextReadStrategy.cs:24-37` | `Read()` |

三处都实现了"优先 Text → 回退 Value/Checked"的读取逻辑。

### 1.3 🟡 控件按路径查找逻辑 — 2 处重复

| 位置 | 方法 |
|------|------|
| `Parameters/ParameterValueResolver.cs:147-183` | `ResolveControlByPath()` |
| `Output/Stages/ControlResolveStage.cs:95-179` | `FindControl()` + 多策略 |

两者都实现了"从父容器按名称查找子控件"的逻辑，但实现方式不同。

### 1.4 🟡 类型转换逻辑 — 2 处重复

| 位置 | 方法 |
|------|------|
| `Parameters/ParameterBuilderStages.cs:248-311` | `ConvertValue()` — 内联实现 |
| `Parameters/ParameterTypeConverter.cs:48-97` | `Convert()` / `ConvertTo()` — 独立服务 |

### 1.5 🟢 回退值获取逻辑 — 2 处重复

| 位置 | 方法 |
|------|------|
| `Parameters/ParameterBuilderStages.cs:316-327` | `GetFallbackValue()` |
| `Parameters/ParameterTypeConverter.cs:100-103` | `GetFallbackValue()` |

---

## 二、冗余逻辑（Redundancy）

### 2.1 🔴 ParameterBuilder 管线与独立 Service 的双轨制

| 体系 A：管线内联（旧） | 体系 B：独立 Service（新） |
|---|---|
| `ParameterBuilderStages.ParameterBindingResolveStage` | `ParameterValueResolver` |
| `ParameterBuilderStages.ConvertValue()` | `ParameterTypeConverter` |
| `ParameterBuilderStages.ReadListViewSelectedValue()` | `ParameterValueResolver.ReadListViewSelectedValue()` |

体系 B 的 Service 已通过 DI 注册，但体系 A 的管线代码完全没有使用体系 B 的 Service。

### 2.2 🟡 Output 管线中 Storage 写入的过度设计

每个 Stage 都向 Storage 写入诊断数据，但这些数据没有任何消费方读取。

### 2.3 🟡 OutputWriteContext 的 Storage 属性未发挥设计价值

所有 Stage 都通过显式属性传递数据，Storage 仅用于写入无人消费的诊断数据。

### 2.4 🟢 ParameterConfigProvider 与 ParameterKeyMapper 的 ParseKeysFromJson 重复

两者都委托给同一个 `IKeyMapper` 接口，但各自接口都暴露了 `ParseKeysFromJson`。

---

## 三、错误逻辑（Bugs / Anti-patterns）

### 3.1 🔴 ParameterBuilderStages 未使用已注册的 Service

`ParameterBindingResolveStage` 自己完成值解析和类型转换，未委托给 `IParameterValueResolver` 和 `IParameterTypeConverter`。

### 3.2 🔴 ListView 类型判断使用脆弱的字符串比较

多处使用 `GetType().Name == "ListView"`，继承自 ListView 的控件会匹配失败。

### 3.3 🟡 Output 管线 Stage 的类型检查冗余

7 个 Stage 中有 7 段完全相同的类型检查样板代码。

### 3.4 🟡 OutputPipelineRunner 的同步/异步方法代码重复

`Execute()` 和 `ExecuteAsync()` 方法体几乎完全一致。

### 3.5 🟢 ParameterBuilder 的显式接口实现可能导致静默丢失

`as List<ParameterBinding>` 在传入数组等其他 `IReadOnlyList` 实现时返回 null。
