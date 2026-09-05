using System.Collections.Generic;

namespace RoslynSpace.RoslynSyntaxUtilities.Base
{
    public enum AccessibilityLevel
    {
        Public,
        Protected,
        Internal,
        Private
    }

    public enum ElementType
    {
        Method,
        Property,
        Field,
        Constructor,
        Event,
        Delegate,
        Enum,
        Interface,
        Struct,
        Record,
        Class,
        Unknown,
        Namespace,
        Parameter,
        LocalVariable
    }

    public enum RefactoringType
    {
        ExtractClass,
        MoveMethod,
        InlineMethod,
        ExtractInterface,
        MergeClasses,
        SplitClass,
        IntroduceParameterObject,
        ExtractMethod
    }

    public enum ScoreCategory
    {
        Architecture,
        Method,
        Variable,
        Stability,
        Complexity,
        General
    }

    public enum RecommendationType
    {
        ArchitectureRefactoring,
        MethodRefactoring,
        VariableOptimization,
        BugFix,
        ComplexityReduction,
        GeneralOptimization
    }

    public enum OptimizationAssessment
    {
        NotRequired,
        LowPriority,
        MediumPriority,
        HighPriority,
        Critical,
        CriticalPriority
    }

    public enum VariableScope
    {
        Local,
        Block,
        Method,
        Class
    }

    public enum VariableLifetime
    {
        Loop,
        Method,
        Class
    }

}
