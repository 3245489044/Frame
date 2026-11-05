
using Share.LogicComposition.Conversion.Models.Ir;

namespace Share.LogicComposition.Conversion.Models
{
    /// <summary>
    /// JSON 产物：已发射文本 + 根节点 + 可观测计量（对齐「结果可观测」设计原则）。
    /// 供物化写盘（MSBuild Task/CLI）与对拍（与手写 JSON 语义等价）共用。
    /// </summary>
    public sealed class JsonArtifact
    {
        /// <summary>JSON 文本（含可选头部注释）。</summary>
        public string Text { get; }

        /// <summary>头部注释（无则 null）。</summary>
        public string? Header { get; }

        /// <summary>IR 根节点。</summary>
        public ConversionNode Root { get; }

        /// <summary>IR 节点总数（含根）。</summary>
        public int NodeCount { get; }

        /// <summary>文本字节数（UTF-8）。</summary>
        public int ByteLength { get; }

        private JsonArtifact(string text, string? header, ConversionNode root, int nodeCount)
        {
            Text = text;
            Header = header;
            Root = root;
            NodeCount = nodeCount;
            ByteLength = text is null ? 0 : System.Text.Encoding.UTF8.GetByteCount(text);
        }

        public static JsonArtifact Create(string text, ConversionNode root, string? header = null, int? nodeCount = null)
            => new JsonArtifact(
                text,
                header,
                root,
                nodeCount ?? ConversionNodeWalker.Count(root));
    }
}
