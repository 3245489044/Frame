#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using DataCenter.Protocol.Projection;

namespace DataCenter.Core.ReadModel;

/// <summary>
/// 读模型注册表（<b>单一注册面</b>）：声明 ↔ 实现闭合；装配期 fail-fast（禁静默缺实现/假版本）。
/// </summary>
public sealed class ReadModelRegistry
{
    private sealed record Entry(ReadModelSpec Spec, IReadModelProjector Projector, Func<object, long>? VersionProbe);

    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    /// <summary>注册一条读模型（重复 id / 缺探针 / 非法 TTL ⇒ 抛）。</summary>
    /// <param name="spec">声明。</param>
    /// <param name="projector">投影实现（每模型恰一个）。</param>
    /// <param name="versionProbe">版本探针（<see cref="ReadModelVersionMode.Event"/> / <see cref="ReadModelVersionMode.GraphMembership"/> 必填）。</param>
    public void Register(ReadModelSpec spec, IReadModelProjector projector, Func<object, long>? versionProbe = null)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(projector);

        if (string.IsNullOrWhiteSpace(spec.ModelId))
            throw new ArgumentException("读模型 id 不能为空。", nameof(spec));
        if (spec.Sources is null || spec.Sources.Count == 0)
            throw new ArgumentException($"读模型 '{spec.ModelId}' 未声明源键。", nameof(spec));
        if (spec.Args is null)
            throw new ArgumentException($"读模型 '{spec.ModelId}' 的 args 为 null（空表请用空数组）。", nameof(spec));
        if (spec.Version == ReadModelVersionMode.Ttl && (spec.TtlMs is null || spec.TtlMs <= 0))
            throw new ArgumentException($"读模型 '{spec.ModelId}' 版本源=Ttl，但 ttl 缺失或非正。", nameof(spec));
        if (spec.RequiresVersionProbe && versionProbe is null)
            throw new InvalidOperationException(
                $"读模型 '{spec.ModelId}' 版本源={spec.Version}，但未注册版本探针（禁「假版本」）。");

        lock (_gate)
        {
            if (_entries.ContainsKey(spec.ModelId))
                throw new InvalidOperationException($"读模型 '{spec.ModelId}' 重复注册（每模型恰一个投影器）。");
            _entries[spec.ModelId] = new Entry(spec, projector, versionProbe);
        }
    }

    /// <summary>按模型 id 查声明与投影器（未注册 ⇒ false）。</summary>
    public bool TryGet(string modelId, out ReadModelSpec spec, out IReadModelProjector projector)
        => TryGet(modelId, out spec, out projector, out _);

    /// <summary>按模型 id 查声明、投影器与版本探针（未注册 ⇒ false）。</summary>
    public bool TryGet(
        string modelId,
        out ReadModelSpec spec,
        out IReadModelProjector projector,
        out Func<object, long>? versionProbe)
    {
        spec = null!;
        projector = null!;
        versionProbe = null;
        if (string.IsNullOrWhiteSpace(modelId)) return false;

        lock (_gate)
        {
            if (!_entries.TryGetValue(modelId.Trim(), out var entry)) return false;
            spec = entry.Spec;
            projector = entry.Projector;
            versionProbe = entry.VersionProbe;
            return true;
        }
    }

    /// <summary>已注册声明（声明序快照）。</summary>
    public IReadOnlyList<ReadModelSpec> Specs
    {
        get
        {
            lock (_gate) return _entries.Values.Select(e => e.Spec).ToArray();
        }
    }

    /// <summary>闭合校验：返回违规项（空 = 闭合）。</summary>
    public IReadOnlyList<string> Validate()
    {
        var violations = new List<string>();
        lock (_gate)
        {
            foreach (var entry in _entries.Values)
            {
                var spec = entry.Spec;
                if (string.IsNullOrWhiteSpace(spec.ModelId))
                    violations.Add("readmodel.empty-id");
                if (spec.Sources.Count == 0)
                    violations.Add($"readmodel.no-source:{spec.ModelId}");
                if (spec.Version == ReadModelVersionMode.Ttl && (spec.TtlMs is null || spec.TtlMs <= 0))
                    violations.Add($"readmodel.ttl-invalid:{spec.ModelId}");
                if (spec.RequiresVersionProbe && entry.VersionProbe is null)
                    violations.Add($"readmodel.missing-probe:{spec.ModelId}");
                if (entry.Projector is null)
                    violations.Add($"readmodel.missing-projector:{spec.ModelId}");
            }
        }
        return violations;
    }
}
