
using System.Diagnostics;
using UnityEditor.Rendering;
using UnityEngine.Rendering.Universal.Internal;

namespace UnityEngine.Rendering.Universal
{
    public class DebugHandler
    {
        private readonly Material m_FullScreenDebugMaterial;
        private readonly Texture2D m_NumberFontTexture;
        private readonly Material m_ReplacementMaterial;

        // Material settings...
        private readonly int m_DebugMaterialModeId;
        private readonly int m_DebugVertexAttributeModeId;

        // Rendering settings...
        private readonly int m_DebugFullScreenModeId;
        private readonly int m_DebugSceneOverrideModeId;
        private readonly int m_DebugMipInfoModeId;

        // Lighting settings...
        private readonly int m_DebugLightingModeId;
        private readonly int m_DebugLightingFeatureFlagsId;

        // Validation settings...
        private readonly int m_DebugValidationModeId;
        private readonly int m_DebugValidateAlbedoMinLuminanceId;
        private readonly int m_DebugValidateAlbedoMaxLuminanceId;
        private readonly int m_DebugValidateAlbedoSaturationToleranceId;
        private readonly int m_DebugValidateAlbedoHueToleranceId;
        private readonly int m_DebugValidateAlbedoCompareColorId;

        private readonly DebugDisplaySettings m_DebugDisplaySettings;

        private DebugDisplaySettingsLighting LightingSettings => m_DebugDisplaySettings.Lighting;
        private DebugMaterialSettings MaterialSettings => m_DebugDisplaySettings.materialSettings;
        private DebugDisplaySettingsRendering RenderingSettings => m_DebugDisplaySettings.renderingSettings;
        private DebugDisplaySettingsValidation ValidationSettings => m_DebugDisplaySettings.Validation;

        public bool IsSceneOverrideActive => RenderingSettings.debugSceneOverrideMode != DebugSceneOverrideMode.None;
        public bool IsVertexAttributeOverrideActive => MaterialSettings.DebugVertexAttributeIndexData != DebugVertexAttributeMode.None;
        public bool IsLightingDebugActive => LightingSettings.DebugLightingMode != DebugLightingMode.None;
        public bool IsLightingFeatureActive => (int)LightingSettings.DebugLightingFeatureFlagsMask != 0;
        public bool IsMaterialOverrideActive => MaterialSettings.DebugMaterialModeData != DebugMaterialMode.None;
        public bool AreShadowCascadesActive => LightingSettings.DebugLightingMode == DebugLightingMode.ShadowCascades;
        public bool IsMipInfoDebugActive => RenderingSettings.mipInfoModeDebugMode != DebugMipInfoMode.None;

        public bool IsReplacementMaterialNeeded => IsSceneOverrideActive || IsVertexAttributeOverrideActive;

        public bool IsDebugMaterialActive
        {
            get
            {
                bool isMaterialDebugActive = IsLightingDebugActive || IsMaterialOverrideActive || IsLightingFeatureActive ||
                                             IsVertexAttributeOverrideActive || IsMipInfoDebugActive ||
                                             ValidationSettings.validationMode == DebugValidationMode.ValidateAlbedo;

                return isMaterialDebugActive;
            }
        }

        public DebugHandler(ScriptableRendererData scriptableRendererData)
        {
            Texture2D numberFontTexture = scriptableRendererData.NumberFont;
            Shader fullScreenDebugShader = scriptableRendererData.fullScreenDebugPS;
            Shader debugReplacementShader = scriptableRendererData.debugReplacementPS;

            m_DebugDisplaySettings = DebugDisplaySettings.Instance;

            m_NumberFontTexture = numberFontTexture;
            m_FullScreenDebugMaterial = (fullScreenDebugShader == null) ? null : CoreUtils.CreateEngineMaterial(fullScreenDebugShader);
            m_ReplacementMaterial = (debugReplacementShader == null) ? null : CoreUtils.CreateEngineMaterial(debugReplacementShader);

            // Material settings...
            m_DebugMaterialModeId = Shader.PropertyToID("_DebugMaterialMode");
            m_DebugVertexAttributeModeId = Shader.PropertyToID("_DebugVertexAttributeMode");

            // Rendering settings...
            m_DebugMipInfoModeId = Shader.PropertyToID("_DebugMipInfoMode");
            m_DebugSceneOverrideModeId = Shader.PropertyToID("_DebugSceneOverrideMode");
            m_DebugFullScreenModeId = Shader.PropertyToID("_DebugFullScreenMode");

            // Lighting settings...
            m_DebugLightingModeId = Shader.PropertyToID("_DebugLightingMode");
            m_DebugLightingFeatureFlagsId = Shader.PropertyToID("_DebugLightingFeatureFlags");

            // ValidationSettings...
            m_DebugValidationModeId = Shader.PropertyToID("_DebugValidationMode");
            m_DebugValidateAlbedoMinLuminanceId = Shader.PropertyToID("_DebugValidateAlbedoMinLuminance");
            m_DebugValidateAlbedoMaxLuminanceId = Shader.PropertyToID("_DebugValidateAlbedoMaxLuminance");
            m_DebugValidateAlbedoSaturationToleranceId = Shader.PropertyToID("_DebugValidateAlbedoSaturationTolerance");
            m_DebugValidateAlbedoHueToleranceId = Shader.PropertyToID("_DebugValidateAlbedoHueTolerance");
            m_DebugValidateAlbedoCompareColorId = Shader.PropertyToID("_DebugValidateAlbedoCompareColor");
        }

        internal DebugPass CreatePass(RenderPassEvent evt)
        {
            return new DebugPass(evt, m_FullScreenDebugMaterial);
        }

        public bool TryGetReplacementMaterial(out Material replacementMaterial)
        {
            if(IsReplacementMaterialNeeded)
            {
                replacementMaterial = m_ReplacementMaterial;
                return true;
            }
            else
            {
                replacementMaterial = default;
                return false;
            }
        }

        public bool TryGetSceneOverride(out DebugSceneOverrideMode debugSceneOverrideMode)
        {
            debugSceneOverrideMode = RenderingSettings.debugSceneOverrideMode;
            return IsSceneOverrideActive;
        }

        public bool TryGetFullscreenDebugMode(out DebugFullScreenMode debugFullScreenMode)
        {
            debugFullScreenMode = RenderingSettings.debugFullScreenMode;
            return debugFullScreenMode != DebugFullScreenMode.None;
        }

        [Conditional("DEVELOPMENT_BUILD"), Conditional("UNITY_EDITOR")]
        public void Setup(ScriptableRenderContext context)
        {
            var cmd = CommandBufferPool.Get("");

            // Material settings...
            cmd.SetGlobalFloat(m_DebugMaterialModeId, (int)MaterialSettings.DebugMaterialModeData);
            cmd.SetGlobalFloat(m_DebugVertexAttributeModeId, (int)MaterialSettings.DebugVertexAttributeIndexData);

            // Rendering settings...
            cmd.SetGlobalInt(m_DebugMipInfoModeId, (int)RenderingSettings.mipInfoModeDebugMode);
            cmd.SetGlobalInt(m_DebugSceneOverrideModeId, (int)RenderingSettings.debugSceneOverrideMode);
            cmd.SetGlobalInt(m_DebugFullScreenModeId, (int)RenderingSettings.debugFullScreenMode);

            // Lighting settings...
            cmd.SetGlobalFloat(m_DebugLightingModeId, (int)LightingSettings.DebugLightingMode);
            cmd.SetGlobalInt(m_DebugLightingFeatureFlagsId, (int)LightingSettings.DebugLightingFeatureFlagsMask);

            // Validation settings...
            cmd.SetGlobalInt(m_DebugValidationModeId, (int)ValidationSettings.validationMode);
            cmd.SetGlobalFloat(m_DebugValidateAlbedoMinLuminanceId, ValidationSettings.AlbedoMinLuminance);
            cmd.SetGlobalFloat(m_DebugValidateAlbedoMaxLuminanceId, ValidationSettings.AlbedoMaxLuminance);
            cmd.SetGlobalFloat(m_DebugValidateAlbedoSaturationToleranceId, ValidationSettings.AlbedoSaturationTolerance);
            cmd.SetGlobalFloat(m_DebugValidateAlbedoHueToleranceId, ValidationSettings.AlbedoHueTolerance);
            cmd.SetGlobalColor(m_DebugValidateAlbedoCompareColorId, ValidationSettings.AlbedoCompareColor.linear);

            cmd.SetGlobalTexture("_DebugNumberTexture", m_NumberFontTexture);
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }
}
