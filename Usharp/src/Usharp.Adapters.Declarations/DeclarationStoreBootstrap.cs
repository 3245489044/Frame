using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Operation.DataStoreDriven.Abstractions;
using Usharp.Semantics;

namespace Usharp.Adapters.Declarations;

/// <summary>
/// 声明入库闭环（L6）：把磁盘 <c>.ushar</c> 声明导入配置库（<c>usharp/**</c> 键域），
/// 返回可注册为运行期唯一声明源的后端声明库（<see cref="IDeclarationStore"/>）。
/// <para>
/// 迁移期口径：磁盘每次引导**覆盖**入库（磁盘 = 导入真源，保证库与盘一致）；装载面经
/// <c>DeclarationStoreAccess</c> 读库。fully 切 primary 后可停用导入（退役磁盘读）。
/// </para>
/// </summary>
public static class DeclarationStoreBootstrap
{
    /// <summary>前缀域：逻辑名 <c>workflows/task-provision</c> ⇒ 配置键 <c>usharp/workflows/task-provision</c>。</summary>
    public const string Prefix = "usharp/";

    /// <summary>构建后端声明库并把磁盘 <c>.ushar</c> 导入库；<paramref name="diskRoot"/> 缺/不存在 ⇒ 仅返回空库。</summary>
    public static async Task<IDeclarationStore> BuildAndImportAsync(
        IConfigStore configStore, string? diskRoot, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(configStore);
        var store = new BackendDeclarationStore(new ConfigDeclarationBackend(configStore), Prefix);
        if (string.IsNullOrWhiteSpace(diskRoot) || !Directory.Exists(diskRoot)) return store;

        var disk = new FileDeclarationStore(diskRoot);
        foreach (var entry in await disk.ListAsync(ct).ConfigureAwait(false))
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(entry.LogicalName)) continue;
            await store.PutAsync(entry, ct).ConfigureAwait(false); // 覆盖：磁盘为导入真源（保库≡盘）
        }
        return store;
    }
}
