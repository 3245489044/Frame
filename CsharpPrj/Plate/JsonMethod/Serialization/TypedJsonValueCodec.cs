using System.Text.Json;

namespace Plate.JsonMethod.Serialization
{
    /// <summary>
    /// 类型保真值编解码（平台级动态值单源）：把任意 object 编码为「类型名 + JSON」、按类型解码还原。
    /// <para>
    /// 语义（逐字保留，自 Operation 侧 NodeOutputCodec 上收）：
    /// <list type="bullet">
    /// <item><see cref="Encode"/>：非 null 值存「运行时类型 + JSON」；不可序列化（<see cref="NotSupportedException"/> /
    /// <see cref="JsonException"/>）→ 降级为 <c>string</c>（保数据，仍可还原为 string）。</item>
    /// <item><see cref="Decode"/>：按类型名还原；类型不可解析 / 反序列化失败 → 回退 <c>JsonElement.Clone()</c>
    /// （不抛、不中断，读取侧不因单个值降级而失败）。</item>
    /// </list>
    /// </para>
    /// <para>纯 BCL，无类型白名单（如需白名单，由宿主经注入钩子提供，平台不内建）。</para>
    /// </summary>
    public static class TypedJsonValueCodec
    {
        /// <summary>编码：泛型值 → 类型保真信封（null 值 → 空位）。</summary>
        public static TypedJsonValue Encode(object? value)
        {
            if (value is null)
            {
                return new TypedJsonValue { Type = null, Value = null };
            }

            var type = value.GetType();
            try
            {
                return new TypedJsonValue
                {
                    Type = type.AssemblyQualifiedName,
                    Value = JsonSerializer.SerializeToElement(value, type),
                };
            }
            catch (Exception ex) when (ex is NotSupportedException or JsonException)
            {
                // 非可序列化值：降级为字符串表达，仍保留数据（读取侧还原为 string）。
                return new TypedJsonValue
                {
                    Type = typeof(string).AssemblyQualifiedName,
                    Value = JsonSerializer.SerializeToElement(value.ToString() ?? string.Empty),
                };
            }
        }

        /// <summary>解码：类型保真信封 → 值；类型不可解析 / 反序列化失败回退为 <see cref="JsonElement"/>。</summary>
        public static object? Decode(TypedJsonValue? typed)
        {
            if (typed is null || typed.Value is not JsonElement element || typed.Type is null)
            {
                return null;
            }

            var type = Type.GetType(typed.Type);
            if (type is null)
            {
                return element.Clone();
            }

            try
            {
                return element.Deserialize(type);
            }
            catch (JsonException)
            {
                return element.Clone();
            }
        }
    }
}
