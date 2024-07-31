using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering;
using UnityEngine;
using System.Collections.Generic;
using UnityEditor;

class VoxelizeRenderPass : ScriptableRenderPass
{
    internal static class ShaderConstants
    {
        public static int _VoxelAlbedo = Shader.PropertyToID("_VoxelAlbedo");
        public static int _VoxelNormal = Shader.PropertyToID("_VoxelNormal");
        public static int _VoxelEmission = Shader.PropertyToID("_VoxelEmission");
        public static int _VoxelOpacity = Shader.PropertyToID("_VoxelOpacity");
        public static int _VoxelRadiance = Shader.PropertyToID("_VoxelRadiance");

        public static int _VolumeMinPoint = Shader.PropertyToID("_VolumeMinPoint");
        public static int _VolumeScale = Shader.PropertyToID("_VolumeScale");
        public static int _VolumeSize = Shader.PropertyToID("_VolumeSize");
        public static int _VolumeResolution = Shader.PropertyToID("_VolumeResolution");

        public static int _LightPosWS = Shader.PropertyToID("_LightPosWS");
        public static int _LightColor = Shader.PropertyToID("_LightColor");
        public static int _LightAttenuation = Shader.PropertyToID("_LightAttenuation");
        public static int _LightOcclusionProbInfo = Shader.PropertyToID("_LightOcclusionProbInfo");
        public static int _LightDirection = Shader.PropertyToID("_LightDirection");
        public static int _LightFlags = Shader.PropertyToID("_LightFlags");
        public static int _ShadowLightIndex = Shader.PropertyToID("_ShadowLightIndex");
        public static int _LightLayerMask = Shader.PropertyToID("_LightLayerMask");
        public static int _CookieLightIndex = Shader.PropertyToID("_CookieLightIndex");
    }

    private List<ShaderTagId> m_ShaderTagIdList;

    private VXGIRenderFeature m_Feature;

    private Matrix4x4[] m_ViewProjMatrices;
    private Matrix4x4[] m_ViewProjInvMatrices;

    private RenderTexture m_VoxelAlbedo;
    private RenderTexture m_VoxelNormal;
    private RenderTexture m_VoxelEmission;
    private RenderTexture m_VoxelOpacity;

    private RenderTexture m_VoxelRadiance;

    private const int k_Anisotropic = 6;
    private int m_MipmapCount;
    private RenderTexture[] m_VoxelAnisos;
    private int m_VoxelAnisoResolution;

    private RTHandle m_EmptyRenderTarget;

    private ProfilingSampler m_VoxelSetupSampler;

    public VoxelizeRenderPass(VXGIRenderFeature renderFeature)
    {
        m_ShaderTagIdList = new List<ShaderTagId>
        {
            //new ShaderTagId("SRPDefaultUnlit"),
            //new ShaderTagId("UniversalForward"),
            //new ShaderTagId("UniversalForwardOnly")
            new ShaderTagId("UniversalGBuffer")
        };

        m_Feature = renderFeature;
        m_VoxelAnisoResolution = m_Feature.m_Resolution / 2;
        m_MipmapCount = (int)Mathf.Log(m_VoxelAnisoResolution, 2f) + 1;

        m_VoxelSetupSampler = new ProfilingSampler("VoxelizeSetup");

        m_ViewProjMatrices = new Matrix4x4[3];
        m_ViewProjInvMatrices = new Matrix4x4[3];
    }

    // This method is called before executing the render pass.
    // It can be used to configure render targets and their clear state. Also to create temporary render target textures.
    // When empty this render pass will render to the active camera render target.
    // You should never call CommandBuffer.SetRenderTarget. Instead call <c>ConfigureTarget</c> and <c>ConfigureClear</c>.
    // The render pipeline will ensure target setup and clearing happens in a performant manner.
    public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
    {
        if (m_VoxelAlbedo == null)
        {
            m_VoxelAlbedo = Create3DTexture(m_Feature.m_Resolution, GraphicsFormat.R8G8B8A8_UNorm);
            Debug.Assert(m_VoxelAlbedo.Create());
        }

        if (m_VoxelNormal == null)
        {
            m_VoxelNormal = Create3DTexture(m_Feature.m_Resolution, GraphicsFormat.R8G8B8A8_UNorm);
            Debug.Assert(m_VoxelNormal.Create());
        }

        if (m_VoxelEmission == null)
        {
            m_VoxelEmission = Create3DTexture(m_Feature.m_Resolution, GraphicsFormat.R8G8B8A8_UNorm);
            Debug.Assert(m_VoxelEmission.Create());
        }

        if (m_VoxelOpacity == null)
        {
            m_VoxelOpacity = Create3DTexture(m_Feature.m_Resolution, GraphicsFormat.R8_UInt);
            Debug.Assert(m_VoxelOpacity.Create());
        }

        if (m_VoxelRadiance == null)
        {
            m_VoxelRadiance = Create3DTexture(m_Feature.m_Resolution, GraphicsFormat.R16G16B16A16_SFloat);
            Debug.Assert(m_VoxelRadiance.Create());
        }

        if (m_VoxelAnisos == null || m_VoxelAnisos[0] == null)
        {
            CreateVoxelAnisos(m_VoxelAnisoResolution, GraphicsFormat.R16G16B16A16_SFloat, m_MipmapCount);
        }

        if (m_EmptyRenderTarget == null)
        {
            m_EmptyRenderTarget = RTHandles.Alloc(
                m_Feature.m_Resolution, m_Feature.m_Resolution, colorFormat: GraphicsFormat.R32_SFloat);
        }

        ConfigureTarget(m_EmptyRenderTarget);
        ConfigureClear(ClearFlag.Color, Color.clear);
    }

    // Here you can implement the rendering logic.
    // Use <c>ScriptableRenderContext</c> to issue drawing commands or execute command buffers
    // https://docs.unity3d.com/ScriptReference/Rendering.ScriptableRenderContext.html
    // You don't have to call ScriptableRenderContext.submit, the render pipeline will call it at specific points in the pipeline.
    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        var cullResults = renderingData.cullResults;
        if (m_Feature.VoxelizeCamera != null && m_Feature.VoxelizeCamera.TryGetCullingParameters(out var cullingParameters))
        {
            cullResults = context.Cull(ref cullingParameters);
        }

        int resolution = m_Feature.m_Resolution;
        var cmd = renderingData.commandBuffer;
        using (new ProfilingScope(cmd, m_VoxelSetupSampler))
        {
            ClearVoxelTextures(ref context, cmd);

            cmd.SetViewport(new Rect(0, 0, resolution, resolution));
            cmd.SetRandomWriteTarget(1, m_VoxelAlbedo);
            cmd.SetRandomWriteTarget(2, m_VoxelNormal);
            cmd.SetRandomWriteTarget(3, m_VoxelEmission);
            cmd.SetRandomWriteTarget(4, m_VoxelOpacity);

            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            Bounds bounds = m_Feature.m_VXGIVolume;

            cmd.SetGlobalVector(ShaderConstants._VolumeMinPoint, bounds.min);
            cmd.SetGlobalVector(ShaderConstants._VolumeScale, new Vector4(1f / bounds.size.x, 1f / bounds.size.y, 1f / bounds.size.z));
            cmd.SetGlobalVector(ShaderConstants._VolumeSize, bounds.size);
            cmd.SetGlobalVector(ShaderConstants._VolumeResolution, new Vector4(resolution, resolution, 1f / resolution, 1f / resolution));

            Matrix4x4[] projs = new Matrix4x4[3] {
                Matrix4x4.Ortho(-bounds.extents[2], bounds.extents[2], -bounds.extents[1], bounds.extents[1], 0, bounds.size[0]),
                Matrix4x4.Ortho(-bounds.extents[2], bounds.extents[2], -bounds.extents[0], bounds.extents[0], 0, bounds.size[1]),
                Matrix4x4.Ortho(-bounds.extents[0], bounds.extents[0], -bounds.extents[1], bounds.extents[1], 0, bounds.size[2])
            };
            for (int i = 0; i < 3; i++)
            {
                Vector3 from = bounds.center;
                from[i] += bounds.extents[i];

                Vector3 forward = Vector3.zero;
                forward[i] = 1f;
                Vector3 up = i == 1 ? Vector3.forward : Vector3.up;
                Vector3 right = Vector3.Cross(forward, up);

                var view = Matrix4x4.identity;
                view.SetRow(0, right);
                view.SetRow(1, up);
                view.SetRow(2, forward);
                view *= Matrix4x4.Translate(-from);

                m_ViewProjMatrices[i] = GL.GetGPUProjectionMatrix(projs[i], false) * view;
                m_ViewProjInvMatrices[i] = m_ViewProjMatrices[i].inverse;
            }

            cmd.SetGlobalMatrixArray("_ViewProjections", m_ViewProjMatrices);
            cmd.SetGlobalMatrixArray("_ViewProjectionsInv", m_ViewProjInvMatrices);

            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            var drawingSettings = CreateDrawingSettings(m_ShaderTagIdList, ref renderingData, SortingCriteria.None);
            drawingSettings.overrideShader = m_Feature.VXGIShader;
            drawingSettings.overrideShaderPassIndex = 0;
            var s = drawingSettings.overrideShader;

            var filterSettings = FilteringSettings.defaultValue;

            context.DrawRenderers(cullResults, ref drawingSettings, ref filterSettings);

            cmd.ClearRandomWriteTargets();
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            ComputeRadiance(ref context, ref renderingData);
            ComputeVoxelAnisoRadiance(ref context, ref renderingData);
            GenerateVoxelAnisoMipmap(ref context, ref renderingData);

            VisualizeTexture3D(ref context, cmd, m_VoxelAnisos[0], 5);
        }
    }

    private void ClearVoxelTextures(ref ScriptableRenderContext context, CommandBuffer cmd)
    {
        int resolution = m_Feature.m_Resolution;
        var clearShader = m_Feature.ClearTexture3DShader;
        int kernalId = clearShader.FindKernel("ClearTexture3D");
        int nameId = Shader.PropertyToID("_MainTex");
        var typeKeyword = clearShader.keywordSpace.FindKeyword("TYPE_FLOAT4");

        clearShader.GetKernelThreadGroupSizes(kernalId, out var x, out var y, out var z);
        int threadGroupsX = resolution / (int)x;
        int threadGroupsY = resolution / (int)y;
        int threadGroupsZ = resolution / (int)z;

        cmd.DisableKeyword(clearShader, typeKeyword);
        context.ExecuteCommandBuffer(cmd);
        cmd.Clear();

        cmd.SetComputeTextureParam(clearShader, kernalId, nameId, m_VoxelAlbedo);
        cmd.DispatchCompute(clearShader, kernalId, threadGroupsX, threadGroupsY, threadGroupsZ);
        context.ExecuteCommandBuffer(cmd);
        cmd.Clear();

        cmd.SetComputeTextureParam(clearShader, kernalId, nameId, m_VoxelNormal);
        cmd.DispatchCompute(clearShader, kernalId, threadGroupsX, threadGroupsY, threadGroupsZ);
        context.ExecuteCommandBuffer(cmd);
        cmd.Clear();

        cmd.SetComputeTextureParam(clearShader, kernalId, nameId, m_VoxelEmission);
        cmd.DispatchCompute(clearShader, kernalId, threadGroupsX, threadGroupsY, threadGroupsZ);
        context.ExecuteCommandBuffer(cmd);
        cmd.Clear();

        cmd.SetComputeTextureParam(clearShader, kernalId, nameId, m_VoxelRadiance);
        cmd.DispatchCompute(clearShader, kernalId, threadGroupsX, threadGroupsY, threadGroupsZ);
        context.ExecuteCommandBuffer(cmd);
        cmd.Clear();

        if (m_Feature.m_VisualizeTexture != null)
        {
            cmd.EnableKeyword(clearShader, typeKeyword);
            cmd.SetComputeTextureParam(clearShader, kernalId, nameId, m_Feature.m_VisualizeTexture);
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            cmd.DispatchCompute(clearShader, kernalId, threadGroupsX, threadGroupsY, threadGroupsZ);
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
        }
    }

    private void ComputeRadiance(ref ScriptableRenderContext context, ref RenderingData renderingData)
    {
        var cmd = renderingData.commandBuffer;

        int resolution = m_Feature.m_Resolution;
        var diShader = m_Feature.DirectIllumShader;
        int kernalId = diShader.FindKernel("main");
        var directionalKeyword = diShader.keywordSpace.FindKeyword("_DIRECTIONAL");

        diShader.GetKernelThreadGroupSizes(kernalId, out var x, out var y, out var z);
        int threadGroupsX = resolution / (int)x;
        int threadGroupsY = resolution / (int)y;
        int threadGroupsZ = resolution / (int)z;

        cmd.SetComputeTextureParam(diShader, kernalId, ShaderConstants._VoxelAlbedo, m_VoxelAlbedo);
        cmd.SetComputeTextureParam(diShader, kernalId, ShaderConstants._VoxelNormal, m_VoxelNormal);
        cmd.SetComputeTextureParam(diShader, kernalId, ShaderConstants._VoxelEmission, m_VoxelEmission);
        cmd.SetComputeTextureParam(diShader, kernalId, ShaderConstants._VoxelRadiance, m_VoxelRadiance);
        context.ExecuteCommandBuffer(cmd);
        cmd.Clear();

        for (int i = 0; i < renderingData.lightData.visibleLights.Length; ++i)
        {
            ref var visibleLight = ref renderingData.lightData.visibleLights.UnsafeElementAtMutable(i);
            UniversalRenderPipeline.InitializeLightConstants_Common(renderingData.lightData.visibleLights, i,
                out var lightPos, out var lightColor, out var lightAttenuation, out var lightSpotDir, out var lightOcclusionProbeChannel);

            if (visibleLight.lightType == LightType.Directional)
            {
                cmd.EnableKeyword(diShader, directionalKeyword);
                lightSpotDir = lightPos;
            }
            else
            {
                cmd.DisableKeyword(diShader, directionalKeyword);
            }

            cmd.SetComputeVectorParam(diShader, ShaderConstants._LightPosWS, lightPos);
            cmd.SetComputeVectorParam(diShader, ShaderConstants._LightColor, lightColor);
            cmd.SetComputeVectorParam(diShader, ShaderConstants._LightAttenuation, lightAttenuation);
            cmd.SetComputeVectorParam(diShader, ShaderConstants._LightOcclusionProbInfo, lightOcclusionProbeChannel);
            cmd.SetComputeVectorParam(diShader, ShaderConstants._LightDirection, lightSpotDir);
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            cmd.DispatchCompute(diShader, kernalId, threadGroupsX, threadGroupsY, threadGroupsZ);
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
        }
    }

    private void ComputeVoxelAnisoRadiance(ref ScriptableRenderContext context, ref RenderingData renderingData)
    {
        var cmd = renderingData.commandBuffer;

        int resolution = m_VoxelAnisoResolution;
        var anisoShader = m_Feature.m_VoxelAnisoShader;
        int kernalId = anisoShader.FindKernel("main");

        anisoShader.GetKernelThreadGroupSizes(kernalId, out var x, out var y, out var z);
        int threadGroupsX = resolution / (int)x;
        int threadGroupsY = resolution / (int)y;
        int threadGroupsZ = resolution / (int)z;

        cmd.SetComputeTextureParam(anisoShader, kernalId, ShaderConstants._VoxelRadiance, m_VoxelRadiance);
        for (int i = 0; i < k_Anisotropic; ++i)
        {
            cmd.SetComputeTextureParam(anisoShader, kernalId, "_VoxelRadianceAniso[" + i + "]", m_VoxelAnisos[i], 0);
        }
        context.ExecuteCommandBuffer(cmd);
        cmd.Clear();

        cmd.DispatchCompute(anisoShader, kernalId, threadGroupsX, threadGroupsY, threadGroupsZ);
        context.ExecuteCommandBuffer(cmd);
        cmd.Clear();
    }

    private void GenerateVoxelAnisoMipmap(ref ScriptableRenderContext context, ref RenderingData renderingData)
    {
        var cmd = renderingData.commandBuffer;

        int resolution = m_VoxelAnisoResolution;
        var mipmapShader = m_Feature.m_VoxelMipmapShader;
        int kernalId = mipmapShader.FindKernel("main");

        mipmapShader.GetKernelThreadGroupSizes(kernalId, out var x, out var y, out var z);

        for (int i = 1; i < m_MipmapCount; ++i)
        {
            resolution /= 2;
            int threadGroupsX = Mathf.Max(resolution / (int)x, 1);
            int threadGroupsY = Mathf.Max(resolution / (int)y, 1);
            int threadGroupsZ = Mathf.Max(resolution / (int)z, 1);

            cmd.SetComputeIntParam(mipmapShader, "_SrcMipmapLevel", i - 1);
            cmd.SetComputeIntParam(mipmapShader, "_DstResolution", resolution);

            for (int anisoIndex = 0; anisoIndex < k_Anisotropic; ++anisoIndex)
            {
                //cmd.SetGlobalTexture("_VoxelMipmapSrc[" + anisoIndex + "]", m_VoxelAnisos[anisoIndex]);
                cmd.SetComputeTextureParam(mipmapShader, kernalId, "_VoxelMipmapSrc[" + anisoIndex + "]", m_VoxelAnisos[anisoIndex]);
                cmd.SetComputeTextureParam(mipmapShader, kernalId, "_VoxelMipmapDst[" + anisoIndex + "]", m_VoxelAnisos[anisoIndex], i);
            }
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            cmd.DispatchCompute(mipmapShader, kernalId, threadGroupsX, threadGroupsY, threadGroupsZ);
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
        }
    }

    private void VisualizeTexture3D(ref ScriptableRenderContext context, CommandBuffer cmd, RenderTexture src, int mipmap = 0)
    {
        if (m_Feature.m_VisualizeTexture != null)
        {
            Vector3Int dstResolution = new Vector3Int(m_Feature.m_VisualizeTexture.width, m_Feature.m_VisualizeTexture.height, m_Feature.m_VisualizeTexture.volumeDepth);
            Vector3Int srcResolution = new Vector3Int(src.width, src.height, src.volumeDepth);
            var visulizeShader = (dstResolution == srcResolution && mipmap == 0) ? m_Feature.m_CopyTexture3DShader : m_Feature.m_SampleTexture3DShader;
            int kernalId = visulizeShader.FindKernel("main");

            cmd.SetComputeTextureParam(visulizeShader, kernalId, "from", src);
            cmd.SetComputeTextureParam(visulizeShader, kernalId, "to", m_Feature.m_VisualizeTexture);
            cmd.SetComputeIntParam(visulizeShader, "_SrcMipmap", mipmap);
            cmd.SetComputeVectorParam(visulizeShader, "_DstResolution", new Vector4(dstResolution.x, dstResolution.y, dstResolution.z));
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            visulizeShader.GetKernelThreadGroupSizes(kernalId, out var x, out var y, out var z);
            int threadGroupsX = dstResolution.x / (int)x;
            int threadGroupsY = dstResolution.y / (int)y;
            int threadGroupsZ = dstResolution.z / (int)z;
            cmd.DispatchCompute(visulizeShader, kernalId, threadGroupsX, threadGroupsY, threadGroupsZ);
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
        }
    }

    // Cleanup any allocated resources that were created during the execution of this render pass.
    public override void OnCameraCleanup(CommandBuffer cmd)
    {
    }

    private RenderTexture Create3DTexture(int resolution, GraphicsFormat colorFormat)
    {
        var rt = new RenderTexture(resolution, resolution, colorFormat, GraphicsFormat.None);
        rt.dimension = TextureDimension.Tex3D;
        rt.volumeDepth = resolution;
        rt.enableRandomWrite = true;

        return rt;
    }

    private void CreateVoxelAnisos(int resolution, GraphicsFormat colorFormat, int mipmap)
    {
        m_VoxelAnisos = new RenderTexture[k_Anisotropic];
        for (int i = 0; i < k_Anisotropic; i++)
        {
            m_VoxelAnisos[i] = new RenderTexture(resolution, resolution, colorFormat, GraphicsFormat.None, mipmap);
            m_VoxelAnisos[i].dimension = TextureDimension.Tex3D;
            m_VoxelAnisos[i].volumeDepth = resolution;
            m_VoxelAnisos[i].enableRandomWrite = true;

            m_VoxelAnisos[i].wrapMode = TextureWrapMode.Clamp;
            m_VoxelAnisos[i].filterMode = FilterMode.Bilinear;

            m_VoxelAnisos[i].useMipMap = true;

            Debug.Assert(m_VoxelAnisos[i].Create());
        }
    }
}
