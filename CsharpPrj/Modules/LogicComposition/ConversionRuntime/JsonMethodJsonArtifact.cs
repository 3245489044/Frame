
using System.Text;
using System.Text.Json.Nodes;
using Plate.JsonMethod.Diagnostics;

namespace Share.LogicComposition.Conversion.Runtime
{
    /// <summary>
    /// 运行期 JSON 产物：文本 + 节点 + 头注释 + 计量。
    /// 计量统一取自 <see cref="JsonSizeAnalyzer"/>（平台单一实现），不再自算节点数/字节数。
    /// </summary>
    public sealed class JsonMethodJsonArtifact
    {
        /// <summary>完整产物文本（含头注释）。</summary>
        public string Text { get; }

        /// <summary>JSON 正文（不含头注释；标准 JSON，可被任意解析器直接读取）。</summary>
        public string Json { get; }

        /// <summary>IR 经桥接后的 JSON 节点（JSON null → null）。</summary>
        public JsonNode? Node { get; }

        /// <summary>头注释（无则 null）。</summary>
        public string? Header { get; }

        /// <summary>正文计量（<see cref="JsonSizeInfo"/>：RawBytes/ElementCount/EstimatedKb）。</summary>
        public JsonSizeInfo Size { get; }

        /// <summary>完整产物字节数（UTF-8，含头注释）。</summary>
        public int TotalBytes { get; }

        /// <summary>元素计数（= <see cref="JsonSizeInfo.ElementCount"/>）。</summary>
        public int ElementCount => Size.ElementCount;

        /// <summary>正文字节数（= <see cref="JsonSizeInfo.RawBytes"/>）。</summary>
        public long JsonBytes => Size.RawBytes;

        public JsonMethodJsonArtifact(string text, string json, JsonNode? node, string? header, JsonSizeInfo size)
        {
            Text = text;
            Json = json;
            Node = node;
            Header = header;
            Size = size;
            TotalBytes = Encoding.UTF8.GetByteCount(text);
        }
    }
}
