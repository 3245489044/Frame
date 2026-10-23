using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Share.StateManagement.Audit;

/// <summary>
/// 状态根取样器（只读、零写）：解析后的根路径 → <see cref="StateRootSample"/>。
/// <para>
/// 内容指纹口径（有意收窄，防运行期抖动制造假 Split）：只哈希根内 <c>agent.db</c> / <c>graph.db</c> 两个权威库文件，
/// <b>不含</b> <c>-wal</c>/<c>-shm</c> 等运行期副产物。文件以 <c>FileShare.ReadWrite</c> 打开（可与运行中的宿主并发读）。
/// </para>
/// <para>本件不做环境变量/配置解析：解析口径归宿主（<c>AgentStateLocator</c>/诊断入口），禁第二实现。</para>
/// </summary>
public static class StateRootSampler
{
    /// <summary>参与内容指纹的库文件（序即指纹输入序）。</summary>
    private static readonly string[] FingerprintFiles = { "agent.db", "graph.db" };

    /// <summary>取一次样本。根不存在/不是目录 ⇒ ContentHash 为空（裁定为 Unknown，不伪造 Single）。</summary>
    /// <param name="rootPath">已解析的根路径。</param>
    /// <param name="atUtc">取样时刻。</param>
    /// <param name="identityHash">根承载者身份指纹；null ⇒ 用规范化路径的稳定指纹（跨进程可复算）。</param>
    public static StateRootSample Sample(string? rootPath, DateTimeOffset atUtc, string? identityHash = null)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
        {
            return new StateRootSample(rootPath ?? string.Empty, string.Empty, string.Empty, atUtc);
        }

        var normalized = NormalizePath(rootPath!);
        var identity = string.IsNullOrWhiteSpace(identityHash) ? StableIdentity(normalized) : identityHash!;
        var content = ComputeContentHash(normalized);
        return new StateRootSample(normalized, identity, content, atUtc);
    }

    /// <summary>根内库文件内容指纹（文件清单 + 长度 + 字节流，SHA256 十六进制）；无库文件 ⇒ 空串。</summary>
    public static string ComputeContentHash(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath)) return string.Empty;

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var found = false;
        foreach (var name in FingerprintFiles)
        {
            var path = Path.Combine(rootPath, name);
            if (!File.Exists(path)) continue;

            found = true;
            hash.AppendData(Encoding.UTF8.GetBytes(name + "|"));

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                hash.AppendData(buffer, 0, read);
                total += read;
            }

            hash.AppendData(BitConverter.GetBytes(total));
        }

        return found ? Convert.ToHexString(hash.GetHashAndReset()) : string.Empty;
    }

    /// <summary>
    /// 规范化路径（同一物理根 ⇒ 同一文本）：绝对化 + 去尾分隔符；Windows 下 OrdinalIgnoreCase 折叠为大写。
    /// 不做符号链接解析（不做 IO 之外的真实性推断）。
    /// </summary>
    public static string NormalizePath(string path)
    {
        var full = Path.GetFullPath(path.Trim())
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return OperatingSystem.IsWindows() ? full.ToUpperInvariant() : full;
    }

    private static string StableIdentity(string normalizedPath)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedPath)));
}
