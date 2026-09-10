#nullable enable
using Plate.JsonMethod.Validation;

namespace UniversalMiddleware.Chain;

/// <summary>
/// 链定义加载的统一结果（对齐 ScriptLoadSkeleton/NodeChainSkeleton 的 LoadResult / 失败标记收敛）。
/// </summary>
public class ChainLoadResult<TDefinition>
{
    public bool Success { get; init; }
    public TDefinition? Definition { get; init; }
    public string? Error { get; init; }

    /// <summary>成功且产出非空定义。</summary>
    public bool HasDefinition => Success && Definition != null;

    public static ChainLoadResult<TDefinition> Ok(TDefinition definition)
        => new() { Success = true, Definition = definition };

    public static ChainLoadResult<TDefinition> Fail(string error)
        => new() { Success = false, Error = error };
}

/// <summary>
/// 链定义加载通用骨架（P0）。
/// 流程：加载源（文件或字符串）→ 可选 JsonMethod 规范化（normalize）→ 可选校验（validate）→ 产出 TDefinition → 统一 ChainLoadResult。
/// JSON 工程（规范化/校验/反序列化）一律内聚 Plate.JsonMethod，本骨架仅注入依赖，不复制 JSON 逻辑（无 ChainInputNormalizer）。
/// 落点说明：置于 UniversalMiddleware（FileDriven.Execution 与 WorkFlowMethod 均引用此层），
/// 避免 FileDriven 向上依赖 WorkFlowMethod.Chain（符合层纪律，消除跨框架上行依赖）。
/// </summary>
public sealed class ChainDefinitionLoadSkeleton<TDefinition>
{
    private readonly Func<string, string>? _normalize;
    private readonly Func<string, bool>? _validate;
    private readonly Func<string, TDefinition?> _parse;

    public ChainDefinitionLoadSkeleton(
        Func<string, TDefinition?> parse,
        Func<string, string>? normalize = null,
        Func<string, bool>? validate = null)
    {
        _parse = parse ?? throw new ArgumentNullException(nameof(parse));
        _normalize = normalize;
        _validate = validate;
    }

    /// <summary>
    /// 从 JSON 字符串加载定义；任何环节失败返回失败结果（不抛异常，parse 自身的异常原样上抛）。
    /// 空/空白字符串交由 parse 决定（骨架不预设空语义，保证既有骨架采纳时行为中立）。
    /// </summary>
    public ChainLoadResult<TDefinition> Load(string json)
    {
        var content = json;

        if (_normalize != null)
        {
            content = _normalize(content);
            if (content == null)
                return ChainLoadResult<TDefinition>.Fail("JSON 规范化失败");
        }

        if (_validate != null && !_validate(content))
            return ChainLoadResult<TDefinition>.Fail("JSON 校验失败");

        var definition = _parse(content);
        if (definition == null)
            return ChainLoadResult<TDefinition>.Fail("JSON 反序列化产出为空");

        return ChainLoadResult<TDefinition>.Ok(definition);
    }

    /// <summary>从文件路径加载定义（读取 → Load）。</summary>
    public ChainLoadResult<TDefinition> LoadFromFile(string path)
    {
        if (!File.Exists(path))
            return ChainLoadResult<TDefinition>.Fail($"JSON 文件不存在: {path}");
        return Load(File.ReadAllText(path));
    }

    /// <summary>
    /// 便捷工厂：默认以 JsonValidationService.ValidateSyntax 作为通用语法校验步骤（校验切分后的单源）。
    /// </summary>
    public static ChainDefinitionLoadSkeleton<TDefinition> ForJson(
        Func<string, TDefinition?> parse,
        Func<string, string>? normalize = null,
        bool validateSyntax = true)
        => new(
            parse,
            normalize,
            validateSyntax ? (string s) => new JsonValidationService().ValidateSyntax(s).IsValid : null);
}
