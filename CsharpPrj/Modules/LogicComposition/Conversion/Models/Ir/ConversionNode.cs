
namespace Share.LogicComposition.Conversion.Models.Ir
{
    /// <summary>IR 节点种类。</summary>
    public enum ConversionNodeKind
    {
        /// <summary>对象（有序键值对）。</summary>
        Object,

        /// <summary>数组。</summary>
        Array,

        /// <summary>标量（null/bool/整数/浮点/字符串）。</summary>
        Scalar,

        /// <summary>原样片段（调用方保证已是目标格式的合法内容）。</summary>
        Raw
    }

    /// <summary>
    /// 转换中间表示（IR）节点基类：与源语法、目标格式均无关的最小结构。
    /// 转换管线只操作 IR，发射器只消费 IR —— 新增目标格式不触碰解析/展开逻辑。
    /// </summary>
    public abstract class ConversionNode
    {
        /// <summary>JSON null 单例。</summary>
        public static ConversionScalar Null => ConversionScalar.Null;

        /// <summary>节点种类。</summary>
        public abstract ConversionNodeKind Kind { get; }
    }
}
