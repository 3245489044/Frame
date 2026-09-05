#nullable enable

using System.Text.Json;
using TConfig.Controls;
using UiBackend.Abstractions;

namespace UiBackend.Html;

/// <summary>
/// HTML 节点访问器（协议形态）——仅持有元素描述（name/type/semantic）+ <see cref="IHtmlChannel"/>，
/// 值/事件/定位全部翻译为受控 DOM 指令经桥发出，不持有物理 DOM 引用。
/// 注意：同步 GetValue/SetValue 内部同步等待桥往返，仅适合无 WebView2 回调依赖的调用方；
/// 事件桥 / 脚本桥应使用 <see cref="IHtmlChannel"/> 的异步 API（避免 UI 线程死锁）。
/// </summary>
public sealed class HtmlElementNode : IUiElementNode
{
    private readonly IHtmlChannel _channel;

    public HtmlElementNode(string name, string typeName, IHtmlChannel channel, ValueSemantic? semantic = null)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        TypeName = typeName ?? throw new ArgumentNullException(nameof(typeName));
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        Semantic = semantic ?? DefaultSemantic(typeName);
    }

    public string Name { get; }

    public string TypeName { get; }

    /// <summary>值语义（textbox→Text、listview→Selection...；缺省按类型映射）。</summary>
    public ValueSemantic Semantic { get; }

    public object? GetValue()
    {
        var raw = _channel.ExecuteAsync(DomOps.Get, Name, null).GetAwaiter().GetResult();
        return UnwrapString(raw);
    }

    public void SetValue(object? value)
        => _channel.ExecuteAsync(DomOps.Set, Name, value?.ToString()).GetAwaiter().GetResult();

    public void Invoke(string eventName, object? payload)
        => _channel.ExecuteAsync(DomOps.Event, Name, eventName).GetAwaiter().GetResult();

    public void SetBounds(int x, int y, int width, int height)
        => _channel.ExecuteAsync(DomOps.Bounds, Name,
            JsonSerializer.Serialize(new { x, y, w = width, h = height })).GetAwaiter().GetResult();

    public (int X, int Y, int Width, int Height) GetBounds()
        // 协议形态不缓存物理位置：布局由声明投影，宿主如需回读应经桥 get（bounds）扩展。
        => (0, 0, 0, 0);

    public IEnumerable<IUiElementNode> Children => Array.Empty<IUiElementNode>();

    /// <summary>解包 ExecuteScriptAsync 返回的 JSON 字符串字面量（"abc" → abc）；非字符串原样返回。</summary>
    public static string UnwrapString(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        try { return JsonSerializer.Deserialize<string>(raw) ?? raw; }
        catch (JsonException) { return raw; }
    }

    /// <summary>类型 → 缺省值语义（与 WinForms ValueContractRegistry 默认语义对齐）。</summary>
    public static ValueSemantic DefaultSemantic(string typeName)
        => typeName.ToLowerInvariant() switch
        {
            "listview" or "combobox" or "treeview" => ValueSemantic.Selection,
            "checkbox" or "radiobutton" => ValueSemantic.Checked,
            "numericupdown" or "trackbar" => ValueSemantic.Range,
            "htmlviewer" or "markdownviewer" => ValueSemantic.Content,
            _ => ValueSemantic.Text,
        };
}
