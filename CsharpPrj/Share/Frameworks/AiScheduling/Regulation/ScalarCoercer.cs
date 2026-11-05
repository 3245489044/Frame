namespace Share.AiScheduling.Regulation;

/// <summary>
/// 标量数值归一器（兼容门面）：归一与饱和逻辑单源在 <see cref="Modules.Regulation.ScalarCoercer"/>。
/// </summary>
public static class ScalarCoercer
{
    public static int ToInt(object? value, int min = 1)
        => Modules.Regulation.ScalarCoercer.ToInt(value, min);
}
