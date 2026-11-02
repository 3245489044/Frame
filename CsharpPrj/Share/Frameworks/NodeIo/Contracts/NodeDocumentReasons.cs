namespace Share.NodeIo;

/// <summary>
/// 节点文档 IO 原因词**单源**：读/写两侧共用（禁各处手写 empty/unavailable 同形出口）。
/// </summary>
public static class NodeDocumentReasons
{
    public const string DomainMissing = "domain-missing";
    public const string LocMissing = "loc-missing";
    public const string KeyContextMissing = "key-context-missing";
    public const string KeyContextIsPositionKey = "key-context-is-position-key";
    public const string PlacementUnresolved = "placement-unresolved";
    public const string PlacementKeyUnresolved = "placement-key-unresolved";
    public const string NoWritablePlacement = "no-writable-placement";
    public const string NoReadablePlacement = "no-readable-placement";
    public const string IdentityUnparsable = "identity-unparsable";
    public const string FormMismatch = "form-mismatch";
    public const string LocUnresolved = "loc-unresolved";
    public const string DomainUnregistered = "domain-unregistered";
    public const string KeyUnresolved = "key-unresolved";
    public const string KeyAmbiguous = "key-ambiguous";
    public const string ProjectionUnknown = "projection-unknown";
    /// <summary>投影失败原因前缀（E9）：投影内部解析失败 ⇒ <c>payload-rejected:&lt;reason&gt;</c>。</summary>
    public const string ProjectionPayloadRejected = "payload-rejected";

    public const string PayloadMissing = "payload-missing";
    public const string PayloadNotObject = "payload-not-object";
    public const string PayloadPartitionMismatch = "payload-partition-mismatch";
    public const string PayloadRequiredMissing = "payload-required-missing";
    public const string PayloadRoundsMissing = "payload-rounds-missing";
    public const string PayloadFrameInvalid = "payload-frame-invalid";

    public const string PatchEmpty = "patch-empty";
    public const string PatchKeyMissing = "patch-key-missing";
    public const string PatchApplyFailed = "patch-apply-failed";
    public const string PatchDigestMismatch = "patch-digest-mismatch";

    public const string StreamFormRequired = "stream-form-required";
    public const string StreamEmpty = "stream-empty";

    public const string CarrierUnbound = "carrier-unbound";
    public const string DeleteKeyMissing = "delete-key-missing";
}
