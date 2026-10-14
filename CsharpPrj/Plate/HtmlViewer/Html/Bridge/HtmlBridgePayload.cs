#nullable enable

using System.Collections.Generic;
using System.Text.Json;
using Plate.JsonMethod.Utilities;
using UiBackend.Abstractions;

namespace UiBackend.Html;

/// <summary>
/// htmlviewer 消息桥负载（通用契约，零领域）——<c>postMessage</c> JSON 的**信封无关视图**：
/// 提供按键的通用取值，以及框架级协议字段（<c>cmd</c> 命令键、<c>scope</c> 变更范围、<c>traceId</c> 追踪号、
/// <c>uiState</c> 交互态，对齐 <see cref="HtmlRefreshProtocol"/>/<see cref="UiInteractionSnapshot"/>）。
/// <para>领域字段（会话 / 任务 / 历史 / 轮次…）**不由本框架预设**：消费方经
/// <see cref="Text"/> / <see cref="Strings"/> / <see cref="Section"/> / <see cref="Number"/> / <see cref="Bool"/> 自取。</para>
/// </summary>
public sealed class HtmlBridgePayload
{
    private readonly JsonElement _root;

    internal HtmlBridgePayload(JsonElement root) => _root = root;

    /// <summary>空负载（解析失败 / 空串的归一值；一切按键取值返回 null）。</summary>
    public static HtmlBridgePayload Empty { get; } = new(JsonDocument.Parse("{}").RootElement.Clone());

    /// <summary>桥命令键（框架点击桥约定字段 <c>cmd</c>，已去空白）；缺失 → null。</summary>
    public string? Command => Trimmed(Text("cmd"));

    /// <summary>变更范围（框架刷新协议 <c>scope</c> 数组）；空 / 无 → null。</summary>
    public IReadOnlyList<string>? Scopes => NonEmpty(JsonTolerantReader.StrList(_root, "scope"));

    /// <summary>受控态展开集合（框架态外置协议 <c>uiState.open</c>）；空 / 无 → null。</summary>
    public IReadOnlyList<string>? OpenKeys
        => Section("uiState") is { } st ? NonEmpty(JsonTolerantReader.StrList(st._root, "open")) : null;

    /// <summary>刷新链路追踪号（框架协议 <c>traceId</c>）；无 → null。</summary>
    public string? TraceId => Text("traceId");

    /// <summary>交互态（框架态外置协议 <c>uiState{focus,scroll:[{k,t,l,b}]}</c>）；无 → null。</summary>
    public UiInteractionSnapshot? Interaction
    {
        get
        {
            if (Section("uiState") is not { } st) return null;

            var focus = JsonTolerantReader.Text(st._root, "focus");
            List<UiScrollState>? scroll = null;
            var elements = JsonTolerantReader.Elements(st._root, "scroll");
            if (elements is not null)
                foreach (var e in elements)
                {
                    if (e.ValueKind != JsonValueKind.Object) continue;
                    var key = JsonTolerantReader.Text(e, "k");
                    if (string.IsNullOrWhiteSpace(key)) continue;
                    (scroll ??= new()).Add(new UiScrollState(
                        key!,
                        JsonTolerantReader.Number(e, "t") ?? 0,
                        JsonTolerantReader.Number(e, "l") ?? 0,
                        JsonTolerantReader.Bool(e, "b") ?? false));
                }

            return focus is null && scroll is null ? null : new UiInteractionSnapshot(focus, scroll);
        }
    }

    /// <summary>按键取字符串（缺失 / 非字符串 → null）。</summary>
    public string? Text(string name) => JsonTolerantReader.Text(_root, name);

    /// <summary>按键取字符串数组（缺失 / 非数组 → null；空数组 → null）。</summary>
    public IReadOnlyList<string>? Strings(string name) => NonEmpty(JsonTolerantReader.StrList(_root, name));

    /// <summary>按键取子对象（缺失 / 非对象 → null）。</summary>
    public HtmlBridgePayload? Section(string name)
        => JsonTolerantReader.TrySection(_root, name, out var section) ? new HtmlBridgePayload(section) : null;

    /// <summary>按键取数值（缺失 / 非数值 → null）。</summary>
    public double? Number(string name) => JsonTolerantReader.Number(_root, name);

    /// <summary>按键取布尔（缺失 / 非布尔 → null）。</summary>
    public bool? Bool(string name) => JsonTolerantReader.Bool(_root, name);

    private static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value!.Trim();

    private static IReadOnlyList<string>? NonEmpty(IReadOnlyList<string>? list) => list is { Count: > 0 } ? list : null;
}
