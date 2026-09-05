#nullable enable
using System.Text;

namespace FrameworkAbstractions.Utilities
{
    public static class JsonErrorContextExtractor
    {
        public static string? ExtractContext(string configPath, int? lineNumber, int contextLines = 3)
        {
            try
            {
                if (!File.Exists(configPath)) return null;

                var lines = File.ReadAllLines(configPath);
                if (lineNumber.HasValue)
                {
                    var errorLine = (int)lineNumber.Value - 1;
                    var startLine = Math.Max(0, errorLine - contextLines);
                    var endLine = Math.Min(lines.Length - 1, errorLine + contextLines);

                    var sb = new StringBuilder();
                    sb.AppendLine($"  JSON 文件: {configPath}");
                    sb.AppendLine($"  错误附近内容 (行 {startLine + 1}-{endLine + 1}):");
                    for (int i = startLine; i <= endLine; i++)
                    {
                        var marker = i == errorLine ? "→ " : "  ";
                        sb.AppendLine($"  {marker}{i + 1,4}| {lines[i]}");
                    }
                    return sb.ToString();
                }
            }
            catch
            {
            }
            return null;
        }

        public static string? ExtractFromJsonException(string configPath, System.Text.Json.JsonException jsonException, int contextLines = 3)
        {
            return ExtractContext(configPath, (int?)jsonException.LineNumber, contextLines);
        }
    }
}
