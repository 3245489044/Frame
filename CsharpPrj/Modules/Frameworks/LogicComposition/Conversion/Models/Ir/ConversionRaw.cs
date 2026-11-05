
using System;

namespace Share.LogicComposition.Conversion.Models.Ir
{
    /// <summary>
    /// 原样节点：调用方保证 <see cref="Text"/> 已是目标格式的合法片段（如预渲染 JSON 片段）。
    /// 发射器不做转义与校验 —— 用于需要「逐字保留」的极少数场景，默认不鼓励使用。
    /// </summary>
    public sealed class ConversionRaw : ConversionNode
    {
        public override ConversionNodeKind Kind => ConversionNodeKind.Raw;

        public string Text { get; }

        public ConversionRaw(string text)
        {
            Text = text ?? throw new ArgumentNullException(nameof(text));
        }

        /// <summary>以预渲染 JSON 片段构造（调用方负责其合法性）。</summary>
        public static ConversionRaw Json(string jsonFragment) => new ConversionRaw(jsonFragment);

        public override string ToString() => Text;
    }
}
