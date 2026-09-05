using System.Collections.Generic;

namespace UniversalWorkflowFramework.ActionMapping.Models
{
    public abstract class ActionBase
    {
        public string ActionId { get; set; }

        public string ActionType { get; set; }

        public Dictionary<string, object> Parameters { get; set; }

        public Dictionary<string, object> Metadata { get; set; }

        protected ActionBase()
        {
            ActionId = System.Guid.NewGuid().ToString();
            ActionType = string.Empty;
            Parameters = new Dictionary<string, object>();
            Metadata = new Dictionary<string, object>();
        }
    }

    public class UIAction : ActionBase
    {
        public UIAction() : base() { }
    }

    public class AIAction : ActionBase
    {
        public AIAction() : base() { }
    }
}
