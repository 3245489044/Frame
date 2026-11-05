
namespace Share.LogicComposition.Conversion.Models
{
    /// <summary>
    /// 转换选项：一次转换的全部可调参数（发射格式/诊断策略）。
    /// 提供静态预设（对齐既有 <c>CompositionOptions</c> 的预设风格），但每次返回新实例，避免共享可变状态。
    /// </summary>
    public sealed class ConversionOptions
    {
        /// <summary>缩进串（空串 = 单行紧凑输出）。</summary>
        public string Indent { get; set; } = "  ";

        /// <summary>对象键排序（false = 保留插入序，保证作者书写顺序可复现）。</summary>
        public bool SortObjectKeys { get; set; }

        /// <summary>产物头部注释（生成物「勿手改」标识；空 = 不写）。</summary>
        public string? HeaderComment { get; set; }

        /// <summary>是否收集全部诊断（false = 遇首个 Error 即短路）。</summary>
        public bool CollectAllDiagnostics { get; set; } = true;

        /// <summary>IR 最大深度（防御性上限，超出即报错，避免畸形/循环结构导致栈溢出）。</summary>
        public int MaxDepth { get; set; } = 64;

        /// <summary>默认选项（缩进 2 空格）。</summary>
        public static ConversionOptions Default => new ConversionOptions();

        /// <summary>紧凑预设（无缩进/换行）。</summary>
        public static ConversionOptions Compact => new ConversionOptions { Indent = string.Empty };

        /// <summary>写入头部注释。</summary>
        public ConversionOptions WithHeader(string headerComment)
        {
            HeaderComment = headerComment;
            return this;
        }

        public ConversionOptions Clone() => new ConversionOptions
        {
            Indent = Indent,
            SortObjectKeys = SortObjectKeys,
            HeaderComment = HeaderComment,
            CollectAllDiagnostics = CollectAllDiagnostics,
            MaxDepth = MaxDepth,
        };
    }
}
