Shader "Hidden/Universal Render Pipeline/ScrollDepth" {
    SubShader {
        Pass {
            Name "Scroll Depth"

            Cull Off
            ColorMask R
            ZWrite On
            ZTest Always
 
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            
            TEXTURE2D (_DepthMap);
            SAMPLER (sampler_linear_clamp);

            float4 _Bound;
            float4 _UVDeform;
            float4 _DepthDeform;
 
            float Frag (Varyings i) : SV_DEPTH {
                float2 uv = i.texcoord;
                uv = uv * _UVDeform.xy + _UVDeform.zw;

                uv = uv * (_Bound.zw - _Bound.xy) + _Bound.xy;
                if (uv.x < _Bound.x || uv.x > _Bound.z || uv.y < _Bound.y || uv.y > _Bound.w)
                    discard;
                float depth = SAMPLE_DEPTH_TEXTURE(_DepthMap, sampler_linear_clamp, uv.xy);
                if (depth >= 0.99)
                    discard;
                depth = depth * _DepthDeform.x + _DepthDeform.y;
                return depth;
            }
 
            ENDHLSL
        }
    }
}
