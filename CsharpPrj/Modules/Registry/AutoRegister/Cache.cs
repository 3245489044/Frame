
using System.Collections.Concurrent;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Modules.ReflectionCommon;
using Microsoft.CodeAnalysis;

namespace Modules.Registry.AutoRegister.Cache;


public static class HashHelper
{
    public static string ComputeFileHash(string path)
    {
        try
        {
            using var sha256 = SHA256.Create();
            using var stream = File.OpenRead(path);
            var hash = sha256.ComputeHash(stream);
            return Convert.ToHexString(hash).ToLower();
        }
        catch
        {
            return string.Empty;
        }
    }

    public static string ComputeHash(string input)
    {
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLower()[..16];
    }
}


public static class TypeHelper
{
    public static MethodInfo? FindMethodInfo(this Assembly assembly, ExtractedMethodInfo method)
    {
        var fullClassName = method.ContainingClassName;
        if (method.ContainingNestedClassName != null)
        {
            fullClassName += "+" + method.ContainingNestedClassName;
        }

        var type = assembly.GetType(fullClassName);
        if (type == null)
        {
            type = assembly.GetTypes()
                .FirstOrDefault(t => t.Name == method.ContainingClassName);
        }

        if (type == null)
            return null;

        return type.GetMethod(method.MethodName,
            BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance);
    }

    public static bool IsAsyncType(Type returnType)
    {
        return returnType == typeof(Task) ||
               (returnType.IsGenericType &&
                returnType.GetGenericTypeDefinition() == typeof(Task<>));
    }

    public static Type ResolveType(string typeName)
    {
        if (string.IsNullOrEmpty(typeName))
            return typeof(object);

        try
        {
            var type = Type.GetType(typeName);
            if (type != null)
                return type;
        }
        catch
        {
        }

        return typeName.ToLower() switch
        {
            "void" => typeof(void),
            "int" or "system.int32" => typeof(int),
            "long" or "system.int64" => typeof(long),
            "float" or "system.single" => typeof(float),
            "double" or "system.double" => typeof(double),
            "bool" or "system.boolean" => typeof(bool),
            "string" or "system.string" => typeof(string),
            "char" or "system.char" => typeof(char),
            "byte" or "system.byte" => typeof(byte),
            "short" or "system.int16" => typeof(short),
            "uint" or "system.uint32" => typeof(uint),
            "ulong" or "system.uint64" => typeof(ulong),
            "ushort" or "system.uint16" => typeof(ushort),
            "sbyte" or "system.sbyte" => typeof(sbyte),
            "object" or "system.object" => typeof(object),
            "decimal" or "system.decimal" => typeof(decimal),
            "task" or "system.threading.tasks.task" => typeof(Task),
            _ => typeof(object)
        };
    }
}


public static class CacheKeyGenerator
{
    public static string GenerateForAssembly(string assemblyPath, RegistrationEntry entry)
    {
        var fileInfo = new FileInfo(assemblyPath);
        var sourceId = $"{assemblyPath}|{fileInfo.LastWriteTimeUtc.Ticks}|{fileInfo.Length}";
        return HashHelper.ComputeHash(sourceId + SerializeConfig(entry));
    }

    public static string GenerateForSource(string sourcePath, string sourceCode, RegistrationEntry entry)
    {
        var sourceHash = HashHelper.ComputeHash(sourceCode);
        return HashHelper.ComputeHash(sourcePath + "|" + sourceHash + SerializeConfig(entry));
    }

    private static string SerializeConfig(RegistrationEntry entry)
    {
        return JsonSerializer.Serialize(new
        {
            entry.ClassName,
            entry.MethodFilters,
            entry.Naming,
            entry.Description
        });
    }
}


public class CachedManifest
{
    public string Version { get; set; } = "1.0";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public CachedSourceInfo? SourceInfo { get; set; }

    public string? ConfigHash { get; set; }

    public List<CachedFunctionInfo> RegisteredFunctions { get; set; } = new();

    public static CachedManifest FromExtractedMethodInfos(List<ExtractedMethodInfo> methods, SourceConfig source, string? configHash = null)
    {
        var manifest = new CachedManifest
        {
            ConfigHash = configHash,
            SourceInfo = new CachedSourceInfo
            {
                Type = source.Type,
                Path = source.Path,
                SourceFile = source.Path
            },

            RegisteredFunctions = methods.Select(m => new CachedFunctionInfo
            {
                Name = m.MethodName,
                ClassName = m.ContainingClassName,
                NestedClassName = m.ContainingNestedClassName,
                Description = m.Description,
                ReturnType = m.ReturnType.FullName ?? m.ReturnType.Name,
                IsStatic = m.IsStatic,
                IsAsync = m.IsAsync,
                Parameters = m.Parameters.Select(p => new CachedParameterInfo
                {
                    Name = p.Name,
                    Type = p.Type.FullName ?? p.Type.Name,
                    IsOptional = p.IsOptional,
                    DefaultValue = p.DefaultValue?.ToString()
                }).ToList()
            }).ToList()
        };

        if (File.Exists(source.Path))
        {
            var fi = new FileInfo(source.Path);
            manifest.SourceInfo.FileHash = HashHelper.ComputeFileHash(source.Path);
            manifest.SourceInfo.LastModified = fi.LastWriteTimeUtc;
            manifest.SourceInfo.FileSize = fi.Length;
        }

        return manifest;
    }

    public List<ExtractedMethodInfo> ToExtractedMethodInfos()
    {
        return RegisteredFunctions.Select(f => new ExtractedMethodInfo
        {
            MethodName = f.Name,
            ContainingClassName = f.ClassName,
            ContainingNestedClassName = f.NestedClassName,
            ReturnType = TypeHelper.ResolveType(f.ReturnType),
            Parameters = f.Parameters.Select(p => new ExtractedMethodInfo.ParameterInfo
            {
                Name = p.Name,
                Type = TypeHelper.ResolveType(p.Type),
                IsOptional = p.IsOptional,
                DefaultValue = p.DefaultValue
            }).ToArray(),
            Description = f.Description,
            IsStatic = f.IsStatic,
            IsAsync = f.IsAsync,
            Executor = null
        }).ToList();
    }
}

public class CachedSourceInfo
{
    public string Type { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string? SourceFile { get; set; }
    public string? FileHash { get; set; }
    public DateTime LastModified { get; set; }
    public long FileSize { get; set; }
}

public class CachedFunctionInfo
{
    public string Name { get; set; } = string.Empty;
    public string ClassName { get; set; } = string.Empty;
    public string? NestedClassName { get; set; }
    public string Description { get; set; } = string.Empty;
    public string ReturnType { get; set; } = string.Empty;
    public bool IsStatic { get; set; }
    public bool IsAsync { get; set; }
    public List<CachedParameterInfo> Parameters { get; set; } = new();
}

public class CachedParameterInfo
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public bool IsOptional { get; set; }
    public string? DefaultValue { get; set; }
}


public interface IAutoRegisterCache
{
    bool TryGetManifest(string cacheKey, out List<ExtractedMethodInfo> methods);

    void SaveManifest(string cacheKey, List<ExtractedMethodInfo> methods, SourceConfig source, string? configHash = null);

    bool TryGetCompiledAssembly(string cacheKey, out Assembly assembly);

    void SaveCompiledAssembly(string cacheKey, Assembly assembly);

    bool TryGetSemanticModel(string cacheKey, out SemanticModel semanticModel);

    void SaveSemanticModel(string cacheKey, SemanticModel semanticModel);

    bool IsValid(string cacheKey, SourceConfig source);

    void Cleanup(TimeSpan maxAge);
}


public class FileAutoRegisterCache : IAutoRegisterCache
{
    private readonly string _manifestDir;
    private readonly string _assemblyDir;
    private readonly string _snapshotDir;
    private readonly string _semanticModelDir;
    private readonly ConcurrentDictionary<string, List<ExtractedMethodInfo>> _memoryCache = new();
    private readonly ConcurrentDictionary<string, SemanticModel> _semanticModelCache = new();

    public FileAutoRegisterCache(string? baseDir = null)
    {
        baseDir ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AutoRegisterCache", "v1");
        _manifestDir = Path.Combine(baseDir, "manifests");
        _assemblyDir = Path.Combine(baseDir, "assemblies");
        _snapshotDir = Path.Combine(baseDir, "snapshots");
        _semanticModelDir = Path.Combine(baseDir, "semanticModels");
        Directory.CreateDirectory(_manifestDir);
        Directory.CreateDirectory(_assemblyDir);
        Directory.CreateDirectory(_snapshotDir);
        Directory.CreateDirectory(_semanticModelDir);
    }

    public string SnapshotDir => _snapshotDir;

    public bool TryGetManifest(string cacheKey, out List<ExtractedMethodInfo> methods)
    {
        if (_memoryCache.TryGetValue(cacheKey, out methods!))
            return true;

        var path = Path.Combine(_manifestDir, $"{cacheKey}.json");
        if (File.Exists(path))
        {
            try
            {
                var json = File.ReadAllText(path);
                var manifest = JsonSerializer.Deserialize<CachedManifest>(json);
                if (manifest != null)
                {
                    methods = manifest.ToExtractedMethodInfos();
                    _memoryCache[cacheKey] = methods;
                    return true;
                }
            }
            catch
            {
            }
        }

        methods = null!;
        return false;
    }

    public void SaveManifest(string cacheKey, List<ExtractedMethodInfo> methods, SourceConfig source, string? configHash = null)
    {
        var manifest = CachedManifest.FromExtractedMethodInfos(methods, source, configHash);
        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(Path.Combine(_manifestDir, $"{cacheKey}.json"), json);
        _memoryCache[cacheKey] = methods;
    }

    public bool TryGetCompiledAssembly(string cacheKey, out Assembly assembly)
    {
        var dllPath = Path.Combine(_assemblyDir, $"{cacheKey}.dll");
        if (File.Exists(dllPath))
        {
            try
            {
                assembly = Assembly.LoadFrom(dllPath);
                return true;
            }
            catch
            {
            }
        }
        assembly = null!;
        return false;
    }

    public void SaveCompiledAssembly(string cacheKey, Assembly assembly)
    {
        var dllPath = Path.Combine(_assemblyDir, $"{cacheKey}.dll");
        if (!string.IsNullOrEmpty(assembly.Location) && File.Exists(assembly.Location))
        {
            try
            {
                File.Copy(assembly.Location, dllPath, overwrite: true);
                var pdbPath = Path.ChangeExtension(assembly.Location, ".pdb");
                if (File.Exists(pdbPath))
                    File.Copy(pdbPath, Path.Combine(_assemblyDir, $"{cacheKey}.pdb"), overwrite: true);
            }
            catch
            {
            }
        }
    }

    public bool TryGetSemanticModel(string cacheKey, out SemanticModel semanticModel)
    {
        if (_semanticModelCache.TryGetValue(cacheKey, out semanticModel!))
            return true;

        semanticModel = null!;
        return false;
    }

    public void SaveSemanticModel(string cacheKey, SemanticModel semanticModel)
    {
        _semanticModelCache[cacheKey] = semanticModel;
    }

    public bool IsValid(string cacheKey, SourceConfig source)
    {
        var path = Path.Combine(_manifestDir, $"{cacheKey}.json");
        if (!File.Exists(path)) return false;

        try
        {
            var json = File.ReadAllText(path);
            var manifest = JsonSerializer.Deserialize<CachedManifest>(json);
            if (manifest?.SourceInfo == null) return false;

            if (source.Type == "assembly" && File.Exists(source.Path))
            {
                var fi = new FileInfo(source.Path);
                return manifest.SourceInfo.LastModified == fi.LastWriteTimeUtc
                    && manifest.SourceInfo.FileSize == fi.Length;
            }

            if (source.Type == "source" && File.Exists(source.Path))
            {
                var currentHash = HashHelper.ComputeFileHash(source.Path);
                return manifest.SourceInfo.FileHash == currentHash;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Cleanup(TimeSpan maxAge)
    {
        var cutoff = DateTime.UtcNow - maxAge;

        CleanupDirectory(_manifestDir, "*.json", cutoff);
        CleanupDirectory(_assemblyDir, "*.dll", cutoff);
    }

    private static void CleanupDirectory(string dir, string pattern, DateTime cutoff)
    {
        if (!Directory.Exists(dir)) return;

        foreach (var f in Directory.GetFiles(dir, pattern))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(f) < cutoff)
                {
                    File.Delete(f);
                    if (pattern == "*.dll")
                    {
                        var pdb = Path.ChangeExtension(f, ".pdb");
                        if (File.Exists(pdb))
                            File.Delete(pdb);
                    }
                }
            }
            catch
            {
            }
        }
    }
}


public static class RegistrySnapshot
{
    public static void Export(AutoRegisterResult result, string filePath)
    {
        var snapshot = new RegistrySnapshotData
        {
            Version = "1.0",
            ExportedAt = DateTime.UtcNow,
            FunctionCount = result.RegisteredItems.Count,
            Functions = result.RegisteredItems
                .Where(item => item.Status == RegisteredItemStatus.Success)
                .Select(item => new CachedFunctionInfo
                {
                    Name = item.RegisteredName,
                    ClassName = item.ClassName,
                    Description = item.Description,
                    ReturnType = item.ReturnType?.FullName ?? "unknown",
                    IsAsync = item.IsAsync,
                    Parameters = item.Parameters?.Select(p => new CachedParameterInfo
                    {
                        Name = p.Name,
                        Type = p.Type.FullName ?? "unknown",
                        IsOptional = p.IsOptional
                    }).ToList() ?? new()
                }).ToList()
        };

        var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        File.WriteAllText(filePath, json);
    }

    public static RegistrySnapshotData Import(string filePath)
    {
        var json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<RegistrySnapshotData>(json)
            ?? throw new InvalidDataException("Invalid snapshot file");
    }
}

public class RegistrySnapshotData : CachedManifest
{
    public DateTime ExportedAt { get; set; }

    public int FunctionCount
    {
        get => RegisteredFunctions.Count;
        set { }
    }

    public List<CachedFunctionInfo> Functions
    {
        get => RegisteredFunctions;
        set => RegisteredFunctions = value;
    }
}
