using Plate.DslMethod.Model;

namespace Usharp.Syntax;

/// <summary>一条命令 op：<c>op &lt;name&gt; | intent=… | handler=… | projector=… | resolver=… | ports=a,b</c>。</summary>
public sealed record UsharpCommandOpDecl(
    string Action, string Op, string? Intent, string? Handler, string? Projector,
    string? Resolver, IReadOnlyList<string> Ports, string? Model = null);

/// <summary>一条键配方：<c>key op=&lt;op&gt; | name=… | family=… | surface=… | required=true | chain=a,b</c>。</summary>
public sealed record UsharpCommandKeyDecl(
    string Action, string Op, string Name, string Family, string Surface,
    bool Required, IReadOnlyList<string> Chain);

/// <summary>
/// <c>[Command] &lt;action&gt;: channel=… | default=… | fallback=… | ports=a,b</c> 语法视图。
/// 续行：<c>op &lt;name&gt; | …</c> / <c>key op=&lt;op&gt; | …</c>（扁平，一行一事实）。
/// </summary>
public sealed record UsharpCommandDecl(
    string Action, string? Channel, string? DefaultOp, string? FallbackOp,
    IReadOnlyList<string> Ports, IReadOnlyList<UsharpCommandOpDecl> Ops,
    IReadOnlyList<UsharpCommandKeyDecl> Keys,
    IReadOnlyList<string> UnparsedLines, string SourceFile, int LineNumber)
{
    /// <summary>投影单条 <c>[Command]</c>（无名 ⇒ null）。</summary>
    public static UsharpCommandDecl? From(DslDeclaration d)
    {
        if (string.IsNullOrWhiteSpace(d.Name)) return null;

        var ops = new List<UsharpCommandOpDecl>();
        var keys = new List<UsharpCommandKeyDecl>();
        var unparsed = new List<string>();
        var action = d.Name.Trim();

        foreach (var raw in d.Continuation)
        {
            var line = (raw ?? string.Empty).Trim();
            if (line.Length == 0) continue;

            var parts = UsharpLineReader.SplitParts(line);
            var head = parts[0];
            var fields = UsharpLineReader.ParseFieldBag(parts, 1, StringComparer.OrdinalIgnoreCase);

            if (UsharpLineReader.TryHead(head, "op", out var opName))
            {
                ops.Add(new UsharpCommandOpDecl(
                    action, opName,
                    UsharpLineReader.Get(fields, "intent"),
                    UsharpLineReader.Get(fields, "handler"),
                    UsharpLineReader.Get(fields, "projector"),
                    UsharpLineReader.Get(fields, "resolver"),
                    UsharpLineReader.SplitList(UsharpLineReader.Get(fields, "ports")),
                    UsharpLineReader.NullIfEmpty(UsharpLineReader.Get(fields, "model"))));
                continue;
            }

            if (UsharpLineReader.TryHead(head, "key", out var keyHead))
            {
                var keyHeadFields = UsharpLineReader.ParseFieldBag(new[] { keyHead }, 0, StringComparer.OrdinalIgnoreCase);
                var op = UsharpLineReader.Get(keyHeadFields, "op")
                         ?? UsharpLineReader.Get(fields, "op") ?? string.Empty;
                var name = UsharpLineReader.Get(fields, "name") ?? string.Empty;
                var family = UsharpLineReader.Get(fields, "family") ?? string.Empty;
                var surface = UsharpLineReader.Get(fields, "surface") ?? "Storage";
                if (name.Length == 0 || family.Length == 0) { unparsed.Add(line); continue; }
                keys.Add(new UsharpCommandKeyDecl(
                    action, op, name, family, surface,
                    !string.Equals(UsharpLineReader.Get(fields, "required"), "false", StringComparison.OrdinalIgnoreCase),
                    UsharpLineReader.SplitList(UsharpLineReader.Get(fields, "chain"))));
                continue;
            }

            unparsed.Add(line);
        }

        return new UsharpCommandDecl(
            action,
            UsharpLineReader.NullIfEmpty(d.Field("channel")),
            UsharpLineReader.NullIfEmpty(d.Field("default")),
            UsharpLineReader.NullIfEmpty(d.Field("fallback")),
            UsharpLineReader.SplitList(d.Field("ports")),
            ops, keys, unparsed, d.SourceFile, d.LineNumber);
    }
}
