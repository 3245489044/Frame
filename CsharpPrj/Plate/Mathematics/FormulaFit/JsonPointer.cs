using System.Globalization;
using System.Text.Json;

namespace MathematicalAlgorithms.FormulaFit;

/// <summary>JSON Pointer（RFC 6901）解析；只依赖 System.Text.Json。</summary>
internal static class JsonPointer
{
    public static bool TryResolve(JsonElement root, string pointer, out JsonElement value)
    {
        value = root;

        if (string.IsNullOrEmpty(pointer))
            return true;

        if (pointer[0] != '/')
            return false;

        foreach (string rawToken in pointer.Split('/').Skip(1))
        {
            string token = rawToken.Replace("~1", "/", StringComparison.Ordinal)
                .Replace("~0", "~", StringComparison.Ordinal);

            if (value.ValueKind == JsonValueKind.Object)
            {
                if (!value.TryGetProperty(token, out value))
                    return false;
            }
            else if (value.ValueKind == JsonValueKind.Array)
            {
                if (!int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out int index))
                    return false;

                if (index < 0 || index >= value.GetArrayLength())
                    return false;

                value = value[index];
            }
            else
            {
                return false;
            }
        }

        return true;
    }

    public static string LastToken(string pointer)
    {
        if (string.IsNullOrEmpty(pointer))
            return string.Empty;

        int slash = pointer.LastIndexOf('/');
        string token = slash >= 0 ? pointer[(slash + 1)..] : pointer;
        return token.Replace("~1", "/", StringComparison.Ordinal)
            .Replace("~0", "~", StringComparison.Ordinal);
    }
}
