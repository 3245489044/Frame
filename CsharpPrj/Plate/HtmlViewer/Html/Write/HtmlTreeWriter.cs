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
        // 目标定位走属性等值匹配（不做 CSS 选择器拼接）：data-name 含引号/反斜杠/方括号时选择器会失效或注入。
        var script = "(function(){var n=" + nameJson + ";var a=document.querySelectorAll('[data-name]');var h=null;"
            + "for(var i=0;i<a.length;i++){if(a[i].getAttribute('data-name')===n){h=a[i];break;}}"
            + "if(!h){return 'NOT_FOUND';}"
            + "if(!(window.__uiReconcile&&window.__uiReconcile.applyTree)){return 'RECONCILE_NOT_READY';}"
            + "return window.__uiReconcile.applyTree(h," + treeJson + "," + stateJson + ");})()";
        var raw = HtmlElementNode.UnwrapString(await _channel.ExecuteScriptAsync(script, ct).ConfigureAwait(false));
        var (status, changed, anomalies) = MapStatus(raw);
        // X-5：对齐异常不构成「成功/放行」依据（判据不合格 ⇒ ⊥，对齐 axioms:AX-FRAME.CriterionWellFormedness）——
        // anomalies 非空 ⇒ Partial 且**不计入 written**（skipped=[name]），供上层显式自愈而非「静默成功」。
        var anomaliesPresent = anomalies is { Count: > 0 };
        var applied = (status is UiWriteStatus.Applied or UiWriteStatus.Partial) && !anomaliesPresent;
        // 写失败/异常可见化（治 E-6 静默）：非 applied 时透出回串原文（截断），供上层定向自愈。
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

    /// <summary>键控树去重指纹（D-1，单一序列化口径）——与 <see cref="ReconcileAsync"/> 同 <see cref="JsonOptions"/> 序列化，
    /// 供上层写协调器在「同树同交互连续写」时短路（不触 DOM）。</summary>
    public static string Fingerprint(UiNode tree, UiInteractionSnapshot? interaction = null)
        => UiFingerprint.Combine(
            JsonSerializer.Serialize(tree, JsonOptions),
            interaction is null || interaction.IsEmpty ? null : JsonSerializer.Serialize(interaction, JsonOptions));

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
            // X-5：anomalies 非空 ⇒ Partial（不判成功），并透传 anomalies 供上层可见化。
            return anomalies.Length > 0
                ? (UiWriteStatus.Partial, 0, anomalies)
                : (UiWriteStatus.Applied, 0, null);
        }
        if (raw == "NOT_FOUND") return (UiWriteStatus.NotFound, 0, null);
        if (raw == "RECONCILE_NOT_READY") return (UiWriteStatus.NotReady, 0, null);
        return (UiWriteStatus.Failed, 0, null);
    }
}
