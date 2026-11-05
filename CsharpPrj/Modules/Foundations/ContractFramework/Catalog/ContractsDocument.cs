using System.Text.Json;
using System.Text.Json.Serialization;

namespace ContractFramework.Catalog;

/// <summary>
/// <c>contracts.json</c> 的 POCO（字段名对齐声明面；读侧宽容，写侧略空）。
/// <para>C#（本类）与 PowerShell（<c>Check-ContractRegistry.ps1</c>）读**同一个文件** ⇒ 无第二份声明。</para>
/// </summary>
public sealed class ContractsDocument
{
    /// <summary>Schema 哨兵（<c>contracts/v1</c>）。</summary>
    [JsonPropertyName("$schema")]
    public string? Schema { get; set; }

    /// <summary>契约条目集。</summary>
    [JsonPropertyName("contracts")]
    public List<ContractEntry> Contracts { get; set; } = [];

    /// <summary>声明文件路径（非 JSON 字段；加载时回填，供诊断）。</summary>
    [JsonIgnore]
    public string? Source { get; set; }

    /// <summary>宽容读取选项（注释 / 尾逗号 / 大小写不敏感 / 略空）。</summary>
    public static JsonSerializerOptions SerializerOptions { get; } = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    /// <summary>单契约条目（声明面字段；缺项以 <c>null</c> 表达"声明不完整"）。</summary>
    public sealed class ContractEntry
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("invariant")]
        public string? Invariant { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("semantics")]
        public string? Semantics { get; set; }

        [JsonPropertyName("carrier")]
        public CarrierEntry? Carrier { get; set; }

        [JsonPropertyName("tier")]
        public string? Tier { get; set; }

        [JsonPropertyName("effect")]
        public string? Effect { get; set; }

        [JsonPropertyName("host")]
        public HostEntry? Host { get; set; }

        [JsonPropertyName("evidence")]
        public EvidenceEntry? Evidence { get; set; }
    }

    /// <summary>载体节点。</summary>
    public sealed class CarrierEntry
    {
        [JsonPropertyName("kind")]
        public string? Kind { get; set; }

        [JsonPropertyName("facts")]
        public List<string> Facts { get; set; } = [];
    }

    /// <summary>执行体节点。</summary>
    public sealed class HostEntry
    {
        [JsonPropertyName("kind")]
        public string? Kind { get; set; }

        [JsonPropertyName("impl")]
        public string? Impl { get; set; }
    }

    /// <summary>证据节点。</summary>
    public sealed class EvidenceEntry
    {
        [JsonPropertyName("recomputeCmd")]
        public string? RecomputeCmd { get; set; }

        [JsonPropertyName("positive")]
        public string? Positive { get; set; }

        [JsonPropertyName("negative")]
        public string? Negative { get; set; }
    }
}

/// <summary>
/// 仓库级声明索引（<c>contracts.index.json</c>；与 <c>modules.json</c> 同构）。
/// </summary>
public sealed class ContractsIndexDocument
{
    [JsonPropertyName("$schema")]
    public string? Schema { get; set; }

    /// <summary>声明文件路径（仓库根相对，<c>/</c> 分隔）。</summary>
    [JsonPropertyName("files")]
    public List<string> Files { get; set; } = [];

    /// <summary>G3「未声明」扫描 glob（仓库根相对；空 = 跳过 G3 并可见化）。</summary>
    [JsonPropertyName("scanGlobs")]
    public List<string> ScanGlobs { get; set; } = [];

    [JsonIgnore]
    public string? Source { get; set; }
}
