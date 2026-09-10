using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Plate.JsonMethod.Serialization;

namespace Strategy.DecisionRules.Configurable
{
    /// <summary>
    /// 文件型决策依据供给源（动态注入落地）—— 按「决策点 → 文件」约定从外部 JSON
    /// 供给 <see cref="DecisionControlSpec"/>，支持 LastWriteTime 失效热重载；加载/校验失败保留已加载旧版
    /// （单写者 + 版本防竞态，复刻既有 <c>FileRuleSetLoader</c> 模式）。经 <see cref="IDecisionControlSource"/>
    /// 注入 <see cref="ConfigDrivenDecisionController"/>，即可让"决策依据"随文件改动免重启动态演算。
    /// </summary>
    public sealed class FileControlSource : IDecisionControlSource
    {
        private readonly Func<string, string> _fileFor;   // 决策点 → 文件路径
        private readonly bool _reloadOnChange;
        private readonly object _lock = new();
        private readonly Dictionary<string, CachedEntry> _cache = new(StringComparer.Ordinal);
        private readonly JsonSerializationService _serializer = JsonSerializationService.Instance;

        private sealed record CachedEntry(DecisionControlSpec? Spec, DateTime LastWrite, bool LoadedOnce);

        /// <param name="fileFor">决策点 → 文件绝对路径映射（可传 null 用默认 `Path.Combine(dir, decisionPoint + ".json")`）。</param>
        /// <param name="directory">默认按决策点找文件的目录（仅当 fileFor 为 null 时生效）。</param>
        /// <param name="reloadOnChange">true = 按文件 LastWriteTime 失效缓存，改动免重启生效。</param>
        public FileControlSource(Func<string, string>? fileFor = null, string? directory = null, bool reloadOnChange = true)
        {
            _fileFor = fileFor ?? (point => directory is null
                ? throw new ArgumentException("未提供 fileFor 且未指定 directory，无法定位决策点文件。", nameof(directory))
                : Path.Combine(directory, point + ".json"));
            _reloadOnChange = reloadOnChange;
        }

        public string? SourceName => "file:" + string.Join(",", _cache.Keys);

        /// <summary>取当前生效 Spec：文件缺失/加载失败保留旧版（LoadedOnce 时）；从未加载成功返回 null（门面兜底）。</summary>
        public DecisionControlSpec? Get(string decisionPoint)
        {
            lock (_lock)
            {
                var path = _fileFor(decisionPoint);
                if (!File.Exists(path))
                    return _cache.TryGetValue(decisionPoint, out var c) && c.LoadedOnce ? c.Spec : null;

                var write = File.GetLastWriteTimeUtc(path);
                if (!_cache.TryGetValue(decisionPoint, out var cached)
                    || !cached.LoadedOnce
                    || (_reloadOnChange && write != cached.LastWrite))
                {
                    var loaded = TryLoadCore(path);
                    if (loaded.errors.Count == 0)
                    {
                        _cache[decisionPoint] = new CachedEntry(loaded.spec, write, LoadedOnce: true);
                    }
                    else if (!_cache.TryGetValue(decisionPoint, out _))
                    {
                        // 无旧版可回退：登记一次失败占位（避免每次重读抛，交由门面 fail-safe 兜底）。
                        _cache[decisionPoint] = new CachedEntry(null, write, LoadedOnce: false);
                    }
                    // 加载失败且有旧版：保留 _cache 已加载版本（不抛，热重载竞态保护）。
                }
                return _cache.TryGetValue(decisionPoint, out var e) ? e.Spec : null;
            }
        }

        /// <summary>强制忽略缓存重载（外部刷新用）。</summary>
        public void Invalidate(string decisionPoint)
        {
            lock (_lock) { _cache.Remove(decisionPoint); }
        }

        private (DecisionControlSpec? spec, IReadOnlyList<string> errors) TryLoadCore(string path)
        {
            // 单次读盘：校验与反序列化共用同一份文本，消除两次读之间的 TOCTOU 不一致窗口。
            var text = File.ReadAllText(path);
            var errors = DecisionControlJson.Validate(text);
            if (errors.Count > 0) return (null, errors);
            return (DecisionControlJson.Deserialize(text), Array.Empty<string>());
        }
    }
}