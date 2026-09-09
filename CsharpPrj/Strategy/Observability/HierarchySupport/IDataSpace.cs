using System;
using System.Collections.Generic;

namespace Share.HierarchySupport
{
    /// <summary>
    /// 类型化数据空间 —— 统一数据传递标准的物理通道接口。
    /// 收敛 <c>AgentTaskItem.Data</c> / <c>RoundPipelineContext.Vars</c> 两类读写为同一契约：
    /// 类型化读写（<see cref="Get{T}"/> / <see cref="Set{T}"/> / <see cref="TryGet{T}"/>）+ 契约校验 + 变更审计。
    /// 实现类同时实现 <see cref="IDictionary{TKey,TValue}"/>，使根数据空间可直接作统一上下文
    /// （AxiomContext.Data / ILoopState.Data）与快照序列化的字典视图。
    /// 写前按 <see cref="DataContractRegistry"/> 校验「已注册 + 类型匹配」，杜绝魔法键 / 弱类型静默流通；
    /// <c>validate = false</c> 时仅做纯字典传递（骨架临时空间 / 测试直连）。
    /// </summary>
    public interface IDataSpace
    {
        /// <summary>类型化读取；键缺失返回 default；类型不符抛异常（写侧已校验，异常仅源于调用方泛型误用）。</summary>
        T? Get<T>(string key);

        /// <summary>类型化安全读取：缺失或类型不符返回 false，<paramref name="value"/> 可为 null。</summary>
        bool TryGet<T>(string key, out T? value);

        /// <summary>类型化写入：默认校验契约已注册且类型匹配。</summary>
        void Set<T>(string key, T value);

        /// <summary>类型化写入（弱类型重载，值可为 null）。</summary>
        void Set(string key, object? value);

        bool ContainsKey(string key);

        /// <summary>移除键；返回是否移除成功（与 IDictionary 语义对齐）。</summary>
        bool Remove(string key);

        int Count { get; }

        /// <summary>全量只读快照（兼容旧裸字典读取 / 序列化 / 迁移 / LINQ）。</summary>
        IReadOnlyDictionary<string, object?> Snapshot();
    }
}
