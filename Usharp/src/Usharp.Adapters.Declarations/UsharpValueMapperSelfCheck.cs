using Plate.DslMethod.Usharp;
using Usharp.Semantics;

namespace Usharp.Adapters.Declarations;

/// <summary>
/// 值映射自检（E-4 反例自动化）：覆盖 <c>enum-invalid</c> / <c>guid-invalid</c> / <c>bool-invalid</c> /
/// <c>depth-limit(8)</c> / <c>setter-failed(...)</c> —— 由 <c>Usharp.Bundle selfcheck</c> 调用，
/// 零测试工程、零 NuGet（对齐施工单 R-12）。
/// </summary>
public static class UsharpValueMapperSelfCheck
{
    /// <summary>执行自检；返回问题清单（空 = 通过）。</summary>
    public static IReadOnlyList<string> Run()
    {
        var issues = new List<string>();

        ExpectFailure<EnumDto>(issues, "enum-invalid",
            Rec(("State", Str("NotAMember"))));

        ExpectFailure<GuidDto>(issues, "guid-invalid",
            Rec(("Id", Str("not-a-guid"))));

        ExpectFailure<BoolDto>(issues, "bool-invalid",
            Rec(("Flag", Str("maybe"))));

        ExpectFailure<ThrowingSetterDto>(issues, "setter-failed",
            Rec(("Name", Str("x"))));

        var deep = Str("leaf");
        for (var i = 0; i < 12; i++) deep = Rec(("Next", deep));
        ExpectFailure<DepthNode>(issues, "depth-limit(8)", deep);

        return issues;
    }

    private static void ExpectFailure<T>(List<string> issues, string expectedReason, UsharpValue value)
        where T : class
    {
        var result = UsharpValueMapper.TryToObject<T>(value);
        if (result.Ok)
        {
            issues.Add($"value-mapper-selfcheck: {typeof(T).Name} 期望失败（{expectedReason}），实得成功");
            return;
        }

        if (result.Reason is null || !result.Reason.Contains(expectedReason, StringComparison.Ordinal))
            issues.Add($"value-mapper-selfcheck: {typeof(T).Name} 期望原因含 '{expectedReason}'，实得 '{result.Reason}'");
    }

    private static UsharpValue Str(string text) => UsharpScalar.Str(text);

    private static UsharpRecord Rec(params (string Key, UsharpValue Value)[] fields)
        => new(fields.Select(f => new KeyValuePair<string, UsharpValue>(f.Key, f.Value)).ToList());

    // ===== 夹具类型（仅自检可见）=====

    private enum Color { Red, Blue }

    private sealed record EnumDto(Color State);

    private sealed record GuidDto(Guid Id);

    private sealed record BoolDto(bool Flag);

    private sealed class ThrowingSetterDto
    {
        public string Name
        {
            get => string.Empty;
            set => throw new InvalidOperationException("setter-boom");
        }
    }

    private sealed class DepthNode
    {
        public DepthNode() => Next = new DepthNode(1);

        private DepthNode(int level)
        {
            Level = level;
            Next = level < 12 ? new DepthNode(level + 1) : null;
        }

        public int Level { get; }

        public DepthNode? Next { get; }
    }
}