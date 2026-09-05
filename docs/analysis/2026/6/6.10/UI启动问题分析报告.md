# UI 启动问题分析报告

## 问题描述

执行 `EnableMent/UI启动.bat` 后，窗口打开但只显示 ConfigSelector（lodconfig），其他所有 UI 控件（工具栏、终端面板、侧边栏等）都没有加载。

## 启动流程

```
UI启动.bat
  └─ FileDriven.Launcher.exe --show-gui --file "init_only.txt"
       └─ Program.Main()
            ├─ FrameworkInitializer.InitializeTemplateEngine()  ← 初始化模板引擎
            ├─ FrameworkInitializer.InitializeAll()              ← 初始化所有组件
            └─ GuiModeRunner.Run(options)
                 ├─ new MainForm()                               ← 创建主窗口
                 ├─ ResolveCommandFile("init_only.txt")          ← 解析脚本文件
                 │    └─ PathResolver.ResolveConfigPath()        ← 在 JsonConfig 中查找 → 不存在
                 │    └─ 回退到 Scripts 目录查找                 ← 存在！返回路径
                 ├─ mainForm.InitializationCompleted += ...      ← 注册初始化完成事件
                 └─ Application.Run(mainForm)                    ← 启动消息循环
```

## 关键代码路径

### 1. MainForm 初始化

```csharp
// MainForm.cs - 构造函数
public MainForm()
{
    // ...
    this.Load += async (s, e) => await OnInitializeAsync();
}

private async Task OnInitializeAsync()
{
    // 1. 加载 MainLayout.json
    await _rootContainer.LoadConfigAsync(MainLayoutPath);
    // 2. OnRootContainerLoaded 回调中：
    //    - 获取 contentContainer、sideContainer 引用
    //    - 调用 LoadConfigSelectorAsync()
    // 3. LoadConfigSelectorAsync() 加载 ConfigSelector.json
    await _contentContainer.LoadConfigAsync(ConfigSelectorPath);
}
```

### 2. 配置加载管线

```
ConfigContainer.LoadConfigAsync(path)
  └─ WdUIJson.LoadConfigAsync(path)
       └─ Serializer.DeserializeFromFileAsync<UIConfiguration>(path)
            └─ UnifiedJsonSerializer.Deserialize<T>(json, options)
                 ├─ Preprocessor.Preprocess(json)    ← 预处理管线
                 │    ├─ Step 1: ReferenceExpansion   ← 展开 @ref 引用
                 │    └─ Step 2: JsonTemplateProcessor.Process(json)
                 │         ├─ FileDrivenAdapter.ConvertBareNamesToPrefixed(doc)
                 │         │    ← "template": "toolbar-button" → "template": "@template.toolbar-button"
                 │         │    ← "style": "dark-panel"       → "style": "@style.dark-panel"
                 │         └─ JsonPipelineRegistry.Process(serialized)
                 │              ├─ UseExtends        ← 继承展开
                 │              ├─ UseStyleApply     ← 样式应用
                 │              ├─ UseTemplateExpand ← 模板展开
                 │              ├─ UseDefaultsApply  ← 默认值应用
                 │              ├─ UseParameterResolve ← 参数解析
                 │              └─ UseValidation     ← 验证
                 └─ JsonSerializer.Deserialize<T>(json, options)  ← 反序列化为 UIConfiguration
```

## 问题根因分析

### 根因 1：预处理步骤未注册（最可能的原因）

`UnifiedJsonSerializer.Preprocessor` 是一个静态实例，预处理步骤需要在 `FrameworkInitializer.InitializeTemplateEngine()` 中注册。

检查 `FrameworkInitializer.cs` 中是否调用了类似以下代码来注册预处理步骤：

```csharp
// 预期注册代码（可能在 FrameworkInitializer 中缺失）
UnifiedJsonSerializer.Preprocessor.RegisterStep(
    json => JsonTemplateProcessor.Process(json),
    "ReferenceExpansion");
```

**如果 `ReferenceExpansion` 步骤没有注册，那么 `MainLayout.json` 中的 `template` 和 `style` 引用就不会被展开，导致反序列化后的 `UIConfiguration` 对象中缺少这些模板展开后的控件。**

### 根因 2：模板文件不存在

`TemplateConfiguration.InitializeAll()` 检查模板文件是否存在：

```
FileDriven/DrivenConfig/TempletConfig/JsonConfig/styles.json
FileDriven/DrivenConfig/TempletConfig/JsonConfig/composites.json
```

如果这些文件不存在，`JsonPipelineRegistry.Configure()` 不会被调用，管线不会初始化。

### 根因 3：ConvertBareNamesToPrefixed 与管线执行顺序问题

`FileDrivenAdapter.ConvertBareNamesToPrefixed` 将裸名转换为 `@` 前缀格式：
- `"template": "toolbar-button"` → `"template": "@template.toolbar-button"`
- `"style": "dark-panel"` → `"style": "@style.dark-panel"`

然后 `StyleApplyConverter` 和 `TemplateExpandConverter` 通过 `TemplateExpansionReferencePrefix.ResolveStyleName()` 和 `ResolveTemplateName()` 解析这些引用。

**如果 `ConvertBareNamesToPrefixed` 执行了但管线没有执行，或者管线执行了但 `ConvertBareNamesToPrefixed` 没有执行，都会导致模板/样式无法展开。**

### 根因 4：TraverseTopLevel 未覆盖 panel1Controls/panel2Controls

`TemplateExpandConverter` 使用 `JsonPipelineHelper.TraverseTopLevel` 遍历 JSON 树，该方法只处理：
- `views` → `controls`
- `steps`
- `commands`

但 `MainLayout.json` 中的 `template` 引用位于：
```
views[0].controls[1].panel1Controls[0].controls[0].panel1Controls[0]  ← contentContainer
views[0].controls[1].panel1Controls[0].controls[0].panel2Controls[0].controls[0]  ← toolbarPanel.controls
```

**`TraverseTopLevel` 只处理了顶层 `views` 中的 `controls`，而 `ProcessControls` 递归处理子控件时使用了 `ChildControlKeys`（`controls`、`panel1Controls`、`panel2Controls`）。**

**但 `TraverseTopLevel` 的入口只处理了 `views` → `controls`，没有处理 `views` → `controls` → `panel1Controls` 和 `panel2Controls` 中的 `template` 引用！**

实际上 `ProcessControls` 会递归处理子控件，所以 `panel1Controls` 和 `panel2Controls` 中的 `template` 引用应该能被处理。但需要确认 `TraverseTopLevel` 是否正确遍历到了这些层级。

### 根因 5：StyleApplyConverter 的 ApplyStylesRecursive 遍历不完整

`StyleApplyConverter.ApplyStylesRecursive` 方法遍历了 `ChildControlKeys`（`controls`、`panel1Controls`、`panel2Controls`），但 `MainLayout.json` 中的 `style` 引用位于：
- `toolbarPanel` → `style: "toolbar-panel"`（在 `controls[0]` 中）
- `terminalPanel` → `style: "dark-panel"`（在 `panel2Controls[0]` 中）
- `outputTextBox` → `style: "terminal-textbox"`（在 `panel2Controls[0].controls[0]` 中）
- `commandBar` → `style: "dark-panel"`（在 `panel2Controls[0].controls[1]` 中）
- `executeButton` → `style: "primary-button"`（在 `panel2Controls[0].controls[1].controls[2]` 中）

这些 `style` 引用应该能被 `ApplyStylesRecursive` 正确处理，因为它递归遍历了所有 `ChildControlKeys`。

## 修复方案

### 方案 1：确认预处理步骤已注册

检查 `FrameworkInitializer.cs` 中是否包含以下注册代码：

```csharp
// 在 InitializeTemplateEngine() 或类似方法中
UnifiedJsonSerializer.Preprocessor.RegisterStep(
    json => JsonTemplateProcessor.Process(json),
    "ReferenceExpansion");
```

如果缺失，添加此注册代码。

### 方案 2：确认模板文件存在

检查以下文件是否存在：
- `FileDriven/DrivenConfig/TempletConfig/JsonConfig/styles.json`
- `FileDriven/DrivenConfig/TempletConfig/JsonConfig/composites.json`

如果不存在，创建它们或从备份恢复。

### 方案 3：简化 UI启动.bat

移除 `--file "init_only.txt"` 参数，因为 `init_only.txt` 只是一个空脚本（仅 `echo "初始化完成"`），其唯一目的是触发预处理管线就绪。但 `FrameworkInitializer.InitializeTemplateEngine()` 已经在 `Program.Main()` 中执行了，预处理管线已经就绪。

```batch
"%LAUNCHER_DIR%\FileDriven.Launcher.exe" --show-gui
```

### 方案 4：添加诊断日志

在 `MainForm.OnRootContainerLoaded` 中添加诊断日志，输出 `_rootContainer` 的子控件数量和类型，确认 `MainLayout.json` 是否被正确加载和展开。

```csharp
// 在 OnRootContainerLoaded 中添加
var allControls = ControlFinder.FindAllByType<Control>(_rootContainer);
FileDrivenLogPlaceholderHandler.Log("MainForm_OnRootContainerLoaded_diagnostic",
    new { 
        controlCount = allControls.Count,
        controlTypes = string.Join(", ", allControls.Select(c => $"{c.Name}({c.GetType().Name})"))
    });
```

### 方案 5：检查 JsonTemplateProcessor.Process 的调用链

确认 `JsonTemplateProcessor.Process` 是否被正确调用，以及 `JsonPipelineRegistry.Process` 是否返回了正确的结果。

```csharp
// 在 JsonTemplateProcessor.Process 中添加日志
var result = JsonPipelineRegistry.Process(serialized);
FileDrivenLogPlaceholderHandler.Log("JsonTemplateProcessor_Process_result",
    new { 
        success = result.Success,
        errors = string.Join("; ", result.Errors?.Select(e => e.Message) ?? Array.Empty<string>())
    });
```

## 诊断步骤

1. **检查模板文件是否存在**：
   ```
   dir FileDriven\DrivenConfig\TempletConfig\JsonConfig\*.json
   ```

2. **检查预处理步骤是否注册**：
   在 `FrameworkInitializer.cs` 中搜索 `Preprocessor.RegisterStep` 或 `ReferenceExpansion`。

3. **检查 JsonPipelineRegistry 是否已配置**：
   在 `TemplateConfiguration.InitializeAll()` 中添加断点，确认 `JsonPipelineRegistry.IsConfigured` 为 `true`。

4. **检查 MainLayout.json 加载日志**：
   查看 `FileDrivenLogPlaceholderHandler` 输出的日志，确认 `MainLayout.json` 是否被成功加载和反序列化。

5. **检查反序列化后的控件结构**：
   在 `OnRootContainerLoaded` 中输出 `_rootContainer` 的控件树，确认 `template` 和 `style` 引用是否被正确展开。
