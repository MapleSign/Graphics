﻿using Unity.GraphToolsFoundation.Editor;
using Unity.CommandStateObserver;
using UnityEngine.UIElements;

namespace UnityEditor.ShaderGraph.GraphUI
{
    class LabelPropertyField : BaseModelPropertyField
    {
        public LabelPropertyField(string labelText, ICommandTarget commandTarget)
            : base(commandTarget)
        {
            var label = new Label(labelText);
            label.name = "sg-inspector-label";

            Add(label);

            this.AddStylesheet("InspectorLabel.uss");
        }

        public override void UpdateDisplayedValue()
        {
            // We don't ever need to update this label
        }
    }
}
