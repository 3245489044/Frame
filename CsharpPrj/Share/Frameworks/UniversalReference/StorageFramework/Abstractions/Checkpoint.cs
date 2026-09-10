using System;

namespace UniversalWorkflowFramework.StorageFramework.Abstractions;

/// <summary>
/// 一个 checkpoint（LangGraph 兼容）。ChannelJson/VersionsJson 为序列化 Channel 值与版本元数据。
/// </summary>
public sealed class Checkpoint
{
    public string ThreadId { get; set; } = string.Empty;

    public string CheckpointNs { get; set; } = string.Empty;

    public string CheckpointId { get; set; } = string.Empty;

    public string? ParentCheckpointId { get; set; }

    public string ChannelJson { get; set; } = "{}";

    public string VersionsJson { get; set; } = "{}";

    public long Step { get; set; }

    public string? MetadataJson { get; set; }
}
