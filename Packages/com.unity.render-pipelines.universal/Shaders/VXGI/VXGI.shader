Shader "Universal Render Pipeline/VXGI"
{
    Properties
    {
        [MainTexture] _BaseMap("Albedo", 2D) = "white" {}
        [MainColor] _BaseColor("Color", Color) = (1,1,1,1)

        _Cutoff("Alpha Cutoff", Range(0.0, 1.0)) = 0.5

        _BumpScale("Scale", Float) = 1.0
        _BumpMap("Normal Map", 2D) = "bump" {}

        [HDR] _EmissionColor("Color", Color) = (0,0,0)
        _EmissionMap("Emission", 2D) = "white" {}
    }
    
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "Voxelize"
            Tags { "LightMode" = "UniversalGBuffer" }

            Cull Off
            ColorMask R
            ZWrite Off
            ZTest Always

            HLSLPROGRAM
            #pragma require geometry
            #pragma require randomwrite
            #pragma vertex vert
            #pragma geometry geom
            #pragma fragment frag

            #pragma enable_d3d11_debug_symbols
            
            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma shader_feature_local_fragment _EMISSION

            #include "Packages/com.unity.render-pipelines.universal/Shaders/VXGI/VoxelizePass.hlsl"

            ENDHLSL
        }
    }
}
