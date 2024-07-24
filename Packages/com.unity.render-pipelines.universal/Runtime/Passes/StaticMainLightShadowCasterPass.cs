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
        static Material clearDepthMaterial;
        static Material scrollDepthMaterial;
        static Material copyDepthMaterial;

        MainLightShadowCacheSystem m_ShadowCacheSystem;

        private RTHandle m_EmptyMainLightShadowmapTexture;
        private const int k_EmptyShadowMapDimensions = 1;
        private const string k_EmptyMainLightShadowMapTextureName = "_EmptyMainLightShadowmapTexture";
        
        int m_StaticMainLightShadowmapID;
        int m_ShadowMapIndex = 0;
        internal RTHandle[] m_StaticMainLightShadowmapTextures;
        private const string k_StaticeMainLightShadowMapTextureName = "_StaticMainLightShadowmapTexture";
        const int k_ShadowMapCount = 2;

        internal RTHandle lastTexture => m_StaticMainLightShadowmapTextures[(m_ShadowMapIndex + 1) % k_ShadowMapCount];
        internal RTHandle nowTexture => m_StaticMainLightShadowmapTextures[m_ShadowMapIndex];
        void NextShadowMap() => m_ShadowMapIndex = (m_ShadowMapIndex + 1) % k_ShadowMapCount;

        bool[] isLastFrameAvailables;
        Vector3[] lastViewPositions;
        Matrix4x4[] lastShadowViewMatrices;
        Matrix4x4[] lastShadowProjMatrices;
        int scrollDepthMapId;

        bool m_CreateEmptyShadowmap;

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
            m_StaticMainLightShadowmapTextures = new RTHandle[k_ShadowMapCount];
            m_ShadowCacheSystem = cacheSystem;

            scrollDepthMapId = Shader.PropertyToID("_DepthMap");
            isLastFrameAvailables = new bool[cacheSystem.k_MaxCascades];
            for (int i = 0; i < isLastFrameAvailables.Length; i++)
            {
                isLastFrameAvailables[i] = false;
            }
            lastViewPositions = new Vector3[cacheSystem.k_MaxCascades];
            lastShadowViewMatrices = new Matrix4x4[cacheSystem.k_MaxCascades];
            lastShadowProjMatrices = new Matrix4x4[cacheSystem.k_MaxCascades];
        }

        /// <summary>
        /// Cleans up resources used by the pass.
        /// </summary>
        public void Dispose()
        {
            for (int i = 0; i < m_StaticMainLightShadowmapTextures.Length; i++)
            {
                m_StaticMainLightShadowmapTextures[i]?.Release();
            }
            m_EmptyMainLightShadowmapTexture?.Release();
        }

        public void Reset()
        {
            for (int i = 0; i < isLastFrameAvailables.Length; i++)
            {
                isLastFrameAvailables[i] = false;
            }
        }

        /// <summary>
        /// Sets up the pass.
        /// </summary>
        /// <param name="renderingData"></param>
        /// <returns>True if the pass should be enqueued, otherwise false.</returns>
        /// <seealso cref="RenderingData"/>
        public bool Setup(ref RenderingData renderingData)
        {
            if (clearDepthMaterial == null)
            {
                clearDepthMaterial = new Material(Shader.Find("Hidden/Universal Render Pipeline/ClearDepth"));
            }

            if (scrollDepthMaterial == null)
            {
                scrollDepthMaterial = new Material(Shader.Find("Hidden/Universal Render Pipeline/ScrollDepth"));
            }

            if (copyDepthMaterial == null)
            {
                copyDepthMaterial = new Material(Shader.Find("Hidden/Universal Render Pipeline/CopyDepth"));
            }

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
            for (int i = 0; i < k_ShadowMapCount; i++)
                ShadowUtils.ShadowRTReAllocateIfNeeded(ref m_StaticMainLightShadowmapTextures[i],
                    m_ShadowCacheSystem.renderTargetWidth, m_ShadowCacheSystem.renderTargetHeight, m_ShadowCacheSystem.k_ShadowmapBufferBits,
                    name: k_StaticeMainLightShadowMapTextureName + i, filterMode: FilterMode.Point);

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
                ConfigureTarget(nowTexture);

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

            RenderMainLightCascadeShadowmap(ref context, ref renderingData);
            renderingData.commandBuffer.SetGlobalTexture(m_StaticMainLightShadowmapID, nowTexture.nameID);
            NextShadowMap();
        }

        void RenderMainLightCascadeShadowmap(ref ScriptableRenderContext context, ref RenderingData renderingData)
        {
            CullingResults cullResults;
            if (renderingData.shadowData.supportsShadowScrolling)
            {
                var cullParams = renderingData.cameraData.cullParams;
                cullParams.cullingMask &= ~(uint)(1 << LayerMask.NameToLayer("Ignore Static Shadow"));
                cullResults = context.Cull(ref cullParams);
            }
            else
            {
                cullResults = renderingData.cullResults;
            }

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
                    var offsetX = m_ShadowCacheSystem.cascadeSlices[cascadeIndex].offsetX;
                    var offsetY = m_ShadowCacheSystem.cascadeSlices[cascadeIndex].offsetY;
                    var resolution = m_ShadowCacheSystem.cascadeSlices[cascadeIndex].resolution;
                    if (!m_ShadowCacheSystem.cascadeStates[cascadeIndex].shouldStaticUpdate)
                    {
                        cmd.SetGlobalTexture("_CameraDepthAttachment", lastTexture.nameID);
                        cmd.EnableShaderKeyword("_OUTPUT_DEPTH");
                        cmd.SetViewport(new Rect(offsetX, offsetY, resolution, resolution));
                        var scaleBias = new Vector4(
                            (float)(resolution) / renderingData.shadowData.mainLightShadowmapWidth,
                            (float)(resolution) / renderingData.shadowData.mainLightShadowmapHeight,
                            (float)offsetX / renderingData.shadowData.mainLightShadowmapWidth,
                            (float)offsetY / renderingData.shadowData.mainLightShadowmapHeight
                            );
                        Blitter.BlitTexture(cmd, lastTexture, scaleBias, copyDepthMaterial, 0);
                        context.ExecuteCommandBuffer(cmd);
                        cmd.Clear();
                    }
                    else
                    {
                        cmd.SetViewport(new Rect(offsetX, offsetY, resolution, resolution));
                        cmd.DrawProcedural(Matrix4x4.identity, clearDepthMaterial, 0, MeshTopology.Triangles, 3);
                        context.ExecuteCommandBuffer(cmd);
                        cmd.Clear();

                        if (isLastFrameAvailables[cascadeIndex] && renderingData.shadowData.supportsShadowScrolling)
                        {
                            ScrollDepth(ref context, ref renderingData, cascadeIndex);
                        }
                        lastViewPositions[cascadeIndex] = m_ShadowCacheSystem.cascadeSlices[cascadeIndex].viewPosLightSpace;
                        lastShadowViewMatrices[cascadeIndex] = m_ShadowCacheSystem.cascadeSlices[cascadeIndex].viewMatrix;
                        lastShadowProjMatrices[cascadeIndex] = m_ShadowCacheSystem.cascadeSlices[cascadeIndex].projectionMatrix;

                        settings.splitData = m_ShadowCacheSystem.cascadeSlices[cascadeIndex].splitData;

                        Vector4 shadowBias = ShadowUtils.GetShadowBias(ref shadowLight, shadowLightIndex, ref renderingData.shadowData,
                            m_ShadowCacheSystem.cascadeSlices[cascadeIndex].projectionMatrix, resolution);
                        ShadowUtils.SetupShadowCasterConstantBuffer(cmd, ref shadowLight, shadowBias);
                        CoreUtils.SetKeyword(cmd, ShaderKeywordStrings.CastingPunctualLightShadow, false);
                        ShadowUtils.RenderShadowSlice(cmd, ref context, ref m_ShadowCacheSystem.cascadeSlices[cascadeIndex],
                            ref settings, m_ShadowCacheSystem.cascadeSlices[cascadeIndex].projectionMatrix, m_ShadowCacheSystem.cascadeSlices[cascadeIndex].viewMatrix);

                        isLastFrameAvailables[cascadeIndex] = true;
                    }
                }
            }
        }

        void ScrollDepth(ref ScriptableRenderContext context, ref RenderingData renderingData, int cascadeIndex)
        {
            var viewMatrix = m_ShadowCacheSystem.cascadeSlices[cascadeIndex].viewMatrix;
            var projMatrix = m_ShadowCacheSystem.cascadeSlices[cascadeIndex].projectionMatrix;
            var cmd = renderingData.commandBuffer;

            Vector4 uvDeform, depthDeform;

            Vector4 dP = -lastViewPositions[cascadeIndex] + m_ShadowCacheSystem.cascadeSlices[cascadeIndex].viewPosLightSpace;
            var fp0 = lastShadowProjMatrices[cascadeIndex].decomposeProjection;
            var fp1 = projMatrix.decomposeProjection;

            var dUV = new Vector4(
                (fp1.right - fp1.left) / (fp0.right - fp0.left),
                (fp1.top - fp1.bottom) / (fp0.top - fp0.bottom),
                (fp1.left - fp0.left + dP.x) / (fp0.right - fp0.left),
                (fp1.bottom - fp0.bottom + dP.y) / (fp0.top - fp0.bottom)
                );
            uvDeform = dUV;

            // from old view to new view
            var dDepth = new Vector4(
                (fp0.zFar - fp0.zNear) / (fp1.zFar - fp1.zNear),
                (fp0.zNear - fp1.zNear + dP.z) / (fp1.zFar - fp1.zNear)
                );
            depthDeform = dDepth;

            var offsetX = m_ShadowCacheSystem.cascadeSlices[cascadeIndex].offsetX;
            var offsetY = m_ShadowCacheSystem.cascadeSlices[cascadeIndex].offsetY;
            var resolution = m_ShadowCacheSystem.cascadeSlices[cascadeIndex].resolution;
            var bound = new Vector4(
                (float)offsetX / renderingData.shadowData.mainLightShadowmapWidth,
                (float)offsetY / renderingData.shadowData.mainLightShadowmapHeight,
                (float)(offsetX + resolution) / renderingData.shadowData.mainLightShadowmapWidth,
                (float)(offsetY + resolution)/ renderingData.shadowData.mainLightShadowmapHeight
                );

            cmd.SetGlobalVector("_Bound", bound);
            cmd.SetGlobalVector("_UVDeform", uvDeform);
            cmd.SetGlobalVector("_DepthDeform", depthDeform);
            cmd.SetGlobalTexture(scrollDepthMapId, lastTexture);
            cmd.SetGlobalVector("_DepthMap_TexelSize",
                new Vector4(
                    1f / renderingData.shadowData.mainLightShadowmapWidth,
                    1f / renderingData.shadowData.mainLightShadowmapHeight,
                    renderingData.shadowData.mainLightShadowmapWidth,
                    renderingData.shadowData.mainLightShadowmapHeight
                    )
                );
            Blitter.BlitTexture(cmd, lastTexture, new Vector4(1f, 1f, 0f, 0f), scrollDepthMaterial, 0);
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
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
                    passData.shadowmapTexture = UniversalRenderer.CreateRenderGraphTexture(graph, nowTexture.rt.descriptor, "Static Main Shadowmap", true, ShadowUtils.m_ForceShadowPointSampling ? FilterMode.Point : FilterMode.Bilinear);
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
