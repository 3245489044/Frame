using Plate.DslMethod.Model;

namespace Plate.DslMethod.Logic;

/// <summary>
/// 符号索引。把各文档的定义点（声明）编译为符号键索引，并提供 [CR] 引用解析与反向引用映射。
/// 索引键 = `lib::STYP::name`（对齐 dsl.md §4.7 锚点 ID 唯一主键语义）。
/// </summary>
public sealed class DslIndex
{
    private readonly Dictionary<string, DslDeclaration> _defined = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<string>> _byLibName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<DslReference>> _reverse = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>反向引用：定义符号键 → 引用它的全部 [CR]。</summary>
    public IReadOnlyDictionary<string, List<DslReference>> Reverse => _reverse;

    public static string Key(string lib, string stub, string name) => $"{lib}::{stub}::{name}";

    /// <summary>索引一个文档（定义点 + 其引用）。</summary>
    public void AddDocument(DslDocument doc)
    {
        foreach (var decl in doc.Declarations)
        {
            if (string.IsNullOrEmpty(decl.Name)) continue;
            var key = Key(doc.Lib, decl.Stub, decl.Name);
            _defined.TryAdd(key, decl);

            var lnKey = $"{doc.Lib}::{decl.Name}";
            _byLibName.GetOrAdd(lnKey).Add(key);

            foreach (var r in decl.References)
            {
                if (TryResolve(r, out var targetKey))
                    _reverse.GetOrAdd(targetKey).Add(r);
            }
        }
    }

    /// <summary>解析 [CR] 引用 → 定义符号键。优先精确匹配（lib::type::name）；type 缺失时按 lib::name 唯一解析。</summary>
    public bool TryResolve(DslReference r, out string targetKey)
    {
        if (!string.IsNullOrEmpty(r.Type))
        {
            targetKey = Key(r.Lib, r.Type, r.Name);
            if (_defined.ContainsKey(targetKey)) return true;
        }

        var lnKey = $"{r.Lib}::{r.Name}";
        if (_byLibName.TryGetValue(lnKey, out var candidates))
        {
            if (candidates.Count == 1)
            {
                targetKey = candidates[0];
                return true;
            }
            if (candidates.Count > 1 && !string.IsNullOrEmpty(r.Type))
            {
                targetKey = Key(r.Lib, r.Type, r.Name);
                return _defined.ContainsKey(targetKey);
            }
        }

        targetKey = "";
        return false;
    }

    /// <summary>引用是否已解析到定义符号。</summary>
    public bool IsDefined(DslReference r) => TryResolve(r, out _);

    /// <summary>按键取定义声明。</summary>
    public DslDeclaration? Get(string key) => _defined.TryGetValue(key, out var d) ? d : null;

    /// <summary>定义符号总数。</summary>
    public int Count => _defined.Count;
}
