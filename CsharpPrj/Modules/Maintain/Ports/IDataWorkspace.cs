#nullable enable

namespace Modules.Maintain.Ports
{
    /// <summary>
    /// 数据工作区：对一组同类数据（无序集合或有序链）的统一读写。
    /// 单工作区单写者语义，多写者由上层编排。
    /// </summary>
    public interface IDataWorkspace<TData>
    {
        /// <summary>当前全部数据（读取时为集合快照，不保证顺序）。</summary>
        IEnumerable<TData> Data { get; }

        /// <summary>键提取。</summary>
        string? GetKey(TData item);

        /// <summary>按工作区识别的键执行幂等写入（身份相同则更新，否则新增）。</summary>
        void Upsert(TData item);

        /// <summary>按键移除，自动应用引用清理（引用清理由上层结合 <see cref="IReferenceMap{TData}"/> 完成）。</summary>
        bool Remove(string key);

        /// <summary>按键查找。</summary>
        TData? Find(string key);
    }
}