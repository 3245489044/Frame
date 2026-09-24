using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Share.Conversation.Ids;

namespace Share.Conversation.Facts.Stores;

/// <summary>
/// JSONL 事实存储（默认实现，方案 §2.3）——一轮一文件、append-only、崩溃安全、**人可读（取证一条命令）**。
/// <para>
/// 落点：<c>&lt;dataDir&gt;\session-facts\&lt;SessionKey&gt;\&lt;RoundKey&gt;.jsonl</c>
/// （如 <c>…\session-facts\default\default#R6.jsonl</c>；轮键不含路径分隔符，Windows 下 <c>#</c> 合法）。
/// </para>
/// <para>
/// 并发：进程内按文件路径串行（<see cref="FileGates"/>，跨 Writer 实例安全）；跨进程写同一文件属误用
/// （同会话由单进程持有，跨 worker 的碎片在各自 CLINE_DIR 下，由摄取器归并）。
/// </para>
/// </summary>
public sealed class JsonlSessionFactStore : ISessionFactStore
{
    /// <summary>事实区根目录名（<c>&lt;dataDir&gt;\session-facts</c>）。</summary>
    public const string RootFolderName = "session-facts";

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> FileGates = new(StringComparer.Ordinal);

    private readonly string _dataDir;
    private readonly string _root;

    /// <summary>以宿主数据根构造（<paramref name="dataDir"/> = Agent 数据目录，与 <c>observability\events.jsonl</c> 同源）。</summary>
    /// <param name="dataDir">宿主数据根目录。</param>
    public JsonlSessionFactStore(string dataDir)
    {
        if (string.IsNullOrWhiteSpace(dataDir))
            throw new ArgumentException("数据根目录不得为空（事实区需确定落点）", nameof(dataDir));
        _dataDir = dataDir;
        _root = Path.Combine(dataDir, RootFolderName);
    }

    /// <summary>事实区根目录（取证 / 诊断用）。</summary>
    public string Root => _root;

    /// <summary>轮级文档路径（唯一实现；键非法即抛，不猜路径）。</summary>
    /// <param name="dataDir">宿主数据根目录。</param>
    /// <param name="key">归属键。</param>
    public static string PathOf(string dataDir, FactId key)
    {
        if (string.IsNullOrWhiteSpace(dataDir))
            throw new ArgumentException("数据根目录不得为空", nameof(dataDir));
        if (key.IsRunKey)
            throw new ArgumentException("事实文档按轮落点（run 级键无文档）", nameof(key));

        return Path.Combine(dataDir, RootFolderName, key.SessionKey, FactIds.Round(key.SessionKey, key.RoundNo) + ".jsonl");
    }

    /// <inheritdoc />
    public async Task AppendAsync(FactEnvelope fact, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(fact);
        await AppendManyAsync(new[] { fact }, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task AppendManyAsync(IReadOnlyList<FactEnvelope> facts, CancellationToken ct = default)
    {
        if (facts is null || facts.Count == 0) return;

        // 按文档分组后逐文档追加（同文档内顺序 = 入参顺序）。
        foreach (var group in facts.GroupBy(f => f.KeyText, StringComparer.Ordinal))
        {
            var first = group.First();
            var path = PathFor(first.Key);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            var builder = new StringBuilder();
            foreach (var fact in group) builder.Append(FactWireCodec.Serialize(fact)).Append('\n');

            var gate = FileGates.GetOrAdd(path, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var bytes = Encoding.UTF8.GetBytes(builder.ToString());
                await using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read,
                    4096, useAsync: true);
                await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
                await stream.FlushAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FactEnvelope>> LoadAsync(FactId key, CancellationToken ct = default)
    {
        if (key.IsRunKey) return Array.Empty<FactEnvelope>();

        var path = PathFor(key);
        if (!File.Exists(path)) return Array.Empty<FactEnvelope>();

        var lines = await File.ReadAllLinesAsync(path, ct).ConfigureAwait(false);
        var facts = new List<FactEnvelope>(lines.Length);
        for (var index = 0; index < lines.Length; index++)
        {
            if (FactWireCodec.TryDeserialize(lines[index], out var fact) && fact is not null)
            {
                facts.Add(fact);
                continue;
            }

            // 破损行不静默：丢弃并可见化（只增不丢的例外须留痕）。
            if (!string.IsNullOrWhiteSpace(lines[index]))
                SessionFactDiagnostics.Report("SessionFact_LineDropped", new { path, line = index + 1 });
        }

        return facts;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<FactId>> ListAsync(int limit = 50, CancellationToken ct = default)
    {
        var keys = new List<FactId>();
        if (!Directory.Exists(_root)) return Task.FromResult<IReadOnlyList<FactId>>(keys);

        foreach (var path in Directory.EnumerateFiles(_root, "*.jsonl", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            var name = Path.GetFileNameWithoutExtension(path);
            if (FactId.TryParse(name, out var key) && !key.IsRunKey) keys.Add(key);
        }

        keys.Sort((x, y) => y.CompareTo(x)); // 最近轮在前
        if (limit > 0 && keys.Count > limit) keys = keys.GetRange(0, limit);
        return Task.FromResult<IReadOnlyList<FactId>>(keys);
    }

    /// <summary>该键对应的文档绝对路径。</summary>
    private string PathFor(FactId key) => PathOf(_dataDir, key);
}
