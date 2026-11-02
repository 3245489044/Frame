namespace Share.NodeIo;

/// <summary>文档摘要（内容指纹；乐观并发判据，非 mtime）。</summary>
internal static class NodeDocumentDigest
{
    public static string Of(string json)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
