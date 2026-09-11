using Share.StateManagement;
using TConfig;

namespace Operation.StateBlock.Layering
{
    /// <summary>
    /// 分层状态键 —— (方案, StorageLayer, scope, 裸键) 的不可变值；由 <see cref="StateLayerScheme.Key"/> 构造。
    /// 全键 = <c>{命名空间}:{scope}:{key}</c>（对齐 <see cref="StateKey"/> 形态），
    /// PartitionPrefix 供按 scope 枚举/清理时隔离（键前缀剥离无需上层感知）。
    /// </summary>
    public readonly record struct LayeredStateKey(StateLayerScheme Scheme, StorageLayer Layer, string Scope, string Key)
    {
        /// <summary>本方案在对应层声明的命名空间。</summary>
        public string Namespace => Scheme.Namespace(Layer);

        public string FullKey => $"{Namespace}:{Scope}:{Key}";

        /// <summary>分区前缀（ns:scope:），枚举/清理时按它过滤并剥离。</summary>
        public string PartitionPrefix => $"{Namespace}:{Scope}:";

        public StateKey ToStateKey() => new(Namespace, Scope, Key);

        public override string ToString() => FullKey;
    }
}
