using System;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;

namespace UnityEngine.Rendering.Universal.Internal
{
    /// <summary>
    /// Renders a shadow map for the main Light for static objects.
    /// </summary>
    public class StaticMainLightShadowCasterPass : ScriptableRenderPass
    {
        MainLightShadowCacheSystem m_ShadowCacheSystem;

        private RTHandle m_EmptyMainLightShadowmapTexture;
        private const int k_EmptyShadowMapDimensions = 1;
        private const string k_EmptyMainLightShadowMapTextureName = "_EmptyMainLightShadowmapTexture";
        
        int m_StaticMainLightShadowmapID;
        internal RTHandle m_StaticMainLightShadowmapTexture;
        private const string k_StaticeMainLightShadowMapTextureName = "_StaticMainLightShadowmapTexture";

        bool m_CreateEmptyShadowmap;

        ShadowTracker m_ShadowTracker;
        bool m_IsDirty = true;

        ProfilingSampler m_ProfilingSetupSampler = new ProfilingSampler("Setup Static Main Shadowmap");

        /// <summary>
        /// Creates a new <c>StaticMainLightShadowCasterPass</c> instance.
        /// </summary>
        /// <param name="evt">The <c>RenderPassEvent</c> to use.</param>
        /// <seealso cref="RenderPassEvent"/>
        public StaticMainLightShadowCasterPass(RenderPassEvent evt, MainLightShadowCacheSystem cacheSystem)
        {
            base.profilingSampler = new ProfilingSampler(nameof(StaticMainLightShadowCasterPass));
            renderPassEvent = evt;

            m_StaticMainLightShadowmapID = Shader.PropertyToID(k_StaticeMainLightShadowMapTextureName);
            m_ShadowTracker = new ShadowTracker();
            m_ShadowCacheSystem = cacheSystem;
        }

        /// <summary>
        /// Cleans up resources used by the pass.
        /// </summary>
        public void Dispose()
        {
            m_StaticMainLightShadowmapTexture?.Release();
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
            if (renderingData.shadowData.shadowUpdateMode == ShadowUpdateMode.Dynamic)
                return false;

            if (!renderingData.shadowData.mainLightShadowsEnabled)
                return false;

            using var profScope = new ProfilingScope(null, m_ProfilingSetupSampler);

            m_CreateEmptyShadowmap = m_ShadowCacheSystem.emptyRendering;
            if (m_CreateEmptyShadowmap)
            {
                return SetupForEmptyRendering(ref renderingData);
            }

            useNativeRenderPass = true;
            ShadowUtils.ShadowRTReAllocateIfNeeded(ref m_StaticMainLightShadowmapTexture, m_ShadowCacheSystem.renderTargetWidth, m_ShadowCacheSystem.renderTargetHeight, m_ShadowCacheSystem.k_ShadowmapBufferBits, name: k_StaticeMainLightShadowMapTextureName);

            m_IsDirty = m_ShadowTracker.CameraChanged(renderingData.cameraData.camera);

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
                ConfigureTarget(m_StaticMainLightShadowmapTexture);

            if (m_IsDirty)
                ConfigureClear(ClearFlag.All, Color.black);
            else
                ConfigureClear(ClearFlag.None, Color.black);
        }

        /// <inheritdoc/>
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (m_CreateEmptyShadowmap)
            {
                renderingData.commandBuffer.SetGlobalTexture(m_StaticMainLightShadowmapID, m_EmptyMainLightShadowmapTexture.nameID);

                return;
            }

            if (m_IsDirty)
                RenderMainLightCascadeShadowmap(ref context, ref renderingData);
            renderingData.commandBuffer.SetGlobalTexture(m_StaticMainLightShadowmapID, m_StaticMainLightShadowmapTexture.nameID);
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
                var settings = new ShadowDrawingSettings(cullResults, shadowLightIndex, BatchCullingProjectionType.Orthographic);
                settings.useRenderingLayerMaskTest = UniversalRenderPipeline.asset.useRenderingLayers;
                settings.objectsFilter = ShadowObjectsFilter.StaticOnly;

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
        }

        private class PassData
        {
            internal StaticMainLightShadowCasterPass pass;
            internal RenderGraph graph;

            internal TextureHandle shadowmapTexture;
            internal RenderingData renderingData;
            internal int shadowmapID;

            internal bool emptyShadowmap;
        }

        internal TextureHandle Render(RenderGraph graph, ref RenderingData renderingData)
        {
            TextureHandle shadowTexture;

            using (var builder = graph.AddRenderPass<PassData>("Static Main Light Shadowmap", out var passData, base.profilingSampler))
            {
                InitPassData(ref passData, ref renderingData, ref graph);

                if (!m_CreateEmptyShadowmap)
                {
                    passData.shadowmapTexture = UniversalRenderer.CreateRenderGraphTexture(graph, m_StaticMainLightShadowmapTexture.rt.descriptor, "Static Main Shadowmap", true, ShadowUtils.m_ForceShadowPointSampling ? FilterMode.Point : FilterMode.Bilinear);
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

            using (var builder = graph.AddRenderPass<PassData>("Set Static Main Shadow Globals", out var passData, base.profilingSampler))
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
            passData.shadowmapID = m_StaticMainLightShadowmapID;
            passData.renderingData = renderingData;
        }
    };
}
