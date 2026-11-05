using System;
using Modules.KeySpace.Model;

namespace Modules.KeySpace.Rules
{
    /// <summary>
    /// **单键域**（流式族）键门面通用框架：一个 <c>FamilyId</c> + 容器轴 ⇒ 身份键 / 位置键 / 前缀 / 反解 / 族判据。
    /// <para>
    /// 存在理由（SingleImplementation / TH-12）：消息流 / 段流 / 会话视图 / 归档 / 观测五个单键域曾各自复制
    /// <c>IdentityKey</c>（直返容器）、<c>StreamPositionKey</c>/<c>PrefixOf</c>（同实现两个名字）、
    /// <c>DisplayKey</c>、<c>TryParse</c>、<c>Is*Key</c>，差异仅剩 <c>FamilyId</c>；任一处改模板即静默漂移。
    /// </para>
    /// <para>纪律：① 一切构造/反解只经 <see cref="KeyOps"/>（禁手拼前缀）；② 轴名由调用方域常量提供（框架零域词）；
    /// ③ 值序/形态由声明面保证，本件只做轴值投影。</para>
    /// </summary>
    public readonly struct SingleKeyFamily
    {
        private readonly KeyOps? _ops;
        private readonly KeyAxis _containerAxis;
        private readonly KeyAxis _roundAxis;
        private readonly KeyAxis _kindAxis;
        private readonly KeyAxis _ordinalAxis;

        /// <summary>仅容器轴（单键族：<c>{container}</c>）。</summary>
        public SingleKeyFamily(KeyOps ops, string familyId, KeyAxis containerAxis)
            : this(ops, familyId, containerAxis, default, default, default)
        {
        }

        /// <summary>全轴（多行显示键：<c>{container}.r{round}.{kind}.{ordinal}</c>）。</summary>
        public SingleKeyFamily(
            KeyOps ops, string familyId, KeyAxis containerAxis,
            KeyAxis roundAxis, KeyAxis kindAxis, KeyAxis ordinalAxis)
        {
            ArgumentNullException.ThrowIfNull(ops);
            ArgumentException.ThrowIfNullOrWhiteSpace(familyId);
            if (containerAxis.IsEmpty) throw new ArgumentException("容器轴不得为空", nameof(containerAxis));

            _ops = ops;
            FamilyId = familyId;
            _containerAxis = containerAxis;
            _roundAxis = roundAxis;
            _kindAxis = kindAxis;
            _ordinalAxis = ordinalAxis;
        }

        /// <summary>键族 id（声明面单源）。</summary>
        public string FamilyId { get; }

        /// <summary>键族前缀（声明派生；族判据唯一来源）。</summary>
        public string Prefix => Ops.Prefix(FamilyId);

        /// <summary>存储面规范模板（声明派生；禁手拼）。</summary>
        public string Template => Ops.Template(FamilyId);

        /// <summary>身份键（= 位置键去族前缀；单键域两者同形 ⇒ 直返容器）。</summary>
        public string IdentityKey(string container)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(container);
            return container;
        }

        /// <summary>Stream 单键位置键（<c>{prefix}.{container}</c>）。</summary>
        public string StreamPositionKey(string container)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(container);
            var ops = Ops;
            return ops.Render(ops.With(ops.Root(FamilyId), _containerAxis, container));
        }

        /// <summary>读侧键前缀（= 单键位置键；同一实现，禁第二份）。</summary>
        public string PrefixOf(string container) => StreamPositionKey(container);

        /// <summary>帧级显示键（读侧内存合成，非持久位置）；需全轴构造。</summary>
        public string DisplayKey(string container, int round, string kind, long ordinal)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(container);
            ArgumentException.ThrowIfNullOrWhiteSpace(kind);
            if (_roundAxis.IsEmpty || _kindAxis.IsEmpty || _ordinalAxis.IsEmpty)
                throw new InvalidOperationException($"族 {FamilyId} 未声明完整显示轴（round/kind/ordinal）");

            var ops = Ops;
            var key = ops
                .With(ops.Root(FamilyId), _containerAxis, container)
                .With(_roundAxis, round)
                .With(_kindAxis, kind)
                .With(_ordinalAxis, ordinal);
            return ops.Render(ops.At(key, KeySurface.Display));
        }

        /// <summary>族判据（结构事实：解析成功；前缀片段不算键）。</summary>
        public bool IsKey(string? raw) => Ops.IsKeyOf(FamilyId, KeySurface.Storage, raw);

        /// <summary>唯一反解：<c>{prefix}.{container}</c> → 容器（尾随点号 = 前缀片段，拒绝）。</summary>
        public bool TryParse(string? raw, out string container)
        {
            container = string.Empty;
            if (string.IsNullOrWhiteSpace(raw)) return false;

            var ops = Ops;
            if (!ops.TryParse(FamilyId, KeySurface.Storage, raw, out var parsed, out _)) return false;

            var value = ops.Value(parsed, _containerAxis);
            if (string.IsNullOrEmpty(value)) return false;

            container = value;
            return true;
        }

        private KeyOps Ops => _ops ?? throw new InvalidOperationException(
            $"单键域门面未装配（FamilyId={FamilyId ?? "<null>"}；禁回落到第二套内置形态）");
    }
}
