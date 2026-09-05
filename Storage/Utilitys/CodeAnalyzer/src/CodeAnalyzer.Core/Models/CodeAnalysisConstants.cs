namespace CodeAnalyzer.Core.Models;

public static class CodeAnalysisConstants
{
    public const double CONFIDENCE_LOW = 0.3;
    public const double CONFIDENCE_MEDIUM = 0.5;
    public const double CONFIDENCE_HIGH = 0.7;
    public const double CONFIDENCE_VERY_HIGH = 0.9;
    public const double CONFIDENCE_MAX = 1.0;

    public const double MEDIUM_COMPLEXITY_THRESHOLD = 5.0;
    public const double HIGH_COMPLEXITY_THRESHOLD = 10.0;
    public const double VERY_HIGH_COMPLEXITY_THRESHOLD = 15.0;
    public const double HIGH_COMPLEXITY_BASE_SCORE = 10.0;
    public const double HIGH_COMPLEXITY_SCORE_DIVISOR = 20.0;
    public const double HIGH_COMPLEXITY_SCORE_MAX = 0.3;

    public const double LONG_METHOD_LINES_THRESHOLD = 30.0;
    public const double VERY_LONG_METHOD_LINES_THRESHOLD = 50.0;

    public const int MANY_PARAMETERS_THRESHOLD = 3;
    public const int TOO_MANY_PARAMETERS_THRESHOLD = 5;

    public const double LOW_VARIABLE_REUSE_THRESHOLD = 0.3;
    public const double GOOD_VARIABLE_REUSE_THRESHOLD = 0.5;
    public const double MAX_EXPECTED_VARIABLE_REUSE = 3.0;
    public const int MANY_LOCAL_VARIABLES_THRESHOLD = 5;

    public const int LARGE_CLASS_ELEMENTS_THRESHOLD = 10;
    public const int VERY_LARGE_CLASS_ELEMENTS_THRESHOLD = 20;
    public const int MIN_CLUSTER_ELEMENTS = 3;
    public const double GOOD_COHESION_THRESHOLD = 0.6;
    public const double LOW_COHESION_THRESHOLD = 0.3;
    public const double HIGH_COUPLING_THRESHOLD = 0.7;

    public const double METHOD_INLINE_COMPLEXITY_THRESHOLD = 5.0;
    public const double METHOD_INLINE_BODY_LINES_THRESHOLD = 15.0;
    public const int METHOD_INLINE_PARAMETER_COUNT_THRESHOLD = 3;
    public const int METHOD_INLINE_REFERENCE_COUNT_THRESHOLD = 3;

    public const double ARCHITECTURE_SCORE_WEIGHT = 0.5;
    public const double VARIABLE_REUSE_WEIGHT = 0.5;
    public const double COMPLEXITY_SCORE_WEIGHT = 0.5;
    public const double METHOD_REFACTORING_WEIGHT = 0.1;

    public const double ASSESSMENT_SCALE_LOW = 0.8;
    public const double ASSESSMENT_SCALE_MEDIUM = 1.0;
    public const double ASSESSMENT_SCALE_HIGH = 1.2;
    public const double ASSESSMENT_SCALE_VERY_HIGH = 1.5;
    public const double ASSESSMENT_SCALE_EXTREME = 2.0;

    public const double ASSESSMENT_NOT_REQUIRED_THRESHOLD = 0.3;
    public const double ASSESSMENT_LOW_THRESHOLD = 0.5;
    public const double ASSESSMENT_MEDIUM_THRESHOLD = 0.7;
    public const double ASSESSMENT_HIGH_THRESHOLD = 0.9;

    public static readonly string[] MethodPrefixesToAvoidInlining = { "init", "create", "build", "configure", "setup" };

    public static readonly string[] TemporaryVariablePrefixes = { "temp", "tmp", "_t", "tempVar", "temporary" };

    public static readonly HashSet<string> LoopVariableNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "i", "j", "k", "index", "counter", "idx", "pos", "temp", "tmp", "result"
    };
}
