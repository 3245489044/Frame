#nullable enable

using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using UiBackend.Abstractions;

namespace UiBackend.Html;

/// <summary>
/// 键控对齐写端口（L2 HTML 实现，L1 <see cref="IUiTreeChannel"/> 的 HTML 落地）——把 <see cref="UiNode"/> 树
/// 序列化为 JSON 并交页面 <c>__uiReconcile.applyTree</c> 按稳定键对齐到具名区域根（未变节点不动）。
/// <para>经 <see cref="IHtmlChannel.ExecuteScriptAsync"/> 单通道执行；结果回串映射为 <see cref="UiWriteResult"/>。</para>
/// </summary>
public sealed class HtmlTreeWriter : IUiTreeChannel
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IHtmlChannel _channel;

    public HtmlTreeWriter(IHtmlChannel channel)
        => _channel = channel ?? throw new System.ArgumentNullException(nameof(channel));

    private readonly DiagnosticPort _diagnostics = new();

    /// <summary>桥链路结构化诊断出口（写失败可见化；未接线为空操作）。</summary>
    public Action<string, string>? Diagnostics { get => _diagnostics.Sink; set => _diagnostics.Sink = value; }

    private void Report(string category, string message) => _diagnostics.Report(category, message);

    public async Task<UiWriteResult> ReconcileAsync(string? rootName, UiNode tree, UiInteractionSnapshot? interaction = null, CancellationToken ct = default)
    {
        var name = rootName ?? string.Empty;
        var nameJson = JsonSerializer.Serialize(name);
        var treeJson = JsonSerializer.Serialize(tree, JsonOptions);
        var stateJson = interaction is null || interaction.IsEmpty
            ? "null"
            : JsonSerializer.Serialize(interaction, JsonOptions);
        // $NAME/$TREE/$STATE 占位展开：name JSON 自带引号 → '[data-name="'+$NAME+'"]'。
        var script = "(function(){var h=document.querySelector('[data-name=\"'+"
            + nameJson
            + "+'\"]');if(!h){return 'NOT_FOUND';}"
            + "if(!(window.__uiReconcile&&window.__uiReconcile.applyTree)){return 'RECONCILE_NOT_READY';}"
            + "return window.__uiReconcile.applyTree(h," + treeJson + "," + stateJson + ");})()";
        var raw = HtmlElementNode.UnwrapString(await _channel.ExecuteScriptAsync(script, ct).ConfigureAwait(false));
        var (status, changed, anomalies) = MapStatus(raw);
        var applied = status is UiWriteStatus.Applied or UiWriteStatus.Partial;
        // 写失败可见化（治 E-6 静默）：非 Applied 时透出回串原文（截断），供上层定向自愈而非仅见 Failed。
        if (!applied)
            Report("EventBridge_writeback",
                $"键控树写回 {name} 未落地 status={status} raw={(string.IsNullOrEmpty(raw) ? "<empty>" : raw.Length > 160 ? raw.Substring(0, 160) : raw)}");
        return new UiWriteResult(
            status,
            applied ? new[] { name } : System.Array.Empty<string>(),
            applied ? System.Array.Empty<string>() : new[] { name },
            Changed: changed,
            Anomalies: anomalies);
    }

    /// <summary>回串映射（类型化；失败原因不被抹平）：OK / OK:&lt;changed&gt; / OK:&lt;anomalies 分号分隔&gt; → Applied（携 anomalies）；
    /// NOT_FOUND → NotFound（区域缺失，可定向自愈）；RECONCILE_NOT_READY → NotReady（对齐内核未就绪，可补注入自愈）；其它 → Failed。
    /// anomalies 供上层可见化（不静默）。</summary>
    private static (UiWriteStatus Status, int Changed, IReadOnlyList<string>? Anomalies) MapStatus(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return (UiWriteStatus.Failed, 0, null);
        if (raw == "OK") return (UiWriteStatus.Applied, 0, null);
        if (raw.StartsWith("OK:"))
        {
            var rest = raw.Substring(3);
            if (int.TryParse(rest, out var n)) return (UiWriteStatus.Applied, n, null);
            var anomalies = rest.Split(new[] { ';' }, System.StringSplitOptions.RemoveEmptyEntries);
            return (UiWriteStatus.Applied, 0, anomalies.Length > 0 ? anomalies : null);
        }
        if (raw == "NOT_FOUND") return (UiWriteStatus.NotFound, 0, null);
        if (raw == "RECONCILE_NOT_READY") return (UiWriteStatus.NotReady, 0, null);
        return (UiWriteStatus.Failed, 0, null);
    }
}
