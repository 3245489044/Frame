using CodeAnalyzer.Core.Models;

namespace CodeAnalyzer.Prompt.Constants;

public static class PromptRecommenderConstants
{
    public static class Thresholds
    {
        public const double ConfidenceLow = 0.3;
        public const double ScoreLow = 0.1;
        public const double DominantScoreMedium = 0.4;
        public const double ConfidenceMultiplier = 0.6;
        public const double MaxConfidence = 0.9;
        public const double MinSupportingScore = 0.3;
    }

    public static class Defaults
    {
        public const double ZeroConfidence = 0.0;
        public const double ZeroScore = 0.0;
        public const int ZeroCount = 0;
        public const string ErrorAnalysisPrefix = "分析异常: ";
        public const string ErrorCodeEmpty = "输入代码为空";
        public const string ErrorResultEmpty = "分析结果为空";
        public const string LowConfidenceMessage = "低置信度推荐";
    }

    public static class Messages
    {
        public const string SystemErrorPrompt = "系统分析过程中出现错误，请检查代码格式或重试。";
        public const string GeneralOptimizationPrompt = "请分析以下代码并提供优化建议，包括提取通用方法、简化复杂逻辑、优化变量使用等。";
        public const string GeneralOptimizationReason = "代码质量较好，仅需常规优化";

        public const string ArchitecturePrompt = "分析代码架构，识别违反单一职责原则的类，将其拆分为多个具有明确职责的类。特别关注那些方法数量超过15个或代码行数超过500行的类。";
        public const string ArchitectureReasonTemplate = "检测到{0}个大型类，建议进行架构重构";
        public const string ArchitectureReasonFallback = "检测到架构设计优化空间，建议重构提升模块内聚性";

        public const string MethodRefactoringPrompt = "识别代码中的重复逻辑和复杂方法，提取通用功能到辅助方法或工具类。特别关注那些圈复杂度超过15、参数数量超过5个或代码行数超过50行的方法。";
        public const string MethodRefactoringReasonTemplate = "检测到{0}个高复杂度方法，建议重构";
        public const string MethodRefactoringReasonFallback = "检测到方法设计优化机会，建议提取通用逻辑";

        public const string VariableOptimizationPrompt = "检查变量的使用模式，识别未充分利用的变量，优化变量生命周期和重用率。特别关注那些声明后只使用一次的变量和临时变量。";
        public const string VariableOptimizationReasonTemplate = "变量平均重用率较低({0:P0})，建议优化变量生命周期";
        public const string VariableOptimizationReasonFallback = "检测到变量使用模式优化空间，建议提升变量重用率";

        public const string BugFixPrompt = "检查代码中的潜在错误和异常处理，增强代码健壮性。特别关注空引用异常、类型转换异常和资源释放问题。";
        public const string BugFixReasonTemplate = "检测到{0}个潜在错误修复建议";
        public const string BugFixReasonFallback = "检测到代码稳定性优化机会，建议增强错误处理";

        public const string ComplexityReductionPrompt = "简化复杂逻辑，提取重复代码，优化条件判断和循环结构，提升代码可读性和可维护性。";
        public const string ComplexityReductionReasonTemplate = "代码平均复杂度较高({0:F1})，建议简化逻辑";
        public const string ComplexityReductionReasonFallback = "检测到代码复杂度优化空间，建议重构提升可读性";

        public const string ErrorUnknownPrompt = "代码分析未返回具体错误信息，请检查分析器配置或输入代码。";
        public const string ErrorUnknownReason = "未知错误（错误信息为空）";
        public const string ErrorEmptyCodePrompt = "您输入的代码为空，请提供需要分析的源代码。";
        public const string ErrorAnalysisEngineExceptionTemplate = "分析引擎异常: {0}";
        public const string ErrorSuggestionGenerationFailedTemplate = "建议生成失败: {0}";
    }

    public static class AnalysisKeys
    {
        public const string LargeClassCount = "LargeClassCount";
        public const string HighComplexityMethods = "HighComplexityMethods";
        public const string AvgVariableReuse = "AvgVariableReuse";
        public const string BugFixSuggestions = "BugFixSuggestions";
        public const string AvgComplexity = "AvgComplexity";
        public const string RecommendationType = "RecommendationType";
        public const string Confidence = "Confidence";
        public const string Message = "Message";
        public const string DominantCategory = "DominantCategory";
        public const string DominantScore = "DominantScore";
        public const string SupportingCategories = "SupportingCategories";
        public const string AnalysisMetrics = "AnalysisMetrics";
        public const string Error = "Error";
        public const string TotalElements = "TotalElements";
        public const string TotalSuggestions = "TotalSuggestions";
        public const string HighConfidenceSuggestions = "HighConfidenceSuggestions";
        public const string Assessment = "Assessment";
        public const string AssessmentDescription = "AssessmentDescription";
        public const string AverageComplexity = "AverageComplexity";
        public const string AverageVariableReuseRatio = "AverageVariableReuseRatio";
        public const string AverageCohesion = "AverageCohesion";
        public const string AverageCoupling = "AverageCoupling";
    }

    public static readonly IReadOnlyDictionary<RecommendationType, (string PromptTemplate, string ReasonTemplate, string FallbackReason, string MetricKey)> RecommendationMapping
        = new Dictionary<RecommendationType, (string, string, string, string)>
        {
            [RecommendationType.ArchitectureRefactoring] = (
                Messages.ArchitecturePrompt,
                Messages.ArchitectureReasonTemplate,
                Messages.ArchitectureReasonFallback,
                AnalysisKeys.LargeClassCount
            ),
            [RecommendationType.MethodRefactoring] = (
                Messages.MethodRefactoringPrompt,
                Messages.MethodRefactoringReasonTemplate,
                Messages.MethodRefactoringReasonFallback,
                AnalysisKeys.HighComplexityMethods
            ),
            [RecommendationType.VariableOptimization] = (
                Messages.VariableOptimizationPrompt,
                Messages.VariableOptimizationReasonTemplate,
                Messages.VariableOptimizationReasonFallback,
                AnalysisKeys.AvgVariableReuse
            ),
            [RecommendationType.BugFix] = (
                Messages.BugFixPrompt,
                Messages.BugFixReasonTemplate,
                Messages.BugFixReasonFallback,
                AnalysisKeys.BugFixSuggestions
            ),
            [RecommendationType.ComplexityReduction] = (
                Messages.ComplexityReductionPrompt,
                Messages.ComplexityReductionReasonTemplate,
                Messages.ComplexityReductionReasonFallback,
                AnalysisKeys.AvgComplexity
            )
        };

    public static class ReportMessages
    {
        public const string Header = "请根据以下代码分析报告，对代码进行全面优化：";
        public const string SeparatorLine = "---";
        public const string AnalysisTimeFormat = "分析耗时：{0:F2}秒。";
        public const string AssessmentLabel = "当前代码优化评估等级为：{0}。请优先处理低等级问题。";
        public const string AssessmentDescriptionLabel = "评估详细说明：{0}。需重点关注描述中的缺陷。";
        public const string CodeOverviewHeader = "代码概览信息：";
        public const string TotalElementsLabel = "代码元素总数：{0}。";
        public const string ElementDistributionLabel = "元素类型分布：{0}。";
        public const string KeyMetricsHeader = "关键质量指标（供优化参考）：";
        public const string AverageComplexityLabel = "平均圈复杂度：{0:F1}。目标值应低于10。";
        public const string AverageVariableReuseRatioLabel = "变量平均重用率：{0:P0}。建议提升至50%以上。";
        public const string AverageParametersPerMethodLabel = "方法平均参数数：{0:F1}。超过3个时考虑封装。";
        public const string ClustersLabel = "代码聚类数量：{0}。";
        public const string AverageCohesionLabel = "聚类平均内聚度：{0:F2}。";
        public const string AverageCouplingLabel = "聚类平均耦合度：{0:F2}。";
        public const string OptimizationSuggestionsHeader = "具体优化建议（请逐一实施）：";
        public const string NoOptimizationSuggestions = "未检测到需重构的代码，保持现状即可。";
        public const string ConfidenceHighTag = "置信度高（强烈推荐）";
        public const string ConfidenceMediumTag = "置信度中（建议实施）";
        public const string ConfidenceLowTag = "置信度低（可选择性优化）";
        public const string MethodSuffixFormat = "（方法：{0}）";
        public const string VariableSuffixFormat = "（变量：{0}）";
        public const string AiOptimizationHintHeader = "AI优化执行指令：";
        public const string AiOptimizationHintSeparator = "---";
        public const string PromptInstructionLabel = "请执行以下指令：{0}";
        public const string ConfidenceLabel = "该建议置信度：{0:P1}";
        public const string ReasonLabel = "优化理由：{0}";
    }

    public static class TestMessages
    {
        public const string DemoHeader = "=== 高级代码聚类分析演示（基于AI提示） ===";
        public const string CodeLabel = "待分析源代码：";
        public const string AnalysisStep1 = "步骤1：执行代码分析（CodeAnalysisEngine.Analyze）";
        public const string AnalysisTimeLabel = "分析耗时：{0:F3}秒";
        public const string AnalysisErrorLabel = "分析错误：{0}";
        public const string NoError = "无";
        public const string Step2 = "步骤2：识别代码元素（前{0}个）";
        public const string Step3 = "步骤3：元素距离矩阵摘要（前{0}条）";
        public const string Step4 = "步骤4：聚类结果（ClusterAnalyzer.ClusterElements）";
        public const string Step5 = "步骤5：重构建议生成（RefactoringSuggestionGenerator）";
        public const string Step6 = "步骤6：优化评估计算（OptimizationAssessmentCalculator）";
        public const string Step7 = "步骤7：关键质量指标（AnalysisResult.Metrics）";
        public const string Step8 = "步骤8：AI优化提示推荐（PromptRecommender）";
        public const string Step9 = "步骤9：生成完整优化报告（EnhancedOptimizationAnalyzer）";
        public const string Step10 = "步骤10：快速质量指标提取（PromptQualityAnalyzer）";
        public const string DemoEnd = "=== 演示结束 ===";
        public const string ErrorOccurred = "【异常】演示中断：";
        public const string ClusterFormat = "聚类 {0}: {1}，内聚度 {2:F2}，耦合度 {3:F2}，包含 {4} 个元素";
        public const string ElementSampleFormat = "  示例元素：{0}{1}";
        public const string Ellipsis = "...";
        public const string AndMore = "……（完整报告过长，已截断）";
        public const string NoSuggestions = "当前无重构建议";
    }

    public static class DisplayConstants
    {
        public const int MaxElementsPreview = 5;
        public const int MaxDistancesPreview = 5;
        public const int MaxElementNamesPreview = 5;
        public const int MaxVariablesPreview = 2;
        public const int ReportPreviewLength = 1300;
    }
}
