namespace Modules.Wiring.Model;

/// <summary>剖面：同一接线表按页面/视图/代际启用子集（声明面单源 + 子集启用）。</summary>
public sealed record ProfileSpec(string Id, IReadOnlyList<string> Enable, string Scope = "global")
{
    public bool Enables(string wireId)
    {
        foreach (var id in Enable)
        {
            if (string.Equals(id, wireId, StringComparison.Ordinal)) return true;
        }

        return false;
    }
}
