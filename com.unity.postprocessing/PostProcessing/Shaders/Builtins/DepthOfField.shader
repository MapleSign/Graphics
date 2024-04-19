Shader "Hidden/PostProcessing/DepthOfField"
{
    // SubShader with SM 5.0 support
    // DX11+, OpenGL 4.3+, OpenGL ES 3.1+AEP, Vulkan, consoles
    // Gather intrinsics are used to reduce texture sample count.
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass // 0
        {
            Name "CoC Calculation"

            HLSLPROGRAM
                #pragma target 3.5
                #pragma vertex VertDefault
                #pragma fragment FragCoC
                #include "Packages/com.unity.postprocessing/PostProcessing/Shaders/Builtins/DepthOfField.hlsl"
            ENDHLSL
        }

        Pass // 1
        {
            Name "CoC Temporal Filter"

            HLSLPROGRAM
                #pragma target 5.0
                #pragma vertex VertDefault
                #pragma fragment FragTempFilter
                #include "Packages/com.unity.postprocessing/PostProcessing/Shaders/Builtins/DepthOfField.hlsl"
            ENDHLSL
        }

        Pass // 2
        {
            Name "Downsample initial MaxCoC"

            HLSLPROGRAM
                #pragma target 3.5
                #pragma vertex VertDefault
                #pragma fragment FragDownsampleCoC
                #define INITIAL_COC
                #include "Packages/com.unity.postprocessing/PostProcessing/Shaders/Builtins/DepthOfField.hlsl"
            ENDHLSL
        }

        Pass // 3
        {
            Name "Downsample MaxCoC"

            HLSLPROGRAM
                #pragma target 3.5
                #pragma vertex VertDownsampleCoC
                #pragma fragment FragDownsampleCoC
                #include "Packages/com.unity.postprocessing/PostProcessing/Shaders/Builtins/DepthOfField.hlsl"
            ENDHLSL
        }

        Pass // 4
        {
            Name "Neighbor MaxCoC"

            HLSLPROGRAM
                #pragma target 3.5
                #pragma vertex VertDefault
                #pragma fragment FragNeighborCoC
                #include "Packages/com.unity.postprocessing/PostProcessing/Shaders/Builtins/DepthOfField.hlsl"
            ENDHLSL
        }

        Pass // 5
        {
            Name "Downsample and Prefilter"

            HLSLPROGRAM
                #pragma target 5.0
                #pragma vertex VertDefault
                #pragma fragment FragPrefilter
                #include "Packages/com.unity.postprocessing/PostProcessing/Shaders/Builtins/DepthOfField.hlsl"
            ENDHLSL
        }

        Pass // 6
        {
            Name "Bokeh Filter (small)"

            HLSLPROGRAM
                #pragma target 3.5
                #pragma vertex VertDefault
                #pragma fragment FragBlurSmallBokeh
                #define KERNEL_SMALL
                #include "Packages/com.unity.postprocessing/PostProcessing/Shaders/Builtins/DepthOfField.hlsl"
            ENDHLSL
        }

        Pass // 7
        {
            Name "Bokeh Filter (dynamic)"

            HLSLPROGRAM
                #pragma target 3.5
                #pragma vertex VertDefault
                #pragma fragment FragBlurDynamic
                #include "Packages/com.unity.postprocessing/PostProcessing/Shaders/Builtins/DepthOfField.hlsl"
            ENDHLSL
        }

        Pass // 8
        {
            Name "Postfilter"

            HLSLPROGRAM
                #pragma target 3.5
                #pragma vertex VertDefault
                #pragma fragment FragPostBlur
                #include "Packages/com.unity.postprocessing/PostProcessing/Shaders/Builtins/DepthOfField.hlsl"
            ENDHLSL
        }

        Pass // 9
        {
            Name "Combine"

            HLSLPROGRAM
                #pragma target 3.5
                #pragma vertex VertDefault
                #pragma fragment FragCombine
                #include "Packages/com.unity.postprocessing/PostProcessing/Shaders/Builtins/DepthOfField.hlsl"
            ENDHLSL
        }

        Pass // 10
        {
            Name "Debug Overlay"

            HLSLPROGRAM
                #pragma target 3.5
                #pragma vertex VertDefault
                #pragma fragment FragDebugOverlay
                #include "Packages/com.unity.postprocessing/PostProcessing/Shaders/Builtins/DepthOfField.hlsl"
            ENDHLSL
        }
    }

    // Fallback SubShader with SM 3.5
    // DX11+, OpenGL 3.2+, OpenGL ES 3+, Metal, Vulkan, consoles
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        Pass // 0
        {
            Name "CoC Calculation"

            HLSLPROGRAM
                #pragma target 3.5
                #pragma vertex VertDefault
                #pragma fragment FragCoC
                #include "Packages/com.unity.postprocessing/PostProcessing/Shaders/Builtins/DepthOfField.hlsl"
            ENDHLSL
        }

        Pass // 1
        {
            Name "CoC Temporal Filter"

            HLSLPROGRAM
                #pragma target 3.5
                #pragma vertex VertDefault
                #pragma fragment FragTempFilter
                #include "Packages/com.unity.postprocessing/PostProcessing/Shaders/Builtins/DepthOfField.hlsl"
            ENDHLSL
        }

        Pass // 2
        {
            Name "Downsample initial MaxCoC"

            HLSLPROGRAM
                #pragma target 3.5
                #pragma vertex VertDefault
                #pragma fragment FragDownsampleCoC
                #define INITIAL_COC
                #include "Packages/com.unity.postprocessing/PostProcessing/Shaders/Builtins/DepthOfField.hlsl"
            ENDHLSL
        }

        Pass // 3
        {
            Name "Downsample MaxCoC"

            HLSLPROGRAM
                #pragma target 3.5
                #pragma vertex VertDownsampleCoC
                #pragma fragment FragDownsampleCoC
                #include "Packages/com.unity.postprocessing/PostProcessing/Shaders/Builtins/DepthOfField.hlsl"
            ENDHLSL
        }

        Pass // 4
        {
            Name "Neighbor MaxCoC"

            HLSLPROGRAM
                #pragma target 3.5
                #pragma vertex VertDefault
                #pragma fragment FragNeighborCoC
                #include "Packages/com.unity.postprocessing/PostProcessing/Shaders/Builtins/DepthOfField.hlsl"
            ENDHLSL
        }

        Pass // 5
        {
            Name "Downsample and Prefilter"

            HLSLPROGRAM
                #pragma target 3.5
                #pragma vertex VertDefault
                #pragma fragment FragPrefilter
                #include "Packages/com.unity.postprocessing/PostProcessing/Shaders/Builtins/DepthOfField.hlsl"
            ENDHLSL
        }

        Pass // 6
        {
            Name "Bokeh Filter (small)"

            HLSLPROGRAM
                #pragma target 3.5
                #pragma vertex VertDefault
                #pragma fragment FragBlurSmallBokeh
                #define KERNEL_SMALL
                #include "Packages/com.unity.postprocessing/PostProcessing/Shaders/Builtins/DepthOfField.hlsl"
            ENDHLSL
        }

        Pass // 7
        {
            Name "Bokeh Filter (dynamic)"

            HLSLPROGRAM
                #pragma target 3.5
                #pragma vertex VertDefault
                #pragma fragment FragBlurDynamic
                #include "Packages/com.unity.postprocessing/PostProcessing/Shaders/Builtins/DepthOfField.hlsl"
            ENDHLSL
        }

        Pass // 8
        {
            Name "Postfilter"

            HLSLPROGRAM
                #pragma target 3.5
                #pragma vertex VertDefault
                #pragma fragment FragPostBlur
                #include "Packages/com.unity.postprocessing/PostProcessing/Shaders/Builtins/DepthOfField.hlsl"
            ENDHLSL
        }

        Pass // 9
        {
            Name "Combine"

            HLSLPROGRAM
                #pragma target 3.5
                #pragma vertex VertDefault
                #pragma fragment FragCombine
                #include "Packages/com.unity.postprocessing/PostProcessing/Shaders/Builtins/DepthOfField.hlsl"
            ENDHLSL
        }

        Pass // 10
        {
            Name "Debug Overlay"

            HLSLPROGRAM
                #pragma target 3.5
                #pragma vertex VertDefault
                #pragma fragment FragDebugOverlay
                #include "Packages/com.unity.postprocessing/PostProcessing/Shaders/Builtins/DepthOfField.hlsl"
            ENDHLSL
        }
    }
}
