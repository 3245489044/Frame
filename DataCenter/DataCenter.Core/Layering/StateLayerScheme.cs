using TConfig;

namespace Operation.StateBlock.Layering
{
    /// <summary>
    /// 分层键方案 —— 把「命名空间 + StorageLayer + scope + 裸键 → 全键/StateKey」的映射从上层调用方收进框架，
    /// 使状态读写客户端只依赖本方案，不再持有 StateCenter 或手工拼接/剥离前缀。
    /// 每个「域（domain）」可声明一套按 StorageLayer 区分的命名空间表；同层允许多 scope 分区，枚举按 PartitionPrefix 隔离。
    /// </summary>
    public sealed class StateLayerScheme
    {
        private readonly string _nsConfig;
        private readonly string _nsSession;
        private readonly string _nsTransient;

        /// <param name="nsConfig">Config 层命名空间。</param>
        /// <param name="nsSession">Session 层命名空间。</param>
        /// <param name="nsTransient">Transient 层命名空间。</param>
        public StateLayerScheme(string nsConfig, string nsSession, string nsTransient)
        {
            _nsConfig = nsConfig ?? throw new ArgumentNullException(nameof(nsConfig));
            _nsSession = nsSession ?? throw new ArgumentNullException(nameof(nsSession));
            _nsTransient = nsTransient ?? throw new ArgumentNullException(nameof(nsTransient));
        }

        /// <summary>按 StorageLayer 取本方案的命名空间。</summary>
        public string Namespace(StorageLayer layer) => layer switch
        {
            StorageLayer.Config => _nsConfig,
            StorageLayer.Session => _nsSession,
            StorageLayer.Transient => _nsTransient,
            _ => throw new ArgumentOutOfRangeException(nameof(layer), layer, $"不支持的存储层级: {layer}")
        };

        /// <summary>按 StorageLayer 构造分层键。</summary>
        public LayeredStateKey Key(StorageLayer layer, string scope, string key)
            => new(this, layer, scope, key);

        public LayeredStateKey Config(string scope, string key) => Key(StorageLayer.Config, scope, key);

        public LayeredStateKey Session(string scope, string key) => Key(StorageLayer.Session, scope, key);

        public LayeredStateKey Transient(string scope, string key) => Key(StorageLayer.Transient, scope, key);

        /// <summary>
        /// 三段命名空间后缀式方案（Config/Session/Transient 各自成独立命名空间，避免前缀剥离开销）。
        /// 命名空间名形如 <c>{domain}.Config</c> / <c>{domain}.Session</c> / <c>{domain}.Transient</c>。
        /// </summary>
        public static StateLayerScheme ForDomain(string domain)
        {
            if (string.IsNullOrWhiteSpace(domain))
                throw new ArgumentException("domain 不能为空", nameof(domain));
            return new StateLayerScheme($"{domain}.Config", $"{domain}.Session", $"{domain}.Transient");
        }
    }
}
