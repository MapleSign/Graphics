using System;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal.Internal
{
    /// <summary>
    /// Renders a shadow map for the main Light.
    /// </summary>
    public class DynamicMainLightShadowCasterPass : ScriptableRenderPass
    {
        private static class MainLightShadowConstantBuffer
        {
            public static int _WorldToShadow;
            public static int _ShadowParams;
            public static int _CascadeShadowSplitSpheres0;
            public static int _CascadeShadowSplitSpheres1;
            public static int _CascadeShadowSplitSpheres2;
            public static int _CascadeShadowSplitSpheres3;
            public static int _CascadeShadowSplitSphereRadii;
            public static int _ShadowOffset0;
            public static int _ShadowOffset1;
            public static int _ShadowmapSize;
        }

        MainLightShadowCacheSystem m_ShadowCacheSystem;

        int m_MainLightShadowmapID;
        internal RTHandle m_MainLightShadowmapTexture;
        private RTHandle m_EmptyMainLightShadowmapTexture;
        private const int k_EmptyShadowMapDimensions = 1;
        private const string k_MainLightShadowMapTextureName = "_MainLightShadowmapTexture";
        private const string k_EmptyMainLightShadowMapTextureName = "_EmptyMainLightShadowmapTexture";
        private static readonly Vector4 s_EmptyShadowParams = new Vector4(1, 0, 1, 0);
        private static readonly Vector4 s_EmptyShadowmapSize = s_EmptyShadowmapSize = new Vector4(k_EmptyShadowMapDimensions, 1f / k_EmptyShadowMapDimensions, k_EmptyShadowMapDimensions, k_EmptyShadowMapDimensions);

        bool m_CreateEmptyShadowmap;

        ShadowObjectsFilter m_ShadowObjectFilter = ShadowObjectsFilter.AllObjects;

        ProfilingSampler m_ProfilingSetupSampler = new ProfilingSampler("Setup Dynamic Main Shadowmap");

        /// <summary>
        /// Creates a new <c>MainLightShadowCasterPass</c> instance.
        /// </summary>
        /// <param name="evt">The <c>RenderPassEvent</c> to use.</param>
        /// <seealso cref="RenderPassEvent"/>
        public DynamicMainLightShadowCasterPass(RenderPassEvent evt, MainLightShadowCacheSystem shadowCacheSystem)
        {
            base.profilingSampler = new ProfilingSampler(nameof(MainLightShadowCasterPass));
            renderPassEvent = evt;

            MainLightShadowConstantBuffer._WorldToShadow = Shader.PropertyToID("_MainLightWorldToShadow");
            MainLightShadowConstantBuffer._ShadowParams = Shader.PropertyToID("_MainLightShadowParams");
            MainLightShadowConstantBuffer._CascadeShadowSplitSpheres0 = Shader.PropertyToID("_CascadeShadowSplitSpheres0");
            MainLightShadowConstantBuffer._CascadeShadowSplitSpheres1 = Shader.PropertyToID("_CascadeShadowSplitSpheres1");
            MainLightShadowConstantBuffer._CascadeShadowSplitSpheres2 = Shader.PropertyToID("_CascadeShadowSplitSpheres2");
            MainLightShadowConstantBuffer._CascadeShadowSplitSpheres3 = Shader.PropertyToID("_CascadeShadowSplitSpheres3");
            MainLightShadowConstantBuffer._CascadeShadowSplitSphereRadii = Shader.PropertyToID("_CascadeShadowSplitSphereRadii");
            MainLightShadowConstantBuffer._ShadowOffset0 = Shader.PropertyToID("_MainLightShadowOffset0");
            MainLightShadowConstantBuffer._ShadowOffset1 = Shader.PropertyToID("_MainLightShadowOffset1");
            MainLightShadowConstantBuffer._ShadowmapSize = Shader.PropertyToID("_MainLightShadowmapSize");

            m_MainLightShadowmapID = Shader.PropertyToID(k_MainLightShadowMapTextureName);
            m_ShadowCacheSystem = shadowCacheSystem;
        }

        /// <summary>
        /// Cleans up resources used by the pass.
        /// </summary>
        public void Dispose()
        {
            m_MainLightShadowmapTexture?.Release();
            m_EmptyMainLightShadowmapTexture?.Release();
        }

        /// <summary>
        /// Sets up the pass.
        /// </summary>
        /// <param name="renderingData"></param>
        /// <returns>True if the pass should be enqueued, otherwise false.</returns>
        /// <seealso cref="RenderingData"/>
        public bool Setup(ref RenderingData renderingData)
        {
            if (!renderingData.shadowData.mainLightShadowsEnabled)
                return false;

            using var profScope = new ProfilingScope(null, m_ProfilingSetupSampler);

            m_CreateEmptyShadowmap = m_ShadowCacheSystem.emptyRendering;
            if (m_CreateEmptyShadowmap)
            {
                return SetupForEmptyRendering(ref renderingData);
            }

            useNativeRenderPass = true;
            ShadowUtils.ShadowRTReAllocateIfNeeded(ref m_MainLightShadowmapTexture, m_ShadowCacheSystem.renderTargetWidth, m_ShadowCacheSystem.renderTargetHeight, m_ShadowCacheSystem.k_ShadowmapBufferBits, name: k_MainLightShadowMapTextureName);

            m_ShadowObjectFilter = renderingData.shadowData.shadowUpdateMode == ShadowUpdateMode.Dynamic ? ShadowObjectsFilter.AllObjects : ShadowObjectsFilter.DynamicOnly;

            return true;
        }

        bool SetupForEmptyRendering(ref RenderingData renderingData)
        {
            if (!renderingData.cameraData.renderer.stripShadowsOffVariants)
                return false;

            m_CreateEmptyShadowmap = true;
            useNativeRenderPass = false;
            ShadowUtils.ShadowRTReAllocateIfNeeded(ref m_EmptyMainLightShadowmapTexture, k_EmptyShadowMapDimensions, k_EmptyShadowMapDimensions, m_ShadowCacheSystem.k_ShadowmapBufferBits, name: k_EmptyMainLightShadowMapTextureName);

            return true;
        }

        /// <inheritdoc />
        public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
        {
            if (m_CreateEmptyShadowmap)
                ConfigureTarget(m_EmptyMainLightShadowmapTexture);
            else
                ConfigureTarget(m_MainLightShadowmapTexture);

            if (m_ShadowObjectFilter == ShadowObjectsFilter.AllObjects)
                ConfigureClear(ClearFlag.All, Color.black);
            else
                ConfigureClear(ClearFlag.None, Color.black);
        }

        /// <inheritdoc/>
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (m_CreateEmptyShadowmap)
            {
                SetEmptyMainLightCascadeShadowmap(ref context, ref renderingData);
                renderingData.commandBuffer.SetGlobalTexture(m_MainLightShadowmapID, m_EmptyMainLightShadowmapTexture.nameID);

                return;
            }

            RenderMainLightCascadeShadowmap(ref context, ref renderingData);
            renderingData.commandBuffer.SetGlobalTexture(m_MainLightShadowmapID, m_MainLightShadowmapTexture.nameID);
        }

        void SetEmptyMainLightCascadeShadowmap(ref ScriptableRenderContext context, ref RenderingData renderingData)
        {
            CommandBuffer cmd = renderingData.commandBuffer;
            CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.MainLightShadows, true);
            SetEmptyMainLightShadowParams(cmd);
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
        }

        internal static void SetEmptyMainLightShadowParams(CommandBuffer cmd)
        {
            cmd.SetGlobalVector(MainLightShadowConstantBuffer._ShadowParams, s_EmptyShadowParams);
            cmd.SetGlobalVector(MainLightShadowConstantBuffer._ShadowmapSize, s_EmptyShadowmapSize);
        }

        void RenderMainLightCascadeShadowmap(ref ScriptableRenderContext context, ref RenderingData renderingData)
        {
            var cullResults = renderingData.cullResults;
            var lightData = renderingData.lightData;
            var shadowData = renderingData.shadowData;

            int shadowLightIndex = lightData.mainLightIndex;
            if (shadowLightIndex == -1)
                return;

            VisibleLight shadowLight = lightData.visibleLights[shadowLightIndex];

            var cmd = renderingData.commandBuffer;
            using (new ProfilingScope(cmd, ProfilingSampler.Get(URPProfileId.MainLightShadow)))
            {
                // Need to start by setting the Camera position and worldToCamera Matrix as that is not set for passes executed before normal rendering
                ShadowUtils.SetCameraPosition(cmd, renderingData.cameraData.worldSpaceCameraPos);

                // Need set the worldToCamera Matrix as that is not set for passes executed before normal rendering,
                // otherwise shadows will behave incorrectly when Scene and Game windows are open at the same time (UUM-63267).
                ShadowUtils.SetWorldToCameraMatrix(cmd, renderingData.cameraData.GetViewMatrix());

                var settings = new ShadowDrawingSettings(cullResults, shadowLightIndex, BatchCullingProjectionType.Orthographic);
                settings.useRenderingLayerMaskTest = UniversalRenderPipeline.asset.useRenderingLayers;
                settings.objectsFilter = m_ShadowObjectFilter;

                if (renderingData.shadowData.shadowUpdateMode != ShadowUpdateMode.Cached)
                {
                    for (int cascadeIndex = 0; cascadeIndex < m_ShadowCacheSystem.cascadesCount; ++cascadeIndex)
                    {
                        settings.splitData = m_ShadowCacheSystem.cascadeSlices[cascadeIndex].splitData;

                        Vector4 shadowBias = ShadowUtils.GetShadowBias(ref shadowLight, shadowLightIndex, ref renderingData.shadowData,
                            m_ShadowCacheSystem.cascadeSlices[cascadeIndex].projectionMatrix, m_ShadowCacheSystem.cascadeSlices[cascadeIndex].resolution);
                        ShadowUtils.SetupShadowCasterConstantBuffer(cmd, ref shadowLight, shadowBias);
                        CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.CastingPunctualLightShadow, false);
                        ShadowUtils.RenderShadowSlice(cmd, ref context, ref m_ShadowCacheSystem.cascadeSlices[cascadeIndex],
                            ref settings, m_ShadowCacheSystem.cascadeSlices[cascadeIndex].projectionMatrix, m_ShadowCacheSystem.cascadeSlices[cascadeIndex].viewMatrix);
                    }
                }

                renderingData.shadowData.isKeywordSoftShadowsEnabled = shadowLight.light.shadows == LightShadows.Soft && renderingData.shadowData.supportsSoftShadows;
                CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.MainLightShadows, renderingData.shadowData.mainLightShadowCascadesCount == 1);
                CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.MainLightShadowCascades, renderingData.shadowData.mainLightShadowCascadesCount > 1);
                ShadowUtils.SetSoftShadowQualityShaderKeywords(cmd, ref renderingData.shadowData);

                SetupMainLightShadowReceiverConstants(cmd, ref shadowLight, ref renderingData.shadowData);
            }
        }

        void SetupMainLightShadowReceiverConstants(CommandBuffer cmd, ref VisibleLight shadowLight, ref ShadowData shadowData)
        {
            Light light = shadowLight.light;
            bool softShadows = shadowLight.light.shadows == LightShadows.Soft && shadowData.supportsSoftShadows;

            int cascadeCount = m_ShadowCacheSystem.cascadesCount;
            for (int i = 0; i < cascadeCount; ++i)
                m_ShadowCacheSystem.shadowMatrices[i] = m_ShadowCacheSystem.cascadeSlices[i].shadowTransform;

            // We setup and additional a no-op WorldToShadow matrix in the last index
            // because the ComputeCascadeIndex function in Shadows.hlsl can return an index
            // out of bounds. (position not inside any cascade) and we want to avoid branching
            Matrix4x4 noOpShadowMatrix = Matrix4x4.zero;
            noOpShadowMatrix.m22 = (SystemInfo.usesReversedZBuffer) ? 1.0f : 0.0f;
            for (int i = cascadeCount; i <= m_ShadowCacheSystem.k_MaxCascades; ++i)
                m_ShadowCacheSystem.shadowMatrices[i] = noOpShadowMatrix;

            float invShadowAtlasWidth = 1.0f / m_ShadowCacheSystem.renderTargetWidth;
            float invShadowAtlasHeight = 1.0f / m_ShadowCacheSystem.renderTargetHeight;
            float invHalfShadowAtlasWidth = 0.5f * invShadowAtlasWidth;
            float invHalfShadowAtlasHeight = 0.5f * invShadowAtlasHeight;
            float softShadowsProp = ShadowUtils.SoftShadowQualityToShaderProperty(light, softShadows);

            ShadowUtils.GetScaleAndBiasForLinearDistanceFade(m_ShadowCacheSystem.maxShadowDistanceSq, m_ShadowCacheSystem.cascadeBorder, out float shadowFadeScale, out float shadowFadeBias);

            cmd.SetGlobalMatrixArray(MainLightShadowConstantBuffer._WorldToShadow, m_ShadowCacheSystem.shadowMatrices);
            cmd.SetGlobalVector(MainLightShadowConstantBuffer._ShadowParams,
                new Vector4(light.shadowStrength, softShadowsProp, shadowFadeScale, shadowFadeBias));

            if (m_ShadowCacheSystem.cascadesCount > 1)
            {
                var cascadeSplitDistances = m_ShadowCacheSystem.cascadeSplitDistances;
                cmd.SetGlobalVector(MainLightShadowConstantBuffer._CascadeShadowSplitSpheres0,
                    cascadeSplitDistances[0]);
                cmd.SetGlobalVector(MainLightShadowConstantBuffer._CascadeShadowSplitSpheres1,
                    cascadeSplitDistances[1]);
                cmd.SetGlobalVector(MainLightShadowConstantBuffer._CascadeShadowSplitSpheres2,
                    cascadeSplitDistances[2]);
                cmd.SetGlobalVector(MainLightShadowConstantBuffer._CascadeShadowSplitSpheres3,
                    cascadeSplitDistances[3]);
                cmd.SetGlobalVector(MainLightShadowConstantBuffer._CascadeShadowSplitSphereRadii, new Vector4(
                    cascadeSplitDistances[0].w * cascadeSplitDistances[0].w,
                    cascadeSplitDistances[1].w * cascadeSplitDistances[1].w,
                    cascadeSplitDistances[2].w * cascadeSplitDistances[2].w,
                    cascadeSplitDistances[3].w * cascadeSplitDistances[3].w));
            }

            // Inside shader soft shadows are controlled through global keyword.
            // If any additional light has soft shadows it will force soft shadows on main light too.
            // As it is not trivial finding out which additional light has soft shadows, we will pass main light properties if soft shadows are supported.
            // This workaround will be removed once we will support soft shadows per light.
            if (shadowData.supportsSoftShadows)
            {
                cmd.SetGlobalVector(MainLightShadowConstantBuffer._ShadowOffset0,
                    new Vector4(-invHalfShadowAtlasWidth, -invHalfShadowAtlasHeight,
                        invHalfShadowAtlasWidth, -invHalfShadowAtlasHeight));
                cmd.SetGlobalVector(MainLightShadowConstantBuffer._ShadowOffset1,
                    new Vector4(-invHalfShadowAtlasWidth, invHalfShadowAtlasHeight,
                        invHalfShadowAtlasWidth, invHalfShadowAtlasHeight));

                cmd.SetGlobalVector(MainLightShadowConstantBuffer._ShadowmapSize, new Vector4(invShadowAtlasWidth,
                    invShadowAtlasHeight,
                    m_ShadowCacheSystem.renderTargetWidth, m_ShadowCacheSystem.renderTargetHeight));
            }
        }
        private class PassData
        {
            internal DynamicMainLightShadowCasterPass pass;
            internal RenderGraph graph;

            internal TextureHandle shadowmapTexture;
            internal RenderingData renderingData;
            internal int shadowmapID;

            internal bool emptyShadowmap;
        }

        internal TextureHandle Render(RenderGraph graph, ref RenderingData renderingData)
        {
            TextureHandle shadowTexture;

            using (var builder = graph.AddRenderPass<PassData>("Main Light Shadowmap", out var passData, base.profilingSampler))
            {
                InitPassData(ref passData, ref renderingData, ref graph);

                if (!m_CreateEmptyShadowmap)
                {
                    passData.shadowmapTexture = UniversalRenderer.CreateRenderGraphTexture(graph, m_MainLightShadowmapTexture.rt.descriptor, "Main Shadowmap", true, ShadowUtils.m_ForceShadowPointSampling ? FilterMode.Point : FilterMode.Bilinear);
                    builder.UseDepthBuffer(passData.shadowmapTexture, DepthAccess.Write);
                }

                // Need this as shadowmap is only used as Global Texture and not a buffer, so would get culled by RG
                builder.AllowPassCulling(false);

                builder.SetRenderFunc((PassData data, RenderGraphContext context) =>
                {
                    if (!data.emptyShadowmap)
                        data.pass.RenderMainLightCascadeShadowmap(ref context.renderContext, ref data.renderingData);
                });

                shadowTexture = passData.shadowmapTexture;
            }

            using (var builder = graph.AddRenderPass<PassData>("Set Main Shadow Globals", out var passData, base.profilingSampler))
            {
                InitPassData(ref passData, ref renderingData, ref graph);

                passData.shadowmapTexture = shadowTexture;

                if (shadowTexture.IsValid())
                    builder.UseDepthBuffer(shadowTexture, DepthAccess.Read);

                builder.AllowPassCulling(false);

                builder.SetRenderFunc((PassData data, RenderGraphContext context) =>
                {
                    if (data.emptyShadowmap)
                    {
                        data.pass.SetEmptyMainLightCascadeShadowmap(ref context.renderContext, ref data.renderingData);
                        data.shadowmapTexture = data.graph.defaultResources.defaultShadowTexture;
                    }

                    data.renderingData.commandBuffer.SetGlobalTexture(data.shadowmapID, data.shadowmapTexture);
                });
                return passData.shadowmapTexture;
            }
        }

        void InitPassData(ref PassData passData, ref RenderingData renderingData, ref RenderGraph graph)
        {
            passData.pass = this;
            passData.graph = graph;

            passData.emptyShadowmap = m_CreateEmptyShadowmap;
            passData.shadowmapID = m_MainLightShadowmapID;
            passData.renderingData = renderingData;
        }
    };
}
