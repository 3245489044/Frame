
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using TConfig;

namespace Modules.Registry;


public interface IRegistryManager
{
    void RegisterRegistry<T>(string name, T registry) where T : IRegistry;

    T GetRegistry<T>(string name) where T : IRegistry;

    bool TryGetRegistry<T>(string name, [MaybeNullWhen(false)] out T registry) where T : IRegistry;
}


public interface IRegistryQuery
{
    IFunctionDescriptor? ResolveFunction(string name);

    bool TryExecuteFunction(string name, object?[]? args, out object? result);
}


public interface IRegistryDiagnostics
{
    IReadOnlyList<DuplicateRegistration> FindDuplicates();

    RegistryDiagnosticInfo Diagnose();

    void DumpAllRegistrations(TextWriter writer);
}

public class DuplicateRegistration
{
    public string Name { get; init; } = string.Empty;

    public List<RegistrationSource> Registrations { get; init; } = new();
}

public class RegistrationSource
{
    public string RegistryName { get; init; } = string.Empty;

    public string RegistryTypeName { get; init; } = string.Empty;

    public string? ValueTypeName { get; init; }
}

public class RegistryDiagnosticInfo
{
    public Dictionary<string, RegistryStats> Stats { get; } = new();

    public List<DuplicateRegistration> Duplicates { get; } = new();

    public DateTime GeneratedAt { get; } = DateTime.UtcNow;

    public void Add(string name, RegistryStats stats) => Stats[name] = stats;
}

public record RegistryStats(int Count, string? TypeName = null);


public interface IRegistryLifecycle
{
    void Initialize();

    void Shutdown();

    bool IsInitialized { get; }
}

public static class RegistryLifecycleExtensions
{
    public static void SafeInitialize(this IRegistryLifecycle lifecycle)
    {
        if (!lifecycle.IsInitialized)
            lifecycle.Initialize();
    }

    public static void InitializeAll(this IEnumerable<IRegistryLifecycle> lifecycles)
    {
        foreach (var lc in lifecycles)
            lc.SafeInitialize();
    }
}


public enum ConflictStrategy
{
    Throw,

    Overwrite,

    Skip,

    Merge
}

public interface IConflictResolver
{
    ConflictStrategy Resolve(string key, string sourceA, string sourceB);
}
