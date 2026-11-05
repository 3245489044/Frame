using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Modules.Registry;

/// <summary>
/// 函数库装配环境：把 Dev / Published 两种运行布局映射为统一 token 根，并承载程序集搜索目录。
/// 组合根（产品侧）构造本对象；Frame 侧只按 token 解析，不再猜测源码仓布局。
/// </summary>
public sealed class FunctionLibraryEnvironment
{
    public const string TokenRuntime = "@runtime";
    public const string TokenConfig = "@config";
    public const string TokenApp = "@app";
    public const string TokenShared = "@shared";
    public const string TokenManifest = "@manifest";

    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _roots;
    private readonly IReadOnlyList<string> _assemblySearchDirectories;
    private readonly Action<string>? _logger;

    public FunctionLibraryEnvironment(
        string profile,
        IReadOnlyDictionary<string, IReadOnlyList<string>> roots,
        IReadOnlyList<string> assemblySearchDirectories,
        Action<string>? logger = null)
    {
        Profile = string.IsNullOrWhiteSpace(profile) ? "Unknown" : profile;
        _roots = roots ?? throw new ArgumentNullException(nameof(roots));
        _assemblySearchDirectories = assemblySearchDirectories ?? Array.Empty<string>();
        _logger = logger;
    }

    public string Profile { get; }

    public bool IsPublished => string.Equals(Profile, "Published", StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<string> AssemblySearchDirectories => _assemblySearchDirectories;

    public IFunctionLibraryContext CreateContext(FunctionLibraryDescriptor descriptor, string manifestPath)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (string.IsNullOrWhiteSpace(manifestPath))
            throw new ArgumentException("清单路径不能为空。", nameof(manifestPath));

        var manifestDirectory = Path.GetDirectoryName(Path.GetFullPath(manifestPath)) ?? AppContext.BaseDirectory;
        var resourceDirectory = string.IsNullOrWhiteSpace(descriptor.ResourceRoot)
            ? manifestDirectory
            : ResolvePath(descriptor.ResourceRoot!, manifestDirectory, requireExisting: false);

        return new Context(this, descriptor, Path.GetFullPath(manifestPath), resourceDirectory);
    }

    internal string ResolvePath(string pathOrToken, string baseDirectory, bool requireExisting)
    {
        var candidates = EnumerateResolvedPaths(pathOrToken, baseDirectory).ToList();
        if (candidates.Count == 0)
            throw new FileNotFoundException($"无法解析路径 '{pathOrToken}'（base={baseDirectory}）。");

        if (IsPublished && Path.IsPathRooted(pathOrToken) && !pathOrToken.StartsWith("@", StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Published 模式禁止绝对路径：'{pathOrToken}'。请改用 @config/@app/@shared/@manifest token。");

        if (requireExisting)
        {
            var hit = candidates.FirstOrDefault(File.Exists) ?? candidates.FirstOrDefault(Directory.Exists);
            if (hit is not null)
                return hit;
            throw new FileNotFoundException(
                $"资源不存在：'{pathOrToken}'。候选：\n  " + string.Join("\n  ", candidates));
        }

        return candidates.FirstOrDefault(File.Exists)
               ?? candidates.FirstOrDefault(Directory.Exists)
               ?? candidates[0];
    }

    internal IEnumerable<string> EnumerateResolvedPaths(string pathOrToken, string baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(pathOrToken))
            yield break;

        if (pathOrToken.StartsWith("@", StringComparison.Ordinal))
        {
            var separator = pathOrToken.IndexOf('/');
            var separatorBackslash = pathOrToken.IndexOf('\\');
            if (separator < 0 || (separatorBackslash >= 0 && separatorBackslash < separator))
                separator = separatorBackslash;

            var token = separator < 0 ? pathOrToken : pathOrToken[..separator];
            var remainder = separator < 0 ? string.Empty : pathOrToken[(separator + 1)..];

            if (!_roots.TryGetValue(token, out var roots) || roots.Count == 0)
                throw new DirectoryNotFoundException($"未注册路径 token：'{token}'。");

            foreach (var root in roots)
            {
                if (string.IsNullOrWhiteSpace(root))
                    continue;
                yield return Path.GetFullPath(Path.Combine(root, remainder));
            }
            yield break;
        }

        if (Path.IsPathRooted(pathOrToken))
        {
            yield return Path.GetFullPath(pathOrToken);
            yield break;
        }

        yield return Path.GetFullPath(Path.Combine(baseDirectory, pathOrToken));
    }

    internal IEnumerable<string> EnumerateAssemblyCandidates(string nameOrPath, string baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(nameOrPath))
            yield break;

        if (Path.IsPathRooted(nameOrPath))
        {
            yield return Path.GetFullPath(nameOrPath);
            yield break;
        }

        yield return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, nameOrPath));

        foreach (var directory in _assemblySearchDirectories)
        {
            if (string.IsNullOrWhiteSpace(directory))
                continue;
            yield return Path.GetFullPath(Path.Combine(directory, nameOrPath));
        }
    }

    internal void Log(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;
        _logger?.Invoke($"[FunctionLibrary:{Profile}] {message}");
    }

    private sealed class Context : IFunctionLibraryContext
    {
        private readonly FunctionLibraryEnvironment _environment;

        public Context(
            FunctionLibraryEnvironment environment,
            FunctionLibraryDescriptor descriptor,
            string manifestPath,
            string resourceDirectory)
        {
            _environment = environment;
            Descriptor = descriptor;
            ManifestPath = manifestPath;
            ResourceDirectory = resourceDirectory;
        }

        public FunctionLibraryDescriptor Descriptor { get; }

        public string ManifestPath { get; }

        public string ResourceDirectory { get; }

        public bool IsPublished => _environment.IsPublished;

        public IReadOnlyList<string> AssemblySearchDirectories => _environment.AssemblySearchDirectories;

        public string ResolveRequiredResource(string relativeOrTokenPath)
        {
            if (string.IsNullOrWhiteSpace(relativeOrTokenPath))
                throw new ArgumentException("资源路径不能为空。", nameof(relativeOrTokenPath));

            var path = _environment.ResolvePath(relativeOrTokenPath, ResourceDirectory, requireExisting: true);
            if (!File.Exists(path) && !Directory.Exists(path))
                throw new FileNotFoundException($"资源不存在：{relativeOrTokenPath}（解析结果：{path}）", path);
            return path;
        }

        public string? ResolveOptionalResource(string? relativeOrTokenPath)
        {
            if (string.IsNullOrWhiteSpace(relativeOrTokenPath))
                return null;

            var candidates = _environment
                .EnumerateResolvedPaths(relativeOrTokenPath, ResourceDirectory)
                .ToList();
            return candidates.FirstOrDefault(File.Exists) ?? candidates.FirstOrDefault(Directory.Exists);
        }

        public Assembly ResolveAssembly(string nameOrPath)
        {
            if (string.IsNullOrWhiteSpace(nameOrPath))
                throw new ArgumentException("程序集名不能为空。", nameof(nameOrPath));

            var tried = new List<string>();

            foreach (var root in Descriptor.AssemblySearchRoots)
            {
                foreach (var directory in _environment.EnumerateResolvedPaths(root, ResourceDirectory))
                {
                    if (string.IsNullOrWhiteSpace(directory))
                        continue;
                    var candidate = Path.GetFullPath(Path.Combine(directory, nameOrPath));
                    tried.Add(candidate);
                    if (File.Exists(candidate))
                        return Assembly.LoadFrom(candidate);
                }
            }

            foreach (var candidate in _environment.EnumerateAssemblyCandidates(nameOrPath, ResourceDirectory))
            {
                if (tried.Contains(candidate, StringComparer.OrdinalIgnoreCase))
                    continue;
                tried.Add(candidate);
                if (File.Exists(candidate))
                    return Assembly.LoadFrom(candidate);
            }

            var simpleName = Path.GetFileNameWithoutExtension(nameOrPath);
            if (!string.IsNullOrWhiteSpace(simpleName))
            {
                try
                {
                    return Assembly.Load(new AssemblyName(simpleName));
                }
                catch (FileNotFoundException) { }
                catch (BadImageFormatException) { }
            }

            throw new FileNotFoundException(
                $"无法定位程序集 '{nameOrPath}'。候选：\n  " + string.Join("\n  ", tried));
        }

        public void Info(string message) => _environment.Log(message);

        public void Warn(string message) => _environment.Log("WARN " + message);
    }
}
