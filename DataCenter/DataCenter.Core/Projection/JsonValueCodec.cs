using DataCenter.Protocol.Projection;

namespace Operation.StateBlock.Projection;

/// <summary>
/// JSON 值编解码（默认 <see cref="IStateValueCodec"/> 实现）—— 把运行时值映射为跨语言稳定的载荷：
/// 标量直通（string/bool/数值），时间与 Guid 归一为字符串，枚举取名字，其余经确定性 JSON 序列化。
/// 载荷即可跨语言交换的形态；反序列化交由消费方按域 Schema 处理（本编码器不猜测目标类型）。
/// </summary>
public sealed class JsonValueCodec : IStateValueCodec
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // 枚举以字符串序列化 —— 与 CSG 跨语言契约（TS 侧字符串枚举）一致
        Converters = { new JsonStringEnumConverter() },
    };

    public object? Encode(object? value) => value switch
    {
        null => null,
        string or bool or char or sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal => value,
        DateTimeOffset dto => dto.ToString("O"),
        DateTime dt => dt.ToString("O"),
        Guid guid => guid.ToString("D"),
        Enum e => e.ToString(),
        _ => JsonSerializer.Serialize(value, value.GetType(), SerializerOptions),
    };
}
