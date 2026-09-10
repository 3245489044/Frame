
#nullable enable
using Share.TaskStatusPool.Abstractions;
using TConfig.Controls;

namespace Share.TaskStatusPool.Morphisms
{
    public class ResultWriteMorphism : IOrderedMorphism<IPoolContext, IPoolContext>
    {
        private readonly ControlPool.ControlPool _controlPool;
        private readonly IControlValueAccessor _valueAccessor;

        public string Name => "ResultWrite";

        public int Order => 3;

        public ResultWriteMorphism(ControlPool.ControlPool controlPool, IControlValueAccessor valueAccessor)
        {
            _controlPool = controlPool ?? throw new ArgumentNullException(nameof(controlPool));
            _valueAccessor = valueAccessor ?? throw new ArgumentNullException(nameof(valueAccessor));
        }

        public IPoolContext Morph(IPoolContext input)
        {
            var resultSuccess = input.Get<bool>("_resultSuccess");
            if (!resultSuccess)
                return input;

            var result = input.Get<object>("_result");
            if (result == null)
                return input;

            var resultStr = result.ToString();

            _controlPool.SetValue("_lastResult", resultStr);

            var controlName = input.Get<string>("_controlName");
            if (!string.IsNullOrEmpty(controlName))
            {
                _controlPool.Write(controlName, resultStr);
                input.Set("_writtenControls", new[] { controlName });
            }

            var syncTargets = input.Get<string[]>("_syncTargets");
            if (syncTargets != null)
            {
                foreach (var target in syncTargets)
                {
                    _controlPool.Write(target, resultStr);
                }
            }

            return input;
        }

        public void SyncToUiControl(object control, object? value)
        {
            if (control != null && value != null)
            {
                _valueAccessor.SetValue(control, value?.ToString());
            }
        }
    }
}
