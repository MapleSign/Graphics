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

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"

            RWTexture3D<uint> _VoxelAlbedo : register(u1);
            RWTexture3D<uint> _VoxelNormal : register(u2);
            RWTexture3D<uint> _VoxelEmission : register(u3);
            RWTexture3D<uint> _VoxelOpacity : register(u4);

            float4x4 _ViewProjections[3];
            float4x4 _ViewProjectionsInv[3];

            float4 _VolumeMinPoint;
            float4 _VolumeScale;
            float _VolumeResolution;

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _DetailAlbedoMap_ST;
                half4 _BaseColor;
                half4 _EmissionColor;
                half _Cutoff;
                half _BumpScale;
            CBUFFER_END

            struct Attributes
            {
                float4 postionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct VSOutput
            {
                float4 positionWS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                half4 tangentWS : TEXCOORD2;

                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct GSOutput
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                half4 tangentWS : TEXCOORD2;

                float4 positionVS : TEXCOORD3;// Volume space position
                float4 aabb : TEXCOORD4;

                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            int CalcDominantAxis(VSOutput input[3])
            {
                float3 v0 = input[1].positionWS.xyz - input[0].positionWS.xyz;
                float3 v1 = input[2].positionWS.xyz - input[0].positionWS.xyz;
                float3 normal = cross(v0, v1);

                float nDX = abs(normal.x);
                float nDY = abs(normal.y);
                float nDZ = abs(normal.z);

                if (nDX > nDY && nDX > nDZ)
                {
                    return 0;
                }
                else if (nDY > nDX && nDY > nDZ)
                {
                    return 1;
                }
                else
                {
                    return 2;
                }
            }

            float4 CalcAABB(float4 pos[3], float2 pixelDiag)
            {
                float4 aabb;
                aabb.xy = min(pos[2].xy, min(pos[1].xy, pos[0].xy));
                aabb.zw = max(pos[2].xy, max(pos[1].xy, pos[0].xy));
                
                aabb.xy -= pixelDiag;
                aabb.zw += pixelDiag;

                return aabb;
            }

            float4 convRGBA8ToFloat4(uint val)
            {
                return float4(float((val & 0x000000FF)), 
                float((val & 0x0000FF00) >> 8), 
                float((val & 0x00FF0000) >> 16), 
                float((val & 0xFF000000) >> 24));
            }

            uint convFloat4ToRGBA8(float4 val)
            {
                return (uint(val.w) & 0x000000FF) << 24 | 
                (uint(val.z) & 0x000000FF) << 16 | 
                (uint(val.y) & 0x000000FF) << 8 | 
                (uint(val.x) & 0x000000FF);
            }

            void imageAtomicRGBA8Avg(RWTexture3D<uint> grid, int3 coord, float4 value)
            {
                value.xyz *= 255.0;
                uint newVal = convFloat4ToRGBA8(value);
                uint prevVal = 0;
                // InterlockedExchange(grid[coord], newVal, prevVal);
                uint curVal;
                InterlockedCompareExchange(grid[coord], prevVal, newVal, curVal);
                uint numIter = 0;

                while (curVal != prevVal && numIter < 255)
                {
                    prevVal = curVal;
                    float4 rval = convRGBA8ToFloat4(curVal);
                    rval.xyz *= rval.w;
                    float4 cumVal = rval + value;
                    cumVal.xyz /= cumVal.w;
                    newVal = convFloat4ToRGBA8(cumVal);

                    InterlockedCompareExchange(grid[coord], prevVal, newVal, curVal);
                    numIter++;
                }
            }

            VSOutput vert(Attributes input)
            {
                VSOutput output = (VSOutput)0;

                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                output.positionWS = float4(TransformObjectToWorld(input.postionOS.xyz), 1.0);
                output.uv = input.uv;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.tangentWS = half4(TransformObjectToWorldDir(input.tangentOS.xyz), 
                    input.tangentOS.w * GetOddNegativeScale());

                return output;
            }

            [maxvertexcount(3)]
            void geom(triangle VSOutput input[3], inout TriangleStream<GSOutput> outStream)
            {
                int dominantAxis = CalcDominantAxis(input);
                float4x4 viewProj = _ViewProjections[dominantAxis];
                float4x4 viewProjInv = _ViewProjectionsInv[dominantAxis];

                float4 pos[3] = {
                    mul(viewProj, input[0].positionWS),
                    mul(viewProj, input[1].positionWS),
                    mul(viewProj, input[2].positionWS)
                };
                float2 halfPixel = float2(1.0 / _VolumeResolution, 1.0 / _VolumeResolution);

                float4 aabb = CalcAABB(pos, halfPixel * 10.0);

                GSOutput output = (GSOutput)0;
                output.aabb = aabb;

                for (int i = 0; i < 3; ++i)
                {
                    UNITY_TRANSFER_INSTANCE_ID(input[i], output);
                    output.positionCS = pos[i];
                    output.uv = input[i].uv;
                    output.normalWS = input[i].normalWS;
                    output.tangentWS = input[i].tangentWS;

                    output.positionVS.xyz = input[i].positionWS.xyz - _VolumeMinPoint.xyz;
                    output.positionVS.xyz *= _VolumeScale.xyz;
                    output.positionVS.xyz *= _VolumeResolution;
                    output.positionVS.w = 1.0;

                    outStream.Append(output);
                }
            }

            uint frag(GSOutput input) : SV_Target
            {
                // if (input.positionCS.x < input.aabb.x || input.positionCS.y < input.aabb.y || 
                //     input.positionCS.x > input.aabb.z || input.positionCS.y > input.aabb.w)
                // {
                //     discard;
                // }

                half4 albedoAlpha = SampleAlbedoAlpha(input.uv, TEXTURE2D_ARGS(_BaseMap, sampler_BaseMap));
                float alpha = Alpha(albedoAlpha.a, _BaseColor, _Cutoff);
                if (alpha == 0.0)
                {
                    discard;
                }
                float4 albedo = float4(albedoAlpha.rgb * _BaseColor.rgb, 1.0);

                int3 position = int3(input.positionVS.xyz);
                
                half3 normalTS = SampleNormal(input.uv, TEXTURE2D_ARGS(_BumpMap, sampler_BumpMap), _BumpScale);
                float3 normalWS;
                #if defined(_NORMALMAP) || defined(_DETAIL)
                    float sgn = input.tangentWS.w;      // should be either +1 or -1
                    float3 bitangent = sgn * cross(input.normalWS.xyz, input.tangentWS.xyz);
                    normalWS = TransformTangentToWorld(normalTS, half3x3(input.tangentWS.xyz, bitangent.xyz, input.normalWS.xyz));
                #else
                    normalWS = input.normalWS;
                #endif

                float4 emission = float4(SampleEmission(input.uv, _EmissionColor.rgb, TEXTURE2D_ARGS(_EmissionMap, sampler_EmissionMap)).rgb, 1.0);

                imageAtomicRGBA8Avg(_VoxelAlbedo, position, albedo);
                imageAtomicRGBA8Avg(_VoxelNormal, position, float4(normalWS * 0.5 + 0.5, 1.0));
                imageAtomicRGBA8Avg(_VoxelEmission, position, emission);

                return 255;
            }

            ENDHLSL
        }
    }
}
