# FrontEndDriven 重复/冗余/错误逻辑分析 — 迭代1报告

> 分析日期: 2026-07-26
> 目标: `CsharpPrj/Operation/FrontEndDriven/`
> 迭代模式: CAMPAIGN (Iteration.md §6.3.5), ROUND=1

---

## 一、重复逻辑（Duplicated Logic）

### R1. [高] 三套事件名集合独立维护

| 集合 | 位置 | 条目数 | 用途 |
|------|------|--------|------|
| `EventHelper.StandardEventTypes` | `Shared/EventHelper.cs:34-75` | 31 | 事件名校验、`Format()` 遍历 |
| `EventBindingValidator.KnownEventNames` | `Validation/Validators/EventBindingValidator.cs:17-45` | 45 | 校验时兜底检查 |
| `EventBindingFormatter.EventNameCasingMap` | `Shared/EventBindingFormatter.cs:18-82` | 31 | PascalCase 格式化 |

**差异分析**：
- `KnownEventNames` 是 `StandardEventTypes` 的**超集**，多出 14 个事件：
  `textchange`, `selectedvaluechanged`, `itemclick`, `itemselectionchanged`, `cellclick`, `cellcontentclick`, `rowenter`, `rowleave`, `splittermoved`, `formclosing`, `formclosed`, `load`, `shown`, `activated`, `deactivate`, `helprequested`
- `EventNameCasingMap` 与 `StandardEventTypes` **条目对应但缺少**上述 14 个事件的 PascalCase 映射

**影响**：新增事件类型需要同时修改 3 处，极易遗漏。

**建议**：以 `EventHelper.StandardEventTypes` 为单一真实来源(SSOT)，删除 `KnownEventNames` 私有集合，让 `EventBindingValidator` 仅引用 `StandardEventTypes`。`EventNameCasingMap` 改为从 `StandardEventTypes` 自动生成或作为统一的映射表声明。

---

### R2. [中] 控件类型字典重复

| 字典 | 位置 | 条目数 | 用途 |
|------|------|--------|------|
| `ContractBuilder.ContractFactories` | `Shared/ContractBuilder.cs:111-246` | 33 | 构建控件 JSON Schema |
| `StandardControlStrategies.DefaultControlFactories` | `WinForms/StandardControlStrategies.cs:15-51` | 33 | 创建控件实例 |

**差异分析**：
- 两字典覆盖的控件类型**几乎完全一致**（均为 33 种）
- `ContractFactories` 含有 `container` 而 `DefaultControlFactories` 也含 `container`
- 新增控件类型（如 `datetimepicker`）需要在两处同步添加

**影响**：新增控件需修改 2 处，且无编译期检查确保同步。

**建议**：抽取控件类型注册为公共列表，两字典均从此列表生成。

---

### R3. [低] PassedCount 计算公式重复

```csharp
result.PassedCount = Math.Max(0, result.TotalChecked - result.TotalIssues);
```

出现在 `EntityValidationEngine.RunValidation()` (L119) 和 `ValidateEntity()` (L146)。

**建议**：将公式移到 `ValidationResult` 的属性/方法中（如 `result.PassedCount` 改为计算属性）。

---

### R4. [低] `BaseStandardEvents` 引用

`EventBindingValidator.cs:54` 定义：
```csharp
private static readonly HashSet<string> BaseStandardEvents = EventHelper.StandardEventTypes;
```

该字段仅用于 L100 的双重检查。由于 `KnownEventNames` 已是 `StandardEventTypes` 的超集，此字段从逻辑上**完全冗余**。

---

## 二、冗余逻辑（Redundant Logic）

### D1. [中] 事件名校验的双重检查

`EventBindingValidator.cs:98-108`：
```csharp
if (!BaseStandardEvents.Contains(binding.EventName) && !KnownEventNames.Contains(binding.EventName))
```

`KnownEventNames` 包含所有 `StandardEventTypes` 的事件名，所以 `BaseStandardEvents.Contains` 检查**永远不会在 `KnownEventNames.Contains` 返回 false 时返回 true**。第一个检查是纯冗余的。

**建议**：直接只用 `KnownEventNames.Contains(binding.EventName)` 作为唯一检查。

---

### D2. [低] `BuildEvent` 中 `_bindings` 无消费者

`EventHelper.BuildEvent.cs:91-104`：
```csharp
// ★ Q1 修复 2026-07-26：bindings 构建逻辑（{eventName}_bindings 键）为未来参数绑定扩展预留，
//    当前无下游消费者。保留代码不变以保持 API 兼容性。
```

注释明确声明**当前无下游消费者**。

**建议**：删除该段代码，必要时通过重载扩展。

---

### D3. [低] `LayoutValidator.Validate()` 中注释编号顺序

L27-34 注释为 `// 1.`, L37-67 为 `// 2.`, L70-80 为 `// 3.`。注释顺序为 1→2→3，但代码块 4（子控件越界检查）被嵌套在块 2 内部（L44-66），与注释编号 4 不一致（`// 4. 检查子控件是否超出父容器范围`）。

**影响**：无功能影响，仅读码混淆。

---

## 三、错误逻辑 / 潜在缺陷（Bug / Potential Defects）

### B1. [高] 事件名集合不一致导致校验漏洞

`EventHelper.IsValidEventType()` (L118) 仅检查 `StandardEventTypes`：
```csharp
return !string.IsNullOrEmpty(eventType) && StandardEventTypes.Contains(eventType);
```

而 `EventBindingValidator.Validate()` 能校验通过的事件名（使用 `KnownEventNames`）范围更广。**两份校验逻辑的校准标准不一致**。

**场景**：UI 配置使用 `itemclick` 事件 → `EventBindingValidator` 发出 Warning（通过 `KnownEventNames` 识别）→ `EventHelper.IsValidEventType()` 返回 false → 契约校验拒绝配置。两处表现矛盾。

---

### B2. [中] `EventNameCasingMap` 缺少多个事件的 PascalCase 映射

`KnownEventNames` 中的以下事件在 `EventNameCasingMap` 中**没有对应的 PascalCase 映射**：
`textchange`, `selectedvaluechanged`, `itemclick`, `itemselectionchanged`, `cellclick`, `cellcontentclick`, `rowenter`, `rowleave`, `splittermoved`, `formclosing`, `formclosed`, `load`, `shown`, `activated`, `deactivate`, `helprequested`

当 `ToPascalCase()` 处理这些事件名时，会回退到 `char.ToUpper(name[0]) + name[1..]`，对多词事件名产生错误结果（如 `"itemclick" → "Itemclick"` 而非 `"ItemClick"`）。

---

### B3. [中] `KnownEventNames` 中的 `textchange` 为潜在拼写错误

`EventBindingValidator.cs:20`：既有 `"textchanged"` 又有 `"textchange"`。
- `"textchange"` 不是标准的 WinForms 事件名（标准为 `TextChanged`）
- 可能是 `"textchanged"` 的拼写变体，也可能是有意为之的自定义事件
- 如果是有意为之，`StandardEventTypes` 和 `EventNameCasingMap` 中均无对应条目

---

### B4. [低] `ControlServiceCollection` 双检锁缺少 volatile

`Initial/Registration/ControlServiceCollection.cs:43`：
```csharp
if (_registered) return;
lock (_lock) { if (_registered) return; ... }
```

`_registered` 为非 volatile 的 `static bool`，在 .NET Framework/#nullable 环境中存在极低概率的指令重排序风险。

**影响**：极低概率的 `RegisterService` 被多次执行。

---

## 四、修复优先级汇总

| 编号 | 类型 | 严重度 | 修复难度 | 修复方式 |
|------|------|--------|---------|---------|
| **B1** | 错误逻辑 | 高 | 小 | 统一事件名校验标准 |
| **R1** | 重复逻辑 | 高 | 中 | 合并三套事件名集合 |
| **B2** | 错误逻辑 | 中 | 小 | 补充 EventNameCasingMap 缺失条目 |
| **B3** | 错误逻辑 | 中 | 小 | 确认 textchange 意图并处理 |
| **D1** | 冗余逻辑 | 中 | 极小 | 删除冗余的 BaseStandardEvents 检查 |
| **R2** | 重复逻辑 | 中 | 中 | 抽取公共控件类型列表 |
| **R3** | 重复逻辑 | 低 | 小 | 公式移至 ValidationResult |
| **R4** | 重复逻辑 | 低 | 极小 | 删除冗余字段 |
| **D2** | 冗余逻辑 | 低 | 小 | 删除无消费者代码 |
| **D3** | 冗余逻辑 | 低 | 极小 | 调整注释 |
| **B4** | 错误逻辑 | 低 | 极小 | 加 volatile 或 remove |

---

## 五、下一步（迭代2）

迭代2将在迭代1修复完成后执行，重点：
1. 验证迭代1的修复是否引入新问题
2. 深入扫描 WinForms/Applier、WinForms/Finder、WinForms/ValueSync 子目录
3. 交叉验证 Validator 间的职责划分完整性