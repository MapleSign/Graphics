using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering;
using UnityEngine;
using System.Collections.Generic;
using UnityEditor;

class VoxelizeRenderPass : ScriptableRenderPass
{
    private List<ShaderTagId> m_ShaderTagIdList;

    private VXGIRenderFeature m_Feature;
    private Camera m_VoxelizeCamera;

    private Matrix4x4[] m_ViewProjMatrices;
    private Matrix4x4[] m_ViewProjInvMatrices;

    private RenderTexture m_VoxelAlbedo;
    private RenderTexture m_VoxelNormal;
    private RenderTexture m_VoxelEmission;
    private RenderTexture m_VoxelOpacity;

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
        if (m_VoxelizeCamera == null)
        {
            foreach (var camera in Camera.allCameras)
            {
                if (camera.CompareTag("Voxelize"))
                {
                    m_VoxelizeCamera = camera;
                    camera.enabled = false;
                    break;
                }
            }
        }

        if (m_VoxelAlbedo == null)
        {
            m_VoxelAlbedo = Create3DTexture(m_Feature.m_Resolution, GraphicsFormat.R32_UInt);
            Debug.Assert(m_VoxelAlbedo.Create());
        }

        if (m_VoxelNormal == null)
        {
            m_VoxelNormal = Create3DTexture(m_Feature.m_Resolution, GraphicsFormat.R32_UInt);
            Debug.Assert(m_VoxelNormal.Create());
        }

        if (m_VoxelEmission == null)
        {
            m_VoxelEmission = Create3DTexture(m_Feature.m_Resolution, GraphicsFormat.R32_UInt);
            Debug.Assert(m_VoxelEmission.Create());
        }

        if (m_VoxelOpacity == null)
        {
            m_VoxelOpacity = Create3DTexture(m_Feature.m_Resolution, GraphicsFormat.R8_UInt);
            Debug.Assert(m_VoxelOpacity.Create());
        }

        if (m_EmptyRenderTarget == null)
        {
            m_EmptyRenderTarget = RTHandles.Alloc(
                m_Feature.m_Resolution, m_Feature.m_Resolution, colorFormat: GraphicsFormat.R8_UInt);
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
        if (m_VoxelizeCamera != null && m_VoxelizeCamera.TryGetCullingParameters(out var cullingParameters))
        {
            cullResults = context.Cull(ref cullingParameters);
        }

        int resolution = m_Feature.m_Resolution;
        var cmd = renderingData.commandBuffer;
        using (new ProfilingScope(cmd, m_VoxelSetupSampler))
        {
            ClearVoxelTextures(context, cmd);

            cmd.SetViewport(new Rect(0, 0, resolution, resolution));
            cmd.SetRandomWriteTarget(1, m_VoxelAlbedo);
            cmd.SetRandomWriteTarget(2, m_VoxelNormal);
            cmd.SetRandomWriteTarget(3, m_VoxelEmission);
            cmd.SetRandomWriteTarget(4, m_VoxelOpacity);

            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            Bounds bounds = m_Feature.m_VXGIVolume;

            cmd.SetGlobalVector("_VolumeMinPoint", bounds.min);
            cmd.SetGlobalVector("_VolumeScale", new Vector4(1f / bounds.size.x, 1f / bounds.size.y, 1f / bounds.size.z));
            cmd.SetGlobalFloat("_VolumeResolution", resolution);

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

            if (m_Feature.VisulizeShader != null && m_Feature.m_VisualizeTexture != null)
            {
                int kernalId = m_Feature.VisulizeShader.FindKernel("main");
                cmd.SetComputeTextureParam(m_Feature.VisulizeShader, kernalId, "from", m_VoxelNormal);
                cmd.SetComputeTextureParam(m_Feature.VisulizeShader, kernalId, "to", m_Feature.m_VisualizeTexture);
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                m_Feature.VisulizeShader.GetKernelThreadGroupSizes(kernalId, out var x, out var y, out var z);
                int threadGroupsX = resolution / (int)x;
                int threadGroupsY = resolution / (int)y;
                int threadGroupsZ = resolution / (int)z;
                cmd.DispatchCompute(m_Feature.VisulizeShader, kernalId, threadGroupsX, threadGroupsY, threadGroupsZ);
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();
            }
        }
    }

    private void ClearVoxelTextures(ScriptableRenderContext context, CommandBuffer cmd)
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
        cmd.SetComputeTextureParam(clearShader, kernalId, nameId, m_VoxelAlbedo);
        context.ExecuteCommandBuffer(cmd);
        cmd.Clear();

        cmd.DispatchCompute(clearShader, kernalId, threadGroupsX, threadGroupsY, threadGroupsZ);
        context.ExecuteCommandBuffer(cmd);
        cmd.Clear();

        if (m_Feature.VisulizeShader != null && m_Feature.m_VisualizeTexture != null)
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
}
