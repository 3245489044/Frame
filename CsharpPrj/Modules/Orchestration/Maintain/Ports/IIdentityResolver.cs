#nullable enable

namespace Modules.Maintain.Ports
{
    /// <summary>
    /// 身份解析：键提取 / 键回写 / 归一化 / 别名匹配 / 迁移新键生成。
    /// 要求同时能读出与写回键字段，以支撑 <c>Rename/Remap</c> 迁移操作。
    /// </summary>
    public interface IIdentityResolver<TData>
    {
        /// <summary>读出数据项的键（可能为 null）。</summary>
        string? GetKey(TData item);

        /// <summary>迁移时把数据项的键改写为新键。</summary>
        void SetKey(TData item, string key);

        /// <summary>归一到比较维度（用于判重 / 别名）。</summary>
        string Normalize(string key);

        /// <summary>两个键是否可视为同一身份（归一化相等）。</summary>
        bool IsAlias(string a, string b);

        /// <summary>迁移时的唯一新键生成。</summary>
        string GenerateNewKey(string baseKey);
    }
}