using System.Text.Json;
using System.Text.Json.Serialization;

namespace CSG.Unified;

public class CanonicalForm
{
    public List<CanonicalNode> Nodes { get; init; } = new();

    public List<CanonicalEdge> Edges { get; init; } = new();

    public Dictionary<string, object?> Metadata { get; init; } = new();

    public FormState State { get; set; } = FormState.Empty;


    public const string UriScheme = "csg";

    public object? ResolveUri(string uri)
    {
        if (string.IsNullOrEmpty(uri))
            throw new ArgumentException("URI 不能为空", nameof(uri));

        if (!uri.StartsWith(UriScheme + "://", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"URI 必须以 '{UriScheme}://' 开头", nameof(uri));

        var path = uri[(UriScheme.Length + 3)..];
        var segments = path.Split('/');

        if (segments.Length == 0)
            return this;

        return segments[0] switch
        {
            "nodes" or "node" => segments.Length switch
            {
                1 => Nodes,
                2 => Nodes.FirstOrDefault(n => n.Id == segments[1]),
                _ => null
            },

            "edges" or "edge" => segments.Length switch
            {
                1 => Edges,
                2 => Edges.FirstOrDefault(e => e.Id == segments[1]),
                _ => null
            },

            "metadata" => segments.Length switch
            {
                1 => Metadata,
                2 => Metadata.GetValueOrDefault(segments[1]),
                _ => null
            },

            _ => null
        };
    }

    public static List<UriTemplate> ListUriTemplates()
    {
        return new List<UriTemplate>
        {
            new("csg://nodes", "所有节点列表"),
            new("csg://node/{id}", "指定节点"),
            new("csg://edges", "所有边列表"),
            new("csg://edge/{id}", "指定边"),
            new("csg://metadata", "所有元数据"),
            new("csg://metadata/{key}", "指定元数据"),
        };
    }


    public string ToJson(bool indented = true)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = indented,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            // 枚举以字符串序列化（"State"/"Transition"）——对齐 csg-canonical-form.schema.json 契约，
            // 与 TS 侧 csg-protocol（字符串枚举）直接对接（跨语言 CanonicalForm 交换）。
            Converters = { new JsonStringEnumConverter() }
        };
        return JsonSerializer.Serialize(this, options);
    }

    public static CanonicalForm? FromJson(string json)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            // 兼容字符串枚举（TS 侧发出 "State"）；缺省 System.Text.Json 只接受数字，会破坏 TS→C# 对接。
            Converters = { new JsonStringEnumConverter() }
        };
        return JsonSerializer.Deserialize<CanonicalForm>(json, options);
    }
}

