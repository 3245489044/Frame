
using Share.LogicComposition.Conversion.Abstractions;
using Share.LogicComposition.Conversion.Models;
using Share.LogicComposition.Conversion.Models.Ir;

namespace Share.LogicComposition.Conversion.Emitters
{
    /// <summary>
    /// JSON 产物发射器：在 <see cref="JsonTextEmitter"/> 之上补充可观测计量（节点数/字节数/头部），
    /// 供物化写盘与「与手写 JSON 语义等价」对拍使用。文本发射逻辑唯一来源仍是 <see cref="JsonTextEmitter"/>。
    /// </summary>
    public sealed class JsonArtifactEmitter : IArtifactEmitter<JsonArtifact>
    {
        /// <summary>默认单例（复用文本发射器单例）。</summary>
        public static readonly JsonArtifactEmitter Instance = new JsonArtifactEmitter(JsonTextEmitter.Instance);

        private readonly JsonTextEmitter _textEmitter;

        public JsonArtifactEmitter(JsonTextEmitter? textEmitter = null)
            => _textEmitter = textEmitter ?? JsonTextEmitter.Instance;

        public ConversionResult<JsonArtifact> Emit(ConversionNode node, ConversionOptions? options = null)
        {
            var textResult = _textEmitter.Emit(node, options);
            if (!textResult.IsSuccess || textResult.Artifact is null)
                return ConversionResult<JsonArtifact>.Failure(textResult.Diagnostics);

            var artifact = JsonArtifact.Create(
                textResult.Artifact,
                node,
                options?.HeaderComment);

            return ConversionResult<JsonArtifact>.Success(artifact, textResult.Diagnostics);
        }
    }
}
