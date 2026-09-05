namespace WorkflowExecution.Abstractions
{
    public enum FieldMappingMode
    {
        ByValueAsNodeIdentifier,

        ByValueAsNodeProperty,

        ByReference
    }

    public class FieldMappingEntry
    {
        public string SourcePropertyPath { get; set; }

        public string TargetPropertyPath { get; set; }

        public FieldMappingMode Mode { get; set; } = FieldMappingMode.ByValueAsNodeProperty;
    }
}
