
using System.Reflection;
using System.Text.Json;
using TConfig;

namespace Modules.Registry;

    public static class FunctionLibraryLoader
    {
        private static readonly HashSet<string> _loadedLibraryTypes = new(StringComparer.Ordinal);

        public sealed class LoadEntryResult
        {
            public string Name { get; init; } = "";

            public bool Success { get; init; }

            public int FunctionCount { get; init; }

            public string? Error { get; init; }
        }

        public static IReadOnlyList<LoadEntryResult> Load(
            FunctionRegistry registry,
            string? configPath = null,
            bool autoDiscover = false)
        {
            ArgumentNullException.ThrowIfNull(registry);

            var results = new List<LoadEntryResult>();

            var nameTracker = new DuplicateNameTracker();

            if (configPath != null)
            {
                LoadFromConfig(registry, configPath, nameTracker, results);
            }

            if (autoDiscover)
            {
                LoadFromAutoDiscovery(registry, nameTracker, results);
            }

            nameTracker.AssertNoDuplicates();

            return results.AsReadOnly();
        }


    private static void LoadFromConfig(
        FunctionRegistry registry,
        string configPath,
        DuplicateNameTracker nameTracker,
        List<LoadEntryResult> results)
    {
        if (!File.Exists(configPath))
            throw new FileNotFoundException($"函数库配置文件不存在: {configPath}", configPath);

        var json = File.ReadAllText(configPath);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("libraries", out var libraries))
            return;

        foreach (var lib in libraries.EnumerateArray())
        {
            var name = lib.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            var typeStr = lib.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
            var enabled = lib.TryGetProperty("enabled", out var e) ? e.GetBoolean() : true;
            var methodName = lib.TryGetProperty("method", out var m) ? m.GetString() : null;

            if (!enabled || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(typeStr))
                continue;

            try
            {
                var type = ResolveTypeFromQualifiedName(typeStr);
                if (type == null)
                {
                    results.Add(new LoadEntryResult
                    {
                        Name = name,
                        Success = false,
                        Error = $"无法加载类型 '{typeStr}'"
                    });
                    continue;
                }

                RegisterType(registry, type, methodName, nameTracker);
                results.Add(new LoadEntryResult
                {
                    Name = name,
                    Success = true,
                    FunctionCount = 0
                });
            }
            catch (Exception ex) when (ex is not InvalidOperationException)
            {
                results.Add(new LoadEntryResult
                {
                    Name = name,
                    Success = false,
                    Error = ex.Message
                });
            }
        }
    }

    /// <summary>
    /// 按程序集限定名解析函数库类型。
    /// 两段式：① 默认 ALC（deps.json 已登记的正常库，如 FileDriven.Business/FrontEndDriven）用 Type.GetType；
    /// ② 默认 ALC 失败时交 ExtensionAssemblyHost 的 scoped ALC 显式发现（扩展层 Agent.* / GameEditor.*）。
    /// 不再依赖全局 AssemblyLoadContext.Default.Resolving 隐式兜底。
    /// </summary>
    private static Type? ResolveTypeFromQualifiedName(string typeStr)
    {
        var type = Type.GetType(typeStr, throwOnError: false);
        if (type != null)
            return type;

        return ExtensionAssemblyHost.ResolveType(typeStr);
    }

    private static void LoadFromAutoDiscovery(
        FunctionRegistry registry,
        DuplicateNameTracker nameTracker,
        List<LoadEntryResult> results)
    {
        var libraryTypes = AppDomain.CurrentDomain.GetAssemblies()
            .Where(asm => !asm.IsDynamic && !asm.ReflectionOnly)
            .SelectMany(asm =>
            {
                try { return asm.GetExportedTypes(); }
                catch { return Type.EmptyTypes; }
            })
            .Where(t => t is { IsClass: true, IsAbstract: true, IsPublic: true }
                        && t.GetCustomAttribute<FunctionLibraryAttribute>() != null)
            .ToList();

        foreach (var type in libraryTypes)
        {
            var attr = type.GetCustomAttribute<FunctionLibraryAttribute>()!;
            if (!attr.AutoDiscover)
                continue;

            var typeKey = type.FullName ?? type.Name;
            lock (_loadedLibraryTypes)
            {
                if (_loadedLibraryTypes.Contains(typeKey))
                    continue;
            }

            try
            {
                var countBefore = registry.GetRegisteredNames().Count();
                RegisterType(registry, type, methodName: null, nameTracker);
                var registeredCount = registry.GetRegisteredNames().Count() - countBefore;

                results.Add(new LoadEntryResult
                {
                    Name = type.Name,
                    Success = true,
                    FunctionCount = registeredCount
                });
            }
            catch (Exception ex) when (ex is not InvalidOperationException)
            {
                results.Add(new LoadEntryResult
                {
                    Name = type.Name,
                    Success = false,
                    Error = ex.Message
                });
            }
        }
    }

    private static void RegisterType(
        FunctionRegistry registry,
        Type type,
        string? methodName,
        DuplicateNameTracker nameTracker)
    {
        var typeKey = type.FullName ?? type.Name;
        lock (_loadedLibraryTypes)
        {
            if (!_loadedLibraryTypes.Add(typeKey))
            {
                throw new InvalidOperationException(
                    $"函数库类型 '{typeKey}' 已被注册一次。每个函数库只允许通过 FunctionLibraryLoader 注册一次，" +
                    "请检查 function-libraries.json 中是否存在重复条目，以及是否存在其他代码路径重复调用了 FunctionLibraryLoader.Load()。");
            }
        }

        MethodInfo? method = null;

        if (!string.IsNullOrEmpty(methodName))
        {
            method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
        }

        if (method == null)
        {
            method = type.GetMethod("RegisterAll", BindingFlags.Public | BindingFlags.Static,
                new[] { typeof(ITrConfigRegistry) });
        }

        if (method == null)
        {
            method = type.GetMethod("Register", BindingFlags.Public | BindingFlags.Static, Type.EmptyTypes);
        }

        if (method == null)
        {
            throw new InvalidOperationException(
                $"类型 '{type.FullName}' 没有可用的注册方法。需要 RegisterAll(ITrConfigRegistry) 或 Register()。");
        }

        var parameters = method.GetParameters();
        if (parameters.Length == 1 && typeof(ITrConfigRegistry).IsAssignableFrom(parameters[0].ParameterType))
        {
            var wrappingRegistry = new ConflictDetectingRegistry(registry, nameTracker);
            method.Invoke(null, new object[] { wrappingRegistry });
        }
        else if (parameters.Length == 0)
        {
            method.Invoke(null, null);
        }
        else
        {
            throw new InvalidOperationException(
                $"'{type.FullName}.{method.Name}' 的签名不兼容。支持 RegisterAll(ITrConfigRegistry) 或 Register()。");
        }
    }


    private sealed class DuplicateNameTracker
    {
        private readonly HashSet<string> _registered = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<(string Name, string Source1, string Source2)> _collisions = new();

        public bool TryRegister(string name, out string? existingSource)
        {
            if (_registered.Contains(name))
            {
                existingSource = "先前已注册";
                _collisions.Add((name, "先前已注册", "本次注册"));
                return false;
            }

            _registered.Add(name);
            existingSource = null;
            return true;
        }

        public void AssertNoDuplicates()
        {
            if (_collisions.Count == 0)
                return;

            var details = string.Join("; ", _collisions.Select(c =>
                $"'{c.Name}'"));
            throw new InvalidOperationException(
                $"检测到重复注册的函数名: [{details}]。每个函数名只允许注册一次。" +
                "请检查 function-libraries.json 以及各个 Library 的 RegisterAll 方法，确保没有重复注册。");
        }
    }


    private sealed class ConflictDetectingRegistry : ITrConfigRegistry
    {
        private readonly FunctionRegistry _inner;
        private readonly DuplicateNameTracker _tracker;

        public ConflictDetectingRegistry(FunctionRegistry inner, DuplicateNameTracker tracker)
        {
            _inner = inner;
            _tracker = tracker;
        }

        public void Register(string name, Delegate function, string? description = null)
        {
            if (!_tracker.TryRegister(name, out _))
            {
                throw new InvalidOperationException(
                    $"函数名 '{name}' 被重复注册。每个函数名在 FunctionRegistry 中必须唯一。");
            }
            _inner.Register(name, function, description);
        }

        public void Register(string name, Delegate function, IReadOnlyList<IParamConfig> parameters, string? description = null)
        {
            if (!_tracker.TryRegister(name, out _))
            {
                throw new InvalidOperationException(
                    $"函数名 '{name}' 被重复注册。每个函数名在 FunctionRegistry 中必须唯一。");
            }
            _inner.Register(name, function, parameters, description);
        }


        public void Register(string name, ITrConfig config)
            => _inner.Register(name, config);

        public void Unregister(string name)
            => _inner.Unregister(name);

        public bool TryGetConfig(string name, out ITrConfig config)
            => _inner.TryGetConfig(name, out config);

        public bool Contains(string name)
            => _inner.Contains(name);

        public IEnumerable<string> GetRegisteredNames()
            => _inner.GetRegisteredNames();

        public void Clear()
            => _inner.Clear();

        public Delegate? Find(string functionName)
            => _inner.Find(functionName);

        public bool TryGetDescriptor(string name, out IFunctionDescriptor? descriptor)
            => _inner.TryGetDescriptor(name, out descriptor);

        public bool TryGetDelegate(string name, out Delegate? function)
            => _inner.TryGetDelegate(name, out function);
    }
}