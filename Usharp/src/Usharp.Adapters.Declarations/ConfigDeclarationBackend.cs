using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Operation.DataStoreDriven.Abstractions;
using Usharp.Semantics;

namespace Usharp.Adapters.Declarations;

/// <summary>
/// 声明库的**配置存储后端**：以 <see cref="IConfigStore"/>（ur_config / config 节点）为键值后端，
/// 承载 USharp 声明文本（<b>编排入库</b>的落地形态：库里存 键 → 声明文本）。
/// <para>
/// 典型用法（组合根）：<c>new BackendDeclarationStore(new ConfigDeclarationBackend(configStore), "usharp/")</c>
/// ⇒ 逻辑名 <c>workflows/task-provision</c> 落在配置键 <c>usharp/workflows/task-provision</c>。
/// </para>
/// <para>前缀过滤在 <see cref="IConfigStore.GetAllAsync"/> 之上实现（配置端口无前缀查询原语）；只读/写经既有端口，不新增存储机制。</para>
/// </summary>
public sealed class ConfigDeclarationBackend : IDeclarationBackend
{
    private readonly IConfigStore _config;

    /// <param name="config">配置即数据行端口（ur_config / NodeType=config）。</param>
    public ConfigDeclarationBackend(IConfigStore config)
        => _config = config ?? throw new ArgumentNullException(nameof(config));

    /// <inheritdoc />
    public async Task<IReadOnlyList<(string Key, string Text)>> ListAsync(string prefix, CancellationToken ct = default)
    {
        var all = await _config.GetAllAsync(ct).ConfigureAwait(false);
        var p = prefix ?? string.Empty;

        return all
            .Where(kv => kv.Key.StartsWith(p, StringComparison.Ordinal))
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => (kv.Key, kv.Value ?? string.Empty))
            .ToList();
    }

    /// <inheritdoc />
    public Task<string?> GetAsync(string key, CancellationToken ct = default)
        => _config.GetAsync(key, ct);

    /// <inheritdoc />
    public Task PutAsync(string key, string text, CancellationToken ct = default)
        => _config.SetAsync(key, text ?? string.Empty, ct);
}
