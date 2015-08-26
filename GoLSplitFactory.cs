using System.Reflection;
using LiveSplit.GoLSplit;
using LiveSplit.UI.Components;
using System;
using LiveSplit.Model;

[assembly: ComponentFactory(typeof(GoLSplitFactory))]

namespace LiveSplit.GoLSplit
{
    public class GoLSplitFactory : IComponentFactory
    {
        public string ComponentName => "Lara Croft: GoL";
        public string Description => "Game Time / Auto-splitting for Lara Croft and the Guardian of Light.";
        public ComponentCategory Category => ComponentCategory.Control;

        public IComponent Create(LiveSplitState state)
        {
            return new GoLSplitComponent(state);
        }

        public string UpdateName => this.ComponentName;
        public string UpdateURL => "http://fatalis.pw/livesplit/update/";
        public Version Version => Assembly.GetExecutingAssembly().GetName().Version;
        public string XMLURL => this.UpdateURL + "Components/update.LiveSplit.GoLSplit.xml";
    }
}
