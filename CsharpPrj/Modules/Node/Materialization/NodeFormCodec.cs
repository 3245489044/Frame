using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Modules.Node.Placement;

namespace Modules.Node.Materialization;

public sealed class NodeFormCodecException : InvalidOperationException
{
    public NodeFormCodecException(string message)
        : base(message)
    {
    }
}

/// <summary>形态策略（G12：形态 ↔ 值的编解码单源；策略键 = <see cref="PlacementForm"/>）。</summary>
public interface INodeFormStrategy
{
    PlacementForm Form { get; }

    /// <summary>可逆性：摘要类为有损（解码不还原原文，须显式可见化）。</summary>
    bool IsLossless { get; }

    string Encode(string json);

    string Decode(string payload);
}

/// <summary>raw：外部原文整存/原样读回。</summary>
public sealed class RawFormStrategy : INodeFormStrategy
{
    public PlacementForm Form => PlacementForm.Raw;

    public bool IsLossless => true;

    public string Encode(string json) => json;

    public string Decode(string payload) => payload;
}

/// <summary>view：规范化 JSON（键序稳定 + 紧凑输出），读回即规范视图。</summary>
public sealed class ViewFormStrategy : INodeFormStrategy
{
    public PlacementForm Form => PlacementForm.View;

    public bool IsLossless => true;

    public string Encode(string json) => Normalize(json);

    public string Decode(string payload) => Normalize(payload);

    private static string Normalize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return json;
        }

        try
        {
            var node = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });

            return node is null ? json : node.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
        }
        catch (JsonException)
        {
            // 非 JSON 原文：保持原样（规范失败不伪造结构）
            return json;
        }
    }
}

/// <summary>digest：内容哈希（N4/N5 幂等与冲突判据的载体）。</summary>
public sealed class DigestFormStrategy : INodeFormStrategy
{
    public PlacementForm Form => PlacementForm.Digest;

    public bool IsLossless => false;

    public string Encode(string json)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes);
    }

    public string Decode(string payload) => payload;
}

/// <summary>形态编解码唯一实现（读写两侧共用；不认识载体，P22 不适用）。</summary>
public sealed class NodeFormCodec
{
    private readonly Dictionary<PlacementForm, INodeFormStrategy> _strategies = new();

    public NodeFormCodec()
    {
        Register(new RawFormStrategy());
        Register(new ViewFormStrategy());
        Register(new DigestFormStrategy());
    }

    public IReadOnlyCollection<PlacementForm> SupportedForms => _strategies.Keys.ToArray();

    public void Register(INodeFormStrategy strategy) => _strategies[strategy.Form] = strategy;

    public bool Supports(PlacementForm form) => _strategies.ContainsKey(form);

    public bool IsLossless(PlacementForm form) => Strategy(form).IsLossless;

    public string Encode(PlacementForm form, string json) => Strategy(form).Encode(json);

    public string Decode(PlacementForm form, string payload) => Strategy(form).Decode(payload);

    public string StrategyName(PlacementForm form) => Strategy(form).GetType().Name;

    private INodeFormStrategy Strategy(PlacementForm form) =>
        _strategies.TryGetValue(form, out var strategy)
            ? strategy
            : throw new NodeFormCodecException($"形态编解码未注册: {form}");
}
