#nullable enable

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using FrameworkAbstractions.ComponentRegistry;
using FrameworkAbstractions.WinFormsAbstractions;
using FrontEndDriven.Diagnostics;
using FrontEndDriven.Shared.Catalog;
using FrontEndDriven.WinForms.Paint;
using Modules.Registry;
using Share.EventBindingOrchestrator.Execution;
using TConfig;
using UiPathManager = FrontEndDriven.Shared.Utils.PathManager;

namespace FrontEndDriven;

/// <summary>
/// 通用命令库：把「输入 + 执行」抽象为命令——选模板(catalog id) + JSON 输入(payload)
/// → 组装请求体 → 调用执行器(executor) → 返回输出。命令为通用能力，不绑定具体领域/执行器/目录。
/// 「JSON 执行」即本库的一条命令（<c>Ui_RunJsonCommand</c> / 命令行别名 <c>runJson</c>），
/// 可经 commandBar（Execute）或 exec DSL（lifecycle/initCommands）以命令方式调用。
/// </summary>
[FunctionLibrary]
public static class UiCommandLibrary
{
    /// <summary>缺省执行器（宿主经 <see cref="Configure"/> 注入；默认值 = 既有装配缺省，属登记债务 A1-DEBT）。</summary>
    public static string DefaultExecutor { get; private set; } = "AgentUi_RunJson";

    /// <summary>宿主装配期覆盖缺省执行器（消除通用命令库对具体插件函数的硬编码）。</summary>
    public static void Configure(string? defaultExecutor)
    {
        if (!string.IsNullOrWhiteSpace(defaultExecutor))
            DefaultExecutor = defaultExecutor.Trim();
    }

    /// <summary>空模板占位（表示"无模板，仅用输入"）。</summary>
    public const string EmptyTemplate = "-";

    public static void RegisterAll(ITrConfigRegistry registry)
    {
        registry.Register("Ui_BuildRequest",
            (Func<string, string, Task<string>>)((template, input) => Task.FromResult(BuildRequest(template, input))),
            new IParamConfig[]
            {
                new ParameterDescriptor { Name = "template", Type = typeof(string) },
                new ParameterDescriptor { Name = "input", Type = typeof(string) },
            },
            "通用：模板 id（分类/文件名，- = 空模板）+ JSON 输入 → 请求体文本（输入对象合并覆盖模板，纯文本包为 input 键）");

        registry.Register("Ui_RunRequest",
            (Func<string, Task<string>>)(requestJson => string.IsNullOrWhiteSpace(DefaultExecutor)
                ? Task.FromResult("未装配缺省执行器：宿主须调用 UiCommandLibrary.Configure(<executor>)")
                : RunRequestAsync(requestJson, DefaultExecutor)),
            new IParamConfig[]
            {
                new ParameterDescriptor { Name = "requestJson", Type = typeof(string) },
            },
            "通用：请求体文本 → 缺省执行器（宿主配置）执行，返回输出");

        registry.Register("Ui_RunRequestEx",
            (Func<string, string, Task<string>>)((requestJson, executor) => RunRequestAsync(requestJson, executor)),
            new IParamConfig[]
            {
                new ParameterDescriptor { Name = "requestJson", Type = typeof(string) },
                new ParameterDescriptor { Name = "executor", Type = typeof(string) },
            },
            "通用：请求体文本 → 指定执行器函数执行，返回输出");

        // JSON 执行命令：选模板 + JSON 输入 → 运行（包装 JSON 执行）。
        var runJson = (Func<string, string, Task<string>>)((template, input) => RunCommandAsync(template, input, DefaultExecutor));
        var runJsonParams = new IParamConfig[]
        {
            new ParameterDescriptor { Name = "template", Type = typeof(string) },
            new ParameterDescriptor { Name = "input", Type = typeof(string) },
        };
        registry.Register("Ui_RunJsonCommand", runJson, runJsonParams,
            $"命令：选模板 + JSON 输入 → 运行（= Ui_BuildRequest → Ui_RunRequest；JSON 执行命令，执行器 {DefaultExecutor}）");
        registry.Register("runJson", runJson, runJsonParams,
            "Ui_RunJsonCommand 的命令行别名");

        registry.Register("Ui_RunCommand",
            (Func<string, string, string, Task<string>>)((template, input, executor) => RunCommandAsync(template, input, executor)),
            new IParamConfig[]
            {
                new ParameterDescriptor { Name = "template", Type = typeof(string) },
                new ParameterDescriptor { Name = "input", Type = typeof(string) },
                new ParameterDescriptor { Name = "executor", Type = typeof(string) },
            },
            "命令：选模板 + JSON 输入 + 指定执行器 → 运行（通用命令形态）");

        registry.Register("Ui_CommandFromTemplate",
            (Func<string, string, string>)((template, paramControl) => BuildCommandLine(template, paramControl)),
            new IParamConfig[]
            {
                new ParameterDescriptor { Name = "template", Type = typeof(string) },
                new ParameterDescriptor { Name = "paramControl", Type = typeof(string) },
            },
            "生成命令文本：`runJson template=<id> input=@<paramControl>`（供命令条 Execute 以命令路径运行；参数在 paramControl 控件内编辑）");

        registry.Register("Ui_CommandManifest",
            (Func<string>)(CommandManifest),
            "命令面清单：列出通用命令库可用命令（命令执行子集的自描述）");

        registry.Register("Ui_ResetLayoutState",
            (Func<string>)(ResetLayoutState),
            "布局用户态重置：清空分隔比例缓存（下次加载恢复配置设计比例）");

        // 面板用户态（与布局缓存同存储件、同落盘策略）：调试面板的输入/选择持久化。
        registry.Register("Ui_PanelStateGet",
            (Func<string, string, Task<string>>)((scope, key) => Task.FromResult(PanelStateGet(scope, key))),
            new IParamConfig[]
            {
                new ParameterDescriptor { Name = "scope", Type = typeof(string) },
                new ParameterDescriptor { Name = "key", Type = typeof(string) },
            },
            "面板状态：读取某 scope 下的单键值（未缓存 ⇒ 空串，调用方按声明默认值回退）");

        registry.Register("Ui_PanelStateSave",
            (Func<string, string, string, Task<string>>)((scope, key, value) => Task.FromResult(PanelStateSave(scope, key, value))),
            new IParamConfig[]
            {
                new ParameterDescriptor { Name = "scope", Type = typeof(string) },
                new ParameterDescriptor { Name = "key", Type = typeof(string) },
                new ParameterDescriptor { Name = "value", Type = typeof(string) },
            },
            "面板状态：写单键值（空值 ⇒ 删除该键；立即落盘，便于外部观察/复算）");

        registry.Register("Ui_PanelStateRestore",
            (Func<string, Task<string>>)(scope => Task.FromResult(PanelStateRestore(scope))),
            new IParamConfig[]
            {
                new ParameterDescriptor { Name = "scope", Type = typeof(string) },
            },
            "面板状态：以 JSON 形式导出某 scope 的全部持久化值（调试/取证用）");

        registry.Register("Ui_PanelStateReset",
            (Func<string, Task<string>>)(scope => Task.FromResult(PanelStateReset(scope))),
            new IParamConfig[]
            {
                new ParameterDescriptor { Name = "scope", Type = typeof(string) },
            },
            "面板状态：清除某 scope（空 ⇒ 全部面板）；不影响分隔比例与窗体边界");

        // 画质设置（游戏式设置面板）：值落在 data（同 UI 布局），应用 = 重新装配即时生效。
        registry.Register("Ui_GraphicsApply",
            (Func<string>)(GraphicsApply),
            "画质设置：按已保存设置重新装配外观（档位/引擎/质量/玻璃上限/光场）");
        registry.Register("Ui_GraphicsReset",
            (Func<string>)(GraphicsReset),
            "画质设置：清除已保存设置并恢复声明缺省（重新装配）");
    }

    /// <summary>应用画质设置（切档走唯一入口；未安装过 ⇒ 明确未应用，不假装生效）。</summary>
    private static string GraphicsApply()
        => SurfaceAppearanceSwitch.ReapplyRequested()
            ? $"画质设置已应用（当前档：{Share.Conversation.Rendering.Surface.Selection.SurfaceSelectionProvider.Current.ProfileId}）"
            : "画质设置未应用：外观尚未安装，或请求档位在声明中不存在";

    /// <summary>恢复画质设置的声明缺省（只清画质面板，不动分隔比例/窗体边界）。</summary>
    private static string GraphicsReset()
    {
        var store = AppServiceProvider.Resolve<FrontEndDriven.Abstractions.IUiLayoutStateStore>();
        store?.ResetPanelValues(SurfaceQualitySettings.Scope);
        store?.Flush();
        return SurfaceAppearanceSwitch.ReapplyRequested()
            ? "画质设置已恢复缺省并重新装配"
            : "画质设置已清除；外观尚未安装";
    }

    /// <summary>读取面板单键值（未装配/未缓存 ⇒ 空串，禁静默编造）。</summary>
    private static string PanelStateGet(string scope, string key)
    {
        var store = AppServiceProvider.Resolve<FrontEndDriven.Abstractions.IUiLayoutStateStore>();
        if (store is null)
            return string.Empty;

        return store.TryGetPanelValues(scope, out var values) && values.TryGetValue(key, out var value)
            ? value
            : string.Empty;
    }

    /// <summary>写面板单键值（立即落盘：调试值需可被外部复算/取证）。</summary>
    private static string PanelStateSave(string scope, string key, string value)
    {
        var store = AppServiceProvider.Resolve<FrontEndDriven.Abstractions.IUiLayoutStateStore>();
        if (store is null)
            return "面板状态：布局缓存端口未装配";

        store.SetPanelValue(scope, key, value);
        store.Flush();
        return string.IsNullOrWhiteSpace(value)
            ? $"面板状态已清除：{scope}/{key}"
            : $"面板状态已保存：{scope}/{key}";
    }

    /// <summary>导出面板全部持久化值（JSON；无缓存 ⇒ 空对象）。</summary>
    private static string PanelStateRestore(string scope)
    {
        var store = AppServiceProvider.Resolve<FrontEndDriven.Abstractions.IUiLayoutStateStore>();
        if (store is null)
            return "面板状态：布局缓存端口未装配";

        var payload = store.TryGetPanelValues(scope, out var values)
            ? values
            : new Dictionary<string, string>();
        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>清除面板状态（空 ⇒ 全部；分隔比例/窗体边界不受影响）。</summary>
    private static string PanelStateReset(string scope)
    {
        var store = AppServiceProvider.Resolve<FrontEndDriven.Abstractions.IUiLayoutStateStore>();
        if (store is null)
            return "面板状态：布局缓存端口未装配";

        store.ResetPanelValues(string.IsNullOrWhiteSpace(scope) ? null : scope);
        store.Flush();
        return string.IsNullOrWhiteSpace(scope)
            ? "面板状态已全部清除"
            : $"面板状态已清除：{scope}";
    }

    /// <summary>
    /// 一键恢复设计比例：清空用户分隔比例缓存（含布局指纹），配置面保持只读。
    /// 生效时机 = 下一次加载/启动（当前会话的 ECS SplitterRatio 仍为拖拽值，不清场以免打断正在进行的布局）。
    /// </summary>
    private static string ResetLayoutState()
    {
        var store = AppServiceProvider.Resolve<FrontEndDriven.Abstractions.IUiLayoutStateStore>();
        if (store is null)
            return "布局缓存端口未装配：无法重置用户布局态";

        store.ResetSplitterRatios();
        store.Flush();
        FrontEndDrivenLog.Log("Ui_ResetLayoutState", new { scope = "*" });
        return "布局用户态已重置：分隔比例缓存已清空，下次加载恢复设计比例";
    }

    /// <summary>组装请求体：载入模板对象 → 应用输入覆盖 → 序列化（JSONC 容忍，注释跳过）。参数支持 @&lt;Control&gt; 解引用。
    /// 模板/控件解引用失败时返回具名诊断文本（不再静默产出空请求体）。</summary>
    private static string BuildRequest(string template, string input)
    {
        var (root, error) = BuildRequestObject(template, input);
        return error ?? root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>组装请求体对象（唯一模板/输入解析实现）：任一环节失败返回诊断，成功返回 (对象, null)。</summary>
    private static (JsonObject Root, string? Error) BuildRequestObject(string template, string input)
    {
        var (templateArg, templateResolveError) = ResolveArg(template);
        if (templateResolveError is not null)
            return (new JsonObject(), templateResolveError);

        var (root, templateLoadError) = LoadTemplateObject(templateArg);
        if (templateLoadError is not null)
            return (new JsonObject(), templateLoadError);

        var (inputArg, inputResolveError) = ResolveArg(input);
        if (inputResolveError is not null)
            return (new JsonObject(), inputResolveError);

        ApplyInput(root, inputArg);
        return (root, null);
    }

    /// <summary>命令参数解引用：`@&lt;Control&gt;` → 读该控件当前值（命令同源：参数可引用界面控件，运行时取真值）。
    /// 解引用失败返回诊断而非回退 `@字面量`（E2：不再把控件名当业务输入提交）。</summary>
    private static (string Value, string? Error) ResolveArg(string? value)
    {
        if (string.IsNullOrEmpty(value) || value[0] != '@')
            return (value ?? string.Empty, null);

        var name = value.Substring(1);
        var accessor = AppServiceProvider.Resolve<IEcsStateAccessor>();
        var live = accessor?.GetLiveValue(name);
        if (live is null)
            return (value, $"（控件解引用失败）未找到控件 '@{name}'，参数未按控件真值解析");

        return (live, null);
    }

    /// <summary>由模板 id + 参数控件名生成命令文本（编辑器控件内容以 @control 引用，运行时解引用）。</summary>
    private static string BuildCommandLine(string template, string paramControl)
    {
        var id = FileCatalogService.ExtractId(template);
        var control = string.IsNullOrWhiteSpace(paramControl) ? "paramJsonTextBox" : paramControl.Trim();
        return $"runJson template={id} input=@{control}";
    }

    private static async Task<string> RunCommandAsync(string template, string input, string executor)
    {
        var (root, error) = BuildRequestObject(template, input);
        if (error is not null)
            return error; // 显式诊断（E1/E5 模板未命中 · E2 控件解引用失败），不静默把诊断文本当请求体执行

        var request = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        return await RunRequestAsync(request, executor).ConfigureAwait(false);
    }

    private static async Task<string> RunRequestAsync(string requestJson, string executor)
    {
        var target = string.IsNullOrWhiteSpace(executor) ? DefaultExecutor : executor.Trim();
        if (string.IsNullOrWhiteSpace(requestJson))
            return $"（空请求体）命令 {target} 未执行";

        var result = await FunctionCallFacade.ExecuteAsync(new FunctionCallRequest
        {
            FunctionName = target,
            Parameters = new Dictionary<string, object?> { ["requestJson"] = requestJson },
            Source = FunctionCallSource.CommandExecutor,
            Mode = ExecutionMode.Auto,
            TimeoutMs = 180_000,
        }).ConfigureAwait(false);

        return result.IsSuccess
            ? (result.Output ?? string.Empty)
            : $"（执行失败）{target} :: {result.ErrorMessage}";
    }

    /// <summary>按 id 载入模板对象（唯一模板解析实现）：空模板合法返回空对象；
    /// 未命中/非 JSON 对象返回诊断（含可用列表），不再静默返回空请求体（E1/E5）。</summary>
    private static (JsonObject Root, string? Error) LoadTemplateObject(string template)
    {
        var id = FileCatalogService.ExtractId(template);
        if (id.Length == 0 || id == EmptyTemplate)
            return (new JsonObject(), null);

        var spec = new FileCatalogSpec { Dir = UiPathManager.GetAgentRunTemplatesRoot() };
        var text = FileCatalogService.LoadText(spec, id);
        if (string.IsNullOrWhiteSpace(text))
            return (new JsonObject(), $"（未找到模板）'{id}'，可用: {FileCatalogService.AvailableIds(spec)}");

        var obj = TryParseObject(text);
        if (obj is null)
            return (new JsonObject(), $"（模板不是 JSON 对象）'{id}'");

        return (obj, null);
    }

    private static void ApplyInput(JsonObject root, string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return;

        var patch = TryParseObject(input);
        if (patch is not null)
        {
            foreach (var kv in patch)
                root[kv.Key] = kv.Value?.DeepClone();
            return;
        }

        // 非对象输入（纯文本消息）→ 包为默认 input 键。
        root["input"] = input;
    }

    private static JsonObject? TryParseObject(string text)
    {
        try
        {
            var node = JsonNode.Parse(text, documentOptions: new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
            return node as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string CommandManifest()
        => string.Join("\n", new[]
        {
            "[通用命令库] JSON 执行命令（包装「选模板 + JSON 输入 → 运行」）：",
            $"  runJson template=<分类/文件名> input=<JSON|文本>          // = Ui_RunJsonCommand（执行器 {DefaultExecutor}）",
            "  Ui_RunCommand template=<id> input=<JSON|文本> executor=<函数名>   // 指定执行器",
            "  Ui_BuildRequest template=<id> input=<JSON|文本>            // 仅组装请求体",
            $"  Ui_RunRequest requestJson=<JSON>                          // 请求体 → {DefaultExecutor}",
            "  Ui_RunRequestEx requestJson=<JSON> executor=<函数名>       // 请求体 → 指定执行器",
            "  模板 id = Templates/AgentRun 下 `分类/文件名`；template=- 表示空模板。",
            "  参数可引用界面控件：`input=@<Control>`（运行时读取该控件真值，命令同源编辑/运行）。",
            "  生成命令：Ui_CommandFromTemplate(template, paramControl) → `runJson template=<id> input=@<paramControl>`。",
            "  用法：命令条输入 `runJson template=conversation/send input=@paramJsonTextBox` 后点 Execute。",
        });
}
