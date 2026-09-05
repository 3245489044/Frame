namespace RoslynSpace.RoslynSyntaxUtilities.Base
{
    public static class CodeAnalysisConstants
    {
        public const double LOW_COMPLEXITY_THRESHOLD = 2.0;
        public const double MEDIUM_COMPLEXITY_THRESHOLD = 8.0;
        public const double HIGH_COMPLEXITY_THRESHOLD = 15.0;
        public const double VERY_HIGH_COMPLEXITY_THRESHOLD = 20.0;

        public const int LONG_METHOD_LINES_THRESHOLD = 30;
        public const int VERY_LONG_METHOD_LINES_THRESHOLD = 50;
        public const int MANY_PARAMETERS_THRESHOLD = 4;
        public const int TOO_MANY_PARAMETERS_THRESHOLD = 5;
        public const int MANY_LOCAL_VARIABLES_THRESHOLD = 6;

        public const int METHOD_INLINE_BODY_LINES_THRESHOLD = 3;
        public const int METHOD_INLINE_PARAMETER_COUNT_THRESHOLD = 1;
        public const int METHOD_INLINE_REFERENCE_COUNT_THRESHOLD = 1;
        public const int METHOD_INLINE_COMPLEXITY_THRESHOLD = 2;

        public const int LARGE_CLASS_ELEMENTS_THRESHOLD = 10;
        public const int VERY_LARGE_CLASS_ELEMENTS_THRESHOLD = 50;
        public const int MIN_CLUSTER_ELEMENTS = 3;

        public const double LOW_COHESION_THRESHOLD = 0.5;
        public const double HIGH_COUPLING_THRESHOLD = 0.5;
        public const double GOOD_COHESION_THRESHOLD = 0.6;

        public const double CONFIDENCE_LOW = 0.3;
        public const double CONFIDENCE_MEDIUM = 0.6;
        public const double CONFIDENCE_HIGH = 0.8;
        public const double CONFIDENCE_VERY_HIGH = 0.95;
        public const double CONFIDENCE_MAX = 1.0;

        public const double LOW_VARIABLE_REUSE_THRESHOLD = 0.4;
        public const double GOOD_VARIABLE_REUSE_THRESHOLD = 0.6;
        public const double MAX_EXPECTED_VARIABLE_REUSE = 3.0;

        public const double ASSESSMENT_SCALE_LOW = 0.6;
        public const double ASSESSMENT_SCALE_MEDIUM = 0.75;
        public const double ASSESSMENT_SCALE_HIGH = 0.9;
        public const double ASSESSMENT_SCALE_VERY_HIGH = 1.0;
        public const double ASSESSMENT_SCALE_EXTREME = 1.1;

        public const double ASSESSMENT_NOT_REQUIRED_THRESHOLD = 0.3;
        public const double ASSESSMENT_LOW_THRESHOLD = 0.7;
        public const double ASSESSMENT_MEDIUM_THRESHOLD = 1.2;
        public const double ASSESSMENT_HIGH_THRESHOLD = 1.8;

        public const double ARCHITECTURE_SCORE_WEIGHT = 0.4;
        public const double COMPLEXITY_SCORE_WEIGHT = 0.4;
        public const double VARIABLE_REUSE_WEIGHT = 0.5;
        public const double METHOD_REFACTORING_WEIGHT = 0.1;
        public const double HIGH_COMPLEXITY_BASE_SCORE = 10.0;
        public const double HIGH_COMPLEXITY_SCORE_DIVISOR = 25.0;
        public const double HIGH_COMPLEXITY_SCORE_MAX = 0.4;

        public static readonly string[] MethodPrefixesToAvoidInlining = new[]
        {
            "init", "initialize", "setup", "configure", "create",
            "get", "set", "add", "remove", "update", "delete"
        };

        public static readonly string[] LoopVariableNames = new[]
        {
            "i", "j", "k", "index", "counter"
        };

        public static readonly string[] TemporaryVariablePrefixes = new[]
        {
            "temp", "tmp", "result"
        };
    }

    public static class RefactoringTypeNames
    {
        public const string METHOD_OPTIMIZATION = "方法优化";
        public const string CLASS_EXTRACTION = "类提取";
        public const string VARIABLE_OPTIMIZATION = "变量优化";
        public const string PARAMETER_OBJECT_EXTRACTION = "参数对象提取";
    }

    public static class RecommendationCategoryNames
    {
        public const string METHOD_EXTRACTION_OPTIMIZATION = "方法提取优化";
        public const string METHOD_INLINE_OPTIMIZATION = "方法内联优化";
        public const string HIGH_COMPLEXITY_METHOD_OPTIMIZATION = "高复杂度方法优化";
        public const string VARIABLE_REUSE_OPTIMIZATION = "变量重用率优化";
        public const string TOO_MANY_LOCAL_VARIABLES_OPTIMIZATION = "局部变量过多优化";
        public const string CLASS_STRUCTURE_OPTIMIZATION = "类结构优化";
        public const string PARAMETER_OPTIMIZATION = "参数优化";
    }
}
