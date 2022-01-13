#if SHADERPASS != SHADERPASS_FOGVOLUME_VOXELIZATION
#error SHADERPASS_is_not_correctly_define
#endif

#include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/VertMesh.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/GeometricTools.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/VolumeRendering.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Core/Utilities/GeometryUtils.cs.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/VolumetricLighting/HDRenderPipeline.VolumetricLighting.cs.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Lighting/LightLoop/LightLoopDef.hlsl"

RW_TEXTURE3D(float4, _VBufferDensity) : register(u1); // RGB = sqrt(scattering), A = sqrt(extinction)
RW_TEXTURE2D_X(float, _FogVolumeDepth) : register(u2);

struct GeometryToFragment
{
    float4 vertex : SV_POSITION;
    // float3 positionSS : SV_POSITION
    uint depthSlice : SV_RenderTargetArrayIndex;
    // UNITY_VERTEX_OUTPUT_STEREO // TODO: VR Support
};

PackedVaryingsType Vert(AttributesMesh inputMesh)
{
    VaryingsType varyingsType;
    varyingsType.vmesh = VertMesh(inputMesh);

    // UNITY_SETUP_INSTANCE_ID(input);
    // UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    return PackVaryingsType(varyingsType);
}

uint DepthToSlice(float depth)
{
    // float de = _VBufferRcpSliceCount; // Log-encoded distance between slices
    float vBufferNearPlane = DecodeLogarithmicDepthGeneralized(0, _VBufferDistanceDecodingParams);

    float t = depth;
    float dt = t - vBufferNearPlane;
    float e1 = EncodeLogarithmicDepthGeneralized(dt, _VBufferDistanceEncodingParams);

    float slice = (e1 - _VBufferRcpSliceCount) / _VBufferRcpSliceCount;

    return uint(slice);
}

float SliceToDepth(uint slice)
{
    float t0 = DecodeLogarithmicDepthGeneralized(0, _VBufferDistanceDecodingParams);
    float de = _VBufferRcpSliceCount; // Log-encoded distance between slices
    float e1 = slice * de + de; // (slice + 1) / sliceCount
    float t1 = DecodeLogarithmicDepthGeneralized(e1, _VBufferDistanceDecodingParams);
    float dt = t1 - t0;
    return t0 + 0.5 * dt;
}

[maxvertexcount(50)]
void Geom(triangle PackedVaryingsType packedInputs[3], inout TriangleStream<GeometryToFragment> triStream)
{
    // UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(packedInputs[0]);
    FragInputs input0 = UnpackVaryingsToFragInputs(packedInputs[0]);
    FragInputs input1 = UnpackVaryingsToFragInputs(packedInputs[1]);
    FragInputs input2 = UnpackVaryingsToFragInputs(packedInputs[2]);

    // Calculate the number of slices need to be generated from SS bounds
    float3 minBounds, maxBounds;
    GetRendererBounds(minBounds, maxBounds);
    float3 minSS = TransformWorldToView(minBounds);
    float3 maxSS = TransformWorldToView(maxBounds);

    int startVoxelIndex = DepthToSlice(minSS.z);
    int stopVoxelIndex = DepthToSlice(maxSS.z);

    for (int i = startVoxelIndex; i < stopVoxelIndex; i++)
    {
        GeometryToFragment o0, o1, o2;
        // Generate triangle
        float sliceDepth = SliceToDepth(i);
        
        o0.vertex = float4(minSS.x, minSS.y, sliceDepth, 1);
        o0.depthSlice = i;

        o1.vertex = float4(maxSS.x, maxSS.y, sliceDepth, 1);
        o1.depthSlice = i;

        o2.vertex = float4(maxSS.x, minSS.y, sliceDepth, 1);
        o2.depthSlice = i;

        triStream.Append(o0);
        triStream.Append(o1);
        triStream.Append(o2);
        triStream.RestartStrip();
    }
}

void Frag(GeometryToFragment g2f, out float4 outColor : SV_Target0)
{
    // UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(g2f.pixelInterpolators);

    // FragInputs input = UnpackVaryingsToFragInputs(g2f.pixelInterpolators);
    // GetVolumeData(input, V, scatteringColor, density);
    outColor = float4(1, 0, 0, 1);

    // input.positionSS is SV_Position
    // PositionInputs posInput = GetPositionInput(input.positionSS.xy, _ScreenSize.zw, input.positionSS.z, input.positionSS.w, input.positionRWS);

// #ifdef VARYINGS_NEED_POSITION_WS
//     float3 V = GetWorldSpaceNormalizeViewDir(input.positionRWS);
// #else
//     // Unused
//     float3 V = float3(1.0, 1.0, 1.0); // Avoid the division by 0
// #endif

//     float3 scatteringColor;
//     float density;

//     // Sample the depth to know when to stop
//     float startDistance = LOAD_TEXTURE2D_X_LOD(_FogVolumeDepth, input.positionSS.xy, 0);
//     float stopDistance = length(input.positionRWS);

//     // Calculate the number of voxels/froxels to write:
//     int startVoxelIndex = DepthToSlice(startDistance);
//     int stopVoxelIndex = DepthToSlice(stopDistance);

//     float de = _VBufferRcpSliceCount; // Log-encoded distance between slices
//     float t0 = DecodeLogarithmicDepthGeneralized(0, _VBufferDistanceDecodingParams);

//     // for (int voxelDepthIndex = startVoxelIndex; voxelDepthIndex <= stopVoxelIndex; voxelDepthIndex++)
//     {
//         // It's possible for objects to be outside of the vbuffer bounds (because depth clip is disabled)
//         if (voxelDepthIndex < 0 || voxelDepthIndex >= _VBufferSliceCount)
//             continue;

//         float e1 = voxelDepthIndex * de + de; // (slice + 1) / sliceCount
//         float t1 = DecodeLogarithmicDepthGeneralized(e1, _VBufferDistanceDecodingParams);
//         float dt = t1 - t0;
//         float t = t0 + 0.5 * dt;

//         // compute world pos from voxel depth index:

//         // TODO: patch position input for SG
//         // input.positionRWS = - V * t;
//         float t2 = (float)(voxelDepthIndex - startVoxelIndex) / max(0.0001, (float)(stopVoxelIndex - startVoxelIndex));
//         input.positionRWS = -V * lerp(startDistance, stopDistance, t2);

//         GetVolumeData(input, V, scatteringColor, density);

//         uint3 voxelCoord = uint3(posInput.positionSS.xy, voxelDepthIndex + _VBufferSliceCount * unity_StereoEyeIndex);
//         _VBufferDensity[voxelCoord] += max(0, float4(scatteringColor, density));
//     }

    // TODO: set color mask to 0 and remove this line
}
