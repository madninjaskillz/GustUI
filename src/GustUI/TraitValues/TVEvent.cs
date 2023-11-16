using System;

namespace GustUI.TraitValues
{
    public class TVEvent : TraitValue
    {
        public Action<TVEventArgs> TriggerAction { get; set; }
        public TVEvent() { }
        public TVEvent(Action<TVEventArgs> triggerAction) { TriggerAction = triggerAction; }
    }
}
