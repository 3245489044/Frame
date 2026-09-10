#nullable enable

namespace UniversalWorkflowFramework.Reference.Models
{
    public enum ReferenceType
    {
        Dependency,
        Includes,
        Uses,
        Extends,
        Requires,
        Other
    }

    public enum FilterType
    {
        Node,
        Edge,
        Property
    }

    public enum ReferenceIssueType
    {
        MissingReference,
        InvalidReference,
        CircularDependency,
        PermissionViolation,
        ConsistencyViolation
    }

    public enum Severity
    {
        Low,
        Medium,
        High,
        Critical
    }

    public enum ReferenceChangeType
    {
        Added,
        Updated,
        Removed,
        Modified,
        Replaced
    }
}