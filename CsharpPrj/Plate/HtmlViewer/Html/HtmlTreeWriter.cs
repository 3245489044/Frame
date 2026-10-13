#nullable enable

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
        var (status, changed) = MapStatus(raw);
        return new UiWriteResult(
            status,
            status == UiWriteStatus.Applied || status == UiWriteStatus.Partial ? new[] { name } : System.Array.Empty<string>(),
            status == UiWriteStatus.Failed ? new[] { name } : System.Array.Empty<string>(),
            Changed: changed);
    }

    /// <summary>回串映射：OK[:changed] → Applied；NOT_FOUND/RECONCILE_NOT_READY/其它 → Failed。</summary>
    private static (UiWriteStatus Status, int Changed) MapStatus(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return (UiWriteStatus.Failed, 0);
        if (raw == "OK") return (UiWriteStatus.Applied, 0);
        if (raw.StartsWith("OK:"))
        {
            return int.TryParse(raw.AsSpan(3), out var n)
                ? (UiWriteStatus.Applied, n)
                : (UiWriteStatus.Applied, 0);
        }
        return (UiWriteStatus.Failed, 0);
    }
}
