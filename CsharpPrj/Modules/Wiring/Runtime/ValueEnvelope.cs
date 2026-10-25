namespace Modules.Wiring.Runtime;

/// <summary>
/// 值信封（运行期唯一值载体）：形态已在编译期闭合，下游只吃类型化端口。
/// <para>禁在信封内承载业务键字典语义以外的编码（JSON 二次解析即形态走私）。</para>
/// </summary>
public readonly record struct ValueEnvelope(
    string Form,
    object? Payload,
    string? TraceId = null,
    Guid? OpId = null,
    string? ProducerWireId = null,
    string? Scope = null)
{
    public bool HasPayload => Payload is not null;

    /// <summary>载荷长度（记账用结构事实；非字符串载荷 = 0）。</summary>
    public int Length => Payload switch
    {
        string text => text.Length,
        null => 0,
        _ => 0,
    };

    public static ValueEnvelope OfText(string form, string? text, string? traceId = null, Guid? opId = null)
        => new(form, text, traceId, opId);
}
