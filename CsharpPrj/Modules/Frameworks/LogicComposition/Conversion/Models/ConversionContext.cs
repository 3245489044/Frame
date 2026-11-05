
using System;
using System.Collections.Generic;

namespace Share.LogicComposition.Conversion.Models
{
    /// <summary>
    /// 转换上下文：一次转换的共享状态（选项 + 源文本 + 诊断汇集器 + 阶段间共享槽）。
    /// 单一实例贯穿全部阶段，避免各阶段自建状态导致诊断丢失/选项漂移。
    /// </summary>
    public sealed class ConversionContext
    {
        public ConversionOptions Options { get; }

        /// <summary>源文件路径（用于诊断定位）。</summary>
        public string? SourceFile { get; }

        /// <summary>源文本（紧凑作者格式原文；阶段可回读原始文本）。</summary>
        public string? SourceText { get; }

        public IConversionDiagnosticSink Diagnostics { get; }

        /// <summary>阶段间共享槽（如解析产物、算子白名单；键大小写敏感）。</summary>
        public IDictionary<string, object?> Items { get; }

        public ConversionContext(
            ConversionOptions? options = null,
            string? sourceFile = null,
            string? sourceText = null,
            IConversionDiagnosticSink? diagnostics = null)
        {
            Options = options ?? ConversionOptions.Default;
            SourceFile = sourceFile;
            SourceText = sourceText;
            Diagnostics = diagnostics ?? new DiagnosticSink();
            Items = new Dictionary<string, object?>(StringComparer.Ordinal);
        }

        /// <summary>派生子上下文（换源文件/源文本，保留选项与诊断汇集器）。</summary>
        public ConversionContext WithSource(string? sourceFile, string? sourceText)
            => new ConversionContext(Options, sourceFile, sourceText, Diagnostics);

        /// <summary>在源文件中构造定位（行列 1 基）。</summary>
        public SourceSpan SpanAt(int line, int column, int length = 0)
            => new SourceSpan(SourceFile, line, column, length);

        /// <summary>读取共享槽（类型不符返回 null）。</summary>
        public T? GetItem<T>(string key) where T : class
            => Items.TryGetValue(key, out var value) ? value as T : null;

        /// <summary>写入共享槽。</summary>
        public void SetItem(string key, object? value)
        {
            if (key is null) throw new ArgumentNullException(nameof(key));
            Items[key] = value;
        }
    }
}
