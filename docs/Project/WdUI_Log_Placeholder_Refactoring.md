# WdUI 日志 PlaceholderFramework 改造方案

## 概述

使用 PlaceholderFramework 统一改写 WdUI 的日志体系，将所有日志提示文本集中到 JSON 文件中管理，代码中只插入占位符（Placeholder），由 PlaceholderFramework 统一解析和输出。

---

## 一、现状分析

### 1.1 当前日志模式（三种方式并存）

| 方式 | 使用场景 | 示例文件 |
|------|---------|---------|
| **Logger 直接输出** | 主流方式 | RenderPipeline.cs, WdUI.Container.cs, ApplyLayoutSkeleton.cs |
| **ILogger 通过 SkeletonContext** | Skeleton 中少量使用 | ConfigLoadSkeleton.cs, ContainerRenderSkeleton.cs |
| **System.Diagnostics.Debug.WriteLine** | 零散使用 | WinFormsEventBinder.cs, WinFormsCommandEngine.cs, WdUIComposer.cs |

### 1.2 日志分布统计

| 模块 | 文件 | 日志数量 | 日志级别 |
|------|------|---------|---------|
| RenderPipeline | WdUI.RenderPipeline.cs | 8 | Info |
| ConfigContainer | UI/WdUI.Container.cs | 14 | Info |
| ApplyLayout | Layout/Skeletons/ApplyLayoutSkeleton.cs | 3 | Info |
| TrackResize | Layout/Skeletons/TrackResizeSkeleton.cs | 3 | Debug |
| CreateLayoutPanel | Layout/Skeletons/CreateLayoutPanelSkeleton.cs | 2 | Info |
| BindEvents | Layout/Skeletons/BindEventsSkeleton.cs | 1 | Info |
| SplitContainerStrategy | Layout/Strategies/WdUI.Strategies.cs | 1 | Debug |
| ConfigLoadSkeleton | Layout/Skeletons/ConfigLoadSkeleton.cs | 4 | Info/Error |
| ContainerRenderSkeleton | Layout/Skeletons/ContainerRenderSkeleton.cs | 6 | Info/Error |
| PostProcessSkeleton | Layout/Skeletons/PostProcessSkeleton.cs | 3 | Info/Error |
| TemplatePreprocessSkeleton | Layout/Skeletons/TemplatePreprocessSkeleton.cs | 3 | Debug/Info/Warning |
| FrameworkModule | Framework/FrameworkModule.cs | 7 | Information/Error |
| WdUIComposer | UI/WdUIComposer.cs | 2 | Debug |
| DataBindingManager | UI/WdUI.Management.cs | 1 | Debug |
| AsyncLoadManager | UI/WdUI.Management.cs | 2 | Debug |
| WdUIEvents | UI/WdUI.Events.cs | 1 | Debug |
| WdUILayout | UI/WdUI.Layout.cs | 2 | Debug |
| ControlExtensions | UI/WdUI.Utilities.cs | 1 | Debug |
| WinFormsEventBinder | UI/Controls/WinFormsEventBinder.cs | 6 | Debug |
| WinFormsCommandEngine | UI/Commands/WinFormsCommandEngine.cs | 16 | Debug |
| **合计** | **21 个文件** | **~80 条** | |

---

## 二、改造方案

### 2.1 核心架构

```
┌─────────────────────────────────────────────────────────────────────┐
│                        改造后的 WdUI 日志体系                          │
│                                                                     │
│  代码中：ctx.SetPlaceholder("wdui:RenderPipeline_Render_start",       │
│            Placeholder.Input(new { targetName = target.Name }))      │
│                                                                     │
│                              ▼                                       │
│  ┌─────────────────────────────────────────────────────────────┐    │
│  │              PlaceholderResolverSkeleton                      │    │
│  │  遍历所有 Placeholder → 解析 → 分发到 Handler → 持久化       │    │
│  └──────────────────────┬──────────────────────────────────────┘    │
│                         │                                           │
│                         ▼                                           │
│  ┌─────────────────────────────────────────────────────────────┐    │
│  │              WdUILogPlaceholderHandler                        │    │
│  │  1. 从 Key 中提取消息 ID（去掉 "wdui:" 前缀）                 │    │
│  │  2. 从 JSON 中查找对应的日志模板                               │    │
│  │  3. 用 resolvedValue 中的参数填充模板                          │    │
│  │  4. 按 level/category 输出到 Logger                            │    │
│  └─────────────────────────────────────────────────────────────┘    │
│                         │                                           │
│                         ▼                                           │
│  ┌─────────────────────────────────────────────────────────────┐    │
│  │              wdui_log_messages.json                           │    │
│  │  {                                                            │    │
│  │    "RenderPipeline_Render_start": {                           │    │
│  │      "level": "Info",                                         │    │
│  │      "category": "WdUI_Render",                               │    │
│  │      "template": "[RenderPipeline] Render: {targetName} ..."  │    │
│  │    },                                                         │    │
│  │    ...                                                        │    │
│  │  }                                                            │    │
│  └─────────────────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────────────────┘
```

### 2.2 命名规范

使用 **`class_function_position`** 格式：

| 元素 | 格式 | 示例 |
|------|------|------|
| **JSON key** | `ClassName_MethodName_description` | `RenderPipeline_Render_start` |
| **Placeholder Key** | `wdui:ClassName_MethodName_description` | `wdui:RenderPipeline_Render_start` |
| **参数** | 命名占位符 `{paramName}` | `{targetName}`, `{viewCount}` |

---

## 三、JSON 消息文件

### 文件位置

`Share/WdUI/wdui_log_messages.json`

### 文件内容

```json
{
  "version": "1.0",
  "description": "WdUI 日志消息定义 - 使用 class_function_position 命名",
  "messages": {
    "RenderPipeline_Render_start": {
      "level": "Info",
      "category": "WdUI_Render",
      "template": "[RenderPipeline] Render: {targetName} (createFlow: TrackResize + CreateLayoutPanel)"
    },
    "RenderPipeline_ApplyLayout_no_session": {
      "level": "Info",
      "category": "WdUI_Render",
      "template": "[RenderPipeline] ApplyLayout: no session for '{targetName}', skip"
    },
    "RenderPipeline_ApplyLayout_start": {
      "level": "Info",
      "category": "WdUI_Render",
      "template": "[RenderPipeline] ApplyLayout: {targetName} ({viewCount} views)"
    },
    "RenderPipeline_Relayout_start": {
      "level": "Info",
      "category": "WdUI_Render",
      "template": "[RenderPipeline] Relayout: {targetName}"
    },
    "RenderPipeline_BindEvents_no_eventbinder": {
      "level": "Info",
      "category": "WdUI_Render",
      "template": "[RenderPipeline] ⚠ 未注册 IEventBindingService 事件绑定服务！所有 UI 事件绑定将被跳过。请确保 Program.cs 中正确注册了 IEventBindingService 实现（如 WinFormsEventBinder）。"
    },
    "RenderPipeline_BindEvents_no_eventbinder_en": {
      "level": "Info",
      "category": "WdUI_Render",
      "template": "[RenderPipeline] ⚠ IEventBindingService not registered, event binding skipped"
    },
    "RenderPipeline_BindEvents_no_session": {
      "level": "Info",
      "category": "WdUI_Render",
      "template": "[RenderPipeline] BindEvents: no session for '{targetName}', skip"
    },
    "RenderPipeline_BindEvents_start": {
      "level": "Info",
      "category": "WdUI_Render",
      "template": "[RenderPipeline] BindEvents: {targetName} ({viewCount} views)"
    },
    "ConfigContainer_OnHandleCreated_auto_load_failed": {
      "level": "Info",
      "category": "WdUI_Container",
      "template": "[ConfigContainer] AutoLoad failed for '{containerName}': {message}"
    },
    "ConfigContainer_LoadConfigAsync_template_processing": {
      "level": "Info",
      "category": "WdUI_Container",
      "template": "[{containerName}] TemplatePreprocessor is SET, processing: {configPath}"
    },
    "ConfigContainer_LoadConfigAsync_template_processed": {
      "level": "Info",
      "category": "WdUI_Container",
      "template": "[{containerName}] Template processed, using temp file: {tempFile}"
    },
    "ConfigContainer_LoadConfigAsync_template_failed": {
      "level": "Info",
      "category": "WdUI_Container",
      "template": "[{containerName}] TemplatePreprocessor FAILED for '{configPath}', using original: {message}"
    },
    "ConfigContainer_LoadConfigAsync_template_failed_alt": {
      "level": "Info",
      "category": "WdUI_Container",
      "template": "[ConfigContainer] TemplatePreprocessor failed for '{configPath}', using original: {message}"
    },
    "ConfigContainer_LoadConfigAsync_load_raw": {
      "level": "Info",
      "category": "WdUI_Container",
      "template": "[{containerName}] TemplatePreprocessor is NULL, loading raw: {configPath}"
    },
    "ConfigContainer_LoadConfigAsync_json_error": {
      "level": "Info",
      "category": "WdUI_Container",
      "template": "[ConfigContainer] JSON 反序列化失败: {message}"
    },
    "ConfigContainer_LoadConfigAsync_failed": {
      "level": "Info",
      "category": "WdUI_Container",
      "template": "[ConfigContainer] LoadConfigAsync FAILED: {exceptionType}: {message}"
    },
    "ConfigContainer_RenderConfigAsync_apply_layout_failed": {
      "level": "Info",
      "category": "WdUI_Container",
      "template": "[RenderConfigAsync] ApplyLayout failed: {exceptionType}: {message}"
    },
    "ConfigContainer_RenderConfigAsync_bind_events_failed": {
      "level": "Info",
      "category": "WdUI_Container",
      "template": "[RenderConfigAsync] BindEvents failed: {exceptionType}: {message}"
    },
    "ConfigContainer_RenderConfigAsync_stack_trace": {
      "level": "Info",
      "category": "WdUI_Container",
      "template": "[RenderConfigAsync] StackTrace: {stackTrace}"
    },
    "ConfigContainer_RegisterSubContainer_propagate_template": {
      "level": "Info",
      "category": "WdUI_Container",
      "template": "[{containerName}] Auto-propagated TemplatePreprocessor to sub-container '{subContainerName}'"
    },
    "ConfigContainer_RegisterSubContainer_no_template": {
      "level": "Info",
      "category": "WdUI_Container",
      "template": "[{containerName}] RegisterSubContainer('{subContainerName}') but _templatePreprocessor is NULL"
    },
    "ApplyLayoutSkeleton_ApplyLayoutRecursive_proportional": {
      "level": "Info",
      "category": "WdUI_Layout",
      "template": "[Layout] {controlName}: ({left},{top},{width},{height}) → {{{actualWidth},{actualHeight}}} @ ({actualLeft},{actualTop})"
    },
    "ApplyLayoutSkeleton_ApplyPostLayoutProperties_splitter": {
      "level": "Info",
      "category": "WdUI_Layout",
      "template": "[Layout] {splitterName}: PostLayout Orientation={orientation}, SplitterDistance={distance} ({ratio:P0} of {totalSize})"
    },
    "ApplyLayoutSkeleton_ApplySplitterDistance_set": {
      "level": "Info",
      "category": "WdUI_Layout",
      "template": "[Layout] {splitterName}: SplitterDistance={distance} ({ratio:P0} of {dimension})"
    },
    "TrackResizeSkeleton_ExecuteTrackResize_subscribed": {
      "level": "Debug",
      "category": "WdUI_Resize",
      "template": "[TrackResize] Subscribed: '{controlName}'"
    },
    "TrackResizeSkeleton_ResizeHandler_skip_apply_splitter": {
      "level": "Debug",
      "category": "WdUI_Resize",
      "template": "[TrackResize] Skipping Relayout for '{controlName}' because ApplySplitter is active"
    },
    "TrackResizeSkeleton_ResizeHandler_event_fired": {
      "level": "Debug",
      "category": "WdUI_Resize",
      "template": "[TrackResize] Resize event fired for '{controlName}' ({width}x{height})"
    },
    "CreateLayoutPanelSkeleton_CreateControlWithChildren_found_ancestor": {
      "level": "Info",
      "category": "WdUI_Render",
      "template": "[CreateLayoutPanel] Found ConfigContainer ancestor '{ancestorName}', registering container '{containerName}'"
    },
    "CreateLayoutPanelSkeleton_CreateControlWithChildren_no_ancestor": {
      "level": "Info",
      "category": "WdUI_Render",
      "template": "[CreateLayoutPanel] No ConfigContainer ancestor found for '{containerName}' (parent type: '{parentType}')"
    },
    "BindEventsSkeleton_ExecuteBindEvents_no_eventbinder": {
      "level": "Info",
      "category": "WdUI_Render",
      "template": "[BindEventsSkeleton] ⚠ IEventBindingService 未注册！事件绑定被跳过。请确保 Program.cs 中正确注册了 IEventBindingService 实现。"
    },
    "SplitContainerStrategy_SubscribeSplitterMoved_moved": {
      "level": "Debug",
      "category": "WdUI_Strategy",
      "template": "[SplitContainerStrategy] SplitterMoved '{splitterName}': ratio={ratio:F3}"
    },
    "ConfigLoadSkeleton_LoadConfigSync_loaded": {
      "level": "Info",
      "category": "WdUI_System",
      "template": "ConfigLoadSkeleton: loaded config from '{configPath}' ({viewCount} views)"
    },
    "ConfigLoadSkeleton_LoadConfigSync_failed": {
      "level": "Error",
      "category": "WdUI_System",
      "template": "ConfigLoadSkeleton: failed to load '{configPath}': {message}"
    },
    "ConfigLoadSkeleton_LoadConfigAsync_loaded": {
      "level": "Info",
      "category": "WdUI_System",
      "template": "ConfigLoadSkeleton: loaded config from '{configPath}' ({viewCount} views)"
    },
    "ConfigLoadSkeleton_LoadConfigAsync_failed": {
      "level": "Error",
      "category": "WdUI_System",
      "template": "ConfigLoadSkeleton: failed to load '{configPath}': {message}"
    },
    "ContainerRenderSkeleton_ExecuteRender_rendered": {
      "level": "Info",
      "category": "WdUI_System",
      "template": "ContainerRenderSkeleton: rendered '{containerName}' ({viewCount} views)"
    },
    "ContainerRenderSkeleton_ExecuteRender_render_failed": {
      "level": "Error",
      "category": "WdUI_System",
      "template": "ContainerRenderSkeleton: Render failed for '{containerName}': {message}"
    },
    "ContainerRenderSkeleton_ExecuteRender_apply_layout_completed": {
      "level": "Info",
      "category": "WdUI_System",
      "template": "ContainerRenderSkeleton: ApplyLayout completed for '{containerName}'"
    },
    "ContainerRenderSkeleton_ExecuteRender_apply_layout_failed": {
      "level": "Error",
      "category": "WdUI_System",
      "template": "ContainerRenderSkeleton: ApplyLayout failed for '{containerName}': {message}"
    },
    "ContainerRenderSkeleton_ExecuteRender_bind_events_completed": {
      "level": "Info",
      "category": "WdUI_System",
      "template": "ContainerRenderSkeleton: BindEvents completed for '{containerName}'"
    },
    "ContainerRenderSkeleton_ExecuteRender_bind_events_failed": {
      "level": "Error",
      "category": "WdUI_System",
      "template": "ContainerRenderSkeleton: BindEvents failed for '{containerName}': {message}"
    },
    "PostProcessSkeleton_ExecutePostProcess_callback_executed": {
      "level": "Info",
      "category": "WdUI_System",
      "template": "PostProcessSkeleton: custom callback executed for '{containerName}'"
    },
    "PostProcessSkeleton_ExecutePostProcess_callback_failed": {
      "level": "Error",
      "category": "WdUI_System",
      "template": "PostProcessSkeleton: custom callback failed for '{containerName}': {message}"
    },
    "PostProcessSkeleton_ExecutePostProcess_completed": {
      "level": "Info",
      "category": "WdUI_System",
      "template": "PostProcessSkeleton: completed for '{containerName}'"
    },
    "TemplatePreprocessSkeleton_ExecutePreprocess_no_processor": {
      "level": "Debug",
      "category": "WdUI_System",
      "template": "TemplatePreprocessSkeleton: no TemplateProcessor, using original path"
    },
    "TemplatePreprocessSkeleton_ExecutePreprocess_processed": {
      "level": "Info",
      "category": "WdUI_System",
      "template": "TemplatePreprocessSkeleton: processed '{configPath}' → '{tempFile}'"
    },
    "TemplatePreprocessSkeleton_ExecutePreprocess_failed": {
      "level": "Warning",
      "category": "WdUI_System",
      "template": "TemplatePreprocessSkeleton: failed for '{configPath}', using original: {message}"
    },
    "FrameworkLifecycle_Initialize_start": {
      "level": "Information",
      "category": "WdUI_System",
      "template": "框架开始初始化..."
    },
    "FrameworkLifecycle_Initialize_completed": {
      "level": "Information",
      "category": "WdUI_System",
      "template": "框架初始化完成"
    },
    "FrameworkLifecycle_Initialize_failed": {
      "level": "Error",
      "category": "WdUI_System",
      "template": "框架初始化失败"
    },
    "FrameworkLifecycle_RegisterModule_registered": {
      "level": "Information",
      "category": "WdUI_System",
      "template": "模块 {moduleName} 已注册"
    },
    "FrameworkLifecycle_Shutdown_start": {
      "level": "Information",
      "category": "WdUI_System",
      "template": "框架开始关闭..."
    },
    "FrameworkLifecycle_Shutdown_completed": {
      "level": "Information",
      "category": "WdUI_System",
      "template": "框架关闭完成"
    },
    "FrameworkLifecycle_Shutdown_failed": {
      "level": "Error",
      "category": "WdUI_System",
      "template": "框架关闭过程中发生错误"
    },
    "WdUIComposer_Shutdown_manager_failed": {
      "level": "Debug",
      "category": "WdUI_System",
      "template": "[WdUIComposer.Shutdown] Manager.Shutdown failed: {message}"
    },
    "WdUIComposer_Shutdown_host_failed": {
      "level": "Debug",
      "category": "WdUI_System",
      "template": "[WdUIComposer.Shutdown] FrameworkHost.Shutdown failed: {message}"
    },
    "DataBindingManager_UpdateControlValue_error": {
      "level": "Debug",
      "category": "WdUI_System",
      "template": "Error updating control value: {message}"
    },
    "ControlExtensions_SetControlValue_error": {
      "level": "Debug",
      "category": "WdUI_System",
      "template": "Error setting control value: {message}"
    },
    "AsyncLoadManager_LoadAndWaitAsyncCore_load_failed": {
      "level": "Debug",
      "category": "WdUI_System",
      "template": "[AsyncLoadManager] Load failed: {exceptionType}: {message}"
    },
    "AsyncLoadManager_LoadAndWaitAsyncCore_stack_trace": {
      "level": "Debug",
      "category": "WdUI_System",
      "template": "[AsyncLoadManager] StackTrace: {stackTrace}"
    },
    "WdUIEvents_Subscribe_async_handler_error": {
      "level": "Debug",
      "category": "WdUI_System",
      "template": "[WdUIEvents] Async handler error for '{eventName}': {message}"
    },
    "WdUILayout_EnableRuntimeOverlapMonitoring_overlap": {
      "level": "Debug",
      "category": "WdUI_System",
      "template": "[运行时重叠监控] {summary}"
    },
    "WdUILayout_EnableRuntimeOverlapMonitoring_overlap_detail": {
      "level": "Debug",
      "category": "WdUI_System",
      "template": "  {overlap}"
    },
    "WinFormsEventBinder_BindClickEvent_no_mediator": {
      "level": "Debug",
      "category": "WdUI_System",
      "template": "[WinFormsEventBinder] ⚠ 按钮 '{buttonName}' 的 Click 事件绑定了 '{functions}'，但未注册 IMediator 或 IEventBus，函数无法执行！"
    },
    "WinFormsEventBinder_BindEvent_error": {
      "level": "Debug",
      "category": "WdUI_System",
      "template": "[EventBinder] {eventType} error ({functionName}): {message}"
    },
    "WinFormsCommandEngine_RegisterSetCommand_not_found": {
      "level": "Debug",
      "category": "WdUI_System",
      "template": "[ERROR] Control '{controlName}' not found"
    },
    "WinFormsCommandEngine_RegisterSetCommand_ok": {
      "level": "Debug",
      "category": "WdUI_System",
      "template": "[OK] Set {controlName} = {value}"
    },
    "WinFormsCommandEngine_RegisterGetCommand_ok": {
      "level": "Debug",
      "category": "WdUI_System",
      "template": "[OK] {controlName} = {value}"
    },
    "WinFormsCommandEngine_RegisterClickCommand_ok": {
      "level": "Debug",
      "category": "WdUI_System",
      "template": "[OK] Clicked {controlName}"
    },
    "WinFormsCommandEngine_RegisterClickCommand_not_clickable": {
      "level": "Debug",
      "category": "WdUI_System",
      "template": "[ERROR] Control '{controlName}' is not clickable"
    },
    "WinFormsCommandEngine_RegisterListCommand_result": {
      "level": "Debug",
      "category": "WdUI_System",
      "template": "{result}"
    },
    "WinFormsCommandEngine_RegisterShowCommand_all": {
      "level": "Debug",
      "category": "WdUI_System",
      "template": "[OK] Showed all forms"
    },
    "WinFormsCommandEngine_RegisterShowCommand_ok": {
      "level": "Debug",
      "category": "WdUI_System",
      "template": "[OK] Showed form '{formName}'"
    },
    "WinFormsCommandEngine_RegisterShowCommand_not_found": {
      "level": "Debug",
      "category": "WdUI_System",
      "template": "[ERROR] Form '{formName}' not found"
    },
    "WinFormsCommandEngine_RegisterHideCommand_all": {
      "level": "Debug",
      "category": "WdUI_System",
      "template": "[OK] Hidden all forms"
    },
    "WinFormsCommandEngine_RegisterHideCommand_ok": {
      "level": "Debug",
      "category": "WdUI_System",
      "template": "[OK] Hidden form '{formName}'"
    },
    "WinFormsCommandEngine_RegisterHideCommand_not_found": {
      "level": "Debug",
      "category": "WdUI_System",
      "template": "[ERROR] Form '{formName}' not found"
    },
    "WinFormsCommandEngine_RegisterCloseCommand_exit": {
      "level": "Debug",
      "category": "WdUI_System",
      "template": "[OK] Application exiting"
    },
    "WinFormsCommandEngine_RegisterCloseCommand_ok": {
      "level": "Debug",
      "category": "WdUI_System",
      "template": "[OK] Closed form '{formName}'"
    },
    "WinFormsCommandEngine_RegisterCloseCommand_not_found": {
      "level": "Debug",
      "category": "WdUI_System",
      "template": "[ERROR] Form '{formName}' not found"
    }
  }
}
```

---

## 四、WdUILogPlaceholderHandler 实现

### 文件位置

`Share/PlaceholderFramework/Handlers/WdUILogPlaceholderHandler.cs`

### 代码设计

```csharp
using System.Text.Json;
using System.Text.RegularExpressions;
using Graphkey.Core;
using SkeletonFlow;
using Common.Diagnostics;

namespace Common.PlaceholderFramework.Handlers
{
    /// <summary>
    /// WdUI 日志 Placeholder Handler
    /// 匹配 wdui: 前缀的 Placeholder，从 JSON 中查找日志模板并输出
    /// </summary>
    public class WdUILogPlaceholderHandler : IPlaceholderHandler
    {
        public string Name => "WdUILog";

        private static Dictionary<string, LogMessageDef>? _messages;
        private static readonly object _loadLock = new();
        private static readonly string _jsonPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..",
            "Share", "WdUI", "wdui_log_messages.json");

        private readonly Logger _logger;

        public WdUILogPlaceholderHandler()
        {
            _logger = new Logger(moduleName: "WdUILogPlaceholderHandler");
        }

        public bool CanHandle(Placeholder placeholder)
            => placeholder.Key?.StartsWith("wdui:") == true;

        public void Handle(Placeholder placeholder, string resolvedValue,
                           SkeletonContext context, string batchId)
        {
            // 1. 从 Key 中提取消息 ID（去掉 "wdui:" 前缀）
            var msgId = placeholder.Key.Substring(5);

            // 2. 从 JSON 中查找消息定义
            if (!TryGetMessage(msgId, out var msgDef)) return;

            // 3. 解析 resolvedValue 中的参数
            Dictionary<string, string>? parameters = null;
            if (!string.IsNullOrEmpty(resolvedValue))
            {
                try
                {
                    parameters = JsonSerializer.Deserialize<Dictionary<string, string>>(resolvedValue);
                }
                catch { }
            }

            // 4. 填充模板
            var message = FormatTemplate(msgDef.Template, parameters);

            // 5. 输出到 Logger
            LogWithLevel(msgDef.Level, msgDef.Category, message);
        }

        private bool TryGetMessage(string msgId, out LogMessageDef msgDef)
        {
            EnsureMessagesLoaded();
            return _messages?.TryGetValue(msgId, out msgDef) ?? false;
        }

        private void EnsureMessagesLoaded()
        {
            if (_messages != null) return;

            lock (_loadLock)
            {
                if (_messages != null) return;

                try
                {
                    var json = File.ReadAllText(_jsonPath);
                    var doc = JsonDocument.Parse(json);
                    var messages = new Dictionary<string, LogMessageDef>();

                    foreach (var prop in doc.RootElement.GetProperty("messages").EnumerateObject())
                    {
                        var msg = prop.Value;
                        messages[prop.Name] = new LogMessageDef
                        {
                            Level = msg.GetProperty("level").GetString(),
                            Category = msg.GetProperty("category").GetString(),
                            Template = msg.GetProperty("template").GetString()
                        };
                    }

                    _messages = messages;
                }
                catch (Exception ex)
                {
                    _logger.Error(LogCategory.WdUI_System,
                        $"[WdUILogPlaceholderHandler] Failed to load log messages JSON: {ex.Message}");
                    _messages = new Dictionary<string, LogMessageDef>();
                }
            }
        }

        private string FormatTemplate(string template, Dictionary<string, string>? parameters)
        {
            if (parameters == null || parameters.Count == 0)
                return template;

            // 替换 {paramName} 占位符
            return Regex.Replace(template, @"\{(\w+)\}", match =>
            {
                var key = match.Groups[1].Value;
                return parameters.TryGetValue(key, out var value) ? value : match.Value;
            });
        }

        private void LogWithLevel(string level, string category, string message)
        {
            var logCategory = category switch
            {
                "WdUI_Render" => LogCategory.WdUI_Render,
                "WdUI_Container" => LogCategory.WdUI_Container,
                "WdUI_Layout" => LogCategory.WdUI_Layout,
                "WdUI_Resize" => LogCategory.WdUI_Resize,
                "WdUI_Strategy" => LogCategory.WdUI_Strategy,
                "WdUI_System" => LogCategory.WdUI_System,
                _ => LogCategory.WdUI_System
            };

            switch (level?.ToLower())
            {
                case "debug":
                    _logger.Debug(logCategory, message);
                    break;
                case "warning":
                case "warn":
                    _logger.Warn(logCategory, message);
                    break;
                case "error":
                    _logger.Error(logCategory, message);
                    break;
                default:
                    _logger.Info(logCategory, message);
                    break;
            }
        }

        private class LogMessageDef
        {
            public string Level { get; set; }
            public string Category { get; set; }
            public string Template { get; set; }
        }
    }
}
```

---

## 五、代码改造示例

### 5.1 RenderPipeline.cs

**改造前：**
```csharp
_logger.Info(LogCategory.WdUI_Render,
    $"[RenderPipeline] Render: {target.Name} (createFlow: TrackResize + CreateLayoutPanel)");
```

**改造后：**
```csharp
ctx.SetPlaceholder("wdui:RenderPipeline_Render_start",
    Placeholder.Input(JsonSerializer.Serialize(new { targetName = target.Name })));
```

### 5.2 ConfigContainer.cs

**改造前：**
```csharp
_logger.Info(LogCategory.WdUI_Container,
    $"[ConfigContainer] AutoLoad failed for '{Name}': {ex.Message}");
```

**改造后：**
```csharp
ctx.SetPlaceholder("wdui:ConfigContainer_OnHandleCreated_auto_load_failed",
    Placeholder.Input(JsonSerializer.Serialize(new
    {
        containerName = Name,
        message = ex.Message
    })));
```

### 5.3 ApplyLayoutSkeleton.cs

**改造前：**
```csharp
_logger.Info(LogCategory.WdUI_Layout,
    $"[Layout] {cfg.Name}: ({cfg.Layout.Left},{cfg.Layout.Top},{cfg.Layout.Width},{cfg.Layout.Height}) → {{{control.Width},{control.Height}}} @ ({control.Left},{control.Top})");
```

**改造后：**
```csharp
ctx.SetPlaceholder("wdui:ApplyLayoutSkeleton_ApplyLayoutRecursive_proportional",
    Placeholder.Input(JsonSerializer.Serialize(new
    {
        controlName = cfg.Name,
        left = cfg.Layout.Left,
        top = cfg.Layout.Top,
        width = cfg.Layout.Width,
        height = cfg.Layout.Height,
        actualWidth = control.Width,
        actualHeight = control.Height,
        actualLeft = control.Left,
        actualTop = control.Top
    })));
```

### 5.4 ConfigLoadSkeleton.cs（ILogger 方式）

**改造前：**
```csharp
logger?.LogInfo($"ConfigLoadSkeleton: loaded config from '{configPath}' ({config.Views?.Count ?? 0} views)");
```

**改造后：**
```csharp
ctx.SetPlaceholder("wdui:ConfigLoadSkeleton_LoadConfigAsync_loaded",
    Placeholder.Input(JsonSerializer.Serialize(new
    {
        configPath = configPath,
        viewCount = config.Views?.Count ?? 0
    })));
```

### 5.5 WinFormsCommandEngine.cs（Debug.WriteLine 方式）

**改造前：**
```csharp
Debug.WriteLine($"[ERROR] Control '{controlName}' not found");
```

**改造后：**
```csharp
ctx.SetPlaceholder("wdui:WinFormsCommandEngine_RegisterSetCommand_not_found",
    Placeholder.Input(JsonSerializer.Serialize(new { controlName = controlName })));
```

---

## 六、改造工作量清单

| # | 文件 | 操作 | 日志点 | 改造方式 |
|---|------|------|--------|---------|
| 1 | `Share/WdUI/wdui_log_messages.json` | **新建** | ~80 条日志定义 | - |
| 2 | `Share/PlaceholderFramework/Handlers/WdUILogPlaceholderHandler.cs` | **新建** | Handler 实现 | - |
| 3 | `Share/WdUI/WdUI.RenderPipeline.cs` | 改造 | 8 处 | Logger → Placeholder |
| 4 | `Share/WdUI/UI/WdUI.Container.cs` | 改造 | 14 处 | Logger → Placeholder |
| 5 | `Share/WdUI/Layout/Skeletons/ApplyLayoutSkeleton.cs` | 改造 | 3 处 | Logger → Placeholder |
| 6 | `Share/WdUI/Layout/Skeletons/TrackResizeSkeleton.cs` | 改造 | 3 处 | Logger → Placeholder |
| 7 | `Share/WdUI/Layout/Skeletons/CreateLayoutPanelSkeleton.cs` | 改造 | 2 处 | Logger → Placeholder |
| 8 | `Share/WdUI/Layout/Skeletons/BindEventsSkeleton.cs` | 改造 | 1 处 | Logger → Placeholder |
| 9 | `Share/WdUI/Layout/Strategies/WdUI.Strategies.cs` | 改造 | 1 处 | Logger → Placeholder |
| 10 | `Share/WdUI/Layout/Skeletons/ConfigLoadSkeleton.cs` | 改造 | 4 处 | ILogger → Placeholder |
| 11 | `Share/WdUI/Layout/Skeletons/ContainerRenderSkeleton.cs` | 改造 | 6 处 | ILogger → Placeholder |
| 12 | `Share/WdUI/Layout/Skeletons/PostProcessSkeleton.cs` | 改造 | 3 处 | ILogger → Placeholder |
| 13 | `Share/WdUI/Layout/Skeletons/TemplatePreprocessSkeleton.cs` | 改造 | 3 处 | ILogger → Placeholder |
| 14 | `Share/WdUI/Framework/FrameworkModule.cs` | 改造 | 7 处 | ILogger → Placeholder |
| 15 | `Share/WdUI/UI/WdUIComposer.cs` | 改造 | 2 处 | Debug → Placeholder |
| 16 | `Share/WdUI/UI/WdUI.Management.cs` | 改造 | 3 处 | Debug → Placeholder |
| 17 | `Share/WdUI/UI/WdUI.Events.cs` | 改造 | 1 处 | Debug → Placeholder |
| 18 | `Share/WdUI/UI/WdUI.Layout.cs` | 改造 | 2 处 | Debug → Placeholder |
| 19 | `Share/WdUI/UI/WdUI.Utilities.cs` | 改造 | 1 处 | Debug → Placeholder |
| 20 | `Share/WdUI/UI/Controls/WinFormsEventBinder.cs` | 改造 | 6 处 | Debug → Placeholder |
| 21 | `Share/WdUI/UI/Commands/WinFormsCommandEngine.cs` | 改造 | 16 处 | Debug → Placeholder |
| **合计** | **21 个文件** | | **~80 条** | |

---

## 七、实施步骤

### 步骤 1：创建 JSON 消息文件
- 新建 `Share/WdUI/wdui_log_messages.json`
- 包含全部 ~80 条日志定义

### 步骤 2：创建 WdUILogPlaceholderHandler
- 新建 `Share/PlaceholderFramework/Handlers/WdUILogPlaceholderHandler.cs`
- 实现 `IPlaceholderHandler` 接口
- 自动加载 JSON 文件，按 key 查找模板并输出

### 步骤 3：改造 Logger 方式日志（8 个文件）
- `WdUI.RenderPipeline.cs`（8 处）
- `WdUI.Container.cs`（14 处）
- `ApplyLayoutSkeleton.cs`（3 处）
- `TrackResizeSkeleton.cs`（3 处）
- `CreateLayoutPanelSkeleton.cs`（2 处）
- `BindEventsSkeleton.cs`（1 处）
- `WdUI.Strategies.cs`（1 处）

### 步骤 4：改造 ILogger 方式日志（5 个文件）
- `ConfigLoadSkeleton.cs`（4 处）
- `ContainerRenderSkeleton.cs`（6 处）
- `PostProcessSkeleton.cs`（3 处）
- `TemplatePreprocessSkeleton.cs`（3 处）
- `FrameworkModule.cs`（7 处）

### 步骤 5：改造 Debug.WriteLine 方式日志（8 个文件）
- `WdUIComposer.cs`（2 处）
- `WdUI.Management.cs`（3 处）
- `WdUI.Events.cs`（1 处）
- `WdUI.Layout.cs`（2 处）
- `WdUI.Utilities.cs`（1 处）
- `WinFormsEventBinder.cs`（6 处）
- `WinFormsCommandEngine.cs`（16 处）

### 步骤 6：在 Pipeline 中注册 Handler
- 在 WdUI 的 Pipeline 组装处注册 `WdUILogPlaceholderHandler`

### 步骤 7：验证
- 编译项目，确保无错误
- 运行测试，验证日志输出正确

---

## 八、关键设计决策

| 决策 | 选择 | 理由 |
|------|------|------|
| **JSON 命名** | `class_function_position` | 清晰标识日志来源位置 |
| **Placeholder 前缀** | `wdui:` | 与现有 `log:`、`order:`、`debug:` 前缀区分 |
| **模板占位符** | 命名占位符 `{paramName}` | 提高可读性，避免数字占位符混淆 |
| **Handler 位置** | `PlaceholderFramework/Handlers/` | 与现有 Handler 统一管理 |
| **JSON 文件位置** | `Share/WdUI/` | 与 WdUI 代码放在一起，便于维护 |
| **异常堆栈** | 保留直接 Logger 输出 | 堆栈信息不适合模板化 |
| **向后兼容** | 保留 `_logger` 字段 | 用于非模板化日志（如异常堆栈） |
