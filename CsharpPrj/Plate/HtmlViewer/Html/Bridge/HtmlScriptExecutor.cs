#nullable enable

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace UiBackend.Html;

/// <summary>
/// 参数化脚本执行器——把「DOM 指令 / 真实点击坐标 / 桥诊断」等 JS 片段从方法体手拼字符串收敛为**占位模板 + 统一展开**：
/// 参数一律经 JSON 序列化注入（消除引号转义漂移），再经 <see cref="IHtmlChannel.ExecuteScriptAsync"/> 统一执行。
/// 对象不持有 CoreWebView2 / UI 线程语义——原始执行收敛在通道实现（HtmlViewer）内，本类只管「拼对 + 执行 + 诊断」。
/// </summary>
public sealed class HtmlScriptExecutor
{
    private static readonly IHtmlAssetProvider DefaultAssets = HtmlAssetRegistry.Default;

    private readonly IHtmlChannel _channel;
    private readonly IHtmlAssetProvider _assets;

    public HtmlScriptExecutor(IHtmlChannel channel, IHtmlAssetProvider? assets = null)
    {
        _channel = channel ?? throw new System.ArgumentNullException(nameof(channel));
        _assets = assets ?? DefaultAssets;
    }

    /// <summary>桥链路结构化诊断出口（category, message 两元；宿主接线到日志门面，未接线为空操作）。</summary>
    public System.Action<string, string>? Diagnostics;

    private void Report(string category, string message) => Diagnostics?.Invoke(category, message);

    /// <summary>渲染 __domBridge.handle 调用脚本（$OP/$NAME/$VALUE 占位 → JSON 序列化展开）。</summary>
    public string RenderHandleScript(string op, string name, string? value)
        => Fill(_assets.GetTemplate(HtmlAssetKeys.DomHandleTemplate),
            ("$OP", JsonSerializer.Serialize(op)),
            ("$NAME", JsonSerializer.Serialize(name)),
            ("$VALUE", value is null ? "null" : JsonSerializer.Serialize(value)));

    /// <summary>执行一条 __domBridge.handle 受控 DOM 指令（依赖桥已注入；桥注入由 <see cref="HtmlScriptHost"/> 负责）。</summary>
    public Task<string> ExecuteHandleAsync(string op, string name, string? value, CancellationToken ct = default)
        => _channel.ExecuteScriptAsync(RenderHandleScript(op, name, value), ct);

    /// <summary>渲染真实点击坐标查询脚本。NOT_FOUND 返回附带文档诊断后缀，NOT_VISIBLE 表示元素不可见。</summary>
    public string RenderRectQuery(string name)
        => Fill(_assets.GetTemplate(HtmlAssetKeys.RectQueryTemplate),
            ("$NAME", JsonSerializer.Serialize(name)));

    /// <summary>渲染桥诊断查询脚本（供注入失败时定位根因）。</summary>
    public string RenderBridgeDiagnosis()
        => _assets.GetTemplate(HtmlAssetKeys.BridgeDiagnosisTemplate);

    /// <summary>执行桥诊断查询；失败返回兜底文本。</summary>
    public async Task<string> GetBridgeDiagnosisAsync(CancellationToken ct)
    {
        try
        {
            var raw = await _channel.ExecuteScriptAsync(RenderBridgeDiagnosis(), ct).ConfigureAwait(false);
            return $"点击桥注入失败, document 诊断={raw}";
        }
        catch (System.Exception ex)
        {
            return $"点击桥注入失败, 诊断查询异常={ex.GetType().Name}: {ex.Message}";
        }
    }

    /// <summary>顺序执行具名值替换（占位符不存在时保留原文，便于排查模板 key 漂移）。</summary>
    private static string Fill(string template, params (string Key, string Value)[] args)
    {
        var sb = new System.Text.StringBuilder(template);
        foreach (var (key, value) in args)
            sb.Replace(key, value);
        return sb.ToString();
    }
}