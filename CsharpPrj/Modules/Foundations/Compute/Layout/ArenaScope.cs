namespace Modules.Compute.Layout;

/// <summary>
/// <see cref="Arena"/> 的作用域租约：Dispose 时回退到进入时的 offset。
/// <para>只回退指针，不保证租借对象被释放 ⇒ 调用方不得在作用域外持有 Span。</para>
/// </summary>
public readonly struct ArenaScope : IDisposable
{
    private readonly Arena? _arena;
    private readonly long _mark;

    internal ArenaScope(Arena arena, long mark)
    {
        _arena = arena;
        _mark = mark;
    }

    public void Dispose() => _arena?.Rollback(_mark);
}
