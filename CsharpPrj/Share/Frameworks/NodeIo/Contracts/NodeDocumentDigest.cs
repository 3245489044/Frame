using Modules.Node;

namespace Share.NodeIo;

/// <summary>
/// 文档摘要（内容指纹；乐观并发判据，非 mtime）。
/// 口径单源 = 框架 <see cref="NodeJson.Sha256Hex"/>：SHA256 → 大写 hex（64 字符），与载体 <c>Digest</c> 可直接互操作。
/// </summary>
public static class NodeDocumentDigest
{
    public static string Of(string json) => NodeJson.Sha256Hex(json);
}
