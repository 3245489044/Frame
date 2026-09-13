
using System;

namespace Share.LogicComposition.Conversion.Models.Ir
{
    /// <summary>标量种类（决定 JSON 渲染形式）。</summary>
    public enum ConversionScalarKind
    {
        Null,

        Boolean,

        /// <summary>64 位整数（避免浮点格式化失真）。</summary>
        Integer,

        /// <summary>双精度浮点。</summary>
        Number,

        String
    }

    /// <summary>标量节点（null/bool/整数/浮点/字符串）。</summary>
    public sealed class ConversionScalar : ConversionNode
    {
        public override ConversionNodeKind Kind => ConversionNodeKind.Scalar;

        public ConversionScalarKind ScalarKind { get; }

        /// <summary>值的 CLR 形态（null/bool/long/double/string）。</summary>
        public object? Value { get; }

        /// <summary>JSON null 单例（与 <see cref="ConversionNode.Null"/> 同一实例，此处收窄返回类型）。</summary>
        public static new ConversionScalar Null { get; } = new ConversionScalar(ConversionScalarKind.Null, null);

        public static ConversionScalar True { get; } = new ConversionScalar(ConversionScalarKind.Boolean, true);

        public static ConversionScalar False { get; } = new ConversionScalar(ConversionScalarKind.Boolean, false);

        private ConversionScalar(ConversionScalarKind scalarKind, object? value)
        {
            ScalarKind = scalarKind;
            Value = value;
        }

        public static ConversionScalar Of(bool value) => value ? True : False;

        public static ConversionScalar Of(long value) => new ConversionScalar(ConversionScalarKind.Integer, value);

        public static ConversionScalar Of(int value) => Of((long)value);

        public static ConversionScalar Of(double value) => new ConversionScalar(ConversionScalarKind.Number, value);

        /// <summary>字符串标量；null 输入降级为 JSON null。</summary>
        public static ConversionScalar Of(string? value)
            => value is null ? Null : new ConversionScalar(ConversionScalarKind.String, value);

        /// <summary>从 CLR 对象自动分派（支持 null/bool/整数/浮点/字符串；其余走 ToString）。</summary>
        public static ConversionScalar FromObject(object? value)
        {
            switch (value)
            {
                case null: return Null;
                case bool b: return Of(b);
                case string s: return Of(s);
                case int i: return Of((long)i);
                case long l: return Of(l);
                case short sh: return Of((long)sh);
                case byte by: return Of((long)by);
                case double d: return Of(d);
                case float f: return Of((double)f);
                case decimal m: return Of((double)m);
                case Enum e: return Of(e.ToString());
                default: return Of(value.ToString());
            }
        }

        public override string ToString() => Value?.ToString() ?? "null";
    }
}
