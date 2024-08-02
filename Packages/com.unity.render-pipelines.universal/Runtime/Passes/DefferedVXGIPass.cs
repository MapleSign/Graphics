using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering;
using UnityEngine;
using System.Collections.Generic;

class DefferedVXGIPass : ScriptableRenderPass
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

        public static string _VoxelAnisosName = "_VoxelRadianceAniso";
    }

    private List<ShaderTagId> m_ShaderTagIdList;

    private VXGIRenderFeature m_Feature;

    private RTHandle m_TmpColorTarget;
    private RTHandle m_CameraColorTarget;

    private ProfilingSampler m_DefferedVXGISampler;

    public DefferedVXGIPass(VXGIRenderFeature renderFeature)
    {
        m_ShaderTagIdList = new List<ShaderTagId>
        {
            new ShaderTagId("UniversalGBuffer")
        };

        m_DefferedVXGISampler = new ProfilingSampler("VoxelizeSetup");

        m_Feature = renderFeature;
    }

    // This method is called before executing the render pass.
    // It can be used to configure render targets and their clear state. Also to create temporary render target textures.
    // When empty this render pass will render to the active camera render target.
    // You should never call CommandBuffer.SetRenderTarget. Instead call <c>ConfigureTarget</c> and <c>ConfigureClear</c>.
    // The render pipeline will ensure target setup and clearing happens in a performant manner.
    public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
    {
        overrideCameraTarget = true;
        m_CameraColorTarget = renderingData.cameraData.renderer.cameraColorTargetHandle;

        var cameraDesc = renderingData.cameraData.cameraTargetDescriptor;
        cameraDesc.depthStencilFormat = GraphicsFormat.None;
        RenderingUtils.ReAllocateIfNeeded(ref m_TmpColorTarget, cameraDesc);
    }

    // Here you can implement the rendering logic.
    // Use <c>ScriptableRenderContext</c> to issue drawing commands or execute command buffers
    // https://docs.unity3d.com/ScriptReference/Rendering.ScriptableRenderContext.html
    // You don't have to call ScriptableRenderContext.submit, the render pipeline will call it at specific points in the pipeline.
    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        var cmd = renderingData.commandBuffer;
        using (new ProfilingScope(cmd, m_DefferedVXGISampler))
        {
            cmd.SetGlobalTexture(ShaderConstants._VoxelRadiance, m_Feature.VoxelRadiance);
            for (int i = 0; i < m_Feature.VoxelAnisos.Length; i++)
            {
                cmd.SetGlobalTexture(ShaderConstants._VoxelAnisosName + string.Format("_{0}", i), m_Feature.VoxelAnisos[i]);
            }
            Blitter.BlitCameraTexture(cmd, m_CameraColorTarget, m_TmpColorTarget, m_Feature.m_VXGIMaterial, 1);

            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            Blitter.BlitCameraTexture(cmd, m_TmpColorTarget, m_CameraColorTarget);
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
        }
    }

    // Cleanup any allocated resources that were created during the execution of this render pass.
    public override void OnCameraCleanup(CommandBuffer cmd)
    {
    }
}
