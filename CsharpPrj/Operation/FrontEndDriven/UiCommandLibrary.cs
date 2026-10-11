#nullable enable

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using FrameworkAbstractions.ComponentRegistry;
using FrameworkAbstractions.WinFormsAbstractions;
using FrontEndDriven.Shared.Catalog;
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
    /// <summary>缺省执行器：Agent 的单一 JSON 请求体入口。</summary>
    public const string DefaultExecutor = "AgentUi_RunJson";

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
            (Func<string, Task<string>>)(requestJson => RunRequestAsync(requestJson, DefaultExecutor)),
            new IParamConfig[]
            {
                new ParameterDescriptor { Name = "requestJson", Type = typeof(string) },
            },
            $"通用：请求体文本 → 缺省执行器（{DefaultExecutor}）执行，返回输出");

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
