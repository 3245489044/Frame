namespace Common.GraphMindStudio
{
    using System;
    using System.Collections;

    public static class BasicFrame
    {
        public static object ForProcess<T>(T functionName, Action[] actions)
        {
            for (int i = 0; i < actions.Length; i++)
            {
                try
                {
                    actions[i]?.Invoke();
                }
                catch (Exception ex)
                {
                    throw new Exception($"An_error_occurred_in_Function;{functionName};{i};{ex.Message}", ex);
                }
            }
            return null;
        }

        public static void Forobj(object objects, Action<object> input, object functionName = null)
        {
            if (objects is IEnumerable ie)
                foreach (var obj in ie)
                {
                    input(obj);
                }
        }
    }
}