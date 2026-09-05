# FileDriven.Tests 测试错误分析与修复状态

> 更新日期: 2026-06-01 (v2 — 全部修复)
> 基于: `CentralConsole/FileDriven.Tests/TestResults/TestErrorAnalysis.md`

---

## 一、总体状态

| 测试套件 | 总数 | 通过 | 失败 | 状态 |
|---------|:----:|:----:|:----:|:----:|
| StyleResolverTests | 15 | 15 | 0 | ✅ 已修复 |
| TemplateResolverTests | 15 | 15 | 0 | ✅ 已修复 |
| ReferenceExpansionMiddlewareTests | 13 | 13 | 0 | ✅ 已修复 |
| **合计** | **43** | **43** | **0** | **100%** |

---

## 二、已修复的问题

### 2.1 StyleResolver.cs — 通配符匹配 Bug

**文件**: `FileDriven/FileDriven.Utilities/ReferenceProcessing/Resolvers/StyleResolver.cs`

**根因**: 源代码错误

`applyTo.Contains("*")` 使用 `JsonArray.Contains(object)` 方法，但 `JsonArray` 中的元素是 `JsonNode` 类型，与字符串 `"*"` 比较永远返回 `false`。

**修复**:
```csharp
// 修改前:
if (!applyTo.Contains("*") && !applyTo.Any(a => a?.GetValue<string>() == targetType))

// 修改后:
if (!applyTo.Any(a => a?.GetValue<string>() == "*") && !applyTo.Any(a => a?.GetValue<string>() == targetType))
```

**影响测试**: `StyleResolverTests.Resolve_ShouldApply_WhenApplyToContainsWildcard`

---

### 2.2 ReferenceExpansionMiddleware.cs — workflow.steps 未处理

**文件**: `FileDriven/FileDriven.Utilities/ReferenceProcessing/Middleware/ReferenceExpansionMiddleware.cs`

**根因**: 源代码错误

`Process()` 方法中处理 `steps` 的代码检查的是顶层 `doc.ContainsKey("steps")`，但测试 JSON 中 `steps` 在 `workflow` 对象内：
```json
{"workflow": {"steps": [...]}}
```
而不是在顶层：
```json
{"steps": [...]}
```

**修复**: 添加 `else if` 分支处理 `workflow.steps`：
```csharp
if (doc.ContainsKey("steps"))
{
    ProcessSteps(doc["steps"]!.AsArray());
}
else if (doc.ContainsKey("workflow") && doc["workflow"] is JsonObject workflowObj)
{
    if (workflowObj.ContainsKey("steps"))
    {
        ProcessSteps(workflowObj["steps"]!.AsArray());
    }
}
```

**影响测试**: `ReferenceExpansionMiddlewareTests.Process_ShouldExpand_TemplateInWorkflowSteps` ✅ 已通过

---

### 2.3 ProcessSteps 方法重写 — 命令组模板展开逻辑

**文件**: `FileDriven/FileDriven.Utilities/ReferenceProcessing/Middleware/ReferenceExpansionMiddleware.cs`

**根因**: 源代码错误

`ProcessSteps` 原来委托给 `ProcessItems`，但 `ProcessItems` 的逻辑是用展开后的数组替换原元素。对于 `steps`，测试期望展开后的 `commands` 数组作为 `commands` 属性添加到 step 中，而不是替换 step 本身。

**修复**: 重写 `ProcessSteps` 方法，直接处理 step 中的 template 引用，将展开后的 `commands` 数组作为 step 的属性。

**影响测试**: `ReferenceExpansionMiddlewareTests.Process_ShouldExpand_TemplateInWorkflowSteps` ✅ 已通过

---

### 2.4 TemplateResolver.cs — 多控件模板判断逻辑错误（v1）

**文件**: `FileDriven/FileDriven.Utilities/ReferenceProcessing/Resolvers/TemplateResolver.cs`

**根因**: 源代码错误

`TemplateResolver.Resolve()` 中多控件模板的判断条件为：
```csharp
if (templateDef.TryGetPropertyValue("controls", out var _) && templateType == null)
```

但 `GetTemplateType()` 从 `defaults` 中提取 `type`，导致 `labeled-input` 模板（有 `defaults.type = "input"`）的 `templateType` 为 `"input"`，不满足 `templateType == null`，被错误地当作单控件模板处理。

**修复（v1）**: 将判断条件改为只检查顶层 `type`，不从 `defaults` 中获取：
```csharp
if (templateDef.TryGetPropertyValue("controls", out var _) && !templateDef.ContainsKey("type"))
```

**影响测试**: `ReferenceExpansionMiddlewareTests.Process_ShouldReplace_SingleControlWithMultipleControls` ✅ 已通过

---

### 2.5 TemplateResolver.cs — 多控件模板判断逻辑（v2 最终修复）

**文件**: `FileDriven/FileDriven.Utilities/ReferenceProcessing/Resolvers/TemplateResolver.cs`

**根因**: 源代码错误（由 2.4 的修复引入）

2.4 的修复使用 `!templateDef.ContainsKey("type")` 替代 `templateType == null`，解决了 `labeled-input` 的问题，但引入了新的问题：

`text-input` 模板定义：
```json
{
    "defaults": {"type": "input", "width": 200},
    "controls": [
        {"name": "${name}", "type": "${type}", "width": "${width}"}
    ]
}
```

该模板有 `controls` 数组（1 个元素），但没有顶层 `type`。2.4 的修复导致它被当作多控件模板处理，进入 `ExpandMultiControl` 分支。但 `ExpandMultiControl` 中的占位符替换逻辑与 `ExpandSingleControl` 不同：
- `ExpandSingleControl` 先应用 `defaults`（设置 `type = "input"`），再应用 `input` 覆盖
- `ExpandMultiControl` 中，`input` 没有 `type` 键，`defaults` 中的 `type` 没有被应用到子控件

**修复（v2）**: 增加更精确的判断逻辑，区分"单控件模板（子控件定义）"和"多控件模板"：

```csharp
if (templateDef.TryGetPropertyValue("controls", out var controlsNode) && controlsNode is JsonArray controlsArray && !templateDef.ContainsKey("type"))
{
    // 单控件模板（子控件定义）：controls 只有 1 个元素且有 defaults
    // 例如 text-input: {defaults: {type:"input", width:200}, controls: [{name:"${name}", ...}]}
    if (controlsArray.Count == 1 && templateDef.ContainsKey("defaults"))
    {
        return ExpandSingleControl(templateDef, target);
    }

    // 多控件模板（如 labeled-input 有 2 个控件，或 event-btn 无 defaults）
    var expanded = ExpandMultiControl(templateDef, target);
    var wrapper = new JsonObject { ["controls"] = expanded };
    return wrapper;
}
```

**判断逻辑**:
- `controls` 数组有 **> 1 个元素** → 多控件模板（如 `labeled-input`）
- `controls` 数组有 **1 个元素 且 有 `defaults`** → 单控件模板（子控件定义，如 `text-input`、`nested-input`）
- `controls` 数组有 **1 个元素 且 无 `defaults`** → 多控件模板（如 `event-btn`、`placeholder-test`）

**影响测试**:
- `ReferenceExpansionMiddlewareTests.Process_ShouldExpand_TemplateInViews` ✅ 已通过
- `ReferenceExpansionMiddlewareTests.Process_ShouldRecurse_ChildControls` ✅ 已通过

---

## 三、修复状态汇总

| 优先级 | 问题 | 影响测试数 | 状态 |
|:-----:|------|:---------:|:----:|
| P0 | 2.1 StyleResolver 通配符匹配 | 1 | ✅ 已修复 |
| P0 | 2.2 workflow.steps 未处理 | 1 | ✅ 已修复 |
| P0 | 2.3 ProcessSteps 命令组展开逻辑 | 1 | ✅ 已修复 |
| P0 | 2.4 多控件模板判断（labeled-input） | 1 | ✅ 已修复 |
| P0 | 2.5 多控件模板判断（text-input/nested-input） | 2 | ✅ 已修复 |

---

## 四、根因分类总结

| 问题类型 | 数量 | 占比 |
|---------|:----:|:----:|
| 源代码错误 | 5 | 100% |
| 测试代码错误 | 0 | 0% |

所有失败测试均由源代码错误导致，测试代码本身是正确的。

---

## 五、最终采用的修复方案

### 5.1 多控件模板判断逻辑的改进方案

最终采用的方案是在 `TemplateResolver.Resolve()` 中，当检测到有 `controls` 数组且没有顶层 `type` 时，进一步检查：

1. **controls 数组元素数量 > 1** → 多控件模板（如 `labeled-input`）
2. **controls 数组元素数量 == 1 且模板有 `defaults`** → 单控件模板（子控件定义，如 `text-input`、`nested-input`）
3. **controls 数组元素数量 == 1 且模板无 `defaults`** → 多控件模板（如 `event-btn`、`placeholder-test`）

这个方案的关键洞察是：**有 `defaults` 字段的单控件模板**（如 `text-input`）的 `defaults` 定义了控件的默认属性（`type`、`width`），需要走 `ExpandSingleControl` 分支来正确应用 defaults 和 input 覆盖。而没有 `defaults` 的单元素 controls 模板（如 `event-btn`）则走 `ExpandMultiControl` 分支，保持向后兼容。

### 5.2 验证结果

| 测试范围 | 结果 |
|---------|:----:|
| ReferenceExpansionMiddlewareTests (13 tests) | ✅ 全部通过 |
| TemplateResolverTests (15 tests) | ✅ 全部通过 |
| StyleResolverTests (15 tests) | ✅ 全部通过 |
| 全部 ReferenceProcessing 测试 (165 tests) | ✅ 全部通过 |
