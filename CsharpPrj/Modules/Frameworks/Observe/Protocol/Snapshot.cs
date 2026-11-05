namespace Modules.Observe;

/// <summary>可观测快照：B 发布带单调版本的可观测投影快照（供 VersionSnapshot 证据策略 / 读方校验版本推进）。</summary>
public sealed record Snapshot<T>(string ChangeId, string Key, long Version, T Value) : Message(ChangeId);
