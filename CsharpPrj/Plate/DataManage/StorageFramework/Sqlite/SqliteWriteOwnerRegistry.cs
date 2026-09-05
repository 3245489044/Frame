using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace UniversalWorkflowFramework.StorageFramework.Sqlite;

/// <summary>
/// 库级写面持有者登记（进程内自报告，零落盘）。
/// SQLite 写锁（WAL 单写者）真正的持锁方是「持 agent.db 写连接 / 写事务 / 写门」的路径；
/// 而进程内两套写面互不可见——<see cref="ISqliteWriteHost"/> 单写通道 host（常驻单写连接 + 事务）与
/// SqliteRelate.DataUniversal（自持按库门 + 每次新建连接）——任一侧撞 busy / 等写权超时只能自证失败，
/// 看不到是谁在长持该库写面。
/// 本登记作为两套写面的共享可见性桥：写方在「持库级写权期间」登记 owner + 线程 + 借出点栈；任一侧等写权
/// 超时或撞 busy 失败时，把当前仍登记的长持者读进异常消息，使运行日志当场显示 agent.db 写面被谁、从哪个
/// 调用点长持（对齐「锁归属定位一律走进程内自报告」约定，不落任何磁盘文件）。
/// 并发正确性：每库持一个「token → Holder」精确集合，<see cref="Exit"/> 按 token TryRemove 精确注销，
/// 与并发 <see cref="Enter"/>/其它 <see cref="Exit"/> 无「整队重建」竞态（不会丢项或虚报长持）。
/// 纯内存、绝不抛出、绝不改变写语义——登记/描述失败一律静默，不影响正常读写。
/// </summary>
internal static class SqliteWriteOwnerRegistry
{
    private sealed class Holder
    {
        public required Guid Token { get; init; }
        public required string? Owner { get; init; }
        public required int ThreadId { get; init; }
        public required long StartedMs { get; init; }
        public required string BorrowStack { get; init; }
    }

    // key = 归一化库文件路径（Data Source 解析 + 绝对化；大小写不敏感）。
    // value = 该库当前在途写权持有者集合（token → Holder，精确增删，杜绝并发整队重建竞态）。
    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, Holder>> Holders =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>真长持阈值（ms）：持有时长 ≥ 此值才视为「确为异常长持」并展开完整借出点栈；
    /// 短持竞争只输出单行摘要（> SqliteWriteChannel busy_timeout 一倍，故设 10s）。</summary>
    internal const long FullStackThresholdMs = 10_000;

    // 借出栈里的机制帧（登记助手 + 通道/命令门面）：borrow 摘要应跳过它们，落在真正持门/写库的业务方法上。
    private static readonly HashSet<string> MechanismMethods = new(StringComparer.Ordinal)
    {
        "Enter", "Exit", "CaptureBorrowStack", "Describe", "DescribeCore", "BorrowDescriptor",
        "Acquire", "RunWrite", "Release", "EnsureOpen",
        "ExecuteSQL", "ExecuteNonQuery", "ExecuteScalar", "SetConnect", "SetReadConnect",
    };

    /// <summary>登记当前写权持有者（返回登记 token，供 <see cref="Exit"/> 配对应注销）。登记失败返回随机 token，不影响调用方。</summary>
    public static Guid Enter(string connectionString, string? owner = null)
    {
        var token = Guid.NewGuid();
        try
        {
            var inner = Holders.GetOrAdd(DbPathOf(connectionString), static _ => new ConcurrentDictionary<Guid, Holder>());
            inner.TryAdd(token, new Holder
            {
                Token = token,
                Owner = owner,
                ThreadId = Environment.CurrentManagedThreadId,
                StartedMs = Environment.TickCount64,
                BorrowStack = CaptureBorrowStack(),
            });
        }
        catch { /* 取证登记失败不影响写路径 */ }
        return token;
    }

    /// <summary>注销一次写权登记（按 token 精确移除，多持有者并发时不误删他人、无整队重建竞态）。</summary>
    public static void Exit(string connectionString, Guid token)
    {
        if (token == Guid.Empty) return;
        try
        {
            var key = DbPathOf(connectionString);
            if (!Holders.TryGetValue(key, out var inner)) return;
            inner.TryRemove(token, out _);
            if (inner.IsEmpty) Holders.TryRemove(new KeyValuePair<string, ConcurrentDictionary<Guid, Holder>>(key, inner));
        }
        catch { /* 静默 */ }
    }

    /// <summary>读取当前仍在持写权的其它持有者描述（默认排除自身 token），供等写权超时 / busy 竞争失败异常附加。
    /// 无其它持有者返回空串。日志收敛（S3-A）：逐持有者按其已持时长独立择一——短持(heldMs&lt;FullStackThresholdMs)
    /// 只给单行摘要（杜绝刷屏），确为真长持(≥阈值)才展开完整借出点栈（供精确定位借出调用行）。</summary>
    public static string Describe(string connectionString, Guid excludeToken = default)
        => DescribeCore(connectionString, excludeToken, useFullWhenHeld);

    /// <summary>单行摘要形态描述（无条件，不因长持展开栈）：供测试/调用方需要短消息时直接取用。
    /// 单行 = <c>owner|tid={tid}|heldMs={held}|borrow=Method(file:line)</c>。</summary>
    public static string DescribeCompact(string connectionString, Guid excludeToken = default)
        => DescribeCore(connectionString, excludeToken, static _ => false);

    /// <summary>完整借出点栈形态描述（无条件全栈）：供测试 / 诊断显式取全栈时使用。</summary>
    public static string DescribeFull(string connectionString, Guid excludeToken = default)
        => DescribeCore(connectionString, excludeToken, static _ => true);

    /// <summary>逐持有者渲染的公共核心：useFull(holder) 决定该持有者展开全栈还是单行摘要。
    /// 多持有者以 <c> ; </c> 连接；全部单行时整体为单行，任一全栈者自然带换行展开。</summary>
    private static string DescribeCore(string connectionString, Guid excludeToken, Func<Holder, bool> useFull)
    {
        try
        {
            if (!Holders.TryGetValue(DbPathOf(connectionString), out var inner) || inner.IsEmpty) return string.Empty;
            var parts = new List<string>();
            foreach (var h in inner.Values)
            {
                if (h.Token == excludeToken) continue;
                parts.Add(useFull(h) ? RenderFull(h) : RenderCompact(h));
            }
            return parts.Count == 0 ? string.Empty : " 当前库写权持有者=" + string.Join(" ; ", parts);
        }
        catch { return string.Empty; }
    }

    private static bool useFullWhenHeld(Holder h) => HeldMsOf(h) >= FullStackThresholdMs;

    private static long HeldMsOf(Holder h) => Environment.TickCount64 - h.StartedMs;

    /// <summary>单行摘要：owner|tid|heldMs|borrow=首业务帧方法名(文件:行)。</summary>
    private static string RenderCompact(Holder h)
        => $"{h.Owner ?? "unknown"}|tid={h.ThreadId}|heldMs={HeldMsOf(h)}|borrow={BorrowDescriptor(h)}";

    /// <summary>全栈形态：owner|tid|heldMs + 完整借出点栈（精确到调用行）。</summary>
    private static string RenderFull(Holder h)
        => $"{h.Owner ?? "unknown"} | thread={h.ThreadId} | heldMs={HeldMsOf(h)}\n    借出点:\n{h.BorrowStack}";

    /// <summary>从借出点栈取首个业务帧（方法名:行），缩为 <c>Method(fileLeaf:line)</c> 供单行摘要；
    /// 跳过登记助手/通道/命令门面等机制帧，使 borrow 落在真正持门/写库的业务方法上（如事务工厂/Store.PutAsync）；失败回退 '?'。</summary>
    private static string BorrowDescriptor(Holder h)
    {
        try
        {
            var text = h.BorrowStack;
            if (string.IsNullOrWhiteSpace(text)) return "?";
            string? fallback = null;
            foreach (var raw in text.Split('\n'))
            {
                var row = raw.Trim();
                if (!row.StartsWith("at ", StringComparison.Ordinal)) continue;
                var s = row.Substring(3).Trim();
                if (s.Length == 0) continue;
                var open = s.IndexOf(" (", StringComparison.Ordinal);
                var methodFull = open < 0 ? s : s.Substring(0, open).Trim();
                var methodShort = ShortenTail(methodFull);
                if (fallback is null) fallback = methodShort;
                if (MechanismMethods.Contains(methodShort)) continue;
                // 位置取叶文件 + 行号 → 单行可读。
                var loc = open < 0 ? string.Empty : s.Substring(open + 1).TrimEnd(')', ' ', '\t');
                var dot = loc.LastIndexOf(':');
                var file = dot > 0 ? loc.Substring(0, dot) : loc;
                var line = dot > 0 ? loc.Substring(dot + 1) : string.Empty;
                var fileLeaf = file.Length == 0 ? file : file.Substring(Math.Max(file.LastIndexOfAny(new[] { '\\', '/' }) + 1, 0));
                return $"{methodShort}({fileLeaf}{(line.Length == 0 ? string.Empty : ":" + line)})";
            }
            return fallback is null ? "?" : fallback;
        }
        catch { return "?"; }
    }

    private static string ShortenTail(string dotted)
    {
        var dot = dotted.LastIndexOf('.');
        return dot < 0 ? dotted : dotted.Substring(dot + 1);
    }


    /// <summary>采集借出点栈（顶部 N 帧，跳过登记自身），供 Describe 精确定位借出调用行；失败返回空。</summary>
    private static string CaptureBorrowStack()
    {
        try
        {
            var st = new System.Diagnostics.StackTrace(true);
            var frames = st.GetFrames();
            if (frames == null || frames.Length <= 1) return string.Empty;
            var sb = new StringBuilder();
            int shown = 0;
            // frame[0] = 本方法(new StackTrace 所在)；frame[1] = 直接调用方(Acquire/ExecuteSQL)，其后为业务借出链。
            for (int i = 1; i < frames.Length && shown < 10; i++)
            {
                var frame = frames[i];
                var method = frame.GetMethod();
                var name = method is null
                    ? "?"
                    : (method.DeclaringType is null ? method.Name : method.DeclaringType.FullName + "." + method.Name);
                var file = frame.GetFileName();
                var line = frame.GetFileLineNumber();
                if (shown > 0) sb.Append('\n');
                sb.Append("      at ").Append(name);
                if (file is not null) sb.Append(" (").Append(file).Append(':').Append(line).Append(')');
                shown++;
            }
            return sb.ToString();
        }
        catch { return string.Empty; }
    }

    /// <summary>从连接串解析归一化库文件路径：提取 Data Source，绝对化（容相对/带引号），去掉无关参数使不同写面命中同一库条目。</summary>
    internal static string DbPathOf(string connectionString)
    {
        try
        {
            var m = Regex.Match(connectionString,
                @"(?i)(?:^|;)\s*Data\s*Source\s*=\s*(?:""(?<q>[^""]*)""|(?<n>[^;]*))");
            var path = m.Success
                ? (m.Groups["q"].Success ? m.Groups["q"].Value : m.Groups["n"].Value).Trim()
                : connectionString.Trim();
            if (path.Length == 0) return connectionString;
            if (!Path.IsPathRooted(path)) path = Path.GetFullPath(path);
            return path;
        }
        catch { return connectionString; }
    }
}
