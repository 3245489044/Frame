namespace Share.Messaging;

/// <summary>有界查询（<see cref="Scope"/> 必填；<see cref="Limit"/> ≤0 归一为缺省上限）。</summary>
public sealed record MessageQuery(
    string Scope,
    MessageKind? Kind = null,
    string? Direction = null,
    MessageState? State = null,
    int Limit = 32)
{
    /// <summary>缺省上限（禁无界扫描）。</summary>
    public const int DefaultLimit = 32;

    /// <summary>生效上限。</summary>
    public int EffectiveLimit => Limit <= 0 ? DefaultLimit : Limit;
}
