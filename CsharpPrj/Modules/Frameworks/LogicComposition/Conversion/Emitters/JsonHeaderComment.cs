
using System;
using System.Text;

namespace Share.LogicComposition.Conversion.Emitters
{
    /// <summary>
    /// JSONC 头注释渲染（唯一实现）：生成物「勿手改」标识。
    /// 消费方（如 <c>TestInfrastructure.DataDriven.SpecJson</c>）以 <c>JsonCommentHandling.Skip</c> 读取，故注释安全。
    /// 分析器侧与运行期侧共用本渲染，避免两处格式漂移。
    /// </summary>
    public static class JsonHeaderComment
    {
        /// <summary>每行前缀。</summary>
        public const string LinePrefix = "// ";

        /// <summary>渲染头注释（每行 <c>// </c> 前缀 + 换行）；null/空 → 空串。</summary>
        public static string Render(string? comment)
        {
            if (string.IsNullOrEmpty(comment)) return string.Empty;

            var lines = comment!.Split('\n');
            var builder = new StringBuilder(comment!.Length + lines.Length * LinePrefix.Length + 1);

            for (var i = 0; i < lines.Length; i++)
            {
                builder.Append(LinePrefix).Append(lines[i].TrimEnd('\r')).Append('\n');
            }

            return builder.ToString();
        }
    }
}
