using Usharp.Syntax;

namespace Usharp.Semantics;

/// <summary>
/// 观测声明注册中心 —— <c>[Obs]</c> 闸A 的唯一判据来源：<c>kind</c> → 声明集合（文件 / 作用域 / 实体 / 事件）。
/// <para>
/// 只登记与判声明自洽（<c>US6003</c> 重复 / <c>US6006</c> 文件多条 / <c>US6007</c> kind 非法）；
/// 跨声明引用解析（<c>US6001</c>/<c>US6002</c>/<c>US6004</c>/<c>US6005</c>）归 <see cref="UsharpValidator"/>。
/// 声明序原样保留（manifest 产出与书写面同序，禁排序）。
/// </para>
/// </summary>
public sealed class ObsRegistry
{
    private readonly List<UsharpObsDecl> _declarations = new();
    private readonly Dictionary<string, UsharpObsDecl> _scopes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, UsharpObsDecl> _entities = new(StringComparer.Ordinal);
    private readonly Dictionary<string, UsharpObsDecl> _events = new(StringComparer.Ordinal);
    private readonly List<UsharpObsDecl> _files = new();
    private readonly List<UsharpDiagnostic> _diagnostics = new();

    /// <summary>全部声明（声明序）。</summary>
    public IReadOnlyList<UsharpObsDecl> Declarations => _declarations;

    /// <summary>作用域声明（声明序）。</summary>
    public IReadOnlyList<UsharpObsDecl> Scopes => _declarations.Where(d => d.Kind == UsharpObsKind.Scope).ToList();

    /// <summary>实体声明（声明序）。</summary>
    public IReadOnlyList<UsharpObsDecl> Entities => _declarations.Where(d => d.Kind == UsharpObsKind.Entity).ToList();

    /// <summary>事件声明（声明序）。</summary>
    public IReadOnlyList<UsharpObsDecl> Events => _declarations.Where(d => d.Kind == UsharpObsKind.Event).ToList();

    /// <summary>语义文件版本（<c>[Obs] …: kind=file | version=…</c>；未声明 → null）。</summary>
    public string? FileVersion => _files.Count > 0 ? _files[0].Version : null;

    public IReadOnlyList<UsharpDiagnostic> Diagnostics => _diagnostics;

    /// <summary>登记一条 <c>[Obs]</c> 声明（重复 / 文件多条 / kind 非法 → 报错并保留首个）。</summary>
    public void Add(UsharpObsDecl decl)
    {
        ArgumentNullException.ThrowIfNull(decl);

        if (decl.Kind == UsharpObsKind.Unknown)
        {
            _diagnostics.Add(UsharpDiagnostic.Error(UsharpCodes.ObsKindUnknown,
                $"[Obs] {decl.Name}: kind 缺省或非法（合法值：{UsharpLanguage.KindFile}/{UsharpLanguage.KindScope}"
                + $"/{UsharpLanguage.KindEntity}/{UsharpLanguage.KindEvent}）",
                decl.SourceFile, decl.LineNumber));
            return;
        }

        _declarations.Add(decl);

        switch (decl.Kind)
        {
            case UsharpObsKind.File:
                if (_files.Count > 0)
                    _diagnostics.Add(UsharpDiagnostic.Error(UsharpCodes.ObsFileDuplicate,
                        $"[Obs] {decl.Name}: kind=file 至多一条（version= 单一来源；首条 = {_files[0].Name}）",
                        decl.SourceFile, decl.LineNumber));
                else
                    _files.Add(decl);
                break;
            case UsharpObsKind.Scope:
                AddUnique(_scopes, decl, "作用域");
                break;
            case UsharpObsKind.Entity:
                AddUnique(_entities, decl, "实体");
                break;
            case UsharpObsKind.Event:
                AddUnique(_events, decl, "事件");
                break;
        }
    }

    /// <summary>作用域是否已声明（闸A：<c>scope=</c>/<c>parent=</c> 判据）。</summary>
    public bool HasScope(string name) => _scopes.ContainsKey(name ?? "");

    /// <summary>实体是否已声明（闸A：<c>entity=</c> 判据）。</summary>
    public bool HasEntity(string name) => _entities.ContainsKey(name ?? "");

    /// <summary>事件分类是否已声明（闸A：<c>[Flow] step emit=</c> 判据）。</summary>
    public bool HasEvent(string name) => _events.ContainsKey(name ?? "");

    private void AddUnique(Dictionary<string, UsharpObsDecl> map, UsharpObsDecl decl, string kindName)
    {
        if (map.TryAdd(decl.Name, decl)) return;

        _diagnostics.Add(UsharpDiagnostic.Error(UsharpCodes.ObsDeclDuplicate,
            $"[Obs] {decl.Name}: {kindName}声明重复（首条已登记于 {map[decl.Name].SourceFile}:{map[decl.Name].LineNumber}）",
            decl.SourceFile, decl.LineNumber));
    }
}
