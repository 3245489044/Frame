using System.IO;

namespace PathManager;

public static class PathResolver
{
    private static readonly Dictionary<string, string> _aliases = new(StringComparer.OrdinalIgnoreCase);

    private static volatile bool _defaultAliasesInitialized;

    private static void EnsureDefaultAliases()
    {
        if (_defaultAliasesInitialized)
            return;

        lock (_aliases)
        {
            if (_defaultAliasesInitialized)
                return;

            try
            {
                RegisterAliasInternal("Configs", ProjectDirectories.FileDriven_JsonConfig);
                RegisterAliasInternal("Scripts", ProjectDirectories.FileDriven_Scripts);
                RegisterAliasInternal("WorkFlow", ProjectDirectories.FileDriven_WorkFlowConfig);
                RegisterAliasInternal("Output", ProjectDirectories.FileDriven_LauncherOutputDir);
                RegisterAliasInternal("Root", ProjectDirectories.ProjectRoot);
            }
            catch
            {
            }

            _defaultAliasesInitialized = true;
        }
    }

    public static void RegisterAlias(string alias, string actualPath)
    {
        if (string.IsNullOrWhiteSpace(alias))
            throw new ArgumentException("Alias cannot be empty", nameof(alias));
        if (string.IsNullOrWhiteSpace(actualPath))
            throw new ArgumentException("Path cannot be empty", nameof(actualPath));

        lock (_aliases)
        {
            RegisterAliasInternal(alias, actualPath);
        }
    }

    private static void RegisterAliasInternal(string alias, string actualPath)
    {
        _aliases[alias] = actualPath;
    }

    public static string? ResolveConfigPath(string configPath)
    {
        if (string.IsNullOrWhiteSpace(configPath))
            return null;

        EnsureDefaultAliases();

        if (Path.IsPathRooted(configPath))
        {
            return File.Exists(configPath) ? configPath : null;
        }

        var aliasResult = TryResolveAliasPath(configPath);
        if (aliasResult != null)
            return aliasResult;

        try
        {
            var jsonConfigDir = ProjectDirectories.FileDriven_JsonConfig;
            var resolvedPath = Path.GetFullPath(Path.Combine(jsonConfigDir, configPath));
            if (File.Exists(resolvedPath))
                return resolvedPath;
        }
        catch
        {
        }

        try
        {
            var projectRoot = ProjectPathManager.FindProjectRoot();
            var projectRelativePath = Path.GetFullPath(Path.Combine(projectRoot, configPath));
            if (File.Exists(projectRelativePath))
                return projectRelativePath;
        }
        catch
        {
        }

        return null;
    }

    private static string? TryResolveAliasPath(string configPath)
    {
        var firstSeparator = configPath.IndexOfAny(new[] { '/', '\\' });
        if (firstSeparator <= 0)
            return null;

        var alias = configPath.Substring(0, firstSeparator);
        var relativePart = configPath.Substring(firstSeparator + 1).TrimStart('/', '\\');

        lock (_aliases)
        {
            if (!_aliases.TryGetValue(alias, out var basePath))
                return null;

            try
            {
                var resolved = Path.GetFullPath(Path.Combine(basePath, relativePart));
                if (File.Exists(resolved))
                    return resolved;
            }
            catch
            {
            }
        }

        return null;
    }

    public static string ResolveConfigPathUnsafe(string configPath)
    {
        if (string.IsNullOrWhiteSpace(configPath))
            return configPath;

        EnsureDefaultAliases();

        if (Path.IsPathRooted(configPath))
            return configPath;

        var aliasResult = TryResolveAliasPathUnsafe(configPath);
        if (aliasResult != null)
            return aliasResult;

        try
        {
            var jsonConfigDir = ProjectDirectories.FileDriven_JsonConfig;
            return Path.GetFullPath(Path.Combine(jsonConfigDir, configPath));
        }
        catch
        {
            return configPath;
        }
    }

    private static string? TryResolveAliasPathUnsafe(string configPath)
    {
        var firstSeparator = configPath.IndexOfAny(new[] { '/', '\\' });
        if (firstSeparator <= 0)
            return null;

        var alias = configPath.Substring(0, firstSeparator);
        var relativePart = configPath.Substring(firstSeparator + 1).TrimStart('/', '\\');

        lock (_aliases)
        {
            if (!_aliases.TryGetValue(alias, out var basePath))
                return null;

            try
            {
                return Path.GetFullPath(Path.Combine(basePath, relativePart));
            }
            catch
            {
                return null;
            }
        }
    }

    public static bool TryResolveAlias(string alias, out string? resolvedPath)
    {
        EnsureDefaultAliases();
        lock (_aliases)
        {
            return _aliases.TryGetValue(alias, out resolvedPath);
        }
    }

    public static IReadOnlyDictionary<string, string> GetAliases()
    {
        EnsureDefaultAliases();
        lock (_aliases)
        {
            return new Dictionary<string, string>(_aliases);
        }
    }


    public static List<string> ScanFiles(string directoryPath, string searchPattern, bool recursive = false)
    {
        var results = new List<string>();
        var resolvedDir = ResolveDirectoryPath(directoryPath);
        if (resolvedDir == null || !Directory.Exists(resolvedDir))
            return results;

        var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        results.AddRange(Directory.GetFiles(resolvedDir, searchPattern, option)
            .Select(f => Path.GetRelativePath(resolvedDir, f)));
        return results;
    }

    public static string? ResolveDirectoryPath(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
            return null;

        EnsureDefaultAliases();

        if (Path.IsPathRooted(directoryPath))
            return directoryPath;

        var aliasResult = TryResolveAliasDirectoryPath(directoryPath);
        if (aliasResult != null)
            return aliasResult;

        var knownDir = ResolveKnownDirectory(directoryPath);
        if (knownDir != null)
            return knownDir;

        try
        {
            var projectRoot = ProjectPathManager.FindProjectRoot();
            var resolvedPath = Path.GetFullPath(Path.Combine(projectRoot, directoryPath));
            if (Directory.Exists(resolvedPath))
                return resolvedPath;
        }
        catch
        {
        }

        return null;
    }

    private static string? TryResolveAliasDirectoryPath(string directoryPath)
    {
        var firstSeparator = directoryPath.IndexOfAny(new[] { '/', '\\' });
        if (firstSeparator <= 0)
        {
            lock (_aliases)
            {
                if (_aliases.TryGetValue(directoryPath, out var basePath))
                    return basePath;
            }
            return null;
        }

        var alias = directoryPath.Substring(0, firstSeparator);
        var relativePart = directoryPath.Substring(firstSeparator + 1).TrimStart('/', '\\');

        lock (_aliases)
        {
            if (!_aliases.TryGetValue(alias, out var basePath))
                return null;

            try
            {
                var resolved = Path.GetFullPath(Path.Combine(basePath, relativePart));
                if (Directory.Exists(resolved))
                    return resolved;
                return resolved;
            }
            catch
            {
                return null;
            }
        }
    }

    private static string? ResolveKnownDirectory(string directoryName)
    {
        return directoryName.ToLowerInvariant() switch
        {
            "scripts" => ProjectDirectories.FileDriven_Scripts,
            "configs" or "jsonconfig" => ProjectDirectories.FileDriven_JsonConfig,
            "workflow" => ProjectDirectories.FileDriven_WorkFlowConfig,
            "templetconfig" => ProjectDirectories.FileDriven_TempletConfig,
            "projectroot" or "root" => ProjectDirectories.ProjectRoot,
            _ => null
        };
    }
}
