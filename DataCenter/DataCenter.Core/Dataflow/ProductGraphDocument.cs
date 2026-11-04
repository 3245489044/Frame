using System;
using System.Collections.Generic;
using System.Text.Json;

namespace DataCenter.Core.Dataflow;

/// <summary>
/// 产品图产物中的一条产品（<c>product.graph.generated.json</c> 的 <c>products[]</c>）—— 中性形态。
/// <para>纪律：只承载声明事实（键 / 层 / 角色 / 字段契约 / 幂等键 / 派生绑定），不含呈现词；
/// 宿主侧映射（→ <c>ProductDeclaration</c> / UI 面选择）由各适配器承担，本层不做判定。</para>
/// </summary>
/// <param name="ProductId">产品规范键（层内唯一）。</param>
/// <param name="LevelId">层级轴引用。</param>
/// <param name="Role">角色轴取值（开放受控词：source / derived / transform / edge …）。</param>
/// <param name="InFields">入字段契约。</param>
/// <param name="OutFields">出字段契约（对外承诺）。</param>
/// <param name="IdempotencyKey">幂等键表达式。</param>
/// <param name="Version">产品版本。</param>
/// <param name="Stage">产品阶段（null = 未声明）。</param>
/// <param name="DeriveId">派生绑定（<c>[Derive]</c> 的 Id；null = 本产品不是派生镜像）。</param>
/// <param name="Derives">字段级血缘（<c>源字段-&gt;目标字段</c>，<c>;</c> 分隔；null = 未声明）。</param>
/// <param name="DependsOn">上游产品引用。</param>
public sealed record ProductGraphProduct(
    string ProductId,
    string LevelId,
    string Role,
    IReadOnlyList<string> InFields,
    IReadOnlyList<string> OutFields,
    string IdempotencyKey,
    int Version,
    string? Stage,
    string? DeriveId,
    string? Derives,
    IReadOnlyList<string> DependsOn);

/// <summary>
/// 产品图产物中的一条面（<c>faces[]</c>）—— 肯定式可见性白名单的结构事实。
/// </summary>
/// <param name="FaceId">面标识。</param>
/// <param name="ProductId">面引用的产品键。</param>
/// <param name="Owners">接受的 owner 取值（空 = 未声明）。</param>
/// <param name="Channels">接受的 channel 取值（空 = 未声明）。</param>
/// <param name="Fields">渲染字段（null = 未声明，取产品全部出字段）。</param>
public sealed record ProductGraphFace(
    string FaceId,
    string ProductId,
    IReadOnlyList<string> Owners,
    IReadOnlyList<string> Channels,
    IReadOnlyList<string>? Fields);

/// <summary>
/// 产品图产物的中性文档（<c>levels</c> / <c>products</c> / <c>faces</c>）—— **唯一解析点**。
/// <para>
/// 纪律：① 保声明序与字段序（等价门可逐字段比对）；② 缺段 = 空集（不伪造）；
/// ③ 形态非法（缺 id / 类型不符）⇒ 抛 <see cref="InvalidOperationException"/>（检出即可见化，
/// 由适配器记账，禁静默丢弃半截声明）。
/// </para>
/// </summary>
/// <param name="Products">产品声明集（声明序）。</param>
/// <param name="Faces">面声明集（声明序）。</param>
public sealed record ProductGraphDocument(
    IReadOnlyList<ProductGraphProduct> Products,
    IReadOnlyList<ProductGraphFace> Faces)
{
    /// <summary>零声明（无产品图产物时的零值；适配器据此产空图而非伪造产品）。</summary>
    public static ProductGraphDocument Empty { get; } = new([], []);

    /// <summary>解析产品图 JSON（形态非法即抛，禁静默补默认值）。</summary>
    /// <param name="json">产品图 JSON 文本。</param>
    public static ProductGraphDocument Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Empty;

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("产品图形态非法：根节点须为对象");
        }

        var products = new List<ProductGraphProduct>();
        if (root.TryGetProperty("products", out var productArray) && productArray.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var element in productArray.EnumerateArray())
            {
                products.Add(ReadProduct(element, index));
                index++;
            }
        }

        var faces = new List<ProductGraphFace>();
        if (root.TryGetProperty("faces", out var faceArray) && faceArray.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var element in faceArray.EnumerateArray())
            {
                faces.Add(ReadFace(element, index));
                index++;
            }
        }

        return new ProductGraphDocument(products, faces);
    }

    private static ProductGraphProduct ReadProduct(JsonElement element, int index)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"产品图形态非法：products[{index}] 不是对象");
        }

        var productId = ReadRequiredString(element, "id", $"products[{index}].id");

        return new ProductGraphProduct(
            productId,
            ReadString(element, "level") ?? string.Empty,
            ReadString(element, "role") ?? string.Empty,
            ReadStringArray(element, "in"),
            ReadStringArray(element, "out"),
            ReadString(element, "idempotency") ?? string.Empty,
            ReadInt32(element, "version") ?? 1,
            ReadString(element, "stage"),
            ReadString(element, "derive"),
            ReadString(element, "derives"),
            ReadStringArray(element, "dependsOn"));
    }

    private static ProductGraphFace ReadFace(JsonElement element, int index)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"产品图形态非法：faces[{index}] 不是对象");
        }

        return new ProductGraphFace(
            ReadRequiredString(element, "id", $"faces[{index}].id"),
            ReadString(element, "product") ?? string.Empty,
            ReadStringArray(element, "owners"),
            ReadStringArray(element, "channels"),
            element.TryGetProperty("fields", out var fields) && fields.ValueKind == JsonValueKind.Array
                ? ReadStringArray(element, "fields")
                : null);
    }

    private static string ReadRequiredString(JsonElement element, string name, string location)
    {
        var value = ReadString(element, name);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"产品图形态非法：{location} 缺失或为空");
        }

        return value;
    }

    private static string? ReadString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var property)) return null;
        return property.ValueKind == JsonValueKind.String ? property.GetString() : null;
    }

    private static int? ReadInt32(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var property)) return null;
        return property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var value) ? value : null;
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var values = new List<string>();
        foreach (var item in property.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String) continue;
            var value = item.GetString();
            if (!string.IsNullOrWhiteSpace(value)) values.Add(value);
        }

        return values;
    }
}

