#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace UiBackend.Html;

/// <summary>
/// 默认前端资产注册表——HtmlViewer 内嵌 JS/CSS/JS 模板的**加载器**（资产原文在 <c>Assets/</c>，与代码同版本化）。
/// <para>优先级：运行时覆盖（<see cref="RegisterStyleModule"/>）&gt; 嵌入资产 &gt; 空（显式降级并记诊断，禁静默）。</para>
/// <para>静态方法供静态渲染器（HtmlDocumentRenderer / HtmlLayoutProjector / HtmlControlFactory）直接取用；
/// 实例实现 <see cref="IHtmlAssetProvider"/> 供执行层 / 生命周期层注入（IoC 可替换为外部源）。</para>
/// </summary>
public sealed partial class HtmlAssetRegistry : IHtmlAssetProvider
{
    /// <summary>嵌入资源前缀（与 HtmlViewer.csproj 的 LogicalName 固定前缀同源）。</summary>
    private const string ResourcePrefix = "Plate.HtmlViewer.Assets.";

    /// <summary>默认单例（组合根未显式装配时的兜底）。</summary>
    public static IHtmlAssetProvider Default { get; } = new HtmlAssetRegistry();

    /// <summary>键 → 资产相对路径（脚本）。</summary>
    private static readonly IReadOnlyDictionary<string, string> ScriptAssets =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [HtmlAssetKeys.BridgeScript] = "scripts/bridge.js",
            [HtmlAssetKeys.UiReconcileScript] = "scripts/reconcile.js",
            [HtmlAssetKeys.TabSwapScript] = "scripts/tabswap.js",
            [HtmlAssetKeys.RefreshBridgeScript] = "scripts/refresh.js",
        };

    /// <summary>键 → 资产相对路径（整段样式）。</summary>
    private static readonly IReadOnlyDictionary<string, string> StyleAssets =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [HtmlAssetKeys.DefaultCss] = "styles/default.css",
            [HtmlAssetKeys.BaseCss] = "styles/base.css",
        };

    /// <summary>键 → templates.runtime.json 中的键名（参数化模板）。</summary>
    private static readonly IReadOnlyDictionary<string, string> TemplateKeys =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [HtmlAssetKeys.DomHandleTemplate] = "domHandle",
            [HtmlAssetKeys.RectQueryTemplate] = "rectQuery",
            [HtmlAssetKeys.BridgeDiagnosisTemplate] = "bridgeDiagnosis",
            [HtmlAssetKeys.ClickAckInstallTemplate] = "clickAckInstall",
            [HtmlAssetKeys.ClickAckReadTemplate] = "clickAckRead",
            [HtmlAssetKeys.EmptyDivTemplate] = "emptyDiv",
            [HtmlAssetKeys.EmptyPTemplate] = "emptyP",
        };

    private const string DefaultTemplatesAsset = "templates.default.json";
    private const string RuntimeTemplatesAsset = "templates.runtime.json";
    private const string ComponentsAsset = "styles/components.json";

    /// <summary>框架内嵌默认模板集（配置可按同名键覆盖）；资产缺失 ⇒ 空集 + 诊断。</summary>
    /// <remarks>延迟加载：静态字段初始化顺序无关（<see cref="AssetCache"/> 等资产基础设施后置声明）。</remarks>
    public static IReadOnlyDictionary<string, string> DefaultTemplates => DefaultTemplatesLazy.Value;

    private static readonly Lazy<IReadOnlyDictionary<string, string>> DefaultTemplatesLazy =
        new(() => LoadTemplates(DefaultTemplatesAsset));

    private static readonly Lazy<IReadOnlyDictionary<string, string>> RuntimeTemplates =
        new(() => LoadTemplates(RuntimeTemplatesAsset));

    private static readonly Lazy<(IReadOnlyDictionary<string, string> Modules, IReadOnlyList<string> Order)>
        ComponentStyles = new(LoadComponentStyles);

    public static string GetScript(string key)
        => ScriptAssets.TryGetValue(key ?? string.Empty, out var path) ? ReadAsset(path) : string.Empty;

    public static string GetStyle(string key)
        => StyleAssets.TryGetValue(key ?? string.Empty, out var path) ? ReadAsset(path) : string.Empty;

    public static string GetTemplate(string key)
        => TemplateKeys.TryGetValue(key ?? string.Empty, out var name)
           && RuntimeTemplates.Value.TryGetValue(name, out var value)
            ? value
            : string.Empty;

    string IHtmlAssetProvider.GetScript(string key) => GetScript(key);
    string IHtmlAssetProvider.GetStyle(string key) => GetStyle(key);
    string IHtmlAssetProvider.GetTemplate(string key) => GetTemplate(key);

    /// <summary>框架内嵌默认模板集（配置覆盖的基底）；经端口暴露，消费方不再直取静态成员。</summary>
    public IReadOnlyDictionary<string, string> GetDefaultTemplates() => DefaultTemplates;

    /// <summary>外部注册的样式模块覆盖（域可注入/覆盖同名通用模块，无需新建配置文件）。</summary>
    private static readonly ConcurrentDictionary<string, string> StyleModuleOverrides =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>注册/覆盖样式模块（幂等：覆盖式）。<paramref name="name"/> 取通用模块名（tokens/block/shell/tree/switch/grouped/state/attach/data）。</summary>
    public static void RegisterStyleModule(string name, string css)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        StyleModuleOverrides[name] = css ?? string.Empty;
    }

    /// <summary>取样式模块：外部覆盖优先，否则资产模块；未知名返回空串。</summary>
    public static string GetStyleModule(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        if (StyleModuleOverrides.TryGetValue(name, out var overridden)) return overridden;
        return ComponentStyles.Value.Modules.TryGetValue(name, out var asset) ? asset : string.Empty;
    }

    /// <summary>通用组件生效样式 = 通用模块按固定序拼接（顺序固定 ⇒ 级联稳定；模块可被域覆盖）
    /// + 声明投影模块（<see cref="DeclarationStyleModule"/>，最后追加 ⇒ 声明优先）。</summary>
    internal static string FrameworkComponentCss =>
        string.Join("\n", ComponentStyles.Value.Order.Select(GetStyleModule))
        + "\n" + GetStyleModule(DeclarationStyleModule);

    /// <summary>声明投影样式模块名（由 <c>SurfaceCssProjector</c> 注册；未注册 ⇒ 空串，零回归）。</summary>
    internal const string DeclarationStyleModule = "ui-declaration";

    /// <summary>内置通用模块（资产加载结果；供诊断/覆盖前对照）。</summary>
    internal static IReadOnlyDictionary<string, string> CssModules => ComponentStyles.Value.Modules;

    /// <summary>通用组件样式组合顺序（模块名有序；顺序固定 = 级联稳定）。</summary>
    internal static IReadOnlyList<string> FrameworkComponentCssOrder => ComponentStyles.Value.Order;


    // ── 资产读取（嵌入资源 → 文本；缺失显式降级 + 一次性诊断） ──

    private static readonly ConcurrentDictionary<string, string> AssetCache = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, byte> ReportedFailures = new(StringComparer.Ordinal);

    private static readonly Lazy<IReadOnlyDictionary<string, string>> ResourceIndex = new(BuildResourceIndex);

    private static IReadOnlyDictionary<string, string> BuildResourceIndex()
    {
        var index = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in typeof(HtmlAssetRegistry).Assembly.GetManifestResourceNames())
            index[NormalizeResourceName(name)] = name;
        return index;
    }

    private static string NormalizeResourceName(string name)
        => name.Replace('\\', '.').Replace('/', '.');

    private static string ReadAsset(string relativePath)
        => AssetCache.GetOrAdd(relativePath, static path => LoadAsset(path));

    private static string LoadAsset(string relativePath)
    {
        var logicalName = NormalizeResourceName(ResourcePrefix + relativePath);
        if (!ResourceIndex.Value.TryGetValue(logicalName, out var resourceName))
        {
            ReportFailure(logicalName, "嵌入资源缺失");
            return string.Empty;
        }

        try
        {
            using var stream = typeof(HtmlAssetRegistry).Assembly.GetManifestResourceStream(resourceName);
            if (stream is null)
            {
                ReportFailure(logicalName, "嵌入资源不可读");
                return string.Empty;
            }

            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return Interpolate(relativePath, reader.ReadToEnd());
        }
        catch (IOException ex)
        {
            ReportFailure(logicalName, ex.Message);
            return string.Empty;
        }
    }

    /// <summary>单次插值（只做字面替换一次；插值结构不进资产原文）。</summary>
    private static string Interpolate(string relativePath, string text)
        => relativePath switch
        {
            "scripts/reconcile.js" => text.Replace("{{alignKeys}}", HtmlKey.JsArrayLiteral, StringComparison.Ordinal),
            "scripts/refresh.js" => text
                .Replace("{{refreshCommand}}", HtmlRefreshProtocol.Command, StringComparison.Ordinal)
                .Replace("{{refreshThrottleMs}}", HtmlRefreshProtocol.ThrottleMs.ToString(), StringComparison.Ordinal)
                .Replace("{{refreshIntervalMs}}", HtmlRefreshProtocol.IntervalMs.ToString(), StringComparison.Ordinal),
            _ => text,
        };

    private static void ReportFailure(string logicalName, string reason)
    {
        if (!ReportedFailures.TryAdd(logicalName, 0)) return;
        Trace.TraceError($"[HtmlAssetRegistry] 资产加载失败（显式降级为空串）：{logicalName} — {reason}");
    }


    private static IReadOnlyDictionary<string, string> LoadTemplates(string relativePath)
    {
        var text = ReadAsset(relativePath);
        if (string.IsNullOrWhiteSpace(text)) return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var doc = JsonDocument.Parse(text);
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in doc.RootElement.EnumerateObject())
                result[property.Name] = property.Value.GetString() ?? string.Empty;
            return result;
        }
        catch (JsonException ex)
        {
            ReportFailure(NormalizeResourceName(ResourcePrefix + relativePath), ex.Message);
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static (IReadOnlyDictionary<string, string> Modules, IReadOnlyList<string> Order) LoadComponentStyles()
    {
        var text = ReadAsset(ComponentsAsset);
        if (string.IsNullOrWhiteSpace(text))
            return (new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), Array.Empty<string>());

        try
        {
            using var doc = JsonDocument.Parse(text);
            var modules = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (doc.RootElement.TryGetProperty("modules", out var modulesElement)
                && modulesElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in modulesElement.EnumerateObject())
                    modules[property.Name] = property.Value.GetString() ?? string.Empty;
            }

            var order = new List<string>();
            if (doc.RootElement.TryGetProperty("order", out var orderElement)
                && orderElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in orderElement.EnumerateArray())
                {
                    var name = item.GetString();
                    if (!string.IsNullOrWhiteSpace(name)) order.Add(name!);
                }
            }

            return (modules, order);
        }
        catch (JsonException ex)
        {
            ReportFailure(NormalizeResourceName(ResourcePrefix + ComponentsAsset), ex.Message);
            return (new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), Array.Empty<string>());
        }
    }
}

