
#nullable enable
using Share.TaskStatusPool.Abstractions;

namespace Share.TaskStatusPool.Morphisms
{
    public class ControlReadMorphism : IOrderedMorphism<IPoolContext, IPoolContext>
    {
        private readonly ControlPool.ControlPool _controlPool;

        public string Name => "ControlRead";

        public int Order => 0;

        public ControlReadMorphism(ControlPool.ControlPool controlPool)
        {
            _controlPool = controlPool ?? throw new ArgumentNullException(nameof(controlPool));
        }

        public IPoolContext Morph(IPoolContext input)
        {
            var controlName = input.Get<string>("_changedControl");
            if (string.IsNullOrEmpty(controlName))
                return input;

            var value = _controlPool.Read(controlName);
            input.Set("_controlValue", value);
            input.Set("_controlName", controlName);

            return input;
        }
    }
}
