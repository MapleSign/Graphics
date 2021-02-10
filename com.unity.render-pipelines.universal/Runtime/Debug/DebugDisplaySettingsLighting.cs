using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace UnityEditor.Rendering
{
    public class DebugDisplaySettingsLighting : IDebugDisplaySettingsData
    {
        public DebugLightingMode DebugLightingMode;
        internal DebugLightingFeatureFlags DebugLightingFeatureFlagsMask;

        private class SettingsPanel : DebugDisplaySettingsPanel
        {
            public override string PanelName => "Lighting";

            public SettingsPanel(DebugDisplaySettingsLighting data)
            {
                AddWidget(new DebugUI.EnumField { displayName = "Lighting Mode", autoEnum = typeof(DebugLightingMode),
                    getter = () => (int)data.DebugLightingMode,
                    setter = (value) => {},
                    getIndex = () => (int)data.DebugLightingMode,
                    setIndex = (value) => data.DebugLightingMode = (DebugLightingMode)value});

                AddWidget(new DebugUI.BitField { displayName = "Lighting Features",
                    getter = () => data.DebugLightingFeatureFlagsMask,
                    setter = (value) => data.DebugLightingFeatureFlagsMask = (DebugLightingFeatureFlags)value,
                    enumType = typeof(DebugLightingFeatureFlags),
                });
            }
        }

        #region IDebugDisplaySettingsData
        public bool AreAnySettingsActive => (DebugLightingMode != DebugLightingMode.None) ||
                                             (DebugLightingFeatureFlagsMask != DebugLightingFeatureFlags.None);

        public bool IsPostProcessingAllowed => true;

        public IDebugDisplaySettingsPanelDisposable CreatePanel()
        {
            return new SettingsPanel(this);
        }
        #endregion
    }
}
