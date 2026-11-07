using System.Text.Json;

namespace BasicMethod.Declarative.Tools.LedgerForge;

/// <summary>
/// 键集文件读取（唯一实现）：JSON 字符串数组；或对象（取 properties 名）；或 <c>{"keys":[...]}</c>。
/// <para>读取失败抛（由调用方转「该面未装载/不可判定」，不得静默为空集）。</para>
/// </summary>
internal static class FaceKeySetReader
{
    internal static IReadOnlyList<string> Read(string path)
    {
        using var stream = File.OpenRead(path);
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;

        if (root.ValueKind == JsonValueKind.Array)
        {
            return root.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString()!)
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .ToList();
        }

        if (root.ValueKind == JsonValueKind.Object)
        {
            if (root.TryGetProperty("keys", out var keys) && keys.ValueKind == JsonValueKind.Array)
            {
                return keys.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.String)
                    .Select(item => item.GetString()!)
                    .Where(key => !string.IsNullOrWhiteSpace(key))
                    .ToList();
            }

            return root.EnumerateObject().Select(property => property.Name).ToList();
        }

        throw new InvalidOperationException("键集文件须为 JSON 数组或对象");
    }
}
