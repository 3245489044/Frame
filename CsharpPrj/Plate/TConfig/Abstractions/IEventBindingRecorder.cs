
using System.Collections.Generic;

namespace TConfig
{
    public interface IEventBindingRecorder
    {
        void Record(BindingResult result);

        void RecordRange(IEnumerable<BindingResult> results);

        IEnumerable<BindingResult> GetAllRecords();

        IEnumerable<BindingResult> GetRecordsByControl(string controlName);

        void Clear();
    }
}
