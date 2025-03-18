using System;

namespace GustUI.TraitValues
{
    public class TVEvent<T> : TraitValue where T : TVEventArgs
    {
        public Action<T> TriggerAction { get; set; }
        public TVEvent() { }
        public TVEvent(Action<T> triggerAction) { TriggerAction = triggerAction; }
    }

    //public class TVEvent : TraitValue
    //{
    //    public Action<TVEvent> TriggerAction { get; set; }
    //    public TVEvent() { }
    //    public TVEvent(Action<TVEvent> triggerAction) { TriggerAction = triggerAction; }
    //}
}
