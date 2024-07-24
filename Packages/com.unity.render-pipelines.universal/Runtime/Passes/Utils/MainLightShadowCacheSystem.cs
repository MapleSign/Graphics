using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
namespace UnityEngine.Rendering.Universal.Internal
{
    /// <summary>
    /// Record cascade data used to determine updating and caching in shadow.
    /// </summary>
    public struct CascadeState
    {
        public bool isDirty;
        public int elapsed;
        public ShadowObjectsFilter filter;

        public bool shouldStaticUpdate => filter != ShadowObjectsFilter.DynamicOnly && elapsed == 0 && isDirty;
        public bool shouldDynamicUpdate => filter != ShadowObjectsFilter.StaticOnly && elapsed == 0;

        public void Clear()
        {
            isDirty = true;
            elapsed = 0;
            filter = ShadowObjectsFilter.AllObjects;
        }
    }

    public class MainLightShadowCacheSystem
    {
        public readonly int k_MaxCascades = 4;
        public readonly int k_ShadowmapBufferBits = 16;
        float m_CascadeBorder;
        float m_MaxShadowDistanceSq;
        int m_ShadowCasterCascadesCount;

        Matrix4x4[] m_MainLightShadowMatrices;
        ShadowSliceData[] m_CascadeSlices;
        Vector4[] m_CascadeSplitDistances;
        CascadeState[] m_CascadeStates;

        int m_RenderTargetWidth;
        int m_RenderTargetHeight;
        int m_ShadowResolution;

        bool m_EmptyRendering = false;

        public float cascadeBorder { get => m_CascadeBorder; }
        public float maxShadowDistanceSq { get => m_MaxShadowDistanceSq; }
        public int cascadesCount { get => m_ShadowCasterCascadesCount; }

        public Matrix4x4[] shadowMatrices { get => m_MainLightShadowMatrices; }
        public ShadowSliceData[] cascadeSlices { get => m_CascadeSlices; }
        public Vector4[] cascadeSplitDistances { get => m_CascadeSplitDistances; }
        public CascadeState[] cascadeStates { get => m_CascadeStates; }

        public int renderTargetWidth { get => m_RenderTargetWidth; }
        public int renderTargetHeight { get => m_RenderTargetHeight; }
        public int shadowResolution { get => m_ShadowResolution; }

        public bool emptyRendering { get => m_EmptyRendering; }

        bool m_StaticPassSuccess;
        bool m_DynamicPassSuccess;

        StaticMainLightShadowCasterPass m_StaticPass;
        DynamicMainLightShadowCasterPass m_DynamicPass;

        CopyDepthPass m_CopyDepthPass;

        public StaticMainLightShadowCasterPass staticPass { get => m_StaticPass; }
        public DynamicMainLightShadowCasterPass dynamicPass { get => m_DynamicPass; }

        ShadowTracker m_ShadowTracker;
        bool m_IsDirty = true;

        public MainLightShadowCacheSystem(RenderPassEvent evt, Material copyDepthMaterial)
        {
            m_MainLightShadowMatrices = new Matrix4x4[k_MaxCascades + 1];
            m_CascadeSlices = new ShadowSliceData[k_MaxCascades];
            m_CascadeSplitDistances = new Vector4[k_MaxCascades];
            m_CascadeStates = new CascadeState[k_MaxCascades];
            for (int i = 0; i < k_MaxCascades; i++)
            {
                m_CascadeStates[i].Clear();
            }

            m_StaticPass = new(evt, this);
            m_DynamicPass = new(evt, this);
            m_CopyDepthPass = new(evt, copyDepthMaterial, shouldClear: true, useDestView: true);

            m_ShadowTracker = new ShadowTracker();
        }

        public void Dispose()
        {
            m_StaticPass.Dispose();
            m_DynamicPass.Dispose();
        }

        public bool Setup(ref RenderingData renderingData)
        {
            if (!renderingData.shadowData.mainLightShadowsEnabled)
                return false;

            m_EmptyRendering = false;

            if (!renderingData.shadowData.supportsMainLightShadows)
                m_EmptyRendering = true;

            int shadowLightIndex = renderingData.lightData.mainLightIndex;
            if (shadowLightIndex == -1)
                m_EmptyRendering = true;

            VisibleLight shadowLight = renderingData.lightData.visibleLights[shadowLightIndex];
            Light light = shadowLight.light;
            if (light.shadows == LightShadows.None)
                m_EmptyRendering = true;

            if (shadowLight.lightType != LightType.Directional)
            {
                Debug.LogWarning("Only directional lights are supported as main light.");
            }

            Bounds bounds;
            if (!renderingData.cullResults.GetShadowCasterBounds(shadowLightIndex, out bounds))
                m_EmptyRendering = true;

            m_ShadowCasterCascadesCount = renderingData.shadowData.mainLightShadowCascadesCount;

            m_ShadowResolution = ShadowUtils.GetMaxTileResolutionInAtlas(renderingData.shadowData.mainLightShadowmapWidth,
                renderingData.shadowData.mainLightShadowmapHeight, m_ShadowCasterCascadesCount);
            m_RenderTargetWidth = renderingData.shadowData.mainLightShadowmapWidth;
            m_RenderTargetHeight = (m_ShadowCasterCascadesCount == 2) ?
                renderingData.shadowData.mainLightShadowmapHeight >> 1 :
                renderingData.shadowData.mainLightShadowmapHeight;

            SetupCascadeStates(ref renderingData);
            for (int cascadeIndex = 0; cascadeIndex < m_ShadowCasterCascadesCount; ++cascadeIndex)
            {
                bool success = true;
                // Only update the shadow data when shadow map should be updated.
                if (m_CascadeStates[cascadeIndex].shouldStaticUpdate)
                    success = ShadowUtils.ExtractDirectionalLightMatrix(ref renderingData.cullResults, ref renderingData.shadowData,
                        shadowLightIndex, cascadeIndex, renderTargetWidth, renderTargetHeight, shadowResolution, light.shadowNearPlane,
                        out m_CascadeSplitDistances[cascadeIndex], out m_CascadeSlices[cascadeIndex]);

                if (!success)
                    m_EmptyRendering = true;
            }

            m_MaxShadowDistanceSq = renderingData.cameraData.maxShadowDistance * renderingData.cameraData.maxShadowDistance;
            m_CascadeBorder = renderingData.shadowData.mainLightShadowCascadeBorder;

            m_StaticPassSuccess = m_StaticPass.Setup(ref renderingData);
            m_DynamicPassSuccess = m_DynamicPass.Setup(ref renderingData);
            m_CopyDepthPass.Setup(m_StaticPass.m_StaticMainLightShadowmapTexture, m_DynamicPass.m_MainLightShadowmapTexture);

            return m_StaticPassSuccess || m_DynamicPassSuccess;
        }

        void SetupCascadeStates(ref RenderingData renderingData)
        {
            if (renderingData.shadowData.shadowUpdateMode == ShadowUpdateMode.Dynamic)
            {
                for (int i = 0; i < m_ShadowCasterCascadesCount; i++)
                {
                    m_CascadeStates[i].Clear();
                }
            }
            else
            {
                m_IsDirty = m_ShadowTracker.CameraChanged(ref renderingData.cameraData);
                for (int i = 0; i < m_ShadowCasterCascadesCount; i++)
                {
                    // shouldUpdate means it has just updated last frame, so we reset it to not dirty.
                    if (m_CascadeStates[i].shouldStaticUpdate)
                        m_CascadeStates[i].isDirty = false;

                    m_CascadeStates[i].isDirty |= m_IsDirty;

                    int elapsed = cascadeStates[i].elapsed;
                    m_CascadeStates[i].filter = ShadowObjectsFilter.AllObjects;
                    m_CascadeStates[i].elapsed = 0;

                    switch (renderingData.shadowData.cascadeUpdateMode)
                    {
                        case CascadeUpdateMode.Immediate:
                            break;
                        case CascadeUpdateMode.Rolling:
                            if (i >= renderingData.shadowData.cascadeRollingStart)
                            {
                                m_CascadeStates[i].filter = ShadowObjectsFilter.StaticOnly;
                                m_CascadeStates[i].elapsed = elapsed > 0 ? elapsed - 1 : i - renderingData.shadowData.cascadeRollingStart + 1;
                            }
                            break;
                        case CascadeUpdateMode.Skip:
                            if (renderingData.shadowData.cascadeSkipFrames[i] > 0)
                            {
                                m_CascadeStates[i].filter = ShadowObjectsFilter.StaticOnly;
                                m_CascadeStates[i].elapsed = elapsed > 0 ? elapsed - 1 : renderingData.shadowData.cascadeSkipFrames[i];
                            }
                            break;
                    }
                }
            }
        }

        public void Enqueue(ScriptableRenderer renderer)
        {
            if (m_StaticPassSuccess)
            {
                renderer.EnqueuePass(m_StaticPass);
                renderer.EnqueuePass(m_CopyDepthPass);
            }

            if (m_DynamicPassSuccess)
                renderer.EnqueuePass(m_DynamicPass);
        }

        internal TextureHandle Render(RenderGraph graph, ref RenderingData renderingData)
        {
            return m_DynamicPass.Render(graph, ref renderingData);
        }
    }
}
