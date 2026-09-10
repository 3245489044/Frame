#nullable enable

namespace Modules.Maintain.Ports
{
    /// <summary>
    /// 状态存储：数据快照的保存 / 还原 / 删除，支撑（审计与）回滚。
    /// 存储背板（内存 / 数据库 / ur_config 等）由实现方绑定，本模块只依赖端口。
    /// </summary>
    public interface IMaintainStateStore<TData>
    {
        /// <summary>保存一份快照，返回状态 id（配合 revision 标识可追溯语义）。</summary>
        string Save(IReadOnlyList<TData> snapshot, string revision);

        /// <summary>按状态 id 还原快照；不存在返回 null。</summary>
        IReadOnlyList<TData>? Restore(string stateId);

        /// <summary>删除指定状态。</summary>
        void Delete(string stateId);
    }
}