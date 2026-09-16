using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Strategy.Observability.Ports;

/// <summary>
/// 端口调用输入（E10）：位置实参 + 具名实参 + 源文本，全部由调用方直传（不再有手写编码解析）。
/// </summary>
public interface IPortInput
{
    /// <summary>端口键（接口/类型.方法 形态）。</summary>
    string PortKey { get; }

    /// <summary>位置实参（声明序即语义）。</summary>
    IReadOnlyList<object?> Args { get; }

    /// <summary>具名实参（结构化 JSON 直传）。</summary>
    IReadOnlyDictionary<string, object?> With { get; }

    /// <summary>内联源文本（供文本解析类端口消费）。</summary>
    string? Text { get; }

    /// <summary>来源锚（用例中的 <c>src</c> 元数据；纯诊断用）。</summary>
    string? Src { get; }

    /// <summary>实参指纹（确定性短哈希，供"同参重放"识别；不可 JSON 序列化的对象实参退化为类型指纹）。</summary>
    string ArgsHash { get; }
}

/// <summary>端口调用输入默认实现（E10）。</summary>
public sealed class PortInput : IPortInput
{
    private static readonly JsonSerializerOptions HashJson = new() { WriteIndented = false };

    private readonly IReadOnlyList<object?> _args;
    private readonly IReadOnlyDictionary<string, object?> _with;
    private string? _hash;

    public PortInput(
        string portKey,
        IEnumerable<object?>? args = null,
        IReadOnlyDictionary<string, object?>? with = null,
        string? text = null,
        string? src = null)
    {
        PortKey = string.IsNullOrWhiteSpace(portKey)
            ? throw new ArgumentException("端口键不能为空", nameof(portKey))
            : portKey;
        _args = (args ?? []).ToArray();
        _with = with ?? new Dictionary<string, object?>(StringComparer.Ordinal);
        Text = text;
        Src = src;
    }

    public string PortKey { get; }

    public IReadOnlyList<object?> Args => _args;

    public IReadOnlyDictionary<string, object?> With => _with;

    public string? Text { get; }

    public string? Src { get; }

    public string ArgsHash => _hash ??= Compute(_args);

    /// <summary>确定性实参指纹：JSON 序列化后取 SHA256 前 12 位十六进制。</summary>
    private static string Compute(IReadOnlyList<object?> args)
    {
        try
        {
            return Digest(JsonSerializer.Serialize(args, HashJson));
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            // 对象直投实参（产品实例 / 委托 / 含环对象）不可 JSON 序列化：退化按运行时类型指纹。
            // 指纹是记录/埋点的读取值，取值本身不得改变调用语义（同型同参仍得同指纹）。
            return Digest(string.Join("|", args.Select(argument => argument?.GetType().FullName ?? "null")));
        }
    }

    private static string Digest(string text)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..12];
}
