namespace Share.NodeIo;

/// <summary>补丁算子（JSON Pointer 语义；零语言语义）。</summary>
public enum NodePatchOp
{
    /// <summary>替换目标位置（不存在则创建）。</summary>
    Set,

    /// <summary>对象合并（目标与值均须为对象；同名键后者胜）。</summary>
    Merge,

    /// <summary>数组追加（目标不存在则创建数组）。</summary>
    Append,

    /// <summary>移除目标（不存在视为成功，幂等）。</summary>
    Remove,
}

/// <summary>单条文档补丁（<see cref="Path"/> = RFC 6901 JSON Pointer，根为空串）。</summary>
public sealed record NodeDocumentPatch(string Path, NodePatchOp Op, string? ValueJson = null)
{
    public static NodeDocumentPatch Set(string path, string valueJson) => new(path, NodePatchOp.Set, valueJson);

    public static NodeDocumentPatch Merge(string path, string valueJson) => new(path, NodePatchOp.Merge, valueJson);

    public static NodeDocumentPatch Append(string path, string valueJson) => new(path, NodePatchOp.Append, valueJson);

    public static NodeDocumentPatch Remove(string path) => new(path, NodePatchOp.Remove);
}
