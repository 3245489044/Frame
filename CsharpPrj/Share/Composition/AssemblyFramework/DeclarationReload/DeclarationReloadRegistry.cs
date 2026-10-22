using System;
using System.Collections.Generic;
using Share.Foundations.Diagnostics;

namespace Share.Composition.AssemblyFramework.DeclarationReload;

/// <summary>
/// 声明热装载参与者注册表：各进程级声明缓存装载器在装配期注册失效委托，
/// 框架统一遍历失效；单个参与者异常被隔离，不阻断其余。
/// </summary>
public sealed class DeclarationReloadRegistry
{
    private readonly List<(string Name, Action Reset)> _participants = new();
    private readonly object _gate = new();
    private readonly IDiagnosticsSink _diagnostics;

    public DeclarationReloadRegistry(IDiagnosticsSink diagnostics)
        => _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));

    /// <summary>进程级默认注册表（组合根装配期绑定诊断端口后使用）。</summary>
    public static DeclarationReloadRegistry Default { get; } =
        new(NullDiagnosticsSink.Instance);

    /// <summary>注册一个缓存失效参与者（幂等：同名重复注册被忽略）。</summary>
    public void Register(string name, Action reset)
    {
        if (string.IsNullOrWhiteSpace(name) || reset is null) return;
        lock (_gate)
        {
            foreach (var (existing, _) in _participants)
                if (string.Equals(existing, name, StringComparison.Ordinal)) return;
            _participants.Add((name, reset));
        }
    }

    /// <summary>已注册参与者名清单（诊断/门禁用）。</summary>
    public IReadOnlyList<string> RegisteredNames
    {
        get
        {
            lock (_gate)
            {
                var names = new List<string>(_participants.Count);
                foreach (var (name, _) in _participants) names.Add(name);
                return names;
            }
        }
    }

    /// <summary>失效全部已注册参与者（遍历注册表；单个异常隔离并记录）。</summary>
    public void ReloadAll()
    {
        (string Name, Action Reset)[] snapshot;
        lock (_gate) snapshot = _participants.ToArray();

        foreach (var (name, reset) in snapshot)
        {
            try { reset(); }
            catch (Exception ex)
            {
                _diagnostics.Report("DeclarationReloadFramework_participant_failed",
                    new { participant = name, error = ex.Message });
            }
        }

        _diagnostics.Report("DeclarationReloadFramework_reloaded", new { participants = snapshot.Length });
    }
}
