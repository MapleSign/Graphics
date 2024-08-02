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

        Pass
        {
            Name "Deffered VXGI"
            Tags { "LightMode" = "UniversalGBuffer" }

            Cull Off
            ZWrite Off
            ZTest Always

            HLSLPROGRAM
            #pragma vertex Vert // from Blit.hlsl
            #pragma fragment frag

            // #pragma enable_d3d11_debug_symbols
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"
            
            TEXTURE2D_X_HALF(_GBuffer0);
            TEXTURE2D_X_HALF(_GBuffer1);
            TEXTURE2D_X_HALF(_GBuffer2);
            TEXTURE2D_X_HALF(_GBuffer3);

            SAMPLER(point_clamp_sampler);

            TEXTURE3D(_VoxelRadiance);
            #define ANISO_DIR_COUNT 6
            TEXTURE3D(_VoxelRadianceAniso_0);
            TEXTURE3D(_VoxelRadianceAniso_1);
            TEXTURE3D(_VoxelRadianceAniso_2);
            TEXTURE3D(_VoxelRadianceAniso_3);
            TEXTURE3D(_VoxelRadianceAniso_4);
            TEXTURE3D(_VoxelRadianceAniso_5);
            // -X +X -Y +Y -Z +Z
            static Texture3D _VoxelRadianceAniso[ANISO_DIR_COUNT] = {
                _VoxelRadianceAniso_0,
                _VoxelRadianceAniso_1,
                _VoxelRadianceAniso_2,
                _VoxelRadianceAniso_3,
                _VoxelRadianceAniso_4,
                _VoxelRadianceAniso_5
            };

            SAMPLER(linear_clamp_sampler);
            
            float4 _VolumeMinPoint;
            float4 _VolumeScale; // 1.0 / _VolumeSize
            float4 _VolumeSize;
            float4 _VolumeResolution; // res, res, 1.0 / res, 1.0 / res
            
            #define MAX_CONE_COUNT 6
            static const float3 diffuseConeDirections[MAX_CONE_COUNT] =
            {
                float3(0.0f, 0.0f, 1.0f),
                float3(0.0f, 0.866025f, 0.5f),
                float3(0.823639f, 0.267617f, 0.5f),
                float3(0.509037f, -0.7006629f, 0.5f),
                float3(-0.50937f, -0.7006629f, 0.5f),
                float3(-0.823639f, 0.267617f, 0.5f)
            };

            static const float diffuseConeWeights[MAX_CONE_COUNT] =
            {
                PI / 4.0f,
                3.0f * PI / 20.0f,
                3.0f * PI / 20.0f,
                3.0f * PI / 20.0f,
                3.0f * PI / 20.0f,
                3.0f * PI / 20.0f,
            };

            float4 AnisotropicSample(float3 coord, float3 weight, uint3 face, float lod)
            {
                // anisotropic volumes level
                float anisoLevel = max(lod - 1.0, 0.0);
                // directional sample
                float4 anisoSample = float4(0, 0, 0, 0);
                if (face.x == 0)
                    anisoSample += weight.x * SAMPLE_TEXTURE3D_LOD(_VoxelRadianceAniso[0], linear_clamp_sampler, coord, anisoLevel);
                else
                    anisoSample += weight.x * SAMPLE_TEXTURE3D_LOD(_VoxelRadianceAniso[1], linear_clamp_sampler, coord, anisoLevel);
                
                if (face.y == 2)
                    anisoSample += weight.y * SAMPLE_TEXTURE3D_LOD(_VoxelRadianceAniso[2], linear_clamp_sampler, coord, anisoLevel);
                else
                    anisoSample += weight.y * SAMPLE_TEXTURE3D_LOD(_VoxelRadianceAniso[3], linear_clamp_sampler, coord, anisoLevel);
                
                if (face.z == 4)
                    anisoSample += weight.z * SAMPLE_TEXTURE3D_LOD(_VoxelRadianceAniso[4], linear_clamp_sampler, coord, anisoLevel);
                else
                    anisoSample += weight.z * SAMPLE_TEXTURE3D_LOD(_VoxelRadianceAniso[5], linear_clamp_sampler, coord, anisoLevel);
                // linearly interpolate on base level
                if (lod < 1.0)
                {
                    float4 baseColor = SAMPLE_TEXTURE3D_LOD(_VoxelRadiance, linear_clamp_sampler, coord, 0.0);
                    anisoSample = lerp(baseColor, anisoSample, clamp(lod, 0.0, 1.0));
                }

                return anisoSample;
            }

            float4 TraceCone(float3 posWS, float3 normalWS, float3 direction, float aperture, bool traceOcclusion)
            {
                uint3 anisoFaces;
                anisoFaces.x = direction.x < 0.0 ? 0 : 1;
                anisoFaces.y = direction.y < 0.0 ? 2 : 3;
                anisoFaces.z = direction.z < 0.0 ? 4 : 5;
                float3 anisoWeight = direction * direction;

                float maxDistance = _VolumeSize.x;
                float anisoVoxelSize = 2.0 * _VolumeSize.x * _VolumeResolution.z;
                float anisoVoxelSizeInv = 0.5 * _VolumeScale.x * _VolumeResolution.x;
                float t = anisoVoxelSize;
                float3 origin = posWS + normalWS * t;
                
                float4 coneSample = float4(0, 0, 0, 0);
                while (coneSample.a < 1.0 && t < maxDistance)
                {
                    float3 samplePosWS = origin + direction * t;
                    float diameter = 2.0 * aperture * t;
                    float mipmapLevel = log2(diameter * anisoVoxelSizeInv);

                    float3 sampleCoord = (samplePosWS - _VolumeMinPoint.xyz) * _VolumeScale.xyz;
                    float4 anisoSample = AnisotropicSample(sampleCoord, anisoWeight, anisoFaces, mipmapLevel);
                    coneSample += (1.0 - coneSample.a) * anisoSample;

                    t += diameter * 0.5;
                }

                return coneSample;
            }

            float4 frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float3 radiance = _BlitTexture.Sample(point_clamp_sampler, uv).rgb;
                float rawDepth = SampleSceneDepth(uv);
                
                if (rawDepth == UNITY_RAW_FAR_CLIP_VALUE)
                {
                    return float4(radiance, 1.0);
                }

                #if UNITY_REVERSED_Z
                    float depth = rawDepth;
                #else
                    // Adjust Z to match NDC for OpenGL ([-1, 1])
                    float depth = lerp(UNITY_NEAR_CLIP_VALUE, 1, rawDepth);
                #endif
                float3 posWS = ComputeWorldSpacePosition(uv, depth, UNITY_MATRIX_I_VP);
                float3 viewPosWS = unity_CameraToWorld._m03_m13_m23;

                float3 normalWS = SampleSceneNormals(uv);
                float3 albedo = SAMPLE_TEXTURE2D(_GBuffer0, point_clamp_sampler, uv).rgb;
                float metallic = SAMPLE_TEXTURE2D(_GBuffer1, point_clamp_sampler, uv).r;
                float smoothness = SAMPLE_TEXTURE2D(_GBuffer2, point_clamp_sampler, uv).a;
                
                float3 tangent = float3(0, 1, 0);
                if (abs(dot(tangent, normalWS)) == 1.0)
                {
                    tangent = float3(0, 0, 1);
                }
                // tangent = normalize(tangent - dot(normalWS, tangent) * normalWS);
                tangent = normalize(cross(normalWS, tangent));
                float3 bitangent = cross(normalWS, tangent);

                float aperture = 0.57735;
                float4 diffuseSample = float4(0, 0, 0, 0);
                for (int i = 0; i < MAX_CONE_COUNT; i++)
                {
                    float3 direction = tangent * diffuseConeDirections[i].x + bitangent * diffuseConeDirections[i].y + normalWS * diffuseConeDirections[i].z;
                    direction = normalize(direction);
                    float4 coneSample = TraceCone(posWS, normalWS, direction, aperture, true) * diffuseConeWeights[i];
                    diffuseSample += coneSample;
                }
                diffuseSample.rgb *= albedo;
                diffuseSample.rgb *= 1.0 - metallic;
                
                float4 specularSample = float4(0, 0, 0, 0);
                float3 viewDir = normalize(viewPosWS - posWS);
                float3 reflectDir = reflect(-viewDir, normalWS);
                reflectDir = normalize(reflectDir);
                float specularAperture = clamp(tan(HALF_PI * (1.0f - smoothness)), 0.013708, PI);
                specularSample = TraceCone(posWS, normalWS, reflectDir, specularAperture, false);
                specularSample.rgb *= metallic;

                // return float4(posWS, 1.0);
                // return float4(normalWS * 0.5 + 0.5, 1.0);
                // return float4(tangent * 0.5 + 0.5, 1.0);
                // return float4(radiance, 1.0);
                // return float4(albedo, 1.0);
                // return float4(metallic, smoothness, 0.0, 1.0);
                // return diffuseSample;
                // return specularAperture;
                return float4(radiance + diffuseSample.rgb + specularSample.rgb, 1.0);
            }

            ENDHLSL
        }
    }
}
