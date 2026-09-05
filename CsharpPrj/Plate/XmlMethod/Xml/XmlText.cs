using System.Text;

namespace Plate.XmlMethod.Xml
{
    /// <summary>
    /// XML 文本转义工具：把用户/数据内容安全地转成 XML 文本或属性值。
    /// 在处理 HTML/SVG 时需要"文本内容不可当标签解析"，均应经 XmlText 产出，避免注入。
    /// </summary>
    public static class XmlText
    {
        /// <summary>转义后作为元素文本/属性值使用（保留引号实体，属性值也安全）。</summary>
        public static string Escape(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            var sb = new StringBuilder(value.Length);
            foreach (var ch in value)
            {
                switch (ch)
                {
                    case '&': sb.Append("&amp;"); break;
                    case '<': sb.Append("&lt;"); break;
                    case '>': sb.Append("&gt;"); break;
                    case '"': sb.Append("&quot;"); break;
                    case '\'': sb.Append("&apos;"); break;
                    default: sb.Append(ch); break;
                }
            }
            return sb.ToString();
        }

        /// <summary>转换为 XML 带来的可读友好值：null → 空串；bool → 小写；其余 ToString。</summary>
        public static string ToAttrValue(object? value)
        {
            return value switch
            {
                null => string.Empty,
                bool b => b ? "true" : "false",
                _ => value.ToString() ?? string.Empty,
            };
        }
    }
}