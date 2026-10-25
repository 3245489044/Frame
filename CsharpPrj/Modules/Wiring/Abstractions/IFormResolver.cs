using Modules.Wiring.Model;

namespace Modules.Wiring.Abstractions;

/// <summary>形态解析（Form 名 → CLR 类型）：Plan 期固化一次，运行期零反射（R5）。</summary>
public interface IFormResolver
{
    bool TryResolve(string formId, out Type? clrType);
}

/// <summary>以声明内 <see cref="FormSpec.ClrType"/> 为唯一来源的解析器（不做类型推断）。</summary>
public sealed class DeclaredFormResolver : IFormResolver
{
    private readonly IReadOnlyDictionary<string, FormSpec> _forms;

    public DeclaredFormResolver(IEnumerable<FormSpec> forms)
    {
        var map = new Dictionary<string, FormSpec>(StringComparer.Ordinal);
        foreach (var form in forms ?? Array.Empty<FormSpec>())
        {
            if (!string.IsNullOrWhiteSpace(form.Id)) map[form.Id] = form;
        }

        _forms = map;
    }

    public bool TryResolve(string formId, out Type? clrType)
    {
        clrType = null;
        if (string.IsNullOrWhiteSpace(formId) || !_forms.TryGetValue(formId, out var form)) return false;
        clrType = form.ClrType;
        return form.ClrType is not null;
    }
}
