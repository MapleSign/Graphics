Shader "Hidden/Universal Render Pipeline/BRGPicking"
{
    SubShader
    {
        // Universal Pipeline tag is required. If Universal render pipeline is not set in the graphics settings
        // this Subshader will fail. One can add a subshader below or fallback to Standard built-in to make this
        // material work with both Universal Render Pipeline and Builtin Unity Pipeline
        Tags{"RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" "ShaderModel"="4.5"}
        LOD 300

        Pass
        {
            Name "ScenePickingPass"
            Tags { "LightMode" = "Picking" }

            Cull [_CullMode]

            HLSLPROGRAM

            #pragma target 4.5
            #pragma exclude_renderers gles gles3 glcore

            #pragma editor_sync_compilation
            #pragma multi_compile DOTS_INSTANCING_ON

            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float4x4 unity_BRGPickingViewMatrix;
            float4x4 unity_BRGPickingProjMatrix;
            int unity_BRGPickingSubmeshIndex;

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float4 positionWS = mul(UNITY_MATRIX_M, input.positionOS);
                float4 positionVS = mul(unity_BRGPickingViewMatrix, positionWS);
                output.positionCS = mul(unity_BRGPickingProjMatrix, positionVS);

                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                static const uint SubmeshIndexBitCount = 5;
                static const uint EntityIndexBitCount = 26;

                static const uint SubmeshIndexBitsAndMask = (1 << SubmeshIndexBitCount) - 1;
                static const uint EntityIndexBitsAndMask = (1 << EntityIndexBitCount) - 1;

                // Add 1 to the ID, so the entity ID 0 gets a value that is not equal to the clear value.
                uint submeshIndex = (uint)unity_BRGPickingSubmeshIndex;
                uint entityIndex = unity_EntityId.x + 1;

                uint highBitMask = 1 << 31;
                uint submeshMask = (submeshIndex & SubmeshIndexBitsAndMask) << EntityIndexBitCount;
                uint entityIndexMask = entityIndex & EntityIndexBitsAndMask;

                uint pickingID = highBitMask | submeshMask | entityIndexMask;

                return PackId32ToRGBA8888(pickingID);
            }

            ENDHLSL
        }
    }

    FallBack Off
}
