using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Experimental.Rendering;

public class VXGIRenderFeature : ScriptableRendererFeature
{
    public int m_Resolution = 128;
    public Bounds m_VXGIVolume;
    public RenderTexture m_VisualizeTexture;

    [SerializeField]
    [HideInInspector]
    [Reload("Shaders/VXGI/VXGI.shader")]
    private Shader m_VXGIShader;

    [SerializeField]
    [HideInInspector]
    [Reload("Shaders/VXGI/ClearTexture3D.compute")]
    private ComputeShader m_ClearTexture3DShader;

    [SerializeField]
    [HideInInspector]
    [Reload("Shaders/VXGI/RGBA8ToFloat4.compute")]
    private ComputeShader m_VisualizeShader;

    private VoxelizeRenderPass m_ScriptablePass;

    public Shader VXGIShader => m_VXGIShader;
    public ComputeShader ClearTexture3DShader => m_ClearTexture3DShader;
    public ComputeShader VisulizeShader => m_VisualizeShader;

    /// <inheritdoc/>
    public override void Create()
    {
#if UNITY_EDITOR
        ResourceReloader.TryReloadAllNullIn(this, UniversalRenderPipelineAsset.packagePath);
#endif

        m_ScriptablePass = new VoxelizeRenderPass(this);

        // Configures where the render pass should be injected.
        m_ScriptablePass.renderPassEvent = RenderPassEvent.BeforeRendering;
    }

    // Here you can inject one or multiple render passes in the renderer.
    // This method is called when setting up the renderer once per-camera.
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        renderer.EnqueuePass(m_ScriptablePass);
    }
}


