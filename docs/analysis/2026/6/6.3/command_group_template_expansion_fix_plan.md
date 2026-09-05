# command-group 模板展开修复方案

## 问题描述

运行 `math_functions_test.json` 测试脚本时，日志显示只有 3 条命令被执行（`sleep 500`、`close Built-in Math Functions Test`、`exit`），但脚本实际包含 22 条命令（含模板展开后）。

## 根因分析

### 数据流

```
GuiModeRunner.Run()
  → ResolveCommandFile(options.CommandFile)
    → ScriptConverter.ConvertJsonScriptToCommandFile()
      → FileDrivenPipelineFactory.Process(jsonContent)    ← 使用新管线
        → ReferenceExpansionStage.Process()
          → CommandsHandler.Process()
            → ProcessItems() 遍历 commands 数组
              → ExpandItemTemplate() 展开 template 引用
      → ScriptToCommandConverter.Convert(processedJson)
        → ScriptDefinition.ToCommandText()
      → 写入临时文件
  → GuiPipelineRunner.RunAsync(options, actualCommandFile, ...)
    → PipelineEntry.CreateFileDrivenPipeline().Execute(context)
      → ScriptLoadSkeleton → ForEachPipeline.itemsSelector → 遍历命令
```

### 两条模板展开路径

系统中存在**两条**模板展开路径：

| 路径 | 类 | 位置 | 对 command-group 的支持 |
|------|---|------|----------------------|
| **旧路径** | `TemplateExpansionMiddleware` | `BasicMethod/JsonProcess/TemplateExpansion/Core/` | ✅ 正确支持 |
| **新路径** | `FileDrivenPipelineFactory` → `CommandsHandler` | `FileDriven/FileDriven.JsonRefer/ReferenceProcessing/Pipeline.cs` | ❌ 不支持 |

### 旧路径的正确实现

`TemplateExpansionMiddleware.ExpandTemplate()`（第263-290行）正确处理了多对象模板：

```csharp
// 多对象模板（如 labeled-textbox、command-group）
if (template.ContainsKey("controls") || template.ContainsKey("commands"))
{
    var itemsKey = template.ContainsKey("controls") ? "controls" : "commands";
    var results = new JsonArray();
    foreach (var subTemplate in template[itemsKey]!.AsArray())
    {
        var subObj = subTemplate?.AsObject();
        if (subObj == null) continue;

        var result = new JsonObject();
        // 应用 defaults
        if (subObj.ContainsKey("defaults"))
        {
            foreach (var kv in subObj["defaults"]!.AsObject())
                result[kv.Key] = kv.Value?.DeepClone();
        }
        // 应用 parameters（占位符替换）
        if (subObj.ContainsKey("parameters"))
        {
            ApplyParametersWithPlaceholders(result, subObj["parameters"]!.AsObject(), item);
        }
        results.Add(result);
    }
    return results;  // 返回 JsonArray，包含多条展开后的命令
}
```

### 新路径的错误实现

`CommandsHandler` 继承自 `BaseStructureHandler`，其 `ExpandItemTemplate()`（第126-142行）调用 `TemplateResolver.Resolve()`：

```csharp
protected JsonArray? ExpandItemTemplate(JsonObject item, string templateName, ...)
{
    var resolver = ReferenceResolverFactory.GetResolver(ReferenceType.Template);
    var result = resolver.Resolve(item, templateName, resolutionContext);
    // 检查 __multi_control / __multi_command 标记
    if (result.TryGetPropertyValue("__multi_control", out var mcNode) && mcNode is JsonArray mcArr) return mcArr;
    if (result.TryGetPropertyValue("__multi_command", out var mcmdNode) && mcmdNode is JsonArray mcmdArr) return mcmdArr;
    return new JsonArray { result };  // ← 默认返回单元素数组
}
```

`TemplateResolver.Resolve()`（第261-287行）只是合并模板定义和目标 JSON：

```csharp
public JsonObject Resolve(JsonObject target, string referenceName, ResolutionContext context)
{
    var templateDef = Pipeline.Resolve(referenceName);
    var result = templateDef.Definition.DeepClone().AsObject();
    JsonMergeService.DeepMerge(result, target, JsonArrayMergeMode.Replace);
    return result;
}
```

对于 `math-operation` 模板（`type: "command-group"`），合并后的结果是一个包含 `commands` 数组的对象，**但没有 `__multi_command` 标记**。因此 `ExpandItemTemplate()` 返回 `new JsonArray { result }`（单元素数组），`ProcessItems()` 将其替换回原位置，导致 `commands` 数组仍然嵌套在单个命令项内部。

### 最终效果

`ScriptToCommandConverter.Convert()` → `ScriptDefinition.ToCommandText()` 只处理顶层的 `commands` 数组，不会递归处理嵌套的 `commands`。所以最终生成的临时命令文件只包含脚本中最后 3 条没有使用模板的命令：

```
sleep 500
close Built-in Math Functions Test
exit
```

## 修复方案

### 方案 A：在 `TemplateResolver.Resolve()` 中添加 `__multi_command` 标记生成

在 `TemplateResolver.Resolve()` 中，检测到模板类型为 `command-group` 时，将 `commands` 数组转换为 `__multi_command` 标记。

**修改文件：** `FileDriven/FileDriven.JsonRefer/ReferenceProcessing/Resolvers.cs`

**修改位置：** `TemplateResolver.Resolve()` 方法，在 `JsonMergeService.DeepMerge` 之后

```csharp
public JsonObject Resolve(JsonObject target, string referenceName, ResolutionContext context)
{
    // ... 现有代码 ...
    var result = templateDef.Definition.DeepClone().AsObject();
    JsonMergeService.DeepMerge(result, target, JsonArrayMergeMode.Replace);

    // ★ 修复：检测 command-group 类型，生成 __multi_command 标记
    if (result.TryGetPropertyValue("type", out var typeNode) &&
        typeNode?.GetValue<string>() == "command-group" &&
        result.TryGetPropertyValue("commands", out var cmdsNode) &&
        cmdsNode is JsonArray cmdsArray)
    {
        result.Remove("type");
        result.Remove("commands");
        result["__multi_command"] = cmdsArray;
    }

    return result;
}
```

### 方案 B：在 `CommandsHandler.ExpandItemTemplate()` 中添加展开逻辑

在 `BaseStructureHandler.ExpandItemTemplate()` 中，检测到模板类型为 `command-group` 时，直接展开 `commands` 数组返回。

**修改文件：** `FileDriven/FileDriven.JsonRefer/ReferenceProcessing/Pipeline.cs`

**修改位置：** `BaseStructureHandler.ExpandItemTemplate()` 方法

```csharp
protected JsonArray? ExpandItemTemplate(JsonObject item, string templateName, ...)
{
    // ... 现有代码 ...
    var resolver = ReferenceResolverFactory.GetResolver(ReferenceType.Template);
    var result = resolver.Resolve(item, templateName, resolutionContext);

    // ★ 修复：检测 command-group 类型，直接展开 commands 数组
    if (result.TryGetPropertyValue("type", out var typeNode) &&
        typeNode?.GetValue<string>() == "command-group" &&
        result.TryGetPropertyValue("commands", out var cmdsNode) &&
        cmdsNode is JsonArray cmdsArray)
    {
        // 对每个命令应用占位符替换（参数已由 TemplateResolver 合并）
        return cmdsArray;
    }

    if (result.TryGetPropertyValue("__multi_control", out var mcNode) && mcNode is JsonArray mcArr) return mcArr;
    if (result.TryGetPropertyValue("__multi_command", out var mcmdNode) && mcmdNode is JsonArray mcmdArr) return mcmdArr;
    return new JsonArray { result };
}
```

### 方案 C：在 `ProcessItems()` 中递归处理嵌套的 `commands`

在 `BaseStructureHandler.ProcessItems()` 中，对展开后的每个子项，如果包含 `commands` 数组，递归展开。

**修改文件：** `FileDriven/FileDriven.JsonRefer/ReferenceProcessing/Pipeline.cs`

**修改位置：** `BaseStructureHandler.ProcessItems()` 方法

```csharp
protected void ProcessItems(JsonArray items, ErrorCollector errors, PipelineContext context,
    Action<JsonObject, ErrorCollector, PipelineContext>? processChild = null,
    string baseJsonPath = "$")
{
    // ... 现有代码 ...
    foreach (var (originalIndex, item) in entries)
    {
        // ... 现有 template 展开逻辑 ...
        if (item.ContainsKey("template"))
        {
            var expanded = ExpandItemTemplate(item, templateName, errors, context, itemPath);
            if (expanded != null && expanded.Count > 0)
            {
                var currentIndex = FindItemIndex(items, item);
                if (currentIndex < 0) continue;

                items.RemoveAt(currentIndex);
                for (int j = expanded.Count - 1; j >= 0; j--)
                {
                    var cloned = expanded[j]?.DeepClone();
                    if (cloned != null)
                        items.Insert(currentIndex, cloned);
                }

                // ★ 修复：递归处理所有展开项（不仅第一个）
                for (int k = 0; k < expanded.Count; k++)
                {
                    if (currentIndex + k < items.Count && items[currentIndex + k] is JsonNode node)
                    {
                        var expandedItem = node.AsObject();
                        ApplyStyleToItem(expandedItem, errors, context, itemPath);
                        processChild?.Invoke(expandedItem, errors, context);
                    }
                }
                continue;
            }
        }
        // ...
    }
}
```

### 推荐方案

**方案 A** 是最优选择，原因：
1. 修改集中在 `TemplateResolver.Resolve()` 中，影响范围最小
2. 与 `ExpandItemTemplate()` 中已有的 `__multi_control`/`__multi_command` 检查机制一致
3. 不需要修改 `ProcessItems()` 的遍历逻辑
4. 对 `command-group` 类型的处理与 `TemplateExpansionMiddleware` 中的逻辑等价

## 验证方法

1. 修改后重新构建 `FileDriven.JsonRefer` 项目
2. 运行 `math_functions_test.json` 测试脚本
3. 验证日志中显示的命令数量为 22 条（而非 3 条）
4. 验证测试通过率仍为 100%

## 影响范围

| 组件 | 影响 | 说明 |
|------|------|------|
| `FileDriven.JsonRefer` | ✅ 修改 | `TemplateResolver.Resolve()` 添加 `__multi_command` 标记生成 |
| `FileDriven.Launcher` | ❌ 无影响 | 仅使用 `ScriptConverter`，不直接调用 Resolver |
| `FileDriven.Pipeline` | ❌ 无影响 | 仅消费命令文件，不参与模板展开 |
| `BasicMethod.TemplateExpansion` | ❌ 无影响 | 旧路径保持不变 |
| 其他 `command-group` 模板 | ✅ 受益 | 所有 `type: command-group` 的模板都会正确展开 |
