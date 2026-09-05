
namespace Share.TreeTraceFramework.Models
{
    public enum TraceQueryStepType
    {
        WhereHasChildren,
        WhereHasInputs,
        WhereHasOutputs,
        WhereNoOutputs,
        WhereName,
        WhereLabel,
        WhereInputsMatch,
        WhereOutputsMatch,
        SelectChildren,
        SelectGrandChildren,
        SelectInputs,
        SelectOutputs,
        First,
        Take,
        Skip
    }
}
