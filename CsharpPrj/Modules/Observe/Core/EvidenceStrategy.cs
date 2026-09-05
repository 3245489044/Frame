namespace Modules.Observe;

/// <summary>收敛证据能力声明（由传输实现声明）：RenderCallback=B 渲染完成回调（事实）；VersionSnapshot=B 发布单调快照（校验版本推进）；Probe=写后轮询读到新版本（逼近）。</summary>
public enum EvidenceStrategy
{
    RenderCallback,
    VersionSnapshot,
    Probe
}
