namespace DataCenter.Protocol.Generation;

/// <summary>
/// 代际读口（DF3 代际链）—— 插件代数 / 内容指纹 / 结构版本的只读消费面。
/// 写口唯属换代流程（Export→Import→generation+1）；本接口不承担写职责。
/// 判据（对齐设计文档 §5.3）：换代前后同一 scope 的 Seq 不重置、值不丢。
/// </summary>
public interface IStateGeneration
{
    /// <summary>插件代数（热更新次数；初始 0）。</summary>
    int Generation { get; }

    /// <summary>内容指纹（换代去重与在途版本判据；未计算时为 null）。</summary>
    string? Fingerprint { get; }

    /// <summary>运行时数据结构版本（跨代际结构演进时递增，触发迁移；初始 1）。</summary>
    int SchemaVersion { get; }
}
