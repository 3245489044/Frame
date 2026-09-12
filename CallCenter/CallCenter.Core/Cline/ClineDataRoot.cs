using System;
using System.IO;

namespace CallCenter.Core.Cline;

/// <summary>
/// Cline 数据根四路径（与 SDK <c>@cline/shared/storage</c> 的四个 resolve* 一一对应）：
/// <see cref="ClineDir"/>（<c>CLINE_DIR</c>）、<see cref="DataDir"/>（<c>CLINE_DATA_DIR</c>）、
/// <see cref="SessionsDir"/>（<c>CLINE_SESSION_DATA_DIR</c>，会话 json/messages 落点）、
/// <see cref="DbDir"/>（<c>CLINE_DB_DATA_DIR</c>，sessions.db 索引库落点）。
/// </summary>
public sealed record ClineDataRootPaths(string ClineDir, string DataDir, string SessionsDir, string DbDir);

/// <summary>
/// Cline 数据根解析器 —— **唯一权威**（Frame 平台层）。
///
/// 存在理由：此前「Cline 写盘根」在 5 处各自解析（C# daemon 注入、C# 归档兜底、TS engine、TS 路由、装配设置），
/// 且注入了 SDK 不识别的变量名 → 隔离根未生效、会话写进用户真实 <c>~/.cline</c>（拦截失效根因 B）。
/// 现收敛为：算法与 SDK 逐字对齐（<c>CLINE_DIR → CLINE_DATA_DIR → CLINE_SESSION_DATA_DIR/CLINE_DB_DATA_DIR</c>
/// + HOME 回退链），跨语言同算法（TS 侧镜像 = <c>ClineApi/src/runtime/cline-data-root.ts</c>）。
///
/// 消费方：<c>Agent.Launcher</c> / <c>Agent.McpRegistryHost</c>（已具备 CallCenter 二进制消费资格）
/// 解析后**以字符串注入** Agent.Modules（daemon 隔离根 / 归档会话目录），
/// 使 Agent.Modules 不新增平台层引用，同时不再自行拼装 SDK 目录布局。
/// </summary>
public static class ClineDataRoot
{
    /// <summary>
    /// 由 Cline 主目录派生四路径（纯函数）：<c>dataDir = &lt;clineDir&gt;/data</c>，
    /// <c>sessionsDir = &lt;dataDir&gt;/sessions</c>，<c>dbDir = &lt;dataDir&gt;/db</c>。
    /// </summary>
    public static ClineDataRootPaths FromClineDir(string clineDir)
    {
        if (string.IsNullOrWhiteSpace(clineDir)) throw new ArgumentException("clineDir 不能为空", nameof(clineDir));
        var dataDir = Path.Combine(clineDir, "data");
        return new ClineDataRootPaths(
            clineDir,
            dataDir,
            Path.Combine(dataDir, "sessions"),
            Path.Combine(dataDir, "db"));
    }

    /// <summary>
    /// 每 worker 的隔离根（Isolated 策略）：<c>clineDir = &lt;isolatedRoot&gt;/&lt;workerId&gt;</c>，
    /// 其余路径由 <see cref="FromClineDir"/> 派生。注入 daemon 子进程的 <c>CLINE_DIR</c> /
    /// <c>CLINE_DB_DATA_DIR</c> 即取本结果，保证 SDK 写盘根与宿主预期根一致。
    /// </summary>
    public static ClineDataRootPaths ForWorker(string isolatedRoot, string workerId)
    {
        if (string.IsNullOrWhiteSpace(isolatedRoot)) throw new ArgumentException("isolatedRoot 不能为空", nameof(isolatedRoot));
        if (string.IsNullOrWhiteSpace(workerId)) throw new ArgumentException("workerId 不能为空", nameof(workerId));
        return FromClineDir(Path.Combine(isolatedRoot, workerId));
    }

    /// <summary>解析当前进程的 Cline 数据根（读本进程环境变量）。</summary>
    public static ClineDataRootPaths Resolve() => Resolve(Environment.GetEnvironmentVariable);

    /// <summary>
    /// 解析 Cline 数据根（env 取值可注入，便于测试）。
    /// 派生链基于**生效**的 dataDir（与 SDK 同序：sessions/db 由 resolveClineDataDir() 派生，非默认值）。
    /// </summary>
    public static ClineDataRootPaths Resolve(Func<string, string?> env)
    {
        if (env is null) throw new ArgumentNullException(nameof(env));
        var clineDir = Env(env, "CLINE_DIR") ?? Path.Combine(ResolveHome(env), ".cline");
        var dataDir = Env(env, "CLINE_DATA_DIR") ?? Path.Combine(clineDir, "data");
        return new ClineDataRootPaths(
            clineDir,
            dataDir,
            Env(env, "CLINE_SESSION_DATA_DIR") ?? Path.Combine(dataDir, "sessions"),
            Env(env, "CLINE_DB_DATA_DIR") ?? Path.Combine(dataDir, "db"));
    }

    /// <summary>HOME 回退链（对齐 SDK resolveHomeDir）：HOME → USERPROFILE → HOMEDRIVE+HOMEPATH → 用户配置文件目录。</summary>
    private static string ResolveHome(Func<string, string?> env)
    {
        var home = Env(env, "HOME");
        if (home is not null && home != "~") return home;
        var userProfile = Env(env, "USERPROFILE");
        if (userProfile is not null) return userProfile;
        var homeDrive = Env(env, "HOMEDRIVE");
        var homePath = Env(env, "HOMEPATH");
        if (homeDrive is not null && homePath is not null) return homeDrive + homePath;
        var fallback = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)?.Trim();
        return string.IsNullOrEmpty(fallback) || fallback == "~" ? "~" : fallback;
    }

    /// <summary>读环境变量并 Trim（空白按「未设置」处理，与 SDK 的 <c>?.trim()</c> 真值判断一致）。</summary>
    private static string? Env(Func<string, string?> env, string name)
    {
        var v = env(name)?.Trim();
        return string.IsNullOrEmpty(v) ? null : v;
    }
}
