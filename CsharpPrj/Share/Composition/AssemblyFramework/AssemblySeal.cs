using System;

namespace Share.Composition.AssemblyFramework
{
    /// <summary>
    /// 装配产物封存：<see cref="Seal"/> 之后禁止写入（同值重入不受限）。
    /// <para>通用件：零领域类型（封存文案为装配语义，不含领域词）。</para>
    /// </summary>
    public sealed class AssemblySeal
    {
        private bool _sealed;

        /// <summary>是否已封存。</summary>
        public bool IsSealed => _sealed;

        /// <summary>封存（幂等）。</summary>
        public void Seal() => _sealed = true;

        /// <summary>已封存且值不同 → <see cref="InvalidOperationException"/>（同引用幂等）。</summary>
        public void Assign<T>(ref T? field, T? value) where T : class
        {
            if (ReferenceEquals(field, value)) return;
            if (_sealed)
                throw new InvalidOperationException(
                    "装配上下文已封存：E 段末 Seal 之后禁止写入（同实例幂等重入不受限）。");
            field = value;
        }
    }
}
