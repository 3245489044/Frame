#nullable enable

namespace Share.Projection;

/// <summary>投影原因码（受控词单源；Host/Carrier/Step 共用）。</summary>
public static class ProjectionReasons
{
    /// <summary>计划无步骤。</summary>
    public const string StepMissing = "projection.step-missing";

    /// <summary>声明步骤未执行。</summary>
    public const string StepSkipped = "projection.step-skipped";

    /// <summary>载体未注册。</summary>
    public const string CarrierUnregistered = "carrier-unregistered";

    /// <summary>载体与上下文不一致。</summary>
    public const string CarrierMismatch = "projection.carrier-mismatch";

    /// <summary>载体投影抛异常。</summary>
    public const string CarrierViolation = "project-violation";

    /// <summary>产物为空。</summary>
    public const string ArtifactNull = "artifact-null";

    /// <summary>源未装配。</summary>
    public const string SourceNotAttached = "projection.source-not-attached";
}
