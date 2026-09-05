
using Newtonsoft.Json;

namespace WorkFlowMethod.Infrastructure
{
    public static class ServiceDefaults
    {
        public static readonly JsonSerializerSettings SerializerSettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.None,
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore
        };
    }
}
