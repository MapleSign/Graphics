﻿
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace UnityEditor.Rendering
{
    public class DebugDisplaySettingsRendering : IDebugDisplaySettingsData
    {
        internal DebugFullScreenMode debugFullScreenMode { get; private set; } = DebugFullScreenMode.None;
        internal DebugSceneOverrideMode debugSceneOverrideMode { get; private set; } = DebugSceneOverrideMode.None;
        internal DebugMipInfoMode mipInfoModeDebugMode { get; private set; } = DebugMipInfoMode.None;

        public DebugPostProcessingMode debugPostProcessingMode { get; private set; } = DebugPostProcessingMode.Auto;
        public bool enableMsaa { get; private set; } = true;
        public bool enableHDR { get; private set; } = true;

        private class SettingsPanel : DebugDisplaySettingsPanel
        {
            public override string PanelName => "Rendering";

            public SettingsPanel(DebugDisplaySettingsRendering data)
            {
                AddWidget(new DebugUI.EnumField { displayName = "Full Screen Modes", autoEnum = typeof(DebugFullScreenMode), getter = () => (int)data.debugFullScreenMode, setter = (value) => {}, getIndex = () => (int)data.debugFullScreenMode, setIndex = (value) => data.debugFullScreenMode = (DebugFullScreenMode)value});
                AddWidget(new DebugUI.EnumField { displayName = "Scene Debug Modes", autoEnum = typeof(DebugSceneOverrideMode), getter = () => (int)data.debugSceneOverrideMode, setter = (value) => {}, getIndex = () => (int)data.debugSceneOverrideMode, setIndex = (value) => data.debugSceneOverrideMode = (DebugSceneOverrideMode)value});
                AddWidget(new DebugUI.EnumField { displayName = "Mip Modes Debug", autoEnum = typeof(DebugMipInfoMode), getter = () => (int)data.mipInfoModeDebugMode, setter = (value) => { }, getIndex = () => (int)data.mipInfoModeDebugMode, setIndex = (value) => data.mipInfoModeDebugMode = (DebugMipInfoMode)value });

                AddWidget(new DebugUI.EnumField { displayName = "Post-processing", autoEnum = typeof(DebugPostProcessingMode), getter = () => (int)data.debugPostProcessingMode, setter = (value) => data.debugPostProcessingMode = (DebugPostProcessingMode)value, getIndex = () => (int)data.debugPostProcessingMode, setIndex = (value) => data.debugPostProcessingMode = (DebugPostProcessingMode)value});
                AddWidget(new DebugUI.BoolField { displayName = "MSAA", getter = () => data.enableMsaa, setter = (value) => data.enableMsaa = value });
                AddWidget(new DebugUI.BoolField { displayName = "HDR", getter = () => data.enableHDR, setter = (value) => data.enableHDR = value });
            }
        }

        #region IDebugDisplaySettingsData
        public bool AreAnySettingsActive => enableMsaa || enableHDR ||
                                            (debugPostProcessingMode != DebugPostProcessingMode.Disabled) ||
                                            (debugFullScreenMode != DebugFullScreenMode.None) ||
                                            (debugSceneOverrideMode != DebugSceneOverrideMode.None);

        public bool IsPostProcessingAllowed => (debugPostProcessingMode != DebugPostProcessingMode.Disabled) &&
                                               (debugSceneOverrideMode == DebugSceneOverrideMode.None);

        public IDebugDisplaySettingsPanelDisposable CreatePanel()
        {
            return new SettingsPanel(this);
        }
        #endregion
    }
}
