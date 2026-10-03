using System.Text.Json;

namespace Plate.JsonMethod.Serialization
{
    /// <summary>
    /// 类型保真动态值封装（类型标签信封）：运行时类型名（<see cref="System.Type.AssemblyQualifiedName"/>）+ 序列化 JSON 元素。
    /// <para>
    /// 与 <see cref="DynamicObjectConverter"/>（Dictionary/List 动态，无类型标签）、
    /// <see cref="Plate.JsonMethod.Utilities.JsonValueConverter"/>（标量转换）互补——
    /// 本类承载「类型标签信封 + 双向容错」，供端口值 / 会话载荷 / 任意 KV 等平台级动态值共用。
    /// </para>
    /// </summary>
    public sealed class TypedJsonValue
    {
        /// <summary>运行时类型（<see cref="System.Type.AssemblyQualifiedName"/>）；null 表示值为 null。</summary>
        public string? Type { get; set; }

        /// <summary>序列化后的值（JSON 元素）；null 表示值为 null。</summary>
        public JsonElement? Value { get; set; }
    }
}
