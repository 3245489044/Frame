using System.Diagnostics;

namespace CallCenter.Mcp;

/// <summary>
/// P4-b TS 编译轨（单文件）：把一段 TypeScript 源经 workspace 的 <c>typescript/bin/tsc</c>（node 直接执行 JS，
/// 不依赖 .cmd/PATH）编译为 CommonJS JS 产物。编译产物由调用方按 fingerprint 写入 <see cref="Core.Cache.BuildCache"/>
/// （编译缓存多轨制）——「TS 走 tsc → 产出 → 写 BuildCache」语义；本文只做编译，不做激活（激活由持久宿主/协调器驱动）。
/// <para><b>已知限制（C6）</b>：用 CLI 编译选项隔离仓库 tsconfig，假设单文件 CommonJS 无外部模块依赖；源含 <c>import</c> 且
/// 未装对应类型/模块解析（新版 TS 对 moduleResolution 敏感，如 <c>node10</c> 移除）会失败。已带 <c>--skipLibCheck</c> 降低类型依赖；
/// 多文件/模块工程应改用目标工程 tsconfig，而非本单文件编译器。</para>
/// </summary>
public sealed class TsScriptCompiler
{
    private readonly string _nodePath;
    private readonly string _tscPath;

    public TsScriptCompiler(string? tscPath = null, string? nodePath = null)
    {
        _tscPath = tscPath ?? ResolveTypescriptBin()
                    ?? throw new InvalidOperationException(
                        "未定位到 workspace 的 typescript/bin/tsc。请先安装 TypeScript 依赖（node install）。");
        _nodePath = nodePath ?? "node";
    }

    public string TscPath => _tscPath;

    /// <summary>从 <paramref name="searchRoot"/>（缺省取当前输出目录）逐级上溯定位 typescript 的 tsc JS 入口。</summary>
    public static string? ResolveTypescriptBin(string? searchRoot = null)
    {
        var dir = searchRoot != null
            ? new DirectoryInfo(searchRoot)
            : new DirectoryInfo(AppContext.BaseDirectory);

        while (dir != null)
        {
            var generic = Path.Combine(dir.FullName, "node_modules", "typescript", "bin", "tsc");
            if (File.Exists(generic))
                return generic;

            // 本仓 TS workspace 布局：TypeScriptPrj/TsWorkspace/<pkg>/node_modules/typescript/bin/tsc
            var tsWorkspace = Path.Combine(dir.FullName, "TypeScriptPrj", "TsWorkspace");
            if (Directory.Exists(tsWorkspace))
            {
                foreach (var pkg in Directory.GetDirectories(tsWorkspace))
                {
                    var candidate = Path.Combine(pkg, "node_modules", "typescript", "bin", "tsc");
                    if (File.Exists(candidate))
                        return candidate;
                }
            }

            dir = dir.Parent;
        }

        return null;
    }

    /// <summary>
    /// 编译单个 TS 源到 <paramref name="outDir"/>，返回产物 <c>outDir/&lt;basename&gt;.js</c> 绝对路径。
    /// 编译失败（tsc 非零退出）抛 <see cref="InvalidOperationException"/> 携带 stderr。用 CLI 编译器选项隔离 tsconfig。
    /// </summary>
    public async Task<string> CompileAsync(string tsSourcePath, string outDir, CancellationToken ct = default)
    {
        var full = Path.GetFullPath(tsSourcePath);
        if (!File.Exists(full))
            throw new FileNotFoundException($"TS 源未找到: {full}", full);

        Directory.CreateDirectory(outDir);
        if (!File.Exists(_tscPath))
            throw new FileNotFoundException($"tsc 入口不存在: {_tscPath}。请先安装 TypeScript 依赖。", _tscPath);

        var psi = new ProcessStartInfo(_nodePath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = outDir, // 无 tsconfig，纯 CLI 选项，隔离仓库 tsconfig 影响
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add(_tscPath);
        psi.ArgumentList.Add(full);
        psi.ArgumentList.Add("--module"); psi.ArgumentList.Add("commonjs");
        psi.ArgumentList.Add("--target"); psi.ArgumentList.Add("ES2020");
        psi.ArgumentList.Add("--skipLibCheck");
        psi.ArgumentList.Add("--sourceMap"); psi.ArgumentList.Add("false");
        psi.ArgumentList.Add("--outDir"); psi.ArgumentList.Add(outDir);

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("无法启动 node/tsc 进程。");

        var stdout = proc.StandardOutput.ReadToEndAsync(ct);
        var stderr = proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct).ConfigureAwait(false);
        var exit = proc.ExitCode;

        if (exit != 0)
            throw new InvalidOperationException($"tsc 编译失败 (exit {exit}): {await stderr.ConfigureAwait(false)}");

        await stdout.ConfigureAwait(false);

        var emitted = Path.Combine(outDir, Path.GetFileNameWithoutExtension(full) + ".js");
        if (!File.Exists(emitted))
            throw new InvalidOperationException($"tsc 未产出预期产物: {emitted}。stdout: {await stdout.ConfigureAwait(false)}");

        return emitted;
    }
}
