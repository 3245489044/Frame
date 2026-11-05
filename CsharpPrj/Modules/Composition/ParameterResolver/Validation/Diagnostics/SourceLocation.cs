
using System.Text;

namespace ParameterResolver.Validation.Diagnostics;

public record SourceLocation(
    string FilePath,
    string JsonPath,
    int? LineNumber = null,
    int? ColumnNumber = null,
    string? ContextSnippet = null)
{
    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.Append(FilePath);

        if (LineNumber.HasValue)
        {
            sb.Append(':');
            sb.Append(LineNumber.Value);
            if (ColumnNumber.HasValue)
            {
                sb.Append(':');
                sb.Append(ColumnNumber.Value);
            }
        }

        sb.Append(" - ");
        sb.Append(JsonPath);

        return sb.ToString();
    }

    public string ToShortString()
        => $"{FilePath} - {JsonPath}";

    public string ToDetailedString()
    {
        var sb = new StringBuilder();
        sb.AppendLine(ToString());

        if (!string.IsNullOrEmpty(ContextSnippet))
        {
            sb.AppendLine("上下文:");
            sb.AppendLine(ContextSnippet);
        }

        return sb.ToString();
    }
}
