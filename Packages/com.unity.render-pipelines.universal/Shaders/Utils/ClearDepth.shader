Shader "Hidden/Universal Render Pipeline/ClearDepth"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline"}
        LOD 100

        Pass
        {
            Name "ClearDepth"
            ZTest Always ZWrite On ColorMask R
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            // Core.hlsl for XR dependencies
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            
            #if UNITY_REVERSED_Z
                #define DEPTH_DEFAULT_VALUE 0.0
                #define DEPTH_OP max
            #else
                #define DEPTH_DEFAULT_VALUE 1.0
                #define DEPTH_OP min
            #endif

            float Frag(Varyings input) : SV_Depth
            {
                return DEPTH_DEFAULT_VALUE;
            }
            ENDHLSL
        }
    }
}
