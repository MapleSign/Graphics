
using System;

namespace UnityEngine.Rendering.Universal
{
    #region Material Settings
    [GenerateHLSL]
    public enum DebugMaterialMode
    {
        None,
        Unlit,
        Diffuse,
        Specular,
        Alpha,
        Smoothness,
        AmbientOcclusion,
        Emission,
        NormalWorldSpace,
        NormalTangentSpace,
        LightingComplexity,
        LOD,
        Metallic,
    }

    [GenerateHLSL]
    public enum DebugVertexAttributeMode
    {
        None,
        Texcoord0,
        Texcoord1,
        Texcoord2,
        Texcoord3,
        Color,
        Tangent,
        Normal,
    }
    #endregion

    #region Rendering Settings
    [GenerateHLSL]
    public enum DebugFullScreenMode
    {
        None,
        Depth,

        // TODO: Restore this once we have access to the screen-space shadow texture...
        //MainLightShadowsOnly,
        AdditionalLightsShadowMap,
        MainLightShadowMap,
    }

    [GenerateHLSL]
    public enum DebugSceneOverrideMode
    {
        None,
        Overdraw,
        Wireframe,
        SolidWireframe,
    }

    [GenerateHLSL]
    public enum DebugMipInfoMode
    {
        None,
        Level,
        Count,

        //CountReduction,
        //StreamingMipBudget,
        //StreamingMip,
    }

    [GenerateHLSL]
    public enum DebugPostProcessingMode
    {
        Disabled,
        Auto,
        Enabled
    };
    #endregion

    #region Lighting settings
    [GenerateHLSL]
    public enum DebugLightingMode
    {
        None,
        ShadowCascades,
        LightOnly,
        LightDetail,
        Reflections,
        ReflectionsWithSmoothness,
    }

    [GenerateHLSL, Flags]
    public enum DebugLightingFeatureFlags
    {
        None,
        GlobalIllumination = 0x1,
        MainLight = 0x2,
        AdditionalLights = 0x4,
        VertexLighting = 0x8,
        Emission = 0x10,
        AmbientOcclusion = 0x20,
    }
    #endregion

    #region Validation settings...
    [GenerateHLSL]
    public enum DebugValidationMode
    {
        None,
        HighlightNanInfNegative,
        HighlightOutsideOfRange,
        ValidateAlbedo,
    }
    #endregion
}
