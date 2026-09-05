
using Share.TreeTraceFramework.Core;
using Share.TreeTraceFramework.Models;

namespace WorkFlowMethod.CallFrame.Models
{
    public class CallFrameQuery : TraceQuery<TraceNode>
    {

        public CallFrameQuery WhereSymbol(string symbol)
        {
            WhereName(symbol);
            return this;
        }

        public CallFrameQuery WhereSymbolContains(string pattern)
        {
            WhereNameContains(pattern);
            return this;
        }


        public new static CallFrameQuery FindNodeOutputs()
        {
            var q = new CallFrameQuery();
            q.WhereHasChildren().SelectChildren().WhereHasOutputs().SelectOutputs();
            return q;
        }

        public static CallFrameQuery FindNodeOutputsMatchSymbol(string symbolPattern)
        {
            var q = new CallFrameQuery();
            q.WhereSymbolContains(symbolPattern);
            q.WhereHasChildren().SelectChildren().WhereHasOutputs().SelectOutputs();
            return q;
        }

        public new static CallFrameQuery FindNodeByInputsSet(IEnumerable<object?> inputs)
        {
            var q = new CallFrameQuery();
            q.WhereInputsMatch(inputs);
            return q;
        }
    }
}
