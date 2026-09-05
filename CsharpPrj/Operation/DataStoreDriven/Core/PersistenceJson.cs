using System.Text.Json;
using Plate.JsonMethod.Serialization;

namespace Operation.DataStoreDriven.Core;

/// <summary>
/// Core 统一 JSON 门户：序列化/反序列化委托平台级 <see cref="Plate.JsonMethod"/>（<see cref="JsonSerializationService"/> + <see cref="JsonSerializerFactory"/>），
/// 收敛 <c>NodeCheckpointStore</c> / <c>NodeRelationDeriver</c> 各自手写的 try/catch 安全反序列化（SingleImplementation / TH-12）。
/// 序列化选项由 <see cref="JsonSerializerFactory.Create"/> 派生并还原既有持久化格式（PascalCase、紧凑、不写 null、宽松转义、无枚举字符串化），
/// 使存量 PayloadJson / RelationsJson 可原样读取，三库收敛后不改写落盘格式。
/// </summary>
internal static class PersistenceJson
{
    /// <summary>节点载荷持久化选项（源 = <see cref="JsonSerializerFactory"/>；还原旧 <c>PersistenceHelper.JsonOptions</c> 的 PascalCase / 数字枚举格式）。</summary>
    public static readonly JsonSerializerOptions NodeOptions = JsonSerializerFactory.Create(
        JsonContext.Compact,
        o =>
        {
            // Compact 基底含 CamelCase + JsonStringEnumConverter；还原为既有格式（不写 null + 宽松转义由基底保留）。
            o.PropertyNamingPolicy = null;
            o.Converters.Clear();
        });

    /// <summary>统一序列化（紧凑、不写 null、宽松转义）。</summary>
    public static string Serialize<T>(T value) => JsonSerializationService.Instance.Serialize(value, NodeOptions);

    /// <summary>安全反序列化：空/空白/非法 JSON → null（收敛 NodeCheckpointStore.Deserialize 与 NodeRelationDeriver 的 try/catch）。</summary>
    public static T? TryDeserialize<T>(string? json, JsonSerializerOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(json)) return default;
        var opts = options ?? NodeOptions;
        try { return JsonSerializationService.Instance.Deserialize<T>(json, opts); }
        catch (JsonException) { return default; }
    }
}
