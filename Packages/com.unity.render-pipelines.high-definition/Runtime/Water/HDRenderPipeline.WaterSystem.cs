using System.Collections.Generic;
using Unity.Mathematics;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace UnityEngine.Rendering.HighDefinition
{
    public partial class HDRenderPipeline
    {
        // Flag that allows us to track if the water system is currently active
        bool m_ActiveWaterSystem = false;

        // Rendering kernels
        ComputeShader m_WaterLightingCS;
        int m_WaterClassifyTilesKernel;
        int m_WaterPrepareSSRIndirectKernel;
        int m_WaterClearIndirectKernel;
        int[] m_WaterIndirectDeferredKernels = new int[WaterConsts.k_NumWaterVariants];
        int m_WaterDeferredKernel;
        int m_WaterFogIndirectKernel;

        // Water evaluation
        ComputeShader m_WaterEvaluationCS;
        int m_FindVerticalDisplacementsKernel;

        // Constant buffer
        GraphicsBuffer m_ShaderVariablesWaterPerCamera;

        // The shader passes used to render the water
        Material m_InternalWaterMaterial;
        Mesh m_GridMesh, m_RingMesh, m_RingMeshLow;
        GraphicsBuffer m_WaterIndirectDispatchBuffer;
        GraphicsBuffer m_WaterPatchDataBuffer;
        GraphicsBuffer m_WaterCameraFrustrumBuffer;
        FrustumGPU[] m_WaterCameraFrustumCPU = new FrustumGPU[1];

        internal const string k_WaterGBufferPass = "WaterGBuffer";
        internal const string k_WaterMaskPass = "WaterMask";
        internal const string k_LowResGBufferPass = "LowRes";
        internal const string k_TessellationPass = "Tessellation";
        readonly static string[] k_PassesGBuffer = new string[] { k_WaterGBufferPass, k_LowResGBufferPass };
        readonly static string[] k_PassesGBufferTessellation = new string[] { k_WaterGBufferPass + k_TessellationPass, k_LowResGBufferPass };
        readonly static string[] k_PassesWaterMask = new string[] { k_WaterMaskPass, k_WaterMaskPass + k_LowResGBufferPass };

        // Other internal rendering data
        MaterialPropertyBlock m_WaterMaterialPropertyBlock;

        // Handles the water profiles
        const int k_MaxNumWaterSurfaceProfiles = 16;
        WaterSurfaceProfile[] m_WaterSurfaceProfileArray = new WaterSurfaceProfile[k_MaxNumWaterSurfaceProfiles];
        WaterSurfaceGBufferData[] m_WaterGBufferDataArray = new WaterSurfaceGBufferData[k_MaxNumWaterSurfaceProfiles];
        ShaderVariablesWaterPerCamera[] m_ShaderVariablesPerCameraArray = new ShaderVariablesWaterPerCamera[k_MaxNumWaterSurfaceProfiles];
        GraphicsBuffer m_WaterProfileArrayGPU;

        // Caustics data
        GraphicsBuffer m_CausticsGeometry;
        bool m_CausticsBufferGeometryInitialized;
        Material m_CausticsMaterial;

        // Water line and under water
        GraphicsBuffer m_WaterCameraHeightBuffer;

        // Local Currents
        Texture2D m_WaterSectorData;

        #region Initialization
        void InitializeWaterSystem()
        {
            m_ActiveWaterSystem = m_Asset.currentPlatformRenderPipelineSettings.supportWater;

            // These buffers are needed even when water is disabled
            m_DefaultWaterLineBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 3, sizeof(uint));
            m_DefaultWaterLineBuffer.SetData(new uint[] { 0xFFFFFFFF, 0, 2 });

            m_WaterProfileArrayGPU = new GraphicsBuffer(GraphicsBuffer.Target.Structured, k_MaxNumWaterSurfaceProfiles, UnsafeUtility.SizeOf<WaterSurfaceProfile>());

            // If the asset doesn't support water surfaces, nothing to do here
            if (!m_ActiveWaterSystem)
                return;

            m_ShaderVariablesWaterPerCamera = new GraphicsBuffer(GraphicsBuffer.Target.Constant, 1, UnsafeUtility.SizeOf<ShaderVariablesWaterPerCamera>());

            // Water simulation
            InitializeWaterSimulation();

            // Water rendering
            m_WaterLightingCS = runtimeShaders.waterLightingCS;
            m_WaterPrepareSSRIndirectKernel = m_WaterLightingCS.FindKernel("PrepareSSRIndirect");
            m_WaterClearIndirectKernel = m_WaterLightingCS.FindKernel("WaterClearIndirect");
            m_WaterClassifyTilesKernel = m_WaterLightingCS.FindKernel("WaterClassifyTiles");
            m_WaterIndirectDeferredKernels[0] = m_WaterLightingCS.FindKernel("WaterDeferredLighting_Variant0");
            m_WaterIndirectDeferredKernels[1] = m_WaterLightingCS.FindKernel("WaterDeferredLighting_Variant1");
            m_WaterIndirectDeferredKernels[2] = m_WaterLightingCS.FindKernel("WaterDeferredLighting_Variant2");
            m_WaterIndirectDeferredKernels[3] = m_WaterLightingCS.FindKernel("WaterDeferredLighting_Variant3");
            m_WaterIndirectDeferredKernels[4] = m_WaterLightingCS.FindKernel("WaterDeferredLighting_Variant4");
            m_WaterDeferredKernel = m_WaterLightingCS.FindKernel("WaterDeferredLighting");
            m_WaterFogIndirectKernel = m_WaterLightingCS.FindKernel("WaterFogIndirect");

            // Water evaluation
            m_WaterEvaluationCS = runtimeShaders.waterEvaluationCS;
            m_FindVerticalDisplacementsKernel = m_WaterEvaluationCS.FindKernel("FindVerticalDisplacements");

            // Allocate the additional rendering data
            m_WaterMaterialPropertyBlock = new MaterialPropertyBlock();
            m_InternalWaterMaterial = runtimeMaterials.waterMaterial;
            InitializeInstancingData();

            // Create the caustics water geometry
            m_CausticsGeometry = new GraphicsBuffer(GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.Index, WaterConsts.k_WaterCausticsMeshNumQuads * 6, sizeof(int));
            m_CausticsBufferGeometryInitialized = false;
            m_CausticsMaterial = CoreUtils.CreateEngineMaterial(runtimeShaders.waterCausticsPS);

            // Waterline / Underwater
            // TODO: This should be entirely dynamic and depend on M_MaxViewCount
            m_WaterCameraHeightBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 2 * 4, sizeof(float));

            // Make sure the under water surface index is invalidated
            m_UnderWaterSurfaceIndex = -1;

            // Make sure the base mesh is built
            BuildGridMeshes(ref m_GridMesh, ref m_RingMesh, ref m_RingMeshLow);

            // Water deformers initialization
            InitializeWaterDeformers();

            // Under water resources
            InitializeUnderWaterResources();

            // Faom resources
            InitializeWaterFoam();
        }

        void InitializeInstancingData()
        {
            // Allocate the indirect instancing buffer
            m_WaterIndirectDispatchBuffer = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 5, sizeof(int));

            // Initialize the parts of the buffer with valid values
            uint meshResolution = WaterConsts.k_WaterTessellatedMeshResolution;
            uint quadCount = 2 * ((meshResolution - meshResolution / 4) * (meshResolution / 4 - 1) + meshResolution / 4);
            uint triCount = quadCount + 3 * meshResolution / 2;

            uint[] indirectBufferCPU = new uint[5];
            indirectBufferCPU[0] = triCount * 3;

            // Push the values to the GPU
            m_WaterIndirectDispatchBuffer.SetData(indirectBufferCPU);

            // Allocate the per instance data
            m_WaterPatchDataBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 7 * 7, sizeof(float) * 2);

            // Allocate the frustum buffer
            m_WaterCameraFrustrumBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, 1, System.Runtime.InteropServices.Marshal.SizeOf(typeof(FrustumGPU)));
        }

        void CheckWaterCurrentData()
        {
            if (m_WaterSectorData == null)
            {
                m_WaterSectorData = new Texture2D(16, 1, TextureFormat.RGBAFloat, -1, true);
                m_WaterSectorData.SetPixelData(WaterConsts.k_SectorSwizzlePacked, 0, 0);
                m_WaterSectorData.Apply();
            }
        }

        void ReleaseWaterSystem()
        {
            // Grab all the water surfaces in the scene
            var waterSurfaces = WaterSurface.instancesAsArray;
            int numWaterSurfaces = WaterSurface.instanceCount;

            // Loop through them and display them
            for (int surfaceIdx = 0; surfaceIdx < numWaterSurfaces; ++surfaceIdx)
            {
                WaterSurface waterSurface = waterSurfaces[surfaceIdx];
                waterSurface.ReleaseResources();
            }

            // Release the default water line array
            CoreUtils.SafeRelease(m_DefaultWaterLineBuffer);

            // Release the water profile array
            CoreUtils.SafeRelease(m_WaterProfileArrayGPU);

            // If the asset doesn't support water surfaces, nothing to do here
            if (!m_ActiveWaterSystem)
                return;

            CoreUtils.SafeRelease(m_ShaderVariablesWaterPerCamera);

            // Faom resources
            ReleaseWaterFoam();

            // Water deformers release
            ReleaseWaterDeformers();

            // Make sure the CPU simulation stuff is properly freed
            ReleaseCPUWaterSimulation();

            // Release the waterline underwater data
            CoreUtils.SafeRelease(m_WaterCameraHeightBuffer);

            // Release the caustics geometry
            CoreUtils.Destroy(m_CausticsMaterial);
            CoreUtils.SafeRelease(m_CausticsGeometry);

            // Water rendering resources
            CoreUtils.SafeRelease(m_WaterCameraFrustrumBuffer);
            CoreUtils.SafeRelease(m_WaterPatchDataBuffer);
            CoreUtils.SafeRelease(m_WaterIndirectDispatchBuffer);

            // Simulation resources
            ReleaseWaterSimulation();

            // Free the meshes
            m_GridMesh = m_RingMesh = m_RingMeshLow = null;
        }
        #endregion

        #region Surface Update

        /// <summary>
        /// Computes the screen space size of an edge after camera projection
        /// </summary>
        /// <param name="hdCamera">camera</param>
        /// <param name="cameraDistance">the distance between the edge and the camera</param>
        /// <param name="sizeWS">the size of the edge in world space</param>
        /// <returns>The screen space size</returns>
        float ComputeScreenSpaceSize(HDCamera hdCamera, float cameraDistance, float sizeWS)
        {
            float4 positionWS = new float4(sizeWS, 0.0f, cameraDistance, 1.0f);
            float4 positionCS = math.mul(hdCamera.mainViewConstants.nonJitteredProjMatrix, positionWS);
            return math.abs(positionCS.x / positionCS.w) * 0.5f * hdCamera.actualWidth;
        }

        static void BindPerSurfaceConstantBuffer(CommandBuffer cmd, ComputeShader cs, GraphicsBuffer buffer)
        {
            cmd.SetComputeConstantBufferParam(cs, HDShaderIDs._ShaderVariablesWaterPerSurface, buffer, 0, buffer.stride);
        }

        void UpdatePerSurfaceConstantBuffer(CommandBuffer cmd, WaterSurface currentWater, int surfaceIndex)
        {
            ref var cb = ref currentWater.constantBufferData[0];

            cb._MaxWaterDeformation = m_MaxWaterDeformation;
            cb._SimulationTime = currentWater.simulation.simulationTime;
            cb._DeltaTime = currentWater.simulation.deltaTime;

            if (currentWater.timeMultiplier == 0.0f)
                cb._DeltaTime = 1.0f; // This is to be able to see the foam generators even when time is paused

            // all the data below mostly don't change from frame to frame

            // Resolution at which the simulation is evaluated
            cb._BandResolution = (uint)m_WaterBandResolution;

            // Per patch data
            cb._PatchGroup = currentWater.simulation.spectrum.patchGroup;
            cb._PatchOrientation = currentWater.simulation.spectrum.patchOrientation * Mathf.Deg2Rad;
            cb._PatchWindSpeed = currentWater.simulation.spectrum.patchWindSpeed;
            cb._PatchDirectionDampener = currentWater.simulation.spectrum.patchWindDirDampener;

            void PackBandData(WaterSimulationResources simulation, int bandIdx, out Vector4 scaleOffsetAmplitude, out float2 fade)
            {
                float invPatchSize = 1.0f / simulation.spectrum.patchSizes[bandIdx];
                float orientation = simulation.spectrum.patchOrientation[bandIdx] * Mathf.Deg2Rad;
                float bandScale = simulation.rendering.patchCurrentSpeed[bandIdx] * simulation.simulationTime * invPatchSize;
                scaleOffsetAmplitude = new Vector4(invPatchSize, Mathf.Cos(orientation) * bandScale, Mathf.Sin(orientation) * bandScale, simulation.rendering.patchAmplitudeMultiplier[bandIdx]);
                fade = new float2(simulation.rendering.patchFadeA[bandIdx], simulation.rendering.patchFadeB[bandIdx]);
            }

            PackBandData(currentWater.simulation, 0, out cb._Band0_ScaleOffset_AmplitudeMultiplier, out cb._Band0_Fade);
            PackBandData(currentWater.simulation, 1, out cb._Band1_ScaleOffset_AmplitudeMultiplier, out cb._Band1_Fade);
            PackBandData(currentWater.simulation, 2, out cb._Band2_ScaleOffset_AmplitudeMultiplier, out cb._Band2_Fade);

            cb._GroupOrientation = currentWater.simulation.spectrum.groupOrientation * Mathf.Deg2Rad;

            // Max wave height for the system
            float patchAmplitude = EvaluateMaxAmplitude(currentWater.simulation.spectrum.patchSizes.x, cb._PatchWindSpeed.x * WaterConsts.k_MeterPerSecondToKilometerPerHour);
            cb._MaxWaveHeight = patchAmplitude;
            cb._ScatteringWaveHeight = Mathf.Max(cb._MaxWaveHeight * WaterConsts.k_ScatteringRange, WaterConsts.k_MinScatteringAmplitude) + currentWater.maximumHeightOverride;

            // Horizontal displacement due to each band
            cb._MaxWaveDisplacement = cb._MaxWaveHeight * WaterConsts.k_WaterMaxChoppinessValue;

            // Water smoothness
            cb._WaterSmoothness = currentWater.startSmoothness;

            // Foam Jacobian offset depends on the number of bands
            if (currentWater.simulation.numActiveBands == 3)
                cb._SimulationFoamAmount = 12.0f * Mathf.Pow(0.8f + currentWater.simulationFoamAmount * 0.28f, 0.25f);
            else if (currentWater.simulation.numActiveBands == 2)
                cb._SimulationFoamAmount = 8.0f * Mathf.Pow(0.72f + currentWater.simulationFoamAmount * 0.28f, 0.25f);
            else
                cb._SimulationFoamAmount = 4.0f * Mathf.Pow(0.72f + currentWater.simulationFoamAmount * 0.28f, 0.25f);

            cb._FoamCurrentInfluence = currentWater.foamCurrentInfluence;

            // Smoothness of the foam
            cb._FoamPersistenceMultiplier = 1.0f / Mathf.Lerp(0.05f, 1f, currentWater.foamPersistenceMultiplier );
            cb._FoamSmoothness = currentWater.foamSmoothness;
            cb._FoamTiling = currentWater.foamTextureTiling;

            // We currently only support properly up to 16 unique water surfaces
            cb._SurfaceIndex = surfaceIndex & 0xF;

            cb._MaxRefractionDistance = Mathf.Min(currentWater.absorptionDistance, currentWater.maxRefractionDistance);

            cb._OutScatteringCoefficient = -Mathf.Log(0.02f) / currentWater.absorptionDistance;
            cb._TransparencyColor = new Vector4(Mathf.Min(currentWater.refractionColor.r, 0.99f), Mathf.Min(currentWater.refractionColor.g, 0.99f), Mathf.Min(currentWater.refractionColor.b, 0.99f));
            cb._WaterUpDirection = new float4(currentWater.UpVector(), 1.0f);

            cb._ScatteringColorTips = currentWater.scatteringColor; /*alpha is unsused*/
            cb._AmbientScattering = currentWater.ambientScattering;
            cb._HeightBasedScattering = currentWater.heightScattering;
            cb._DisplacementScattering = currentWater.displacementScattering;

            Matrix4x4 worldToWater = currentWater.simulation.rendering.worldToWaterMatrix;
            cb._WaterForwardXZ = new float2(worldToWater.m00, worldToWater.m02);

            var localScale = currentWater.transform.localScale;
            Vector2 invertScale = new Vector2(localScale.x < 0.0f ? -1.0f : 1.0f, localScale.z < 0.0f ? -1.0f : 1.0f);

            // Foam
            Vector3 foamCenter = new Vector3(currentWater.foamAreaOffset.x, 1.0f, currentWater.foamAreaOffset.y);
            if (!currentWater.IsCustomMesh())
                foamCenter = currentWater.transform.rotation * foamCenter;

            var simulationFoamWindAttenuation = Mathf.Clamp(currentWater.simulationFoamWindCurve.Evaluate(currentWater.simulation.spectrum.patchWindSpeed.x / WaterConsts.k_SwellMaximumWindSpeedMpS), 0.0f, 1.0f);
            cb._FoamRegionOffset = new Vector2(invertScale.x * foamCenter.x + currentWater.transform.position.x, invertScale.y * foamCenter.z + currentWater.transform.position.z);
            cb._FoamRegionScale.Set(1.0f / currentWater.foamAreaSize.x, 1.0f / currentWater.foamAreaSize.y);
            cb._SimulationFoamIntensity = m_ActiveWaterFoam && currentWater.HasSimulationFoam() ? simulationFoamWindAttenuation : 0.0f;
            cb._SimulationFoamMaskOffset = currentWater.simulationFoamMaskOffset;
            cb._SimulationFoamMaskScale.Set(1.0f / currentWater.simulationFoamMaskExtent.x, 1.0f / currentWater.simulationFoamMaskExtent.y);
            cb._WaterFoamRegionResolution = currentWater.foam ? (int)currentWater.foamResolution : 0;

            // Water Mask
            cb._WaterMaskOffset = Vector2.Scale(currentWater.waterMaskOffset, invertScale);
            cb._WaterMaskScale.Set(1.0f / currentWater.waterMaskExtent.x, 1.0f / currentWater.waterMaskExtent.y);
            cb._WaterMaskRemap.Set(currentWater.waterMaskRemap.x, currentWater.waterMaskRemap.y - currentWater.waterMaskRemap.x);

            // Caustics
            cb._CausticsBandIndex = SanitizeCausticsBand(currentWater.causticsBand, currentWater.simulation.numActiveBands);
            cb._CausticsRegionSize = currentWater.simulation.spectrum.patchSizes[cb._CausticsBandIndex];

            // Deformation
            Vector3 deformationCenter = new Vector3(currentWater.deformationAreaOffset.x, 1.0f, currentWater.deformationAreaOffset.y);
            if (!currentWater.IsCustomMesh())
                deformationCenter = currentWater.transform.rotation * deformationCenter;

            cb._DeformationRegionOffset = new Vector2(invertScale.x * deformationCenter.x + currentWater.transform.position.x, invertScale.y * deformationCenter.z + currentWater.transform.position.z);
            cb._DeformationRegionScale.Set(1.0f / currentWater.deformationAreaSize.x, 1.0f / currentWater.deformationAreaSize.y);
            cb._DeformationRegionResolution = (int)currentWater.deformationRes;

            // Cautics
            cb._CausticsIntensity = currentWater.caustics ? currentWater.causticsIntensity : 0.0f;
            cb._CausticsShadowIntensity = currentWater.causticsDirectionalShadow ? currentWater.causticsDirectionalShadowDimmer : 1.0f;
            cb._CausticsPlaneBlendDistance = currentWater.causticsPlaneBlendDistance;
            cb._CausticsMaxLOD = EvaluateCausticsMaxLOD(currentWater.causticsResolution);
            cb._CausticsTilingFactor = 1.0f / currentWater.causticsTilingFactor;

            // Current maps
            cb._Group0CurrentRegionScaleOffset.Set(invertScale.x / currentWater.largeCurrentRegionExtent.x, invertScale.y / currentWater.largeCurrentRegionExtent.y, currentWater.largeCurrentRegionOffset.x, -currentWater.largeCurrentRegionOffset.y);
            if (currentWater.ripplesMotionMode == WaterPropertyOverrideMode.Inherit && currentWater.surfaceType != WaterSurfaceType.Pool)
            {
                cb._Group1CurrentRegionScaleOffset = cb._Group0CurrentRegionScaleOffset;
                cb._CurrentMapInfluence.Set(currentWater.largeCurrentMapInfluence, currentWater.largeCurrentMapInfluence);
            }
            else
            {
                cb._Group1CurrentRegionScaleOffset.Set(invertScale.x / currentWater.ripplesCurrentRegionExtent.x, invertScale.y / currentWater.ripplesCurrentRegionExtent.y, currentWater.ripplesCurrentRegionOffset.x, -currentWater.ripplesCurrentRegionOffset.y);
                cb._CurrentMapInfluence.Set(currentWater.largeCurrentMapInfluence, currentWater.ripplesCurrentMapInfluence);
            }

            // Tessellation
            cb._WaterMaxTessellationFactor = currentWater.maxTessellationFactor;
            cb._WaterTessellationFadeStart = currentWater.tessellationFactorFadeStart;
            cb._WaterTessellationFadeRange = currentWater.tessellationFactorFadeRange;

            // Bind the rendering layer data for decal layers
            cb._WaterRenderingLayer = (uint)currentWater.renderingLayerMask;

            // Evaluate the matrices
            cb._WaterSurfaceTransform = currentWater.simulation.rendering.waterToWorldMatrix;
            cb._WaterSurfaceTransform_Inverse = currentWater.simulation.rendering.worldToWaterMatrix;
            cb._WaterCustomTransform_Inverse = currentWater.simulation.rendering.worldToWaterMatrixCustom;

            ConstantBuffer.UpdateData(cmd, cb);
            cmd.SetBufferData(currentWater.constantBuffer, currentWater.constantBufferData);
        }

        void UpdataPerCameraConstantBuffer(WaterSurface currentWater, HDCamera hdCamera, WaterRendering settings,
                                            bool instancedQuads, bool infinite, bool customMesh,
                                            ref ShaderVariablesWaterPerCamera cb)
        {
            float3 waterPosition = currentWater.transform.position;
            float2 extent = currentWater.IsProceduralGeometry() ? new Vector2(Mathf.Abs(currentWater.transform.lossyScale.x), Mathf.Abs(currentWater.transform.lossyScale.z)) : Vector2.one;

            ref var perSurfaceCB = ref currentWater.constantBufferData[0];
            float maxWaveDisplacement = perSurfaceCB._MaxWaveDisplacement;
            float maxWaveHeight = perSurfaceCB._MaxWaveHeight;

            // Rotation, size and offsets (patch, water mask and foam mask)
            if (instancedQuads)
            {
                Matrix4x4 worldToWater = currentWater.simulation.rendering.worldToWaterMatrix;

                // Compute the grid size to maintain a constant triangle size in screen space
                float distanceToCamera = maxWaveDisplacement + worldToWater.MultiplyPoint3x4(hdCamera.camera.transform.position).y;
                float vertexSpace = Mathf.Min(ComputeScreenSpaceSize(hdCamera, distanceToCamera, 1.0f / WaterConsts.k_WaterTessellatedMeshResolution), 1.0f);
                float gridSize = (1 << (int)(Mathf.Log(1.0f / vertexSpace, 2))) * settings.triangleSize.value;
                cb._GridSize = gridSize;

                // Move the patches with the camera in locksteps to reduce vertex wobbling
                int stableLODCount = 5;
                float cameraStep = (1 << stableLODCount) * gridSize / WaterConsts.k_WaterTessellatedMeshResolution;
                float cameraDirOffset = maxWaveDisplacement * (1.0f - settings.triangleSize.value / 200.0f);
                float3 patchOffset = worldToWater.MultiplyPoint3x4(hdCamera.camera.transform.position + hdCamera.camera.transform.forward * cameraDirOffset);
                cb._PatchOffset = math.round((waterPosition.xz + patchOffset.xz) / cameraStep) * cameraStep - waterPosition.xz;
            }
            else
            {
                cb._GridSize = extent;
                cb._PatchOffset = 0.0f;
            }

            // Used for non infinite surfaces
            if (instancedQuads && !infinite)
            {
                cb._RegionExtent = extent * 0.5f;

                // compute biggest lod that touches region
                float distance = math.max(
                    math.abs(waterPosition.x - cb._PatchOffset.x) + cb._RegionExtent.x,
                    math.abs(waterPosition.z - cb._PatchOffset.y) + cb._RegionExtent.y);
                int lod = (int)math.ceil(math.log2(math.max(distance * 2.0f / cb._GridSize.x, 1.0f)));
                // determine vertex step for this lod level
                float triangleSize = (1 << lod) * cb._GridSize.x / WaterConsts.k_WaterTessellatedMeshResolution;
                // align grid size on region extent
                float2 optimalTriangleSize = new float2(
                    extent.x / Mathf.Max(Mathf.Floor(extent.x / triangleSize), 1),
                    extent.y / Mathf.Max(Mathf.Floor(extent.y / triangleSize), 1));
                cb._GridSize = optimalTriangleSize * cb._GridSize.x / triangleSize;
                // align grid pos on one region corner
                float2 corner = -(cb._PatchOffset + 0.5f * cb._GridSize) - cb._RegionExtent;
                cb._PatchOffset = cb._PatchOffset + (corner - math.round(corner / optimalTriangleSize) * optimalTriangleSize);
            }
            else
                cb._RegionExtent = new float2(float.MaxValue, float.MaxValue);

            if (instancedQuads)
            {
                // Max offset for patch culling
                float3 cameraPosition = hdCamera.camera.transform.position;
                float maxOffsetRelative = math.max(math.abs(cb._PatchOffset.x - cameraPosition.x), math.abs(cb._PatchOffset.y - cameraPosition.z));
                float maxPatchOffset = -maxWaveDisplacement - maxOffsetRelative;

                // Compute last LOD with displacement
                float maxFadeDistance = currentWater.simulation.rendering.maxFadeDistance;
                if (maxFadeDistance != float.MaxValue)
                {
                    float cameraHeight = cameraPosition.y - (waterPosition.y + maxWaveHeight);
                    maxFadeDistance = Mathf.Sqrt(maxFadeDistance * maxFadeDistance - cameraHeight * cameraHeight) - maxPatchOffset;
                    maxFadeDistance = Mathf.Max(maxFadeDistance * 2.0f / Mathf.Max(cb._GridSize.x, cb._GridSize.y), 1);

                    cb._MaxLOD = (uint)Mathf.Ceil(Mathf.Log(maxFadeDistance, 2)); // only keep highest bit
                }
                else
                    cb._MaxLOD = 8; // we could support a maximum of 12 LODs (max 49 patches, 12 * 4 = 48), but this is enough

                cb._GridSizeMultiplier = (1 << (int)cb._MaxLOD) * 0.5f;
            }
            else
                cb._GridSizeMultiplier = 1.0f;
        }

        void FillWaterSurfaceProfile(HDCamera hdCamera, WaterSurface waterSurface, int waterSurfaceIndex)
        {
            ref var cb = ref waterSurface.constantBufferData[0];

            WaterSurfaceProfile profile = new WaterSurfaceProfile();
            profile.maxRefractionDistance = cb._MaxRefractionDistance;
            profile.renderingLayers = cb._WaterRenderingLayer;
            profile.transparencyColor = cb._TransparencyColor;
            profile.outScatteringCoefficient = cb._OutScatteringCoefficient;
            profile.scatteringColor = cb._ScatteringColorTips;
            profile.upDirection = cb._WaterUpDirection.xyz;

            // Scattering parameters
            profile.bodyScatteringHeight = waterSurface.directLightBodyScattering;
            profile.tipScatteringHeight = waterSurface.directLightTipScattering;
            profile.cameraUnderWater = waterSurfaceIndex == m_UnderWaterSurfaceIndex ? 1 : 0;
            profile.envPerceptualRoughness = waterSurface.surfaceType == WaterSurfaceType.Pool ? 0.0f : Mathf.Lerp(0.0f, 0.15f, Mathf.Clamp(waterSurface.largeWindSpeed / WaterConsts.k_EnvRoughnessWindSpeed, 0.0f, 1.0f));
            profile.disableIOR = waterSurface.underWaterRefraction ? 0 : 1;

            // Smoothness fade
            profile.smoothnessFadeStart = waterSurface.smoothnessFadeStart;
            profile.smoothnessFadeDistance = waterSurface.smoothnessFadeDistance;
            profile.roughnessEndValue = 1.0f - waterSurface.endSmoothness;

            profile.foamColor.Set(waterSurface.foamColor.r, waterSurface.foamColor.g, waterSurface.foamColor.b);

            // Under water stuff
            profile.underWaterAmbientProbeContribution = waterSurface.underWaterAmbientProbeContribution;
            profile.absorptionDistanceMultiplier = 1.0f / waterSurface.absorptionDistanceMultiplier;

            // Profile has been filled, we're done
            m_WaterSurfaceProfileArray[waterSurfaceIndex] = profile;
        }

        void UpdateWaterSurface(CommandBuffer cmd, WaterSurface currentWater, int surfaceIndex)
        {
            if (!m_ActiveWaterSimulationCPU && currentWater.scriptInteractions)
                InitializeCPUWaterSimulation();

            // If the function returns false, this means the resources were just created and they need to be initialized.
            currentWater.CheckResources((int)m_WaterBandResolution, m_ActiveWaterFoam, m_GPUReadbackMode, out bool validGPUResources, out bool validCPUResources);

            // Update the simulation time (include timescale)
            currentWater.simulation.Update(currentWater.timeMultiplier);

            // Update the constant buffer
            UpdatePerSurfaceConstantBuffer(cmd, currentWater, surfaceIndex);

            // Update the GPU simulation for the water
            UpdateGPUWaterSimulation(cmd, currentWater, !validGPUResources);

            // Here we replicate the ocean simulation on the CPU (if requested)
            UpdateCPUWaterSimulation(currentWater, !validCPUResources);

            // Update the foam texture
            UpdateWaterFoamSimulation(cmd, currentWater);

            // Update the deformation data
            UpdateWaterDeformation(cmd, currentWater);

            // Here we need to replicate the water CPU Buffers
            UpdateCPUBuffers(cmd, currentWater);

            // Render the caustics from the current simulation state if required
            if (currentWater.caustics)
                EvaluateWaterCaustics(cmd, currentWater);
            else
                currentWater.simulation.CheckCausticsResources(false, 0);
        }

        void UpdateWaterSurfaces(CommandBuffer cmd)
        {
            // Grab all the water surfaces in the scene
            var waterSurfaces = WaterSurface.instancesAsArray;
            int numWaterSurfaces = WaterSurface.instanceCount;

            // If water surface simulation is disabled, skip.
            if (!m_ActiveWaterSystem || numWaterSurfaces == 0)
                return;

            float ct = waterSurfaces[0].simulation != null ? waterSurfaces[0].simulation.simulationTime : 0.0f;
            Vector4 _WaterDecalTimeParameters = new Vector4(ct, Mathf.Sin(ct), Mathf.Cos(ct), 0.0f);
            Shader.SetGlobalVector(HDShaderIDs._WaterDecalTimeParameters, _WaterDecalTimeParameters);

            // Make sure that all the deformers are on the GPU
            UpdateWaterDeformersData(cmd);

            // Make sure that all the foam generators are on the GPU
            UpdateWaterGeneratorsData(cmd);

            // In case we had a scene switch, it is possible the resource became null
            if (m_GridMesh == null)
                BuildGridMeshes(ref m_GridMesh, ref m_RingMesh, ref m_RingMeshLow);

            using (new ProfilingScope(cmd, ProfilingSampler.Get(HDProfileId.WaterSurfaceUpdate)))
            {
                // Bind the noise textures
                BlueNoise.BindDitheredTextureSet(cmd, GetBlueNoiseManager().DitheredTextureSet1SPP());

                // Loop through them and update them
                for (int surfaceIdx = 0; surfaceIdx < numWaterSurfaces; ++surfaceIdx)
                {
                    // Grab the current water surface
                    WaterSurface currentWater = waterSurfaces[surfaceIdx];

                    // Update the water surface (dispersion)
                    UpdateWaterSurface(cmd, currentWater, surfaceIdx);
                }
            }
        }
        #endregion

        #region GBuffer
        internal struct WaterGBuffer
        {
            // Flag that defines if at least one water surface was rendered into the gbuffer
            public bool valid;

            // Flag that defines if at least one water surface will need to be rendered as debug
            public bool debugRequired;

            // GBuffer targets
            public TextureHandle waterGBuffer0;
            public TextureHandle waterGBuffer1;
            public TextureHandle waterGBuffer2;
            public TextureHandle waterGBuffer3;

            // Indirect dispatch and tile data
            public BufferHandle indirectBuffer;
            public BufferHandle tileBuffer;

            public BufferHandle cameraHeight;
        }

        struct WaterSurfaceGBufferData
        {
            public bool render;
            public bool renderDebug;
            public int surfaceIndex;

            // Simulation & Deformation buffers
            public RenderTargetIdentifier displacementTexture;
            public RenderTargetIdentifier deformationBuffer;

            // Geometry parameters
            public bool drawInfiniteMesh;
            public bool tessellation;
            public int numActiveBands;

            // Geometry properties
            public bool instancedQuads;
            public bool infinite;
            public bool customMesh;
            public List<MeshRenderer> meshRenderers;

            public bool foam;
            public bool deformation;
            public bool evaluateCameraPosition;

            // Water Mask
            public Texture waterMask;

            // Current
            public bool activeCurrent;
            public Texture largeCurrentMap;
            public Texture ripplesCurrentMap;

            // Material data
            public Material waterMaterial;
            public MaterialPropertyBlock mpb;

            // Constant buffer
            public GraphicsBuffer perSurfaceCB;
            public ShaderVariablesWaterPerCamera[] sharedPerCameraDataArray;

            // Matrices
            public Matrix4x4 worldToWaterMatrixCustom;
        }

        void EvaluateWaterRenderingData(WaterSurface currentWater, out bool instancedQuads, out bool infinite, out bool customMesh, out List<MeshRenderer> meshRenderers)
        {
            instancedQuads = currentWater.IsInstancedQuads();
            if (instancedQuads)
            {
                // See if the surface is infinite or clamped
                infinite = currentWater.IsInfinite();

                // We're not using custom meshes in this case
                customMesh = false;
                meshRenderers = null;
            }
            else
            {
                infinite = false;
                if (currentWater.geometryType == WaterGeometryType.Quad || currentWater.meshRenderers.Count == 0)
                {
                    // We're not using custom meshes in this case
                    customMesh = false;
                    meshRenderers = null;
                }
                else
                {
                    customMesh = true;
                    meshRenderers = currentWater.meshRenderers;
                }
            }
        }

        void PrepareSurfaceGBufferData(HDCamera hdCamera, WaterRendering settings, WaterSurface currentWater, int surfaceIndex, ref WaterSurfaceGBufferData parameters)
        {
            parameters.surfaceIndex = surfaceIndex;
            parameters.foam = m_ActiveWaterFoam && currentWater.foam;
            parameters.deformation = hdCamera.frameSettings.IsEnabled(FrameSettingsField.WaterDeformation) && currentWater.deformation;
            parameters.evaluateCameraPosition = surfaceIndex == m_UnderWaterSurfaceIndex;

            // Import all the textures into the system
            parameters.displacementTexture = currentWater.simulation.gpuBuffers.displacementBuffer;
            parameters.deformationBuffer = parameters.deformation ? currentWater.deformationBuffer : Texture2D.blackTexture;

            // Geometry parameters
            parameters.drawInfiniteMesh = currentWater.simulation.rendering.maxFadeDistance != float.MaxValue;
            parameters.tessellation = currentWater.tessellation;
            parameters.numActiveBands = currentWater.simulation.numActiveBands;

            // Evaluate which mesh shall be used, etc
            EvaluateWaterRenderingData(currentWater, out parameters.instancedQuads, out parameters.infinite, out parameters.customMesh, out parameters.meshRenderers);

            // At the moment indirect buffer for instanced mesh draw with tessellation does not work on metal
            if (parameters.instancedQuads && SystemInfo.graphicsDeviceType == GraphicsDeviceType.Metal)
                parameters.tessellation = false;

            // Water mask
            parameters.waterMask = currentWater.waterMask != null ? currentWater.waterMask : Texture2D.whiteTexture;

            // Current
            bool supportLarge = currentWater.surfaceType != WaterSurfaceType.Pool;
            bool supportRipple = currentWater.ripples || !supportLarge;
            parameters.activeCurrent = (supportRipple ? currentWater.ripplesCurrentMap != null : false) || (supportLarge ?  currentWater.largeCurrentMap != null : false);
            parameters.largeCurrentMap = currentWater.largeCurrentMap != null ? currentWater.largeCurrentMap : Texture2D.blackTexture;
            parameters.ripplesCurrentMap = currentWater.ripplesCurrentMap != null ? currentWater.ripplesCurrentMap : Texture2D.blackTexture;

            // Water material
            parameters.waterMaterial = currentWater.customMaterial != null ? currentWater.customMaterial : m_InternalWaterMaterial;

            // Property block used for binding the textures
            bool supportDeformation = hdCamera.frameSettings.IsEnabled(FrameSettingsField.WaterDeformation);
            currentWater.FillMaterialPropertyBlock(m_ActiveWaterFoam, supportDeformation);
            parameters.mpb = currentWater.mpb;

            // Setup the constant buffers
            parameters.perSurfaceCB = currentWater.constantBuffer;
            parameters.sharedPerCameraDataArray = m_ShaderVariablesPerCameraArray;
            UpdataPerCameraConstantBuffer(currentWater, hdCamera, settings,
                parameters.instancedQuads, parameters.infinite, parameters.customMesh,
                ref m_ShaderVariablesPerCameraArray[surfaceIndex]);

            parameters.worldToWaterMatrixCustom = currentWater.simulation.rendering.worldToWaterMatrixCustom;
        }

        class WaterRenderingData
        {
            public GraphicsBuffer patchDataBuffer;
            public GraphicsBuffer indirectBuffer;
            public GraphicsBuffer frustumBuffer;
            public GraphicsBuffer heightBuffer;

            public Texture2D surfaceFoamTexture;
            public Texture2D sectorDataBuffer;

            public int numSurfaces;
            public WaterSurfaceGBufferData[] surfaces;
            public GraphicsBuffer surfaceProfiles;

            // Meshes
            public Mesh tessellableMesh, ringMesh, ringMeshLow;

            // Shaders
            public ComputeShader waterSimulation;
            public int patchEvaluation, patchEvaluationInfinite;
            public ComputeShader evaluationCS;
            public int findVerticalDisplKernel;

            // Camera parameters
            public int viewCount;
            public bool exclusion;
            public GraphicsBuffer perCameraCB;

            public void BindGlobal(CommandBuffer cmd)
            {
                cmd.SetGlobalBuffer(HDShaderIDs._WaterPatchData, patchDataBuffer);
                cmd.SetGlobalBuffer(HDShaderIDs._WaterSurfaceProfiles, surfaceProfiles);
                cmd.SetGlobalBuffer(HDShaderIDs._FrustumGPUBuffer, frustumBuffer);

                cmd.SetGlobalTexture(HDShaderIDs._FoamTexture, surfaceFoamTexture);
                cmd.SetGlobalTexture(HDShaderIDs._WaterSectorData, sectorDataBuffer);
            }
        }

        void PrepareWaterRenderingData(WaterRenderingData passData, HDCamera hdCamera)
        {
            PropagateFrustumDataToGPU(hdCamera);
            CheckWaterCurrentData();

            passData.patchDataBuffer = m_WaterPatchDataBuffer;
            passData.indirectBuffer = m_WaterIndirectDispatchBuffer;
            passData.frustumBuffer = m_WaterCameraFrustrumBuffer;
            passData.heightBuffer = m_WaterCameraHeightBuffer;

            passData.surfaceFoamTexture = runtimeTextures.foamMask;
            passData.sectorDataBuffer = m_WaterSectorData;

            passData.numSurfaces = Mathf.Min(WaterSurface.instanceCount, k_MaxNumWaterSurfaceProfiles);
            passData.surfaces = m_WaterGBufferDataArray;
            passData.surfaceProfiles = m_WaterProfileArrayGPU;

            // Meshes
            passData.tessellableMesh = m_GridMesh;
            passData.ringMesh = m_RingMesh;
            passData.ringMeshLow = m_RingMeshLow;

            // Patch evaluation parameters
            passData.waterSimulation = m_WaterSimulationCS;
            passData.patchEvaluation = m_EvaluateInstanceDataKernel;
            passData.patchEvaluationInfinite = m_EvaluateInstanceDataInfiniteKernel;

            // Underwater
            passData.evaluationCS = m_WaterEvaluationCS;
            passData.findVerticalDisplKernel = m_FindVerticalDisplacementsKernel;

            // Camera data
            passData.viewCount = hdCamera.viewCount;
            passData.exclusion = hdCamera.frameSettings.IsEnabled(FrameSettingsField.WaterExclusion);
            passData.perCameraCB = m_ShaderVariablesWaterPerCamera;
        }

        class WaterGBufferData : WaterRenderingData
        {
            // Buffers
            public bool decalsEnabled;
            public BufferHandle layeredOffsetsBuffer;
            public BufferHandle logBaseBuffer;

            public TextureHandle normalBuffer;
            public TextureHandle depthPyramid;
        }

        void PrepareWaterGBufferData(RenderGraphBuilder builder, HDCamera hdCamera, TextureHandle normalBuffer, TextureHandle depthPyramid,
            in BuildGPULightListOutput lightLists, ref WaterGBuffer gbuffer, WaterGBufferData passData)
        {
            WaterRendering settings = hdCamera.volumeStack.GetComponent<WaterRendering>();
            PrepareWaterRenderingData(passData, hdCamera);

            // Buffers
            passData.decalsEnabled = (hdCamera.frameSettings.IsEnabled(FrameSettingsField.Decals)) && (DecalSystem.m_DecalDatasCount > 0);
            passData.layeredOffsetsBuffer = builder.ReadBuffer(lightLists.perVoxelOffset);
            passData.logBaseBuffer = builder.ReadBuffer(lightLists.perTileLogBaseTweak);

            passData.normalBuffer = builder.ReadTexture(normalBuffer);
            passData.depthPyramid = builder.ReadTexture(depthPyramid);

            builder.WriteBuffer(gbuffer.cameraHeight);

            // Grab all the water surfaces in the scene
            var waterSurfaces = WaterSurface.instancesAsArray;
            for (int surfaceIdx = 0; surfaceIdx < passData.numSurfaces; ++surfaceIdx)
            {
                // Grab the current water surface
                WaterSurface currentWater = waterSurfaces[surfaceIdx];
                ref var surfaceData = ref passData.surfaces[surfaceIdx];

                surfaceData.render = ShouldRenderSurface(hdCamera, currentWater, ref gbuffer.debugRequired);
                if (!surfaceData.render) continue;

                // GBuffer is valid as long as one surface is rendered
                gbuffer.valid = true;

                // Fill the water surface profile
                FillWaterSurfaceProfile(hdCamera, currentWater, surfaceIdx);

                // Prepare all the internal parameters
                PrepareSurfaceGBufferData(hdCamera, settings, currentWater, surfaceIdx, ref surfaceData);
            }

            // Push the water profiles to the GPU for the deferred lighting pass
            m_WaterProfileArrayGPU.SetData(m_WaterSurfaceProfileArray);
        }

        void PropagateFrustumDataToGPU(HDCamera hdCamera)
        {
            // Plane 0
            FrustumGPU frustum;
            frustum.normal0 = hdCamera.frustum.planes[0].normal;
            frustum.dist0 = hdCamera.frustum.planes[0].distance;

            // Plane 1
            frustum.normal1 = hdCamera.frustum.planes[1].normal;
            frustum.dist1 = hdCamera.frustum.planes[1].distance;

            // Plane 2
            frustum.normal2 = hdCamera.frustum.planes[2].normal;
            frustum.dist2 = hdCamera.frustum.planes[2].distance;

            // Plane 3
            frustum.normal3 = hdCamera.frustum.planes[3].normal;
            frustum.dist3 = hdCamera.frustum.planes[3].distance;

            // Plane 4
            frustum.normal4 = hdCamera.frustum.planes[4].normal;
            frustum.dist4 = hdCamera.frustum.planes[4].distance;

            // Plane 4
            frustum.normal5 = hdCamera.frustum.planes[5].normal;
            frustum.dist5 = hdCamera.frustum.planes[5].distance;

            // Corners
            frustum.corner0 = hdCamera.frustum.corners[0];
            frustum.corner1 = hdCamera.frustum.corners[1];
            frustum.corner2 = hdCamera.frustum.corners[2];
            frustum.corner3 = hdCamera.frustum.corners[3];
            frustum.corner4 = hdCamera.frustum.corners[4];
            frustum.corner5 = hdCamera.frustum.corners[5];
            frustum.corner6 = hdCamera.frustum.corners[6];
            frustum.corner7 = hdCamera.frustum.corners[7];

            // Copy the data to the GPU
            m_WaterCameraFrustumCPU[0] = frustum;
            m_WaterCameraFrustrumBuffer.SetData(m_WaterCameraFrustumCPU);
        }

        bool ShouldRenderWater(HDCamera hdCamera)
        {
            WaterRendering settings = hdCamera.volumeStack.GetComponent<WaterRendering>();
            return !(!settings.enable.value
                || !hdCamera.frameSettings.IsEnabled(FrameSettingsField.Water)
                || WaterSurface.instanceCount == 0);
        }

        bool ShouldRenderSurface(HDCamera hdCamera, WaterSurface currentWater, ref bool debugRequired)
        {
            // At least one surface will need to be rendered as a debug view.
            if (currentWater.debugMode != WaterDebugMode.None)
            {
                debugRequired = true;
                return false;
            }

            // Only render the water surface if it is included in the layers that the camera requires
            int waterCullingMask = 1 << currentWater.gameObject.layer;
            if (hdCamera.camera.cullingMask != 0 && (waterCullingMask & hdCamera.camera.cullingMask) == 0)
                return false;

#if UNITY_EDITOR
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (!CoreUtils.IsSceneViewPrefabStageContextHidden() && stage != null && stage.mode == PrefabStage.Mode.InContext)
            {
                bool isInPrefabScene = stage.scene == currentWater.gameObject.scene;
                if ((hdCamera.camera.sceneViewFilterMode == Camera.SceneViewFilterMode.Off && isInPrefabScene)
                    || (hdCamera.camera.sceneViewFilterMode == Camera.SceneViewFilterMode.ShowFiltered && !isInPrefabScene))
                    return false;
            }

            if (currentWater.customMaterial != null && !WaterSurface.IsWaterMaterial(currentWater.customMaterial))
                return false;
#endif

            return true;
        }

        static void RenderWaterSurface(CommandBuffer cmd, WaterRenderingData parameters, ref WaterSurfaceGBufferData surfaceData)
        {
            cmd.SetBufferData(parameters.perCameraCB, surfaceData.sharedPerCameraDataArray, surfaceData.surfaceIndex, 0, 1);

            // Raise the keywords for band count
            SetupWaterShaderKeyword(cmd, surfaceData.numActiveBands, surfaceData.activeCurrent);

            // First we need to evaluate if we are in the underwater region of this water surface if the camera
            // is above of under water. This will need to be done on the CPU later
            if (surfaceData.evaluateCameraPosition)
            {
                BindPerSurfaceConstantBuffer(cmd, parameters.evaluationCS, surfaceData.perSurfaceCB);

                cmd.SetComputeConstantBufferParam(parameters.evaluationCS, HDShaderIDs._ShaderVariablesWaterPerCamera, parameters.perCameraCB, 0, parameters.perCameraCB.stride);
                cmd.SetComputeBufferParam(parameters.evaluationCS, parameters.findVerticalDisplKernel, HDShaderIDs._WaterCameraHeightBufferRW, parameters.heightBuffer);
                cmd.SetComputeTextureParam(parameters.evaluationCS, parameters.findVerticalDisplKernel, HDShaderIDs._WaterDisplacementBuffer, surfaceData.displacementTexture);
                cmd.SetComputeTextureParam(parameters.evaluationCS, parameters.findVerticalDisplKernel, HDShaderIDs._WaterDeformationBuffer, surfaceData.deformationBuffer);
                cmd.SetComputeTextureParam(parameters.evaluationCS, parameters.findVerticalDisplKernel, HDShaderIDs._WaterMask, surfaceData.waterMask);
                if (surfaceData.activeCurrent)
                {
                    cmd.SetComputeTextureParam(parameters.evaluationCS, parameters.findVerticalDisplKernel, HDShaderIDs._WaterSectorData, parameters.sectorDataBuffer);
                    cmd.SetComputeTextureParam(parameters.evaluationCS, parameters.findVerticalDisplKernel, HDShaderIDs._Group0CurrentMap, surfaceData.largeCurrentMap);
                    cmd.SetComputeTextureParam(parameters.evaluationCS, parameters.findVerticalDisplKernel, HDShaderIDs._Group1CurrentMap, surfaceData.ripplesCurrentMap);
                }

                cmd.DispatchCompute(parameters.evaluationCS, parameters.findVerticalDisplKernel, 1, 1, parameters.viewCount);
            }

            // Raise the right stencil flags
            cmd.SetGlobalFloat(HDShaderIDs._StencilWaterRefGBuffer, (int)(StencilUsage.WaterSurface | StencilUsage.TraceReflectionRay));
            cmd.SetGlobalFloat(HDShaderIDs._StencilWaterWriteMaskGBuffer, (int)(StencilUsage.WaterSurface | StencilUsage.TraceReflectionRay));
            cmd.SetGlobalFloat(HDShaderIDs._StencilWaterReadMaskGBuffer, parameters.exclusion ? (int)(StencilUsage.WaterExclusion) : 0);
            cmd.SetGlobalFloat(HDShaderIDs._CullWaterMask, surfaceData.evaluateCameraPosition ? (int)CullMode.Off : (int)CullMode.Back);

            var passNames = surfaceData.tessellation ? k_PassesGBufferTessellation : k_PassesGBuffer;
            DrawWaterSurface(cmd, passNames, parameters, ref surfaceData);

            // Reset the keywords
            ResetWaterShaderKeyword(cmd);
        }

        WaterGBuffer RenderWaterGBuffer(RenderGraph renderGraph, CullingResults cull, HDCamera hdCamera,
                                        TextureHandle depthBuffer, TextureHandle normalBuffer,
                                        TextureHandle colorPyramid, TextureHandle depthPyramid,
                                        in BuildGPULightListOutput lightLists)
        {
            // Tile sizes
            int tileX = (hdCamera.actualWidth + 7) / 8;
            int tileY = (hdCamera.actualHeight + 7) / 8;
            int numTiles = tileX * tileY;

            // We need to tag the stencil for water rejection
            WaterRejectionTag(renderGraph, cull, hdCamera, depthBuffer);

            // Request all the gbuffer textures we will need
            WaterGBuffer outputGBuffer = new WaterGBuffer()
            {
                valid = false,
                debugRequired = false,
                cameraHeight = renderGraph.ImportBuffer(m_WaterCameraHeightBuffer),

                waterGBuffer0 = renderGraph.CreateTexture(new TextureDesc(Vector2.one, true, true)
                { colorFormat = GraphicsFormat.B10G11R11_UFloatPack32, enableRandomWrite = true, name = "Water GBuffer 0", fallBackToBlackTexture = true }),
                waterGBuffer1 = renderGraph.CreateTexture(new TextureDesc(Vector2.one, true, true)
                { colorFormat = GraphicsFormat.R8G8B8A8_UNorm, enableRandomWrite = true, name = "Water GBuffer 1", fallBackToBlackTexture = true }),
                waterGBuffer2 = renderGraph.CreateTexture(new TextureDesc(Vector2.one, true, true)
                { colorFormat = GraphicsFormat.R8G8B8A8_UNorm, enableRandomWrite = true, name = "Water GBuffer 2", fallBackToBlackTexture = true }),
                waterGBuffer3 = renderGraph.CreateTexture(new TextureDesc(Vector2.one, true, true)
                { colorFormat = GraphicsFormat.R8G8B8A8_UNorm, enableRandomWrite = true, name = "Water GBuffer 3", fallBackToBlackTexture = true }),

                indirectBuffer = renderGraph.CreateBuffer(new BufferDesc((WaterConsts.k_NumWaterVariants + 1) * 3, sizeof(uint), GraphicsBuffer.Target.IndirectArguments) { name = "Water Deferred Indirect" }),
                tileBuffer = renderGraph.CreateBuffer(new BufferDesc((WaterConsts.k_NumWaterVariants + 1) * numTiles * m_MaxViewCount, sizeof(uint)) { name = "Water Deferred Tiles" })
            };

            using (var builder = renderGraph.AddRenderPass<WaterGBufferData>("Render Water GBuffer", out var passData, ProfilingSampler.Get(HDProfileId.WaterGBuffer)))
            {
                // Prepare data
                PrepareWaterGBufferData(builder, hdCamera, normalBuffer, depthPyramid, in lightLists, ref outputGBuffer, passData);

                // Request the output textures
                builder.UseColorBuffer(outputGBuffer.waterGBuffer0, 0);
                builder.UseColorBuffer(outputGBuffer.waterGBuffer1, 1);
                builder.UseColorBuffer(outputGBuffer.waterGBuffer2, 2);
                builder.UseColorBuffer(outputGBuffer.waterGBuffer3, 3);
                builder.UseDepthBuffer(depthBuffer, DepthAccess.ReadWrite);

                builder.SetRenderFunc(
                    (WaterGBufferData data, RenderGraphContext ctx) =>
                    {
                        if (data.decalsEnabled)
                            DecalSystem.instance.SetAtlas(ctx.cmd);

                        if (data.layeredOffsetsBuffer.IsValid())
                            ctx.cmd.SetGlobalBuffer(HDShaderIDs.g_vLayeredOffsetsBuffer, data.layeredOffsetsBuffer);
                        if (data.logBaseBuffer.IsValid())
                            ctx.cmd.SetGlobalBuffer(HDShaderIDs.g_logBaseBuffer, data.logBaseBuffer);

                        ctx.cmd.SetGlobalTexture(HDShaderIDs._NormalBufferTexture, data.normalBuffer);
                        ctx.cmd.SetGlobalTexture(HDShaderIDs._CameraDepthTexture, data.depthPyramid);

                        data.BindGlobal(ctx.cmd);

                        for (int surfaceIdx = 0; surfaceIdx < data.numSurfaces; ++surfaceIdx)
                        {
                            ref var surfaceData = ref data.surfaces[surfaceIdx];

                            if (surfaceData.render)
                                RenderWaterSurface(ctx.cmd, data, ref surfaceData);
                        }
                    });
            }

            if (outputGBuffer.valid)
                PrepareWaterLighting(renderGraph, hdCamera, depthBuffer, normalBuffer, lightLists, ref outputGBuffer);

            return outputGBuffer;
        }
        #endregion

        #region Prepare Lighting
        class WaterPrepareLightingData
        {
            // Camera data
            public int width;
            public int height;
            public int viewCount;
            public int tileX;
            public int tileY;
            public int numTiles;
            public bool transparentSSR;

            // Shader data
            public ComputeShader waterLighting;
            public int clearIndirectKernel;
            public int classifyTilesKernel;
            public int prepareSSRKernel;

            // Input textures
            public TextureHandle depthBuffer;
            public TextureHandle gbuffer1;
            public TextureHandle gbuffer3;
            public BufferHandle perVoxelOffset;
            public BufferHandle perTileLogBaseTweak;

            // Output texture
            public TextureHandle normalBuffer;
            public BufferHandle indirectBuffer;
            public BufferHandle tileBuffer;
        }

        void PrepareWaterLighting(RenderGraph renderGraph, HDCamera hdCamera, TextureHandle depthBuffer, TextureHandle normalBuffer, in BuildGPULightListOutput lightLists, ref WaterGBuffer gbuffer)
        {
            using (var builder = renderGraph.AddRenderPass<WaterPrepareLightingData>("Prepare water for lighting", out var passData, ProfilingSampler.Get(HDProfileId.WaterPrepareLighting)))
            {
                // Camera parameters
                passData.width = hdCamera.actualWidth;
                passData.height = hdCamera.actualHeight;
                passData.viewCount = hdCamera.viewCount;
                passData.tileX = (passData.width + 7) / 8;
                passData.tileY = (passData.height + 7) / 8;
                passData.numTiles = passData.tileX * passData.tileY;
                passData.transparentSSR = hdCamera.IsSSREnabled(true);

                // CS and kernels
                passData.waterLighting = m_WaterLightingCS;
                passData.prepareSSRKernel = m_WaterPrepareSSRIndirectKernel;
                passData.clearIndirectKernel = m_WaterClearIndirectKernel;
                passData.classifyTilesKernel = m_WaterClassifyTilesKernel;

                // Input resources
                passData.gbuffer1 = builder.ReadTexture(gbuffer.waterGBuffer1);
                passData.gbuffer3 = builder.ReadTexture(gbuffer.waterGBuffer3);
                passData.depthBuffer = builder.UseDepthBuffer(depthBuffer, DepthAccess.Read);
                passData.perVoxelOffset = builder.ReadBuffer(lightLists.perVoxelOffset);
                passData.perTileLogBaseTweak = builder.ReadBuffer(lightLists.perTileLogBaseTweak);

                // Output resources
                passData.normalBuffer = builder.WriteTexture(normalBuffer);
                passData.indirectBuffer = builder.WriteBuffer(gbuffer.indirectBuffer);
                passData.tileBuffer = builder.WriteBuffer(gbuffer.tileBuffer);

                builder.SetRenderFunc(
                    (WaterPrepareLightingData data, RenderGraphContext ctx) =>
                    {
                        // Clear indirect args
                        ctx.cmd.SetComputeBufferParam(data.waterLighting, data.clearIndirectKernel, HDShaderIDs._WaterDispatchIndirectBuffer, data.indirectBuffer);
                        ctx.cmd.DispatchCompute(data.waterLighting, data.clearIndirectKernel, 1, 1, 1);

                        // Bind the input gbuffer data
                        int kernel = data.classifyTilesKernel;
                        ctx.cmd.SetComputeIntParam(data.waterLighting, HDShaderIDs._WaterNumTiles, data.numTiles);
                        ctx.cmd.SetComputeBufferParam(data.waterLighting, kernel, HDShaderIDs._WaterDispatchIndirectBuffer, data.indirectBuffer);
                        ctx.cmd.SetComputeBufferParam(data.waterLighting, kernel, HDShaderIDs._WaterTileBufferRW, data.tileBuffer);
                        ctx.cmd.SetComputeTextureParam(data.waterLighting, kernel, HDShaderIDs._DepthTexture, data.depthBuffer);
                        ctx.cmd.SetComputeTextureParam(data.waterLighting, kernel, HDShaderIDs._StencilTexture, data.depthBuffer, 0, RenderTextureSubElement.Stencil);
                        ctx.cmd.SetComputeBufferParam(data.waterLighting, kernel, HDShaderIDs.g_vLayeredOffsetsBuffer, data.perVoxelOffset);
                        ctx.cmd.SetComputeBufferParam(data.waterLighting, kernel, HDShaderIDs.g_logBaseBuffer, data.perTileLogBaseTweak);
                        ctx.cmd.DispatchCompute(data.waterLighting, kernel, data.tileX, data.tileY, data.viewCount);

                        if (data.transparentSSR)
                        {
                            // Prepare the normal buffer for SSR
                            ctx.cmd.SetComputeTextureParam(data.waterLighting, data.prepareSSRKernel, HDShaderIDs._WaterGBufferTexture1, data.gbuffer1);
                            ctx.cmd.SetComputeTextureParam(data.waterLighting, data.prepareSSRKernel, HDShaderIDs._WaterGBufferTexture3, data.gbuffer3);
                            ctx.cmd.SetComputeTextureParam(data.waterLighting, data.prepareSSRKernel, HDShaderIDs._StencilTexture, data.depthBuffer, 0, RenderTextureSubElement.Stencil);
                            ctx.cmd.SetComputeTextureParam(data.waterLighting, data.prepareSSRKernel, HDShaderIDs._NormalBufferRW, data.normalBuffer);
                            ctx.cmd.SetComputeBufferParam(data.waterLighting, data.prepareSSRKernel, HDShaderIDs._WaterTileBuffer, data.tileBuffer);
                            ctx.cmd.DispatchCompute(data.waterLighting, data.prepareSSRKernel, data.indirectBuffer, (uint)WaterConsts.k_NumWaterVariants * 3 * sizeof(uint));
                        }
                    });
            }
        }
        #endregion

        #region Deferred Lighting
        struct WaterRenderingDeferredParameters
        {
            // Deferred Lighting
            public ComputeShader waterLighting;
            public int[] indirectLightingKernels;
            public int lightingKernel;
            public int numVariants;
            public int waterFogKernel;
        }

        WaterRenderingDeferredParameters PrepareWaterRenderingDeferredParameters(HDCamera hdCamera)
        {
            WaterRenderingDeferredParameters parameters = new WaterRenderingDeferredParameters();

            // Deferred Lighting
            parameters.waterLighting = m_WaterLightingCS;
            parameters.indirectLightingKernels = m_WaterIndirectDeferredKernels;
            parameters.lightingKernel = m_WaterDeferredKernel;
            parameters.numVariants = WaterConsts.k_NumWaterVariants;
            parameters.waterFogKernel = m_WaterFogIndirectKernel;

            return parameters;
        }

        class WaterRenderingDeferredData
        {
            // All the parameters required to simulate and render the water
            public WaterRenderingDeferredParameters parameters;
            public bool pbrSkyActive;

            // GBuffer Data
            public BufferHandle indirectBuffer;
            public BufferHandle tileBuffer;
            public TextureHandle gbuffer0;
            public TextureHandle gbuffer1;
            public TextureHandle gbuffer2;
            public TextureHandle gbuffer3;
            public TextureHandle depthBuffer;
            public TextureHandle depthPyramid;

            // Lighting textures/buffers
            public TextureHandle volumetricLightingTexture;
            public TextureHandle transparentSSRLighting;
            public BufferHandle perVoxelOffset;
            public BufferHandle perTileLogBaseTweak;
            public BufferHandle cameraHeightBuffer;
            public BufferHandle waterLine;

            // Temporary buffers
            public TextureHandle waterLightingBuffer;

            // Water rendered to this buffer
            public TextureHandle colorBuffer;
        }

        void RenderWaterLighting(RenderGraph renderGraph, HDCamera hdCamera,
            TextureHandle colorBuffer, TextureHandle depthBuffer, TextureHandle depthPyramid,
            TextureHandle volumetricLightingTexture, TextureHandle ssrLighting,
            in TransparentPrepassOutput prepassOutput, in BuildGPULightListOutput lightLists)
        {
            // We do not render the deferred lighting if:
            // - Water rendering is disabled.
            // - The water gbuffer was never written.
            if (!ShouldRenderWater(hdCamera) || !prepassOutput.waterGBuffer.valid)
                return;

            // Execute the unique lighting pass
            using (var builder = renderGraph.AddRenderPass<WaterRenderingDeferredData>("Water Deferred Lighting", out var passData, ProfilingSampler.Get(HDProfileId.WaterDeferredLighting)))
            {
                // Prepare all the internal parameters
                passData.parameters = PrepareWaterRenderingDeferredParameters(hdCamera);
                passData.pbrSkyActive = hdCamera.volumeStack.GetComponent<VisualEnvironment>().skyType.value == (int)SkyType.PhysicallyBased;

                // GBuffer data
                passData.indirectBuffer = builder.ReadBuffer(prepassOutput.waterGBuffer.indirectBuffer);
                passData.tileBuffer = builder.ReadBuffer(prepassOutput.waterGBuffer.tileBuffer);
                passData.gbuffer0 = builder.ReadTexture(prepassOutput.waterGBuffer.waterGBuffer0);
                passData.gbuffer1 = builder.ReadTexture(prepassOutput.waterGBuffer.waterGBuffer1);
                passData.gbuffer2 = builder.ReadTexture(prepassOutput.waterGBuffer.waterGBuffer2);
                passData.gbuffer3 = builder.ReadTexture(prepassOutput.waterGBuffer.waterGBuffer3);

                passData.depthBuffer = builder.UseDepthBuffer(depthBuffer, DepthAccess.Read);
                passData.depthPyramid = builder.ReadTexture(depthPyramid);
                passData.volumetricLightingTexture = builder.ReadTexture(volumetricLightingTexture);
                passData.transparentSSRLighting = builder.ReadTexture(ssrLighting);
                passData.perVoxelOffset = builder.ReadBuffer(lightLists.perVoxelOffset);
                passData.perTileLogBaseTweak = builder.ReadBuffer(lightLists.perTileLogBaseTweak);
                passData.cameraHeightBuffer = builder.ReadBuffer(prepassOutput.waterGBuffer.cameraHeight);
                passData.waterLine = builder.ReadBuffer(prepassOutput.waterLine);

                // Request the output textures
                passData.waterLightingBuffer = builder.CreateTransientTexture(colorBuffer);
                passData.colorBuffer = builder.WriteTexture(colorBuffer);

                // Run the deferred lighting
                builder.SetRenderFunc(
                    (WaterRenderingDeferredData data, RenderGraphContext ctx) =>
                    {
                        for (int variantIdx = 0; variantIdx < data.parameters.numVariants; ++variantIdx)
                        {
                            // Kernel to be used for the target variant
                            int kernel = data.parameters.indirectLightingKernels[variantIdx];

                            // Bind the input gbuffer data
                            ctx.cmd.SetComputeBufferParam(data.parameters.waterLighting, kernel, HDShaderIDs._WaterTileBuffer, data.tileBuffer);
                            ctx.cmd.SetComputeTextureParam(data.parameters.waterLighting, kernel, HDShaderIDs._WaterGBufferTexture0, data.gbuffer0);
                            ctx.cmd.SetComputeTextureParam(data.parameters.waterLighting, kernel, HDShaderIDs._WaterGBufferTexture1, data.gbuffer1);
                            ctx.cmd.SetComputeTextureParam(data.parameters.waterLighting, kernel, HDShaderIDs._WaterGBufferTexture2, data.gbuffer2);
                            ctx.cmd.SetComputeTextureParam(data.parameters.waterLighting, kernel, HDShaderIDs._WaterGBufferTexture3, data.gbuffer3);
                            ctx.cmd.SetComputeTextureParam(data.parameters.waterLighting, kernel, HDShaderIDs._DepthTexture, data.depthBuffer);
                            ctx.cmd.SetComputeTextureParam(data.parameters.waterLighting, kernel, HDShaderIDs._StencilTexture, data.depthBuffer, 0, RenderTextureSubElement.Stencil);
                            ctx.cmd.SetComputeTextureParam(data.parameters.waterLighting, kernel, HDShaderIDs._SsrLightingTexture, data.transparentSSRLighting);
                            ctx.cmd.SetComputeBufferParam(data.parameters.waterLighting, kernel, HDShaderIDs.g_vLayeredOffsetsBuffer, data.perVoxelOffset);
                            ctx.cmd.SetComputeBufferParam(data.parameters.waterLighting, kernel, HDShaderIDs.g_logBaseBuffer, data.perTileLogBaseTweak);
                            ctx.cmd.SetComputeTextureParam(data.parameters.waterLighting, kernel, HDShaderIDs._CameraDepthTexture, data.depthPyramid);
                            ctx.cmd.SetComputeTextureParam(data.parameters.waterLighting, kernel, HDShaderIDs._ColorPyramidTexture, data.colorBuffer); // caution, this is not a pyramid, we can't access LODs

                            // Bind the output texture
                            ctx.cmd.SetComputeTextureParam(data.parameters.waterLighting, kernel, HDShaderIDs._CameraColorTextureRW, data.waterLightingBuffer);

                            // Run the lighting
                            ctx.cmd.DispatchCompute(data.parameters.waterLighting, kernel, data.indirectBuffer, (uint)variantIdx * 3 * sizeof(uint));
                        }

                        if (!data.pbrSkyActive)
                            PhysicallyBasedSkyRenderer.SetDefaultGlobalSkyData(ctx.cmd);

                        // Evaluate the fog
                        int fogKernel = data.parameters.waterFogKernel;
                        ctx.cmd.SetComputeBufferParam(data.parameters.waterLighting, fogKernel, HDShaderIDs._WaterLineBuffer, data.waterLine);
                        ctx.cmd.SetComputeBufferParam(data.parameters.waterLighting, fogKernel, HDShaderIDs._WaterTileBuffer, data.tileBuffer);
                        ctx.cmd.SetComputeBufferParam(data.parameters.waterLighting, fogKernel, HDShaderIDs._WaterCameraHeightBuffer, data.cameraHeightBuffer);
                        ctx.cmd.SetComputeTextureParam(data.parameters.waterLighting, fogKernel, HDShaderIDs._WaterGBufferTexture3, data.gbuffer3);
                        ctx.cmd.SetComputeTextureParam(data.parameters.waterLighting, fogKernel, HDShaderIDs._VBufferLighting, data.volumetricLightingTexture);
                        ctx.cmd.SetComputeTextureParam(data.parameters.waterLighting, fogKernel, HDShaderIDs._DepthTexture, data.depthBuffer);
                        ctx.cmd.SetComputeTextureParam(data.parameters.waterLighting, fogKernel, HDShaderIDs._StencilTexture, data.depthBuffer, 0, RenderTextureSubElement.Stencil);
                        ctx.cmd.SetComputeTextureParam(data.parameters.waterLighting, fogKernel, HDShaderIDs._CameraColorTexture, data.waterLightingBuffer);
                        ctx.cmd.SetComputeTextureParam(data.parameters.waterLighting, fogKernel, HDShaderIDs._CameraColorTextureRW, data.colorBuffer);
                        ctx.cmd.DispatchCompute(data.parameters.waterLighting, fogKernel, data.indirectBuffer, (uint)WaterConsts.k_NumWaterVariants * 3 * sizeof(uint));
                    });
            }
        }
        #endregion

        #region Exclusion
        class WaterExclusionPassData
        {
            public FrameSettings frameSettings;
            public TextureHandle depthBuffer;
            public RendererListHandle opaqueRenderList;
        }

        void WaterRejectionTag(RenderGraph renderGraph, CullingResults cull, HDCamera hdCamera, TextureHandle depthBuffer)
        {
            if (!hdCamera.frameSettings.IsEnabled(FrameSettingsField.WaterExclusion))
                return;

            using (var builder = renderGraph.AddRenderPass<WaterExclusionPassData>("Water Exclusion", out var passData, ProfilingSampler.Get(HDProfileId.WaterExclusion)))
            {
                passData.frameSettings = hdCamera.frameSettings;
                passData.depthBuffer = builder.UseDepthBuffer(depthBuffer, DepthAccess.ReadWrite);
                passData.opaqueRenderList = builder.UseRendererList(renderGraph.CreateRendererList(CreateOpaqueRendererListDesc(cull, hdCamera.camera, m_WaterStencilTagNames, stateBlock: m_DepthStateNoWrite)));

                builder.SetRenderFunc(
                    (WaterExclusionPassData data, RenderGraphContext ctx) =>
                    {
                        ctx.cmd.SetGlobalInteger(HDShaderIDs._StencilWriteMaskStencilTag, (int)StencilUsage.WaterExclusion);
                        ctx.cmd.SetGlobalInteger(HDShaderIDs._StencilRefMaskStencilTag, (int)StencilUsage.WaterExclusion);
                        DrawOpaqueRendererList(ctx.renderContext, ctx.cmd, data.frameSettings, data.opaqueRenderList);
                    });
            }
        }
        #endregion
    }
}
