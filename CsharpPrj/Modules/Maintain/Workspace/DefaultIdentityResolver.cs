#nullable enable

using Modules.GraphProcessing.Normalization;
using Modules.Maintain.Ports;

namespace Modules.Maintain.Workspace
{
    /// <summary>
    /// 通用身份解析器：复用 <see cref="IdentifierExtractor"/> 做归一化，
    /// 复用 <see cref="Modules.GraphProcessing.Utilities.DefaultNameGenerator"/> 生成迁移新键。
    /// 键的读/写访问器由调用方以委托注入（数据无关）。
    /// </summary>
    public sealed class DefaultIdentityResolver<TData> : IIdentityResolver<TData>
    {
        private readonly Func<TData, string?> _keyReader;
        private readonly Action<TData, string> _keyWriter;
        private readonly IdentifierExtractor _extractor;
        private readonly Modules.GraphProcessing.Utilities.INameGenerator _nameGenerator;

        public DefaultIdentityResolver(
            Func<TData, string?> keyReader,
            Action<TData, string> keyWriter,
            IdentifierExtractor? extractor = null)
        {
            _keyReader = keyReader ?? throw new ArgumentNullException(nameof(keyReader));
            _keyWriter = keyWriter ?? throw new ArgumentNullException(nameof(keyWriter));
            _extractor = extractor ?? new IdentifierExtractor();
            _nameGenerator = new Modules.GraphProcessing.Utilities.DefaultNameGenerator();
        }

        public string? GetKey(TData item) => _keyReader(item);

        public void SetKey(TData item, string key) => _keyWriter(item, key);

        public string Normalize(string key)
            => _extractor.Extract(key ?? string.Empty);

        public bool IsAlias(string a, string b)
            => _extractor.AreMatching(a, b);

        public string GenerateNewKey(string baseKey)
            => _nameGenerator.GenerateName(baseKey);
    }
}