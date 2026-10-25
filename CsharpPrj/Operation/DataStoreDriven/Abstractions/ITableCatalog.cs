using UniversalWorkflowFramework.StorageFramework.Abstractions;

namespace Operation.DataStoreDriven.Abstractions;

/// <summary>
/// 单表声明：建表/收敛策略由**各模块**声明，执行仍走 SqliteSchema（U2）。
/// <para>收敛策略固定经 <c>KvUsharpTable.EnsureConverged</c>（单入口），本类型不新增第二条收敛路径。</para>
/// </summary>
public interface ITableDeclaration
{
    /// <summary>物理表名（已含前缀）。</summary>
    string TableName { get; }

    /// <summary>幂等建表（收敛 + 建表 + 索引）。</summary>
    Task EnsureAsync(IDbCommandExecutor executor, CancellationToken ct = default);
}

/// <summary>委托式表声明（各 Store 用自身 InitializeAsync 作为单源实现）。</summary>
public sealed class DelegateTableDeclaration : ITableDeclaration
{
    private readonly Func<IDbCommandExecutor, CancellationToken, Task> _ensure;

    public DelegateTableDeclaration(string tableName, Func<IDbCommandExecutor, CancellationToken, Task> ensure)
    {
        TableName = string.IsNullOrWhiteSpace(tableName)
            ? throw new ArgumentException("表名不能为空。", nameof(tableName))
            : tableName;
        _ensure = ensure ?? throw new ArgumentNullException(nameof(ensure));
    }

    public string TableName { get; }

    public Task EnsureAsync(IDbCommandExecutor executor, CancellationToken ct = default)
        => _ensure(executor, ct);
}

/// <summary>表清单单源：DeclareTables 汇入 → EnsureAllAsync 统一执行 → Register。</summary>
public interface ITableCatalog
{
    /// <summary>声明一张表（不执行任何 IO）。</summary>
    void Add(ITableDeclaration declaration);

    /// <summary>已声明表（声明序 = 执行序）。</summary>
    IReadOnlyList<ITableDeclaration> Declarations { get; }

    /// <summary>按声明序幂等建表。</summary>
    Task EnsureAllAsync(IDbCommandExecutor executor, CancellationToken ct = default);
}

/// <summary>表清单默认实现。</summary>
public sealed class TableCatalog : ITableCatalog
{
    private readonly List<ITableDeclaration> _declarations = new();

    public void Add(ITableDeclaration declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        _declarations.Add(declaration);
    }

    public IReadOnlyList<ITableDeclaration> Declarations => _declarations;

    public async Task EnsureAllAsync(IDbCommandExecutor executor, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(executor);
        foreach (var declaration in _declarations)
        {
            ct.ThrowIfCancellationRequested();
            await declaration.EnsureAsync(executor, ct).ConfigureAwait(false);
        }
    }
}
