
using System;

namespace Share.LogicComposition.Conversion.Models
{
    /// <summary>
    /// 源位置：紧凑作者格式的报错定位（<c>文件:行:列</c>）。
    /// 行列均为 1 基；<see cref="Line"/>=0 表示位置未知（如运行器内部错误）。
    /// </summary>
    public readonly struct SourceSpan
    {
        /// <summary>位置未知（无文件/行信息）。</summary>
        public static readonly SourceSpan None = default;

        /// <summary>源文件路径（可为 null）。</summary>
        public string? File { get; }

        /// <summary>行号（1 基；0 = 未知）。</summary>
        public int Line { get; }

        /// <summary>列号（1 基；0 = 未知）。</summary>
        public int Column { get; }

        /// <summary>跨字符数（0 = 单点位置）。</summary>
        public int Length { get; }

        /// <summary>是否携带可用定位信息。</summary>
        public bool HasPosition => !string.IsNullOrEmpty(File) && Line > 0;

        public SourceSpan(string? file, int line, int column, int length = 0)
        {
            File = file;
            Line = line;
            Column = column;
            Length = length;
        }

        /// <summary>构造带定位的跨度。</summary>
        public static SourceSpan At(string? file, int line, int column, int length = 0)
            => new SourceSpan(file, line, column, length);

        /// <summary>渲染为 <c>文件(行,列)</c>；无定位时为 <c>&lt;unknown&gt;</c>。</summary>
        public override string ToString()
            => HasPosition ? $"{File}({Line},{Column})" : "<unknown>";

        public bool Equals(SourceSpan other)
            => string.Equals(File, other.File, StringComparison.Ordinal)
               && Line == other.Line
               && Column == other.Column
               && Length == other.Length;

        public override bool Equals(object? obj) => obj is SourceSpan other && Equals(other);

        public override int GetHashCode()
        {
            var hash = File is null ? 0 : File.GetHashCode();
            hash = (hash * 397) ^ Line;
            hash = (hash * 397) ^ Column;
            hash = (hash * 397) ^ Length;
            return hash;
        }
    }
}
