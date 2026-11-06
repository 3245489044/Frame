using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Share.ContextGeometry;
using Usharp.Emit;
using Usharp.Semantics;

namespace Usharp.Adapters.Node;

/// <summary>单域声明编译形态：单文件（无键空间上下文）/ 单文件 + 整包键空间上下文。</summary>
public enum DomainCompileProfile
{
    /// <summary>仅编译该域声明文件（跨层判据缺席 ⇒ 只可见化）。</summary>
    SingleFile,

    /// <summary>该域声明 + 整包 <c>[KeySpace]</c> 上下文（跨层坐标绑定参与判红）。</summary>
    SingleFileWithKeyspace,
}

/// <summary>单域编译结果（<see cref="Json"/> 为空 ⇒ 编译失败或未声明该域，原因见 <see cref="Reason"/>）。</summary>
public sealed record DomainCompileOutcome(
    string? Json,
    IReadOnlyList<UsharpDiagnostic> Diagnostics,
    bool CacheHit,
    string Reason);

/// <summary>
/// 单域声明编译**唯一入口**：内容哈希缓存 + 键空间上下文按目录整体装载。
/// <para>
/// 纪律：① 编译期零写库；② 同内容同形态只编译一次（域装配与位置对账共用同一份结果）；
/// ③ 上下文 = <c>keyspaces</c> 目录全部 <c>.ushar</c>（单文件只带主键空间会因 <c>[KeyLexicon]</c> 缺声明而假红）。
/// </para>
/// </summary>
public static class DeclaredDomainCompiler
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, DomainCompileOutcome> Cache = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, (string Fingerprint, IReadOnlyList<(string Path, string Text)> Docs)> ContextCache =
        new(StringComparer.Ordinal);

    /// <summary>编译单个域声明；命中缓存时 <see cref="DomainCompileOutcome.CacheHit"/> = true。</summary>
    /// <param name="domainPath">域声明文件绝对路径。</param>
    /// <param name="domainKey">期望域键（与 <c>[Domain]</c> 的 id 一致）。</param>
    /// <param name="profile">编译形态。</param>
    /// <param name="keyspaceDirectory">键空间目录（<see cref="DomainCompileProfile.SingleFileWithKeyspace"/> 必填）。</param>
    public static DomainCompileOutcome CompileDomain(
        string domainPath,
        string domainKey,
        DomainCompileProfile profile,
        string? keyspaceDirectory)
    {
        var context = profile == DomainCompileProfile.SingleFileWithKeyspace && !string.IsNullOrWhiteSpace(keyspaceDirectory)
            ? LoadKeyspaceContext(keyspaceDirectory!)
            : Array.Empty<(string Path, string Text)>();

        var text = File.ReadAllText(domainPath);
        var key = Key(domainPath, text, profile, context);

        lock (Gate)
        {
            if (Cache.TryGetValue(key, out var hit) && hit is not null)
            {
                return hit with { CacheHit = true };
            }
        }

        var documents = new List<(string Path, string Text)>(context.Count + 1);
        documents.AddRange(context);
        documents.Add((domainPath, text));

        var compilation = UsharpCompilation.Compile(documents);
        if (compilation.HasErrors)
        {
            return Store(key, new DomainCompileOutcome(null, compilation.Diagnostics, false, "compile-errors"));
        }

        var domain = compilation.Domains
            .FirstOrDefault(d => string.Equals(d.DomainId, domainKey, StringComparison.Ordinal));
        if (domain is null)
        {
            return Store(key, new DomainCompileOutcome(null, compilation.Diagnostics, false, "domain-undeclared"));
        }

        var emitted = UsharpNodeSpecEmitter.Emit(compilation, domain.DomainId);
        var diagnostics = compilation.Diagnostics
            .Concat(emitted.Diagnostics)
            .ToArray();
        if (emitted.Json is null || emitted.HasErrors)
        {
            return Store(key, new DomainCompileOutcome(null, diagnostics, false, "emit-errors"));
        }

        return Store(key, new DomainCompileOutcome(emitted.Json, diagnostics, false, "ok"));
    }

    /// <summary>清空缓存（测试 / 声明变更后重载）。</summary>
    public static void Reset()
    {
        lock (Gate)
        {
            Cache.Clear();
            ContextCache.Clear();
        }
    }

    private static DomainCompileOutcome Store(string key, DomainCompileOutcome outcome)
    {
        lock (Gate)
        {
            Cache[key] = outcome;
        }

        return outcome;
    }

    private static IReadOnlyList<(string Path, string Text)> LoadKeyspaceContext(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return Array.Empty<(string, string)>();
        }

        // 长驻宿主：目录内容（文件集/长度/最后写时间）变化即失效，禁以目录键永久命中旧上下文。
        var fingerprint = Fingerprint(directory);
        lock (Gate)
        {
            if (ContextCache.TryGetValue(directory, out var cached)
                && string.Equals(cached.Fingerprint, fingerprint, StringComparison.Ordinal))
            {
                return cached.Docs;
            }
        }

        var docs = Directory
            .EnumerateFiles(directory, "*.ushar", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => (Path: path, Text: File.ReadAllText(path)))
            .ToArray();

        lock (Gate)
        {
            ContextCache[directory] = (fingerprint, docs);
        }

        return docs;
    }

    private static string Fingerprint(string directory)
    {
        var builder = new StringBuilder();
        foreach (var path in Directory.EnumerateFiles(directory, "*.ushar", SearchOption.TopDirectoryOnly)
                     .OrderBy(static path => path, StringComparer.Ordinal))
        {
            var info = new FileInfo(path);
            builder.Append(Path.GetFileName(path)).Append('|')
                .Append(info.Length).Append('|')
                .Append(info.LastWriteTimeUtc.Ticks).Append('\n');
        }

        return ContentHash.Sha256Hex(builder.ToString());
    }

    private static string Key(
        string path,
        string text,
        DomainCompileProfile profile,
        IReadOnlyList<(string Path, string Text)> context)
    {
        var builder = new StringBuilder();
        builder.Append(path).Append('|').Append(profile).Append('|');
        foreach (var doc in context)
        {
            builder.Append(Path.GetFileName(doc.Path)).Append('|').Append(doc.Text).Append('\n');
        }

        builder.Append("--domain--|").Append(text);
        return ContentHash.Sha256Hex(builder.ToString());
    }
}
